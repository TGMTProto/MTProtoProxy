using PCLCrypto;
using System;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;
using System.Threading.Tasks;

namespace MTProtoProxy
{
    internal class MTProtoPacket
    {
        private byte[] _encryptKey;
        private byte[] _encryptIv;
        private byte[] _decryptKey;
        private byte[] _decryptIv;
        private byte[] _encryptCountBuf;
        private uint _encryptNum;
        private byte[] _decryptCountBuf;
        private uint _decryptNum;
        private readonly object _object = new object();
        public void SetInitBufferObfuscated2(byte[] buffer, byte[] reversed, byte[] key, byte[] keyRev)
        {
            _encryptKey = keyRev;
            _encryptIv = reversed.SubArray(32, 16);
            _decryptKey = key;
            _decryptIv = buffer.SubArray(40, 16);
        }
        public byte[] GetInitBufferObfuscated2()
        {
            var buffer = new byte[64];
            var random = new Random();
            while (true)
            {
                random.NextBytes(buffer);

                var val = (buffer[3] << 24) | (buffer[2] << 16) | (buffer[1] << 8) | (buffer[0]);
                var val2 = (buffer[7] << 24) | (buffer[6] << 16) | (buffer[5] << 8) | (buffer[4]);
                if (buffer[0] != 0xef
                    && val != 0x44414548
                    && val != 0x54534f50
                    && val != 0x20544547
                    && val != 0x4954504f
                    && val != 0xeeeeeeee
                    && val2 != 0x00000000)
                {
                    buffer[56] = buffer[57] = buffer[58] = buffer[59] = 0xef;
                    break;
                }
            }
            var keyIvEncrypt = buffer.SubArray(8, 48);
            _encryptKey = keyIvEncrypt.SubArray(0, 32);
            _encryptIv = keyIvEncrypt.SubArray(32, 16);

            Array.Reverse(keyIvEncrypt);
            _decryptKey = keyIvEncrypt.SubArray(0, 32);
            _decryptIv = keyIvEncrypt.SubArray(32, 16);

            var encryptedBuffer = EncryptObfuscated2(buffer);
            for (var i = 56; i < encryptedBuffer.Length; i++)
            {
                buffer[i] = encryptedBuffer[i];
            }

            return buffer;
        }
        public byte[] EncryptObfuscated2(byte[] data)
        {
            if (_encryptCountBuf == null)
            {
                _encryptCountBuf = new byte[16];
                _encryptNum = 0;
            }
            return AESCTR128Encrypt(data, _encryptKey, ref _encryptIv, ref _encryptCountBuf, ref _encryptNum);
        }
        public byte[] DecryptObfuscated2(byte[] data)
        {
            if (_decryptCountBuf == null)
            {
                _decryptCountBuf = new byte[16];
                _decryptNum = 0;
            }
            return AESCTR128Encrypt(data, _decryptKey, ref _decryptIv, ref _decryptCountBuf, ref _decryptNum);
        }
        public byte[] CreatePacketObfuscated2(byte[] payLoad)
        {
            lock (_object)
            {
                var packet = CreatePacketAbridged(payLoad);
                packet = EncryptObfuscated2(packet);
                return packet;
            }
        }
        private byte[] CreatePacketAbridged(byte[] payLoad)
        {
            var length = payLoad.Length / 4;
            byte[] bytes;
            if (length < 0x7F)
            {
                bytes = new[] { (byte)length }.Concat(payLoad).ToArray();
            }
            else
            {
                byte[] bytesLength = new byte[3];
                Array.Copy(BitConverter.GetBytes(length), 0, bytesLength, 0, 3);
                bytes = new byte[] { 0x7F }.Concat(bytesLength).Concat(payLoad).ToArray();
            }
            return bytes;
        }
        public byte[] AESCTR128Encrypt(byte[] input, byte[] key, ref byte[] ivec, ref byte[] ecountBuf, ref uint num)
        {
            var output = new byte[input.Length];
            uint number = num;

            var provider = WinRTCrypto.SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithm.AesEcb);
            var keySymmetric = provider.CreateSymmetricKey(key);

            for (uint i = 0; i < input.Length; i++)
            {
                if (number == 0)
                {
                    var ivecBuffer = ivec;
                    var ecountBuffer = WinRTCrypto.CryptographicEngine.Encrypt(keySymmetric, ivecBuffer);
                    WinRTCrypto.CryptographicBuffer.CopyToByteArray(ecountBuffer, out ecountBuf);
                    Array.Reverse(ivec);
                    var bigInteger = new BigInteger(ArrayUtils.Combine(ivec, new byte[] { 0x00 }));
                    bigInteger++;
                    var bigIntegerArray = bigInteger.ToByteArray();
                    var bytes = new byte[16];
                    Buffer.BlockCopy(bigIntegerArray, 0, bytes, 0, Math.Min(bigIntegerArray.Length, bytes.Length));
                    Array.Reverse(bytes);
                    ivec = bytes;
                }
                output[i] = (byte)(input[i] ^ ecountBuf[number]);
                number = (number + 1) % 16;
            }
            num = number;
            return output;
        }
        public Task SendInitBufferObfuscated2Async(Socket socket)
        {
            lock (_object)
            {
                _encryptCountBuf = null;
                _decryptCountBuf = null;
                var initBufferObfuscated2 = GetInitBufferObfuscated2();
                return socket.SendAsync(initBufferObfuscated2, 0, initBufferObfuscated2.Length);
            }
        }
    }
}
