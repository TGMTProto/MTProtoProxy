using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace MTProtoProxy
{
    internal static class TgSockets
    {
        private static volatile int _numberOfSockets;
        private static volatile bool _stop;
        private static readonly object _lockSockets = new object();
        private static readonly List<Socket> _sockets = new List<Socket>();
        private static readonly List<string> _ipServers = new List<string> { "149.154.175.50", "149.154.167.51", "149.154.175.100", "149.154.167.91", "149.154.171.5" };
        private static readonly List<string> _ipServersConfig = new List<string> { "149.154.175.50", "149.154.167.50", "149.154.175.100", "91.108.4.204", "91.108.56.161" };
        public static Task StartAsync()
        {
            return Task.Run(() =>
            {
                int index = 0;
                while (!_stop)
                {
                    SpinWait.SpinUntil(() => _numberOfSockets < Constants.NumberOfTgSocket);
                    if (_stop)
                    {
                        break;
                    }
                    if (index == 5)
                    {
                        index = 0;
                    }
                    var ip = _ipServersConfig[index];
                    var ipAddress = IPAddress.Parse(ip);
                    var endPoint = new IPEndPoint(ipAddress, Constants.TelegramPort);
                    var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                    var socketAsyncEventArgs = new SocketAsyncEventArgs();
                    socketAsyncEventArgs.RemoteEndPoint = endPoint;
                    socketAsyncEventArgs.Completed += SocketAsyncEventArgsCompleted;
                    socket.ConnectAsync(socketAsyncEventArgs);
                    Interlocked.Increment(ref _numberOfSockets);
                    index++;
                    Thread.Sleep(Constants.SleepTgSockets);
                }
            });
        }
        public static void Stop()
        {
            _stop = true;
        }
        public static Socket GetSocket(in int dcId)
        {
            Socket socket = null;
            lock (_lockSockets)
            {
                var ip = _ipServersConfig[dcId - 1];
                socket = _sockets.FirstOrDefault(x => ((IPEndPoint)x.RemoteEndPoint).Address.ToString() == ip);
                if (socket == null)
                {
                    ip = _ipServers[dcId - 1];
                    socket = _sockets.FirstOrDefault(x => ((IPEndPoint)x.RemoteEndPoint).Address.ToString() == ip);
                    if (socket != null)
                    {
                        _sockets.Remove(socket);
                        Interlocked.Decrement(ref _numberOfSockets);
                        return socket;
                    }
                }
                else
                {
                    _sockets.Remove(socket);
                    Interlocked.Decrement(ref _numberOfSockets);
                    return socket;
                }
            }
            var ip1 = _ipServersConfig[dcId - 1];
            var ipAddress = IPAddress.Parse(ip1);
            var endPoint = new IPEndPoint(ipAddress, Constants.TelegramPort);
            socket = new Socket(endPoint.AddressFamily, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            try
            {
                socket.Connect(endPoint);
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.TimedOut)
                {
                    ip1 = _ipServers[dcId - 1];
                    ipAddress = IPAddress.Parse(ip1);
                    endPoint = new IPEndPoint(ipAddress, Constants.TelegramPort);
                    try
                    {
                        socket.Connect(endPoint);
                    }
                    catch (Exception ex)
                    {
                        socket = null;
                        Console.WriteLine($"The server can not connect to the telegram server {ip1}");
                        Console.WriteLine(ex);
                    }
                }
            }
            catch (Exception e)
            {
                socket = null;
                Console.WriteLine($"The server can not connect to the telegram server {ip1}");
                Console.WriteLine(e);
            }
            return socket;
        }
        private static void SocketAsyncEventArgsCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                lock (_lockSockets)
                {
                    _sockets.Add(e.ConnectSocket);
                }
            }
            else if (e.SocketError == SocketError.TimedOut)
            {
                var index = _ipServersConfig.IndexOf(((IPEndPoint)e.RemoteEndPoint).Address.ToString());
                var ip = _ipServers[index];
                var ipAddress = IPAddress.Parse(ip);
                var endPoint = new IPEndPoint(ipAddress, Constants.TelegramPort);
                var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                e.RemoteEndPoint = endPoint;
                e.Completed += SocketAsyncEventArgsCompleted;
                e.UserToken = "TimedOut";
                socket.ConnectAsync(e);
            }
            else if ((string)e.UserToken == "TimedOut")
            {
                Console.WriteLine($"The server can not connect to the telegram server {e.RemoteEndPoint}");
                Interlocked.Decrement(ref _numberOfSockets);
            }
            e.Dispose();
            e = null;
            sender = null;
        }
        public static void Close()
        {
            Stop();
            lock (_lockSockets)
            {
                foreach (var socket in _sockets)
                {
                    try
                    {
                        socket.Shutdown(SocketShutdown.Both);
                        socket.Disconnect(false);
                        socket.Dispose();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                _sockets.Clear();
            }
        }
    }
}