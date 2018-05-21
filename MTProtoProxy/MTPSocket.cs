using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MTProtoProxy
{
    internal class MTPSocket : IDisposable
    {
        public long SessionId { get => _sessionId; }
        public bool IsConnected { get => _isConnected; }
        public bool IsClosed { get => _isDisposed; }
        private readonly long _sessionId;
        private Socket _socket;
        private Thread _thread;
        private readonly object _lockConnection = new object();
        private volatile bool _isConnected;
        private volatile bool _isDisposed;
        private readonly MTProtoPacket _mtprotoPacket;
        private readonly ProtocolType _protocolType;
        public event EventHandler<byte[]> PacketReceived;
        public event EventHandler ReceiverEnded;
        public MTPSocket(in long sessionId, in ProtocolType protocolType)
        {
            _sessionId = sessionId;
            _protocolType = protocolType;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            _mtprotoPacket = new MTProtoPacket();
        }
        public bool Connect(in IPEndPoint ipEndPoint)
        {
            lock (_lockConnection)
            {
                if (_isDisposed)
                {
                    return false;
                }
                if (!_isConnected)
                {
                    try
                    {
                        _socket.ConnectAsync(ipEndPoint).GetAwaiter().GetResult();
                        _mtprotoPacket.Clear();
                        var buffer = _mtprotoPacket.GetInitBufferObfuscated2(_protocolType);
                        _socket.SendAsync(buffer, 0, buffer.Length).GetAwaiter().GetResult();
                        _thread = new Thread(async () => await StartReceiverAsync().ConfigureAwait(false));
                        _thread.Start();
                        _isConnected = true;
                        Array.Clear(buffer, 0, buffer.Length);
                        return true;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        return false;
                    }
                }
                return true;
            }
        }
        public Task SendAsync(in byte[] buffer)
        {
            lock (_lockConnection)
            {
                if (_isDisposed)
                {
                    return null;
                }
                if (_isConnected)
                {
                    try
                    {
                        var packet = _mtprotoPacket.CreatePacketObfuscated2(buffer);
                        return _socket.SendAsync(packet, 0, packet.Length);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                return null;
            }
        }
        private async ValueTask<bool> ReceiveAsync(byte[] buffer)
        {
            var failedcomplete = false;

            var bytesRead = 0;
            do
            {
                var result = await _socket.ReceiveAsync(buffer, bytesRead, buffer.Length - bytesRead).ConfigureAwait(false);
                if (result == 0)
                {
                    failedcomplete = true;
                    break;
                }
                bytesRead += result;

            } while (bytesRead != buffer.Length);

            return failedcomplete;
        }
        private async ValueTask<byte[]> ReceiveAsync()
        {
            if (_isDisposed)
            {
                return null;
            }
            switch (_protocolType)
            {
                case ProtocolType.AbridgedObfuscated2:
                    {
                        var packetLengthBytes = new byte[1];
                        var resultPacketLength = await ReceiveAsync(packetLengthBytes).ConfigureAwait(false);
                        if (resultPacketLength)
                        {
                            return null;
                        }
                        packetLengthBytes = _mtprotoPacket.DecryptObfuscated2(packetLengthBytes);
                        if (packetLengthBytes == null)
                        {
                            return null;
                        }

                        var packetLength = BitConverter.ToInt32(ArrayUtils.Combine(packetLengthBytes, new byte[] { 0x00, 0x00, 0x00 }), 0);
                        Array.Clear(packetLengthBytes, 0, packetLengthBytes.Length);

                        int lengthBytes;
                        if (packetLength < 0x7F)
                        {
                            lengthBytes = packetLength << 2;
                        }
                        else
                        {
                            var lenBytes = new byte[3];
                            var resultLengthBytes = await ReceiveAsync(lenBytes).ConfigureAwait(false);
                            if (resultLengthBytes)
                            {
                                return null;
                            }
                            lenBytes = _mtprotoPacket.DecryptObfuscated2(lenBytes);
                            if(lenBytes==null)
                            {
                                return null;
                            }
                            lengthBytes = BitConverter.ToInt32(ArrayUtils.Combine(lenBytes, new byte[] { 0x00 }), 0) << 2;
                            Array.Clear(lenBytes, 0, lenBytes.Length);
                        }
                        var packetBytes = new byte[lengthBytes];
                        var resultpacket = await ReceiveAsync(packetBytes).ConfigureAwait(false);
                        if (resultpacket)
                        {
                            return null;
                        }
                        packetBytes = _mtprotoPacket.DecryptObfuscated2(packetBytes);
                        if (packetBytes == null)
                        {
                            return null;
                        }
                        return packetBytes;
                    }
                case ProtocolType.IntermediateObfuscated2:
                    {
                        var packetLengthBytes = new byte[4];
                        var resultPacketLength = await ReceiveAsync(packetLengthBytes).ConfigureAwait(false);
                        if (resultPacketLength)
                        {
                            return null;
                        }
                        packetLengthBytes = _mtprotoPacket.DecryptObfuscated2(packetLengthBytes);
                        if (packetLengthBytes == null)
                        {
                            return null;
                        }

                        var packetLength = BitConverter.ToInt32(packetLengthBytes, 0);
                        Array.Clear(packetLengthBytes, 0, packetLengthBytes.Length);
                        var packetBytes = new byte[packetLength];
                        var resultPacketBytes = await ReceiveAsync(packetBytes).ConfigureAwait(false);
                        if (resultPacketBytes)
                        {
                            return null;
                        }
                        packetBytes = _mtprotoPacket.DecryptObfuscated2(packetBytes);
                        if (packetBytes == null)
                        {
                            return null;
                        }
                        return packetBytes;
                    }
                default:
                    return null;
            }
        }
        private async Task StartReceiverAsync()
        {
            while (_socket.IsConnected())
            {
                try
                {
                    var result = await ReceiveAsync().ConfigureAwait(false);
                    if (result is null)
                    {
                        break;
                    }
                    PacketReceived?.BeginInvoke(this, result, null, null);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error receiving: " + e);
                    break;
                }
            }
            lock (_lockConnection)
            {
                if (!_isDisposed)
                {
                    ReceiverEnded.BeginInvoke(this, null, null, null);
                }
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(in bool isDisposing)
        {
            lock (_lockConnection)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;

                if (!isDisposing)
                {
                    return;
                }
                if (_thread != null)
                {
                    try
                    {
                        _thread.Abort();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                    _thread = null;
                }
                if (_socket != null)
                {
                    try
                    {
                        _socket.Shutdown(SocketShutdown.Both);
                        _socket.Disconnect(false);
                        _socket.Dispose();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                    finally
                    {
                        _socket = null;
                    }
                }
                _isConnected = false;
                _mtprotoPacket.Dispose();
            }
        }
    }
}