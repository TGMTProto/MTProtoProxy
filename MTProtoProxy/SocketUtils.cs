using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MTProtoProxy
{
    internal static class SocketUtils
    {
        public static Task ConnectAsync(this Socket socket, in EndPoint endPoint)
        {
            var connectSource = new TaskCompletionSource<bool>();
            var cts = new CancellationTokenSource();
            cts.Token.Register(() => connectSource.TrySetCanceled());
            cts.CancelAfter(2000);

            void ConnectHandler(object sender, SocketAsyncEventArgs eventArgs)
            {
                var error = eventArgs.SocketError;
                eventArgs.Completed -= ConnectHandler;
                eventArgs.Dispose();

                if (error != SocketError.Success)
                {
                    connectSource.TrySetException(new SocketException((int)error));
                    return;
                }

                connectSource.TrySetResult(true);
            }

            var e = new SocketAsyncEventArgs()
            {
                RemoteEndPoint = endPoint
            };
            e.Completed += ConnectHandler;

            try
            {
                if (!socket.ConnectAsync(e))
                {
                    ConnectHandler(null, e);
                }
            }
            catch (Exception ex)
            {
                connectSource.TrySetException(ex);
            }

            return connectSource.Task;
        }
        public static Task DisconnectAsync(this Socket socket)
        {
            socket.Shutdown(SocketShutdown.Both);

            var disconnectSource = new TaskCompletionSource<bool>();

            void DisconnectHandler(object sender, SocketAsyncEventArgs eventArgs)
            {
                var error = eventArgs.SocketError;
                eventArgs.Completed -= DisconnectHandler;
                eventArgs.Dispose();

                if (error != SocketError.Success)
                {
                    disconnectSource.TrySetException(new SocketException((int)error));
                    return;
                }

                disconnectSource.TrySetResult(true);
            }
            var e = new SocketAsyncEventArgs();
            e.Completed += DisconnectHandler;
            e.DisconnectReuseSocket = true;

            try
            {
                if (!socket.DisconnectAsync(e))
                {
                    DisconnectHandler(null, e);
                }
            }
            catch (Exception ex)
            {
                disconnectSource.TrySetException(ex);
            }

            return disconnectSource.Task;
        }
        public static Task SendAsync(this Socket socket, in byte[] buffer, in int offset, in int count)
        {
            var sendSource = new TaskCompletionSource<bool>();

            void SendHandler(object sender, SocketAsyncEventArgs eventArgs)
            {
                eventArgs.Completed -= SendHandler;
                eventArgs.Dispose();

                sendSource.TrySetResult(true);
            }

            var e = new SocketAsyncEventArgs();
            e.SetBuffer(buffer, offset, count);
            e.Completed += SendHandler;

            try
            {
                if (!socket.SendAsync(e))
                {
                    SendHandler(null, e);
                }

            }
            catch (Exception ex)
            {
                sendSource.TrySetException(ex);
            }

            return sendSource.Task;
        }
        public static ValueTask<int> ReceiveAsync(this Socket socket, in byte[] buffer, in int offset, in int count)
        {
            var receiveSource = new TaskCompletionSource<int>();

            void ReceiveHandler(object sender, SocketAsyncEventArgs eventArgs)
            {
                eventArgs.Completed -= ReceiveHandler;
                eventArgs.Dispose();

                receiveSource.TrySetResult(eventArgs.BytesTransferred);
            }

            var e = new SocketAsyncEventArgs();
            e.SetBuffer(buffer, offset, count);
            e.Completed += ReceiveHandler;

            try
            {
                if (!socket.ReceiveAsync(e))
                {
                    ReceiveHandler(null, e);
                }
            }
            catch (Exception ex)
            {
                receiveSource.SetException(ex);
            }

            return new ValueTask<int>(receiveSource.Task);
        }
        public static ValueTask<Socket> AcceptSocketAsync(this Socket socket)
        {
            var acceptSource = new TaskCompletionSource<Socket>();

            void AcceptHandler(object sender, SocketAsyncEventArgs eventArgs)
            {
                var error = eventArgs.SocketError;
                eventArgs.Completed -= AcceptHandler;
                eventArgs.Dispose();

                if (error != SocketError.Success)
                {
                    acceptSource.TrySetException(new SocketException((int)error));
                    return;
                }

                acceptSource.TrySetResult(eventArgs.AcceptSocket);
            }

            var e = new SocketAsyncEventArgs();
            e.Completed += AcceptHandler;

            try
            {
                if (!socket.AcceptAsync(e))
                {
                    AcceptHandler(null, e);
                }
            }
            catch (Exception ex)
            {
                acceptSource.TrySetException(ex);
            }

            return new ValueTask<Socket>(acceptSource.Task);
        }
        public static bool IsConnected(this Socket socket)
        {
            try
            {
                return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}