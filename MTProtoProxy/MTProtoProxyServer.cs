using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace MTProtoProxy
{
    public class MTProtoProxyServer
    {
        public bool IsRunning { get; private set; }
        private readonly string _secret;
        private readonly int _port;
        private readonly List<string> _ipServersConfig = new List<string> { "149.154.175.50", "149.154.167.51", "149.154.175.100", "149.154.167.91", "149.154.171.5" };
        private Socket _listener;
        private readonly List<MTPSocket> _mtpSockets = new List<MTPSocket>();
        private bool _isClosed;
        public MTProtoProxyServer(string secret, int port)
        {
            _secret = secret;
            _port = port;
            Console.WriteLine("MTProtoProxy Server By Telegram @MTProtoProxy v1.0.1-alpha");
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
            if (!_isClosed)
            {
                try
                {
                    _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                    _listener.Bind(new IPEndPoint(GetLocalIpAddress(), _port));
                    _listener.Listen(100);
                    Console.WriteLine("Start listner with {0}:{1}", GetLocalIpAddress(), _port);
                    IsRunning = true;
                    while (IsRunning)
                    {
                        try
                        {
                            var socket = await _listener.AcceptSocketAsync().ConfigureAwait(false);
                            StartSocketAsync(socket);
                        }
                        catch (Exception e)
                        {
                            if (e.InnerException != null)
                            {
                                Console.WriteLine(e.InnerException.Message);
                            }
                            else
                            {
                                Console.WriteLine(e.Message);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    if (e.InnerException != null)
                    {
                        Console.WriteLine(e.InnerException.Message);
                    }
                    else
                    {
                        Console.WriteLine(e.Message);
                    }
                    StartAsync();
                }
            }
        }
        private async void StartSocketAsync(Socket socket)
        {
            try
            {
                var randomBuffer = new byte[64];
                var resultRandom = await socket.ReceiveAsync(randomBuffer, 0, randomBuffer.Length).ConfigureAwait(false);
                if (resultRandom != 64)
                {
                    socket.Close();
                    socket = null;
                    return;
                }
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

                var socketClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socketClient.ConnectAsync(new IPEndPoint(IPAddress.Parse(ip), 443)).ConfigureAwait(false);
                var mtprotoPacketClient = new MTProtoPacket();
                await mtprotoPacketClient.SendInitBufferObfuscated2Async(socketClient).ConfigureAwait(false);
                var mtpSocket = new MTPSocket(socketClient, socket, mtprotoPacketClient, mtprotoPacketServer);
                mtpSocket.Disconnected += MTPSocketDisconnected;
                mtpSocket.Start();
                _mtpSockets.Add(mtpSocket);
                Console.WriteLine("Number of connections:{0}", _mtpSockets.Count);
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
        private void MTPSocketDisconnected(object sender, EventArgs ev)
        {
            try
            {
                var mtpSocket = (MTPSocket)sender;
                mtpSocket.Dispose();
                _mtpSockets.Remove(mtpSocket);
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
        public void Close()
        {
            try
            {
                foreach (var mtpSocket in _mtpSockets)
                {
                    mtpSocket.Dispose();
                }
                IsRunning = false;
                _listener.Dispose();
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    Console.WriteLine(e.InnerException.Message);
                }
                else
                {
                    Console.WriteLine(e.Message);
                }
            }
            _isClosed = true;
        }
    }
}