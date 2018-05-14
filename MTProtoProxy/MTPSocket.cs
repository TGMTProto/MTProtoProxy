using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MTProtoProxy
{
    internal class MTPSocket : IDisposable
    {
        private readonly Socket _socketClient;
        private readonly Socket _socketServer;
        private readonly MTProtoPacket _mtprotoPacketClient;
        private readonly MTProtoPacket _mtprotoPacketServer;
        private readonly object _lockSend = new object();
        private readonly object _lockdis = new object();
        private CancellationTokenSource _senderCancellationTokenSource;
        private Task _senderTask;
        private CancellationTokenSource _receiverCancellationTokenSource;
        private Task _receiverTask;
        private bool _isDisposed;
        public event EventHandler Disconnected;
        
        public MTPSocket(Socket socketClient, Socket socketServer, MTProtoPacket mtprotoPacketClient, MTProtoPacket mtprotoPacketServer)
        {
            _socketClient = socketClient;
            _socketServer = socketServer;
            _mtprotoPacketClient = mtprotoPacketClient;
            _mtprotoPacketServer = mtprotoPacketServer;
        }
        public void Start()
        {
            _senderCancellationTokenSource = new CancellationTokenSource();
            _senderTask = SenderSocketAsync(_senderCancellationTokenSource.Token);
            _receiverCancellationTokenSource = new CancellationTokenSource();
            _receiverTask = StartReceiverAsync(_receiverCancellationTokenSource.Token);
        }
        private async ValueTask<bool> ReceiveAsync(byte[] buffer, Socket socket)
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
        private async ValueTask<byte[]> GetPacketBytesAsync(Socket socket, MTProtoPacket mtprotoPacket)
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
                var resultLengthBytes = await ReceiveAsync(lenBytes, socket).ConfigureAwait(false);
                if (resultLengthBytes)
                {
                    return null;
                }
                lenBytes = mtprotoPacket.DecryptObfuscated2(lenBytes);
                lengthBytes = BitConverter.ToInt32(lenBytes.Concat(new byte[] { 0x00 }).ToArray(), 0) << 2;
            }
            var packetBytes = new byte[lengthBytes];
            var resultpacket = await ReceiveAsync(packetBytes, socket).ConfigureAwait(false);
            if (resultpacket)
            {
                return null;
            }
            packetBytes = mtprotoPacket.DecryptObfuscated2(packetBytes);
            return packetBytes;
        }

        private Task SenderSocketAsync(CancellationToken token)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            SpinWait.SpinUntil(() => _socketClient.Available != 0);
                            var result = await GetPacketBytesAsync(_socketClient, _mtprotoPacketClient).ConfigureAwait(false);
                            if (result == null)
                            {
                                break;
                            }
                            var packetEnc = _mtprotoPacketServer.CreatePacketObfuscated2(result);
                            await _socketServer.SendAsync(packetEnc, 0, packetEnc.Length).ConfigureAwait(false);
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
                            break;
                        }
                        await Task.Delay(1);
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
                }
                lock (_lockdis)
                {
                    if (!_isDisposed)
                    {
                        Disconnected?.Invoke(this, null);
                    }
                }
            }, token);
        }
        private Task StartReceiverAsync(CancellationToken token)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var packetBytes = await GetPacketBytesAsync(_socketServer, _mtprotoPacketServer).ConfigureAwait(false);
                            if (packetBytes == null)
                            {
                                break;
                            }
                            var packet = _mtprotoPacketClient.CreatePacketObfuscated2(packetBytes);

                            lock (_lockSend)
                            {
                                _socketClient.SendAsync(packet, 0, packet.Length).GetAwaiter().GetResult();
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
                            break;
                        }
                        await Task.Delay(1);
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
                }
                lock (_lockdis)
                {
                    if (!_isDisposed)
                    {
                        Disconnected?.Invoke(this, null);
                    }
                }
            }, token);
        }
        protected virtual void Dispose(bool disposing)
        {
            try
            {
                if (!disposing)
                {
                    try
                    {
                        if (_receiverCancellationTokenSource != null)
                        {
                            _receiverCancellationTokenSource.Cancel();
                            _receiverCancellationTokenSource = null;
                        }
                        if (_receiverTask != null)
                        {
                            try
                            {
                                if (!_receiverTask.IsCompleted)
                                {
                                    _receiverTask.Wait(1000);
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
                            }
                            if (_receiverTask.IsCompleted)
                            {
                                _receiverTask.Dispose();
                            }
                            else
                            {
                                Console.WriteLine("Receiver task did not completed on transport disposing.");
                            }
                            _receiverTask = null;
                        }
                        if (_senderCancellationTokenSource != null)
                        {
                            _senderCancellationTokenSource.Cancel();
                            _senderCancellationTokenSource = null;
                        }
                        if (_senderTask != null)
                        {
                            try
                            {
                                if (!_senderTask.IsCompleted)
                                {
                                    _senderTask.Wait(1000);
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
                            }
                            if (_senderTask.IsCompleted)
                            {
                                _senderTask.Dispose();
                            }
                            else
                            {
                                Console.WriteLine("Receiver task did not completed on transport disposing.");
                            }
                            _senderTask = null;
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
                    }
                    try
                    {
                        _socketClient.Dispose();
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
                    _socketServer.Dispose();
                    _mtprotoPacketClient.Clear();
                    _mtprotoPacketServer.Clear();
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
            }
            _isDisposed = true;
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
