using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace FileSync.Android
{
    internal struct XxHashIntermediate32
    {
        public ulong Length;

        public uint Seed;

        public uint[] Values;

        public int MemorySize;

        public byte[] Payload;
    }

    internal struct XxHashIntermediate64
    {
        public ulong Length;

        public uint Seed;

        public ulong[] Values;

        public int MemorySize;

        public byte[] Payload;
    }

    public sealed class XxHash64 : HashAlgorithm
    {
        private XxHash64()
        {
        }

        private static readonly IList<ulong> Primes64 =
            new[]
            {
                11400714785074694791UL,
                14029467366897019727UL,
                1609587929392839161UL,
                9650029242287828579UL,
                2870177450012600261UL
            };

        private XxHashIntermediate64 _intermediate;

        public override int HashSize => 64;

        public new static XxHash64 Create(string hashName = "System.Security.Cryptography.xxHash64")
        {
            var ret = new XxHash64();
            ret.Initialize();
            return ret;
        }

        public override void Initialize()
        {
            _intermediate.Seed = 0;
            _intermediate.Values = new[]
            {
                Primes64[0] + Primes64[1],
                Primes64[1],
                0UL,
                0 - Primes64[0]
            };
            _intermediate.Length = 0;
            _intermediate.MemorySize = 0;
            _intermediate.Payload = new byte[32];
        }

        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            array.ForEachSlice(ibStart, cbSize, 32,
                (dataGroup, pos, len) =>
                {
                    for (var x = pos; x < pos + len; x += 32)
                    {
                        for (var y = 0; y < 4; ++y)
                        {
                            var val = _intermediate.Values[y];
                            val += UnSafeGetUInt64(dataGroup, x + (y << 3)) * Primes64[1];
                            val = val.RotateLeft(31);
                            val *= Primes64[0];
                            _intermediate.Values[y] = val;
                        }
                    }

                    _intermediate.Length += (ulong) len;
                },
                (remainder, pos, len) =>
                {
                    Buffer.BlockCopy(remainder, pos, _intermediate.Payload, 0, len);
                    _intermediate.Length += (ulong) len;
                    _intermediate.MemorySize = len;
                });
        }

        protected override byte[] HashFinal()
        {
            ulong h;
            if (_intermediate.Length >= 32)
            {
                h = _intermediate.Values[0].RotateLeft(1) +
                    _intermediate.Values[1].RotateLeft(7) +
                    _intermediate.Values[2].RotateLeft(12) +
                    _intermediate.Values[3].RotateLeft(18);

                for (var index = 0; index < _intermediate.Values.Length; index++)
                {
                    var t = _intermediate.Values[index];

                    var val = t;
                    val *= Primes64[1];
                    val = val.RotateLeft(31);
                    val *= Primes64[0];

                    h ^= val;
                    h = (h * Primes64[0]) + Primes64[3];
                }
            }
            else
            {
                h = _intermediate.Seed + Primes64[4];
            }

            h += _intermediate.Length;
            if (_intermediate.MemorySize > 0)
            {
                for (var x = 0; x < _intermediate.MemorySize >> 3; ++x)
                {
                    h ^= (UnSafeGetUInt64(_intermediate.Payload, x << 3) * Primes64[1]).RotateLeft(31) * Primes64[0];
                    h = (h.RotateLeft(27) * Primes64[0]) + Primes64[3];
                }

                if ((_intermediate.MemorySize & 7) >= 4)
                {
                    h ^= UnSafeGetUInt32(_intermediate.Payload, _intermediate.MemorySize - (_intermediate.MemorySize & 7)) * Primes64[0];
                    h = (h.RotateLeft(23) * Primes64[1]) + Primes64[2];
                }

                for (var x = _intermediate.MemorySize - (_intermediate.MemorySize & 3); x < _intermediate.MemorySize; ++x)
                {
                    h ^= _intermediate.Payload[x] * Primes64[4];
                    h = h.RotateLeft(11) * Primes64[0];
                }
            }

            h ^= h >> 33;
            h *= Primes64[1];
            h ^= h >> 29;
            h *= Primes64[2];
            h ^= h >> 32;

            return UnSafeGetBytes(h);
        }

        private static unsafe ulong UnSafeGetUInt64(byte[] bytes, int index)
        {
            fixed (byte* pointer = &bytes[index])
            {
                return *(ulong*) pointer;
            }
        }

        private static unsafe uint UnSafeGetUInt32(byte[] bytes, int index)
        {
            fixed (byte* pointer = &bytes[index])
            {
                return *(uint*) pointer;
            }
        }

        private static unsafe byte[] UnSafeGetBytes(ulong value)
        {
            var bytes = new byte[sizeof(ulong)];
            fixed (byte* pointer = bytes)
            {
                *(ulong*) pointer = value;
            }

            return bytes;
        }
    }

    internal static class ByteArrayExtensions
    {
        public static void ForEachSlice(this byte[] data, int startIndex, int count, int sliceSize,
            Action<byte[], int, int> action, Action<byte[], int, int> remainderAction)
        {
            if (sliceSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(sliceSize));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var remainderLength = count & (sliceSize - 1);
            if (count - remainderLength > 0)
                action(data, startIndex, count - remainderLength);
            if (remainderAction != null && remainderLength > 0)
                remainderAction(data, startIndex + count - remainderLength, remainderLength);
        }

        public static uint RotateLeft(this uint operand, int shiftCount)
        {
            shiftCount &= 0x1f;
            return (operand << shiftCount) | (operand >> (32 - shiftCount));
        }

        public static ulong RotateLeft(this ulong operand, int shiftCount)
        {
            shiftCount &= 0x3f;
            return (operand << shiftCount) | (operand >> (64 - shiftCount));
        }
    }

    public sealed class XxHash32 : HashAlgorithm
    {
        private XxHash32()
        {
        }

        private static readonly IList<uint> Primes32 =
            new[] {
                2654435761U,
                2246822519U,
                3266489917U,
                668265263U,
                374761393U
            };

        private XxHashIntermediate32 _intermediate;

        /// <summary>
        /// Gets the size, in bits, of the computed hash code.
        /// </summary>
        /// <returns>
        /// The size, in bits, of the computed hash code.
        /// </returns>
        public override int HashSize => 32;

        public new static XxHash32 Create(string hashName = "System.Security.Cryptography.xxHash32")
        {
            var ret = new XxHash32();
            ret.Initialize();
            return ret;
        }

        /// <summary>
        /// Initializes an implementation of the <see cref="T:System.Security.Cryptography.HashAlgorithm"/> class.
        /// </summary>
        public override void Initialize()
        {
            _intermediate.Seed = 0;
            _intermediate.Values = new[]
            {
                Primes32[0] + Primes32[1],
                Primes32[1],
                0U,
                0 - Primes32[0]
            };
            _intermediate.Length = 0;
            _intermediate.MemorySize = 0;
            _intermediate.Payload = new byte[16];
        }

        /// <summary>
        /// When overridden in a derived class, routes data written to the object into the hash algorithm for computing the hash.
        /// </summary>
        /// <param name="array">The input to compute the hash code for. </param><param name="ibStart">The offset into the byte array from which to begin using data. </param><param name="cbSize">The number of bytes in the byte array to use as data. </param>
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            array.ForEachSlice(ibStart, cbSize, 16,
                (dataGroup, pos, len) =>
                {
                    for (int x = pos; x < pos + len; x += 16)
                    {
                        for (var y = 0; y < 4; ++y)
                        {
                            _intermediate.Values[y] += BitConverter.ToUInt32(dataGroup, x + (y << 2)) * Primes32[1];
                            _intermediate.Values[y] = _intermediate.Values[y].RotateLeft(13);
                            _intermediate.Values[y] *= Primes32[0];
                        }
                    }
                    _intermediate.Length += (ulong)len;
                },
                (remainder, pos, len) =>
                {
                    Buffer.BlockCopy(remainder, pos, _intermediate.Payload, 0, len);
                    _intermediate.Length += (ulong)len;
                    _intermediate.MemorySize = len;
                });
        }

        /// <summary>
        /// When overridden in a derived class, finalizes the hash computation after the last data is processed by the cryptographic stream object.
        /// </summary>
        /// <returns>
        /// The computed hash code.
        /// </returns>
        protected override byte[] HashFinal()
        {
            uint h;
            if (_intermediate.Length >= 16)
            {
                h = _intermediate.Values[0].RotateLeft(1) +
                    _intermediate.Values[1].RotateLeft(7) +
                    _intermediate.Values[2].RotateLeft(12) +
                    _intermediate.Values[3].RotateLeft(18);
            }
            else
            {
                h = _intermediate.Seed + Primes32[4];
            }
            h += (uint)_intermediate.Length;
            if (_intermediate.MemorySize > 0)
            {
                for (var x = 0; x < _intermediate.MemorySize >> 2; ++x)
                {
                    h += BitConverter.ToUInt32(_intermediate.Payload, x << 2) * Primes32[2];
                    h = h.RotateLeft(17) * Primes32[3];
                }

                for (var x = _intermediate.MemorySize - (_intermediate.MemorySize & 3); x < _intermediate.MemorySize; ++x)
                {
                    h += _intermediate.Payload[x] * Primes32[4];
                    h = h.RotateLeft(11) * Primes32[0];
                }
            }

            h ^= h >> 15;
            h *= Primes32[1];
            h ^= h >> 13;
            h *= Primes32[2];
            h ^= h >> 16;

            return BitConverter.GetBytes(h);
        }
    }
}