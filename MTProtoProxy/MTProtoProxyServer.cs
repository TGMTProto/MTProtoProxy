using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MTProtoProxy
{
    public class MTProtoProxyServer : IDisposable
    {
        public bool IsRunning { get; private set; }
        private readonly string _secret;
        private readonly int _port;
        private readonly List<string> _ipServersConfig = new List<string> { "149.154.175.50", "149.154.167.51", "149.154.175.100", "149.154.167.91", "149.154.171.5" };
        private Socket _listener;
        private readonly List<Socket> _listSocket = new List<Socket>();
        private bool _isDisposed;
        public MTProtoProxyServer(string secret, int port)
        {
            _secret = secret;
            _port = port;
            Console.WriteLine("MTProtoProxy Server By Telegram @MTProtoProxy v1.0.0");
            Console.WriteLine("open source => https://github.com/TGMTProto/MTProtoProxy");
        }
        public IPAddress GetLocalIpAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip;
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }
        public async void StartAsync()
        {
            ThrowIfDisposed();
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            _listener.Bind(new IPEndPoint(GetLocalIpAddress(), _port));
            _listener.Listen(100);
            Console.WriteLine("Start listner with {0}:{1}", GetLocalIpAddress(), _port);
            IsRunning = true;
            while (IsRunning)
            {
                ThrowIfDisposed();
                var socket = await _listener.AcceptSocketAsync().ConfigureAwait(false);
                StartSocketAsync(socket);
                Console.WriteLine("Number of connections:{0}", Convert.ToInt32(_listSocket.Count));
            }
        }
        public void Stop()
        {
            if (IsRunning)
            {
                IsRunning = false;
                foreach (var socket in _listSocket)
                {
                    try
                    {
                        socket.Close();
                    }
                    catch(Exception e)
                    {
                        if (e.InnerException != null)
                        {
                            Console.WriteLine(e.InnerException.Message);
                            return;
                        }
                        Console.WriteLine(e.Message);
                    }
                }
                try
                {
                    _listener.Close();
                }
                catch(Exception e)
                {
                    if (e.InnerException != null)
                    {
                        Console.WriteLine(e.InnerException.Message);
                        return;
                    }
                    Console.WriteLine(e.Message);
                }
                _listSocket.Clear();
            }
        }
        private async void StartSocketAsync(Socket socket)
        {
            try
            {
                _listSocket.Add(socket);
                var randomBuffer = new byte[64];
                await socket.ReceiveAsync(randomBuffer, 0, randomBuffer.Length).ConfigureAwait(false);

                var reversed = randomBuffer.SubArray(8, 48).Reverse().ToArray();
                var key = randomBuffer.SubArray(8, 32);
                var keyReversed = reversed.SubArray(0, 32);
                var binSecret = ArrayUtils.HexToByteArray(_secret);
                key = SHA256Helper.ComputeHashsum(key.Concat(binSecret).ToArray());
                keyReversed = SHA256Helper.ComputeHashsum(keyReversed.Concat(binSecret).ToArray());

                var mtprotoPacketServer = new MTProtoPacket();
                mtprotoPacketServer.SetInitBufferObfuscated2(randomBuffer, reversed, key, keyReversed);
                byte[] decryptBuf = mtprotoPacketServer.DecryptObfuscated2(randomBuffer).SubArray(56, 8);
                for (int i = 56; i < 64; i++)
                {
                    randomBuffer[i] = decryptBuf[i - 56];
                }
                byte[] res = randomBuffer.SubArray(56, 4);
                for (int i = 0; i < 4; i++)
                {
                    if (res[i] != 0xef)
                    {
                        Console.WriteLine("Error in buffer");
                        return;
                    }
                }

                var dcId = Math.Abs(BitConverter.ToInt16(randomBuffer.SubArray(60, 2), 0));
                Console.WriteLine("Create new connection with dataCenterId:{0}", dcId);
                var ip = _ipServersConfig[dcId - 1];

                Socket socketClient = null;
                MTProtoPacket mtprotoPacketClient = null;
                object lockSend = new object();
                while (socket.IsConnected())
                {
                    ThrowIfDisposed();
                    var packetBytes = await GetPacketBytesAsync(socket, mtprotoPacketServer).ConfigureAwait(false);
                    if (packetBytes == null)
                    {
                        continue;
                    }
                    if (socketClient == null || !socketClient.Connected)
                    {
                        socketClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        socketClient.Connect(ip, 443);
                        _listSocket.Add(socketClient);
                        mtprotoPacketClient = new MTProtoPacket();
                        await mtprotoPacketClient.SendInitBufferObfuscated2Async(socketClient).ConfigureAwait(false);
                        SenderSocketAsync(socketClient, socket, mtprotoPacketClient, mtprotoPacketServer);
                    }

                    var packet = mtprotoPacketClient.CreatePacketObfuscated2(packetBytes);

                    lock (lockSend)
                    {
                        socketClient.SendAsync(packet, 0, packet.Length).GetAwaiter().GetResult();
                    }
                }
                if (socketClient != null)
                {
                    socketClient.Close();
                    _listSocket.Remove(socketClient);
                }
                Console.WriteLine("A connection was closed");
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    Console.WriteLine(e.InnerException.Message);
                    return;
                }
                Console.WriteLine(e.Message);
            }
            try
            {
                socket.Close();
                _listSocket.Remove(socket);
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    Console.WriteLine(e.InnerException.Message);
                    return;
                }
                Console.WriteLine(e.Message);
            }
        }
        private async void SenderSocketAsync(Socket socketClient, Socket socketServer, MTProtoPacket mtprotoPacketClient, MTProtoPacket mtprotoPacketServer)
        {
            try
            {
                while (true)
                {
                    var packetBytesClient = await GetPacketBytesAsync(socketClient, mtprotoPacketClient).ConfigureAwait(false);
                    if (packetBytesClient == null)
                    {
                        return;
                    }
                    var packetEnc = mtprotoPacketServer.CreatePacketObfuscated2(packetBytesClient);
                    await socketServer.SendAsync(packetEnc, 0, packetEnc.Length).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    Console.WriteLine(e.InnerException.Message);
                    return;
                }
                Console.WriteLine(e.Message);
            }
        }
        private async Task<bool> ReceiveAsync(byte[] buffer, Socket socket)
        {
            var failedcomplete = false;

            var bytesRead = 0;
            do
            {
                var result = await socket.ReceiveAsync(buffer, bytesRead, buffer.Length - bytesRead)
                    .ConfigureAwait(false);
                if (result == 0)
                {
                    failedcomplete = true;
                    break;
                }
                bytesRead += result;

            } while (bytesRead != buffer.Length);

            return failedcomplete;
        }
        private async Task<byte[]> GetPacketBytesAsync(Socket socket, MTProtoPacket mtprotoPacket)
        {
            var packetLengthBytes = new byte[1];
            var resultPacketLength = await ReceiveAsync(packetLengthBytes, socket).ConfigureAwait(false);
            if (resultPacketLength)
            {
                return null;
            }
            packetLengthBytes = mtprotoPacket.DecryptObfuscated2(packetLengthBytes);

            var packetLength = BitConverter.ToInt32(packetLengthBytes.Concat(new byte[] { 0x00, 0x00, 0x00 }).ToArray(), 0);

            int lengthBytes;
            if (packetLength < 0x7F)
            {
                lengthBytes = packetLength << 2;
            }
            else
            {
                var lenBytes = new byte[3];
                var resultLengthBytes = ReceiveAsync(lenBytes, socket).Result;
                if (resultLengthBytes)
                {
                    return null;
                }
                lenBytes = mtprotoPacket.DecryptObfuscated2(lenBytes);
                lengthBytes = BitConverter.ToInt32(lenBytes.Concat(new byte[] { 0x00 }).ToArray(), 0) << 2;
            }
            var packetBytes = new byte[lengthBytes];
            var resultpacket = ReceiveAsync(packetBytes, socket).Result;
            if (resultpacket)
            {
                return null;
            }
            packetBytes = mtprotoPacket.DecryptObfuscated2(packetBytes);
            return packetBytes;
        }
        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            if (!disposing)
            {
                Stop();
            }
            _listener = null;
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException("MTProtoProxyServer was disposed.");
            }
        }
    }
}