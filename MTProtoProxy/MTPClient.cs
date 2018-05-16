using System;
using System.Linq;
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
        public IPEndPoint IPEndPoint { get => _ipEndPoint; }
        private readonly IPEndPoint _ipEndPoint;
        private readonly long _sessionId;
        private Socket _socket;
        private Thread _thread;
        private readonly object _lockSend = new object();
        private readonly object _lockStart = new object();
        private readonly MTProtoPacket _mtprotoPacket;
        private int _dcId;
        private volatile bool _isDisposed;
        public event EventHandler<Tuple<byte[], int>> PacketReceived;
        public event EventHandler ReceiverEnded;
        public MTPClient(Socket socket, long sessionId)
        {
            _sessionId = sessionId;
            _socket = socket;
            _mtprotoPacket = new MTProtoPacket();
            _ipEndPoint = (IPEndPoint)_socket.RemoteEndPoint;
        }
        public void Start(byte[] buffer, string secret)
        {
            lock (_lockStart)
            {
                ThrowIfDisposed();
                lock (_lockSend)
                {
                    _mtprotoPacket.Clear();
                    _mtprotoPacket.SetInitBufferObfuscated2(buffer, secret);
                }
                byte[] decryptBuf = _mtprotoPacket.DecryptObfuscated2(buffer).SubArray(56, 8);
                for (int i = 56; i < 64; i++)
                {
                    buffer[i] = decryptBuf[i - 56];
                }
                byte[] res = buffer.SubArray(56, 4);
                for (int i = 0; i < 4; i++)
                {
                    if (res[i] != 0xef)
                    {
                        Console.WriteLine("Error in buffer");
                        return;
                    }
                }
                _dcId = Math.Abs(BitConverter.ToInt16(buffer.SubArray(60, 2), 0));
                _thread = new Thread(async () => await StartReceiverAsync().ConfigureAwait(false));
                _thread.Start();
            }
        }
        public Task SendAsync(byte[] buffer)
        {
            lock (_lockSend)
            {
                ThrowIfDisposed();
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
        private async Task<bool> ReceiveAsync(byte[] buffer)
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
        private async Task<byte[]> ReceiveAsync()
        {
            ThrowIfDisposed();
            var packetLengthBytes = new byte[1];
            var resultPacketLength = await ReceiveAsync(packetLengthBytes).ConfigureAwait(false);
            if (resultPacketLength)
            {
                return null;
            }
            packetLengthBytes = _mtprotoPacket.DecryptObfuscated2(packetLengthBytes);

            var packetLength = BitConverter.ToInt32(packetLengthBytes.Concat(new byte[] { 0x00, 0x00, 0x00 }).ToArray(), 0);

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
                lengthBytes = BitConverter.ToInt32(lenBytes.Concat(new byte[] { 0x00 }).ToArray(), 0) << 2;
            }
            var packetBytes = new byte[lengthBytes];
            var resultpacket = await ReceiveAsync(packetBytes).ConfigureAwait(false);
            if (resultpacket)
            {
                return null;
            }
            packetBytes = _mtprotoPacket.DecryptObfuscated2(packetBytes);
            return packetBytes;
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
                    PacketReceived?.BeginInvoke(this, new Tuple<byte[], int>(result, _dcId), null, null);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error receiving: " + e);
                    break;
                }
                Thread.Sleep(50);
            }
            ReceiverEnded.BeginInvoke(this, null, null, null);
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool isDisposing)
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
        }
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException("Connection was disposed.");
            }
        }
    }
}