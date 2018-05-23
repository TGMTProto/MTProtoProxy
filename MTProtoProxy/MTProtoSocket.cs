using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MTProtoProxy
{
    internal class MTProtoSocket : IDisposable
    {
        public IPEndPoint IPEndPoint { get => _ipEndPoint; }
        private readonly IPEndPoint _ipEndPoint;
        private readonly MTProtoPacket _mtprotoPacketTgSocket;
        private readonly MTProtoPacket _mtprotoPacketClientSocket;
        private Socket _tgSocket;
        private Socket _clientSocket;
        private readonly object _lockConnection = new object();
        private readonly object _lockTgSocket = new object();
        private volatile bool _isDisposed;
        public event EventHandler MTProtoSocketDisconnected;
        public MTProtoSocket(in Socket clientSocket)
        {
            _clientSocket = clientSocket;
            _mtprotoPacketTgSocket = new MTProtoPacket();
            _mtprotoPacketClientSocket = new MTProtoPacket();
            _ipEndPoint = (IPEndPoint)_clientSocket.RemoteEndPoint;
        }
        public void StartAsync(in byte[] buffer, in string secret)
        {
            if (_isDisposed)
            {
                Console.WriteLine("MTProtoSocket disposed");
                return;
            }

            _mtprotoPacketClientSocket.Clear();
            _mtprotoPacketClientSocket.SetInitBufferObfuscated2(buffer, secret);


            if (_mtprotoPacketClientSocket.ProtocolType == ProtocolType.None)
            {
                Console.WriteLine("Error in protocol");
                return;
            }
            var dcId = Math.Abs(BitConverter.ToInt16(buffer.SubArray(60, 2), 0));

            _tgSocket = TgSockets.GetSocket(dcId);

            if (_tgSocket != null)
            {
                var randomBuffer = _mtprotoPacketTgSocket.GetInitBufferObfuscated2(_mtprotoPacketClientSocket.ProtocolType);
                _tgSocket.Send(randomBuffer);
                Array.Clear(randomBuffer, 0, randomBuffer.Length);
                StartTGListener();
                StartClientListener();
            }
            else
            {
                MTProtoSocketDisconnected?.Invoke(this, null);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        private Task StartTGListener()
        {
            return Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_tgSocket != null && !_isDisposed)
                        {
                            var buffer = new byte[Constants.BufferSize];
                            var bytesTransferred = _tgSocket.Receive(buffer);
                            if (bytesTransferred == 0)
                            {
                                Console.WriteLine("A connection was closed");
                                break;
                            }
                            else
                            {
                                SendToClientSocket(buffer, bytesTransferred);
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    catch (SocketException ex)
                    {
                        switch (ex.SocketErrorCode)
                        {
                            case SocketError.OperationAborted:
                            case SocketError.Shutdown:
                            case SocketError.Interrupted:
                            case SocketError.ConnectionReset:
                            case SocketError.ConnectionAborted:
                                Console.WriteLine("A connection was closed");
                                break;
                            default:
                                Console.WriteLine(ex);
                                break;
                        }
                        break;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        break;
                    }
                }
                MTProtoSocketDisconnected?.Invoke(this, null);
            });
        }
        private Task StartClientListener()
        {
            return Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_clientSocket != null && !_isDisposed)
                        {
                            var buffer = new byte[Constants.BufferSize];
                            var bytesTransferred = _clientSocket.Receive(buffer);
                            if (bytesTransferred == 0)
                            {
                                Console.WriteLine("A connection was closed");
                                break;
                            }
                            else
                            {
                                SendToTgSocket(buffer, bytesTransferred);
                            }
                        }
                    }
                    catch (SocketException ex)
                    {
                        switch (ex.SocketErrorCode)
                        {
                            case SocketError.OperationAborted:
                            case SocketError.Shutdown:
                            case SocketError.Interrupted:
                            case SocketError.ConnectionReset:
                            case SocketError.ConnectionAborted:
                                Console.WriteLine("A connection was closed");
                                break;
                            default:
                                Console.WriteLine(ex);
                                break;
                        }
                        break;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        break;
                    }
                }
                MTProtoSocketDisconnected?.Invoke(this, null);
            });
        }
        private void SendToTgSocket(in byte[] buffer, in int length)
        {
            try
            {
                if (_tgSocket != null)
                {
                    var decrypt = _mtprotoPacketClientSocket.DecryptObfuscated2(buffer, length);
                    var encrypt = _mtprotoPacketTgSocket.EncryptObfuscated2(decrypt, decrypt.Length);
                    _tgSocket.Send(encrypt);
                    Array.Clear(decrypt, 0, decrypt.Length);
                    Array.Clear(encrypt, 0, encrypt.Length);
                }
                Array.Clear(buffer, 0, buffer.Length);
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
        private void SendToClientSocket(in byte[] buffer, in int length)
        {
            try
            {
                if (_clientSocket != null)
                {
                    var decrypt = _mtprotoPacketTgSocket.DecryptObfuscated2(buffer, length);
                    var encrypt = _mtprotoPacketClientSocket.EncryptObfuscated2(decrypt, decrypt.Length);
                    _clientSocket.Send(encrypt);
                    Array.Clear(decrypt, 0, decrypt.Length);
                    Array.Clear(encrypt, 0, encrypt.Length);
                }
                Array.Clear(buffer, 0, buffer.Length);
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
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
                if (_tgSocket != null)
                {
                    try
                    {
                        _tgSocket.Shutdown(SocketShutdown.Both);
                        _tgSocket.Disconnect(false);
                        _tgSocket.Dispose();
                    }
                    catch (SocketException e)
                    {
                        switch (e.SocketErrorCode)
                        {
                            case SocketError.NotConnected:
                                {
                                    try
                                    {
                                        _tgSocket.Dispose();
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine(ex);
                                    }
                                    Console.WriteLine("A connection was closed(Socket is not connected)");
                                }
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                    finally
                    {
                        _tgSocket = null;
                    }
                }
                if (_clientSocket != null)
                {
                    try
                    {
                        _clientSocket.Shutdown(SocketShutdown.Both);
                        _clientSocket.Disconnect(false);
                        _clientSocket.Dispose();
                    }
                    catch (SocketException e)
                    {
                        switch (e.SocketErrorCode)
                        {
                            case SocketError.NotConnected:
                                {
                                    try
                                    {
                                        _clientSocket.Dispose();
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine(ex);
                                    }
                                    Console.WriteLine("A connection was closed(Socket is not connected)");
                                }
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                    finally
                    {
                        _clientSocket = null;
                    }
                }
            }
        }
    }
}