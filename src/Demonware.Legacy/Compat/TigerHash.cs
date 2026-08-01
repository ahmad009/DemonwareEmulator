using System;

namespace DWServer
{
    public sealed class TigerHash : IDisposable
    {
        private readonly Demonware.Core.Crypto.TigerHash _inner = new();
        public byte[] ComputeHash(byte[] buffer) => _inner.ComputeHash(buffer);
        public void Initialize() => _inner.Initialize();
        public void Clear() => _inner.Initialize();
        public void Dispose() => _inner.Dispose();
    }
}
