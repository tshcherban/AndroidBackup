using System.Security.Cryptography;

namespace FileSync.Common
{
    public class MurmurHash3UnsafeProvider : HashAlgorithm
    {
        private const ulong C1 = 0x87c37b91114253d5;
        private const ulong C2 = 0x4cf5ad432745937f;

        private ulong _h1;
        private ulong _h2;
        private ulong _totalCount;

        public override void Initialize()
        {
            _totalCount = 0;
        }

        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            ComputeHashImpl(array, ibStart, cbSize);
        }

        protected override unsafe byte[] HashFinal()
        {
            // finalization
            _h1 ^= _totalCount;
            _h2 ^= _totalCount;

            _h1 += _h2;
            _h2 += _h1;

            _h1 = FMix64(_h1);
            _h2 = FMix64(_h2);

            _h1 += _h2;
            _h2 += _h1;

            var ret = new byte[16];

            fixed (byte* pret = ret)
            {
                var ulpret = (ulong*) pret;

                ulpret[0] = Reverse(_h1);
                ulpret[1] = Reverse(_h2);
            }

            return ret;
        }

        private unsafe void ComputeHashImpl(byte[] buffer, int offset, int count)
        {
            var nblocks = count / 16;

            _totalCount += (ulong) count;

            fixed (byte* pbuffer = buffer)
            {
                var pinput = pbuffer + offset;
                var body = (ulong*) pinput;

                ulong k1;
                ulong k2;

                for (var i = 0; i < nblocks; i++)
                {
                    k1 = body[i * 2];
                    k2 = body[i * 2 + 1];

                    k1 *= C1;
                    k1 = (k1 << 31) | (k1 >> (64 - 31)); // ROTL64(k1, 31);
                    k1 *= C2;
                    _h1 ^= k1;

                    _h1 = (_h1 << 27) | (_h1 >> (64 - 27)); // ROTL64(h1, 27);
                    _h1 += _h2;
                    _h1 = _h1 * 5 + 0x52dce729;

                    k2 *= C2;
                    k2 = (k2 << 33) | (k2 >> (64 - 33)); // ROTL64(k2, 33);
                    k2 *= C1;
                    _h2 ^= k2;

                    _h2 = (_h2 << 31) | (_h2 >> (64 - 31)); // ROTL64(h2, 31);
                    _h2 += _h1;
                    _h2 = _h2 * 5 + 0x38495ab5;
                }

                k1 = 0;
                k2 = 0;

                var tail = pinput + nblocks * 16;
                switch (count & 15)
                {
                    case 15:
                        k2 ^= (ulong) tail[14] << 48;
                        goto case 14;
                    case 14:
                        k2 ^= (ulong) tail[13] << 40;
                        goto case 13;
                    case 13:
                        k2 ^= (ulong) tail[12] << 32;
                        goto case 12;
                    case 12:
                        k2 ^= (ulong) tail[11] << 24;
                        goto case 11;
                    case 11:
                        k2 ^= (ulong) tail[10] << 16;
                        goto case 10;
                    case 10:
                        k2 ^= (ulong) tail[9] << 8;
                        goto case 9;
                    case 9:
                        k2 ^= tail[8];
                        k2 *= C2;
                        k2 = (k2 << 33) | (k2 >> (64 - 33)); // ROTL64(k2, 33);
                        k2 *= C1;
                        _h2 ^= k2;
                        goto case 8;
                    case 8:
                        k1 ^= (ulong) tail[7] << 56;
                        goto case 7;
                    case 7:
                        k1 ^= (ulong) tail[6] << 48;
                        goto case 6;
                    case 6:
                        k1 ^= (ulong) tail[5] << 40;
                        goto case 5;
                    case 5:
                        k1 ^= (ulong) tail[4] << 32;
                        goto case 4;
                    case 4:
                        k1 ^= (ulong) tail[3] << 24;
                        goto case 3;
                    case 3:
                        k1 ^= (ulong) tail[2] << 16;
                        goto case 2;
                    case 2:
                        k1 ^= (ulong) tail[1] << 8;
                        goto case 1;
                    case 1:
                        k1 ^= tail[0];
                        k1 *= C1;
                        k1 = (k1 << 31) | (k1 >> (64 - 31)); // ROTL64(k1, 31);
                        k1 *= C2;
                        _h1 ^= k1;
                        break;
                }
            }
        }

        private static ulong FMix64(ulong k)
        {
            k ^= k >> 33;
            k *= 0xff51afd7ed558ccd;
            k ^= k >> 33;
            k *= 0xc4ceb9fe1a85ec53;
            k ^= k >> 33;

            return k;
        }

        private static ulong Reverse(ulong value)
        {
            return (value & 0x00000000000000FFUL) << 56 | (value & 0x000000000000FF00UL) << 40 |
                   (value & 0x0000000000FF0000UL) << 24 | (value & 0x00000000FF000000UL) << 8 |
                   (value & 0x000000FF00000000UL) >> 8 | (value & 0x0000FF0000000000UL) >> 24 |
                   (value & 0x00FF000000000000UL) >> 40 | (value & 0xFF00000000000000UL) >> 56;
        }
    }
}