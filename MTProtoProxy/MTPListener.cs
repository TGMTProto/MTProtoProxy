using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MTProtoProxy
{
    internal class MTPListener : IDisposable
    {
        public int Port { get; set; }
        public bool IsClosed { get => _isDisposed; }
        private Socket _socket;
        private readonly int _port;
        private readonly string _ip;
        private readonly object _lockConnection = new object();
        private Thread _thread;
        private volatile bool _isDisposed;
        public event EventHandler<Socket> SocketAccepted;
        public event EventHandler ListenEnded;
        public MTPListener(in string ip, in int port)
        {
            _ip = ip;
            _port = port;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        }
        public void Start(in int backLog)
        {
            ThrowIfDisposed();
            IPAddress ipAddress = null;
            if (_ip == "default")
            {
                ipAddress = IPAddress.Any;
            }
            else
            {
                if (!IPAddress.TryParse(_ip, out ipAddress))
                {
                    throw new Exception("ipAddress is not valid");
                }
            }
            var ipEndPoint = new IPEndPoint(ipAddress, _port);
            _socket.Bind(ipEndPoint);
            _socket.Listen(backLog);
            _thread = new Thread(async () => await StartListenAsync().ConfigureAwait(false));
            _thread.Start();
        }
        private async Task StartListenAsync()
        {
            while (true)
            {
                try
                {
                    var socket = await _socket.AcceptSocketAsync().ConfigureAwait(false);
                    SocketAccepted?.BeginInvoke(this, socket, null, null);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error listen: " + e);
                    break;
                }
            }
            lock (_lockConnection)
            {
                if (!_isDisposed)
                {
                    ListenEnded?.BeginInvoke(this, null, null, null);
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