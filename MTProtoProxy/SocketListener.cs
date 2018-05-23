using System;
using System.Net.Sockets;
using System.Net;

namespace MTProtoProxy
{
    internal class SocketListener
    {
        private Socket _listenSocket;
        public void StartListen(in IPEndPoint ipEndPoint, in int backlog)
        {
            var socket = new Socket(ipEndPoint.AddressFamily, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);

            try
            {
                socket.Bind(ipEndPoint);
                socket.Listen(int.MaxValue);
                _listenSocket = socket;
            }
            catch (Exception e)
            {
                socket.Dispose();
                Console.WriteLine(e);
            }
        }

        public Socket Accept()
        {
            return _listenSocket.Accept();
        }

        public void Stop()
        {
            _listenSocket.Dispose();
            _listenSocket = null;
        }
    }
}