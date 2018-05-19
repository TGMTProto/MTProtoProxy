using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace MTProtoProxy
{
    public class MTProtoProxyServer : IDisposable
    {
        public string Secret { get => _secret; }
        public int Port { get => _port; }
        public bool IsClosed { get => _isDisposed; }
        private readonly string _secret;
        private readonly int _port;
        private readonly MTPListener _mtplistener;
        private readonly Random _random = new Random();
        private readonly object _lockDic = new object();
        private static readonly List<string> _ipServers = new List<string> { "149.154.175.50", "149.154.167.51", "149.154.175.100", "149.154.167.91", "149.154.171.5" };
        private static readonly List<string> _ipServersConfig = new List<string> { "149.154.175.50", "149.154.167.50", "149.154.175.100", "91.108.4.204", "91.108.56.161" };
        private readonly Dictionary<long, MTPClient> _mtpClientDictionary = new Dictionary<long, MTPClient>();
        private readonly Dictionary<long, MTPSocket> _mtpSocketDictionary = new Dictionary<long, MTPSocket>();
        private volatile bool _isDisposed;
        public MTProtoProxyServer(in string secret, in int port, in string ip = "default")
        {
            _secret = secret;
            _port = port;
            _mtplistener = new MTPListener(ip, port);
            _mtplistener.SocketAccepted += MTPListenerSocketAccepted;
            _mtplistener.ListenEnded += MTPListenerListenEnded;
            Console.WriteLine("MTProtoProxy Server By Telegram @MTProtoProxy v1.0.3-alpha");
            Console.WriteLine("open source => https://github.com/TGMTProto/MTProtoProxy");
        }
        public void Start(in int backLog = 100)
        {
            ThrowIfDisposed();
            _mtplistener.Start(backLog);
        }
        private void MTPListenerListenEnded(object sender, EventArgs e)
        {
            var mtpListener = (MTPListener)sender;
            mtpListener.Dispose();
            Dispose();
        }
        private void MTPListenerSocketAccepted(object sender, Socket e)
        {
            var sessionId = GenerateSessionId();
            var buffer = new byte[64];
            var result = e.Receive(buffer);
            if (result == 64)
            {
                var mtpClient = new MTPClient(e, sessionId);
                mtpClient.PacketReceived += MTPClientPacketReceived;
                mtpClient.ReceiverEnded += MTPClientReceiverEnded;
                mtpClient.Start(buffer, _secret);
                lock (_lockDic)
                {
                    _mtpClientDictionary.Add(sessionId, mtpClient);
                }
            }
            else
            {
                e.Dispose();
                e = null;
            }
            lock (_lockDic)
            {
                var endPointsCount = _mtpClientDictionary.Select(x => x.Value.IPEndPoint.Address).Distinct().Count();
                Console.WriteLine("Number of users(Ips):{0}", endPointsCount);
                Console.WriteLine("Number of connections:{0}", _mtpClientDictionary.Count());
            }
            Array.Clear(buffer, 0, buffer.Length);
        }
        private void MTPClientReceiverEnded(object sender, EventArgs e)
        {
            var mtpClient = (MTPClient)sender;
            MTPSocket mtpSocket = null;
            lock (_lockDic)
            {
                if (_mtpSocketDictionary.ContainsKey(mtpClient.SessionId))
                {
                    mtpSocket = _mtpSocketDictionary[mtpClient.SessionId];
                    _mtpSocketDictionary.Remove(mtpClient.SessionId);
                }
                if (_mtpClientDictionary.ContainsKey(mtpClient.SessionId))
                {
                    _mtpClientDictionary.Remove(mtpClient.SessionId);
                }
            }
            if (mtpSocket != null)
            {
                mtpSocket.Dispose();
            }
            mtpClient.Dispose();
            Console.WriteLine("A connection was closed");
        }
        private async void MTPClientPacketReceived(object sender,Tuple<byte[], int> e)
        {
            try
            {
                var mtpClient = (MTPClient)sender;
                MTPSocket mtpSocket = null;
                lock (_lockDic)
                {
                    mtpSocket = _mtpSocketDictionary.FirstOrDefault(x => x.Value.SessionId == mtpClient.SessionId).Value;
                }
                if (mtpSocket == null)
                {
                    mtpSocket = new MTPSocket(new IPEndPoint(IPAddress.Parse(_ipServersConfig[e.Item2 - 1]), 443), mtpClient.SessionId);
                    mtpSocket.PacketReceived += MTPSocketPacketReceived;
                    mtpSocket.ReceiverEnded += MTPSocketReceiverEnded;
                    if (!mtpSocket.Connect())
                    {
                        mtpSocket.Dispose();
                        mtpSocket = new MTPSocket(new IPEndPoint(IPAddress.Parse(_ipServers[e.Item2 - 1]), 443), mtpClient.SessionId);
                        mtpSocket.PacketReceived += MTPSocketPacketReceived;
                        mtpSocket.ReceiverEnded += MTPSocketReceiverEnded;
                        if (!mtpSocket.Connect())
                        {
                            mtpSocket.Dispose();
                            lock (_lockDic)
                            {
                                _mtpClientDictionary.Remove(mtpClient.SessionId);
                            }
                            return;
                        }
                    }
                    Console.WriteLine("Create new connection with dataCenterId:{0}", e.Item2);
                    lock (_lockDic)
                    {
                        _mtpSocketDictionary.Add(mtpClient.SessionId, mtpSocket);
                    }
                }
                if (!mtpSocket.IsClosed)
                {
                    await mtpSocket.SendAsync(e.Item1).ConfigureAwait(false);
                }
                Array.Clear(e.Item1, 0, e.Item1.Length);
                GC.Collect();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private void MTPSocketReceiverEnded(object sender, EventArgs e)
        {
            var mtpSocket = (MTPSocket)sender;
            MTPClient mtpClient = null;
            lock (_lockDic)
            {
                if (_mtpClientDictionary.ContainsKey(mtpSocket.SessionId))
                {
                    mtpClient = _mtpClientDictionary[mtpSocket.SessionId];
                    _mtpClientDictionary.Remove(mtpSocket.SessionId);
                }
                if (_mtpSocketDictionary.ContainsKey(mtpSocket.SessionId))
                {
                    _mtpSocketDictionary.Remove(mtpSocket.SessionId);
                }
            }
            if (mtpClient != null)
            {
                mtpClient.Dispose();
            }
            mtpSocket.Dispose();
            Console.WriteLine("A connection was closed");
        }
        private async void MTPSocketPacketReceived(object sender, byte[] e)
        {
            try
            {
                var mtpSocket = (MTPSocket)sender;
                MTPClient mtpClient = null;
                lock (_lockDic)
                {
                    if (_mtpClientDictionary.ContainsKey(mtpSocket.SessionId))
                    {
                        mtpClient = _mtpClientDictionary[mtpSocket.SessionId];
                    }
                }
                if (mtpClient != null && !mtpClient.IsClosed)
                {
                    await mtpClient.SendAsync(e).ConfigureAwait(false);
                    Array.Clear(e, 0, e.Length);
                    GC.Collect();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private long GenerateSessionId()
        {
            var randomlong = (Convert.ToInt64(_random.Next()) << 32) | Convert.ToInt64(_random.Next());
            return randomlong;
        }
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException("Connection was disposed.");
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(in bool isDisposing)
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
            lock (_lockDic)
            {
                foreach (var mtpClient in _mtpClientDictionary)
                {
                    mtpClient.Value.Dispose();
                }
                foreach (var mtpSocket in _mtpSocketDictionary)
                {
                    mtpSocket.Value.Dispose();
                }
                _mtpClientDictionary.Clear();
                _mtpSocketDictionary.Clear();
            }
            //MemoryManager.Stop();
        }
    }
}