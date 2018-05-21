using System;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;

namespace MTProtoProxy
{
    internal enum ProtocolType
    {
        None = 0,
        AbridgedObfuscated2 = 1,
        IntermediateObfuscated2 = 2
    }
    internal class MTProtoPacket : IDisposable
    {
        private byte[] _encryptKey;
        private byte[] _encryptIv;
        private byte[] _decryptKey;
        private byte[] _decryptIv;
        private byte[] _encryptCountBuf;
        private uint _encryptNum;
        private byte[] _decryptCountBuf;
        private uint _decryptNum;
        private ProtocolType _protocolType;
        private ICryptoTransform _cryptoTransformEncrypt;
        private ICryptoTransform _cryptoTransformDecrypt;
        public ProtocolType ProtocolType { get => _protocolType; }
        public void SetInitBufferObfuscated2(in byte[] randomBuffer, in string secret)
        {
            var reversed = randomBuffer.SubArray(8, 48);
            Array.Reverse(reversed);
            var key = randomBuffer.SubArray(8, 32);
            var keyReversed = reversed.SubArray(0, 32);
            var binSecret = ArrayUtils.HexToByteArray(secret);
            key = SHA256Helper.ComputeHashsum(ArrayUtils.Combine(key, binSecret));
            keyReversed = SHA256Helper.ComputeHashsum(ArrayUtils.Combine(keyReversed, binSecret));

            _encryptKey = keyReversed;
            _encryptIv = reversed.SubArray(32, 16);
            _decryptKey = key;
            _decryptIv = randomBuffer.SubArray(40, 16);

            _cryptoTransformEncrypt = AesHelper.CreateEncryptorFromAes(_encryptKey);
            _cryptoTransformDecrypt = AesHelper.CreateEncryptorFromAes(_decryptKey);

            var decryptedBuffer = DecryptObfuscated2(randomBuffer);
            for (var i = 56; i < decryptedBuffer.Length; i++)
            {
                randomBuffer[i] = decryptedBuffer[i];
            }

            byte[] protocolResult = randomBuffer.SubArray(56, 4);
            if (protocolResult[0] == 0xef && protocolResult[1] == 0xef && protocolResult[2] == 0xef && protocolResult[3] == 0xef)
            {
                _protocolType = ProtocolType.AbridgedObfuscated2;
            }
            else if (protocolResult[0] == 0xee && protocolResult[1] == 0xee && protocolResult[2] == 0xee && protocolResult[3] == 0xee)
            {
                _protocolType = ProtocolType.IntermediateObfuscated2;
            }
            else
            {
                _protocolType = ProtocolType.None;
            }
            Array.Clear(reversed, 0, reversed.Length);
            Array.Clear(key, 0, key.Length);
            Array.Clear(keyReversed, 0, keyReversed.Length);
            Array.Clear(binSecret, 0, binSecret.Length);
            Array.Clear(decryptedBuffer, 0, decryptedBuffer.Length);
            Array.Clear(protocolResult, 0, protocolResult.Length);
        }
        public byte[] GetInitBufferObfuscated2(in ProtocolType protocolType)
        {
            _protocolType = protocolType;

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
                    switch (_protocolType)
                    {
                        case ProtocolType.AbridgedObfuscated2:
                            buffer[56] = buffer[57] = buffer[58] = buffer[59] = 0xef;
                            break;
                        case ProtocolType.IntermediateObfuscated2:
                            buffer[56] = buffer[57] = buffer[58] = buffer[59] = 0xee;
                            break;
                        case ProtocolType.None:
                            return null;
                    }
                    break;
                }
            }
            var keyIvEncrypt = buffer.SubArray(8, 48);
            _encryptKey = keyIvEncrypt.SubArray(0, 32);
            _encryptIv = keyIvEncrypt.SubArray(32, 16);

            Array.Reverse(keyIvEncrypt);
            _decryptKey = keyIvEncrypt.SubArray(0, 32);
            _decryptIv = keyIvEncrypt.SubArray(32, 16);

            _cryptoTransformEncrypt = AesHelper.CreateEncryptorFromAes(_encryptKey);
            _cryptoTransformDecrypt = AesHelper.CreateEncryptorFromAes(_decryptKey);

            var encryptedBuffer = EncryptObfuscated2(buffer);
            for (var i = 56; i < encryptedBuffer.Length; i++)
            {
                buffer[i] = encryptedBuffer[i];
            }
            Array.Clear(keyIvEncrypt, 0, keyIvEncrypt.Length);
            Array.Clear(encryptedBuffer, 0, encryptedBuffer.Length);
            return buffer;
        }
        public byte[] EncryptObfuscated2(in byte[] data)
        {
            if (_encryptCountBuf == null)
            {
                _encryptCountBuf = new byte[16];
                _encryptNum = 0;
            }
            return AesCtr128Encrypt(data, ref _encryptIv, ref _encryptCountBuf, ref _encryptNum);
        }
        public byte[] DecryptObfuscated2(in byte[] data)
        {
            if (_decryptCountBuf == null)
            {
                _decryptCountBuf = new byte[16];
                _decryptNum = 0;
            }
            return AesCtr128Decrypt(data, ref _decryptIv, ref _decryptCountBuf, ref _decryptNum);
        }
        public byte[] CreatePacketObfuscated2(in byte[] payLoad)
        {
            byte[] packet = null;
            switch (_protocolType)
            {
                case ProtocolType.AbridgedObfuscated2:
                    packet = CreatePacketAbridged(payLoad);
                    break;
                case ProtocolType.IntermediateObfuscated2:
                    packet = CreatePacketIntermediate(payLoad);
                    break;
            }
            return EncryptObfuscated2(packet);
        }
        private byte[] CreatePacketAbridged(in byte[] payLoad)
        {
            var length = payLoad.Length / 4;
            byte[] bytes;
            if (length < 0x7F)
            {
                bytes = ArrayUtils.Combine(new[] { (byte)length }, payLoad);
            }
            else
            {
                byte[] bytesLength = new byte[3];
                Buffer.BlockCopy(BitConverter.GetBytes(length), 0, bytesLength, 0, 3);
                bytes = ArrayUtils.Combine(new byte[] { 0x7F }, bytesLength, payLoad);
                Array.Clear(bytesLength, 0, bytesLength.Length);
            }
            return bytes;
        }
        private byte[] CreatePacketIntermediate(in byte[] payLoad)
        {
            using (var memoryStream = new MemoryStream())
            {
                using (var binaryWriter = new BinaryWriter(memoryStream))
                {
                    binaryWriter.Write(payLoad.Length);
                    binaryWriter.Write(payLoad);
                    return memoryStream.ToArray();
                }
            }
        }
        private byte[] AesCtr128Encrypt(in byte[] input, ref byte[] ivec, ref byte[] ecountBuf, ref uint num)
        {
            if (_encryptKey == null)
            {
                return null;
            }

            var output = new byte[input.Length];
            uint number = num;

            for (uint i = 0; i < input.Length; i++)
            {
                if (number == 0)
                {
                    var ecountBuffer = _cryptoTransformEncrypt.TransformFinalBlock(ivec, 0, ivec.Length);
                    Buffer.BlockCopy(ecountBuffer, 0, ecountBuf, 0, ecountBuf.Length);
                    Array.Clear(ecountBuffer, 0, ecountBuffer.Length);
                    Array.Reverse(ivec);
                    var bigInteger = new BigInteger(ArrayUtils.Combine(ivec, new byte[] { 0x00 }));
                    bigInteger++;
                    var bigIntegerArray = bigInteger.ToByteArray();
                    ivec = new byte[16];
                    Buffer.BlockCopy(bigIntegerArray, 0, ivec, 0, Math.Min(bigIntegerArray.Length, ivec.Length));
                    Array.Reverse(ivec);
                    Array.Clear(bigIntegerArray, 0, bigIntegerArray.Length);
                }
                output[i] = (byte)(input[i] ^ ecountBuf[number]);
                number = (number + 1) % 16;
            }
            num = number;
            return output;
        }
        private byte[] AesCtr128Decrypt(in byte[] input, ref byte[] ivdc, ref byte[] dcountBuf, ref uint num)
        {
            if (_decryptKey == null)
            {
                return null;
            }

            var output = new byte[input.Length];
            uint number = num;

            for (uint i = 0; i < input.Length; i++)
            {
                if (number == 0)
                {
                    var ecountBuffer = _cryptoTransformDecrypt.TransformFinalBlock(ivdc, 0, ivdc.Length);
                    Buffer.BlockCopy(ecountBuffer, 0, dcountBuf, 0, dcountBuf.Length);
                    Array.Clear(ecountBuffer, 0, ecountBuffer.Length);
                    Array.Reverse(ivdc);
                    var bigInteger = new BigInteger(ArrayUtils.Combine(ivdc, new byte[] { 0x00 }));
                    bigInteger++;
                    var bigIntegerArray = bigInteger.ToByteArray();
                    ivdc = new byte[16];
                    Buffer.BlockCopy(bigIntegerArray, 0, ivdc, 0, Math.Min(bigIntegerArray.Length, ivdc.Length));
                    Array.Reverse(ivdc);
                    Array.Clear(bigIntegerArray, 0, bigIntegerArray.Length);
                }
                output[i] = (byte)(input[i] ^ dcountBuf[number]);
                number = (number + 1) % 16;
            }
            num = number;
            return output;
        }
        public void Clear()
        {
            _encryptKey = null;
            _encryptIv = null;
            _decryptKey = null;
            _decryptIv = null;
            _encryptCountBuf = null;
            _encryptNum = 0;
            _decryptCountBuf = null;
            _decryptNum = 0;
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(in bool isDisposing)
        {
            if (!isDisposing)
            {
                return;
            }
            Clear();
            if (_cryptoTransformDecrypt != null)
            {
                try
                {
                    _cryptoTransformDecrypt.Dispose();
                    _cryptoTransformDecrypt = null;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
            if (_cryptoTransformEncrypt != null)
            {
                try
                {
                    _cryptoTransformEncrypt.Dispose();
                    _cryptoTransformEncrypt = null;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }
    }
}