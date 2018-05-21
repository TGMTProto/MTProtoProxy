using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MTProtoProxy
{
    internal class MTPClient : IDisposable
    {
        public long SessionId { get => _sessionId; }
        public bool IsClosed { get => _isDisposed; }
        public ProtocolType ProtocolType { get => _protocolType; }
        public IPEndPoint IPEndPoint { get => _ipEndPoint; }
        private readonly IPEndPoint _ipEndPoint;
        private readonly long _sessionId;
        private Socket _socket;
        private Thread _thread;
        private readonly object _lockConnection = new object();
        private readonly MTProtoPacket _mtprotoPacket;
        private ProtocolType _protocolType;
        private volatile bool _isDisposed;
        public event EventHandler<int> ClientCreated;
        public event EventHandler<byte[]> PacketReceived;
        public event EventHandler ReceiverEnded;
        public MTPClient(in Socket socket, in long sessionId)
        {
            _sessionId = sessionId;
            _socket = socket;
            _mtprotoPacket = new MTProtoPacket();
            _ipEndPoint = (IPEndPoint)_socket.RemoteEndPoint;
        }
        public void Start(in byte[] buffer, in string secret)
        {
            lock (_lockConnection)
            {
                if (_isDisposed)
                {
                    return;
                }
                _mtprotoPacket.Clear();
                _mtprotoPacket.SetInitBufferObfuscated2(buffer, secret);

                if (_mtprotoPacket.ProtocolType == ProtocolType.None)
                {
                    Console.WriteLine("Error in protocol");
                    return;
                }
                _protocolType = _mtprotoPacket.ProtocolType;

                var dcId = Math.Abs(BitConverter.ToInt16(buffer.SubArray(60, 2), 0));
                ClientCreated?.Invoke(this, dcId);

                _thread = new Thread(async () => await StartReceiverAsync().ConfigureAwait(false));
                _thread.Start();
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
            switch (_mtprotoPacket.ProtocolType)
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
                            if (lenBytes == null)
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
                            packetLengthBytes = null;
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
                _mtprotoPacket.Dispose();
            }
        }
    }
}