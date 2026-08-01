using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Demonware.Core.Crypto;

namespace DWServer
{
    public static class DWCrypto
    {
        public static byte[] CalculateInitialVector(uint initialValue) => DwCrypto.TigerIv(initialValue);
        public static byte[] GenerateRandom(int length) => DwCrypto.RandomBytes(length);
        public static byte[] Decrypt(byte[] iv, byte[] key, byte[] data) => DwCrypto.TripleDesDecrypt(iv, key, data);
        public static byte[] Encrypt(byte[] iv, byte[] key, byte[] data) => DwCrypto.TripleDesEncrypt(iv, key, data);
    }
}
