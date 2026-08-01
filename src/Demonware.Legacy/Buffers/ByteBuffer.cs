using System.Text;

namespace Demonware.Legacy.Buffers;

public sealed class ByteBuffer
{
    private byte[] _bytes;
    private int _cur;

    public bool UseDataTypes { get; set; } = true;
    public byte[] Bytes => _bytes;
    public int Remaining => _bytes.Length - _cur;

    public ByteBuffer(byte[] data) { _bytes = data; _cur = 0; }

    public void WriteRaw(byte[] data)
    {
        if (_cur + data.Length > _bytes.Length) Array.Resize(ref _bytes, _cur + data.Length);
        Array.Copy(data, 0, _bytes, _cur, data.Length);
        _cur += data.Length;
    }

    public void WriteType(byte t) { if (UseDataTypes) WriteRaw([t]); }
    public void Write(uint v) { WriteType(8); WriteRaw(BitConverter.GetBytes(v)); }
    public void Write(int v) { WriteType(7); WriteRaw(BitConverter.GetBytes(v)); }
    public void Write(ulong v) { WriteType(0xA); WriteRaw(BitConverter.GetBytes(v)); }
    public void Write(long v) { WriteType(9); WriteRaw(BitConverter.GetBytes(v)); }
    public void Write(byte v) { WriteType(3); WriteRaw([v]); }
    public void Write(float v) { WriteType(13); WriteRaw(BitConverter.GetBytes(v)); }
    public void Write(string v) { WriteType(16); WriteRaw(Encoding.ASCII.GetBytes(v ?? "")); WriteRaw([0]); }
    public void WriteBlob(byte[] data) { WriteType(0x13); Write((uint)data.Length); WriteRaw(data); }
}
