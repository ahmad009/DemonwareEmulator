namespace Demonware.Legacy.Buffers;

public sealed class BitBuffer
{
    private byte[] _bytes;
    private int _curBit;
    private int _maxBit;

    public bool UseDataTypes { get; set; } = true;
    public byte[] Bytes => _bytes;

    public BitBuffer(byte[] data)
    {
        _bytes = data;
        _maxBit = data.Length * 8;
    }

    public bool ReadBytes(int bytes, out byte[] output)
    {
        output = new byte[bytes];
        return Read(bytes * 8, output);
    }

    public bool ReadBoolean(out bool output)
    {
        output = false;
        if (!ReadDataType(1)) return false;
        var b = new byte[1];
        if (!Read(1, b)) return false;
        output = b[0] != 0;
        return true;
    }

    public bool ReadUInt32(out uint output)
    {
        output = 0;
        if (!ReadDataType(8)) return false;
        var b = new byte[4];
        if (!Read(32, b)) return false;
        output = BitConverter.ToUInt32(b, 0);
        return true;
    }

    public bool ReadDataType(byte expected)
    {
        if (!UseDataTypes) return true;
        var actual = new byte[1];
        if (!Read(5, actual)) return false;
        return actual[0] == expected;
    }

    public bool WriteBoolean(bool data) => WriteDataType(1) && Write(1, [(byte)(data ? 1 : 0)]);
    public bool WriteBytes(byte[] data) => Write(data.Length * 8, data);
    public bool WriteUInt32(uint value) => WriteDataType(8) && Write(32, BitConverter.GetBytes(value));
    public bool WriteDataType(byte dataType) => !UseDataTypes || Write(5, [dataType]);

    public bool Read(int bits, byte[] output)
    {
        if (bits == 0 || _curBit + bits > _maxBit) return false;
        var curByte = _curBit >> 3;
        var curOut = 0;
        while (bits > 0)
        {
            var minBit = bits < 8 ? bits : 8;
            var thisByte = (int)_bytes[curByte++];
            var remain = _curBit & 7;
            if (minBit + remain <= 8)
                output[curOut] = (byte)((0xFF >> (8 - minBit)) & (thisByte >> remain));
            else
                output[curOut] = (byte)((0xFF >> (8 - minBit)) & (_bytes[curByte] << (8 - remain) | (thisByte >> remain)));
            curOut++;
            _curBit += minBit;
            bits -= minBit;
        }
        return true;
    }

    public bool Write(int bits, byte[] data)
    {
        if (bits == 0 || data.Length * 8 < bits) return false;
        if (_bytes.Length * 8 < _curBit + bits)
            Array.Resize(ref _bytes, (int)Math.Ceiling((_curBit + bits) / 8.0));

        var bit = bits;
        while (bit > 0)
        {
            var bitPos = _curBit & 7;
            var remBit = 8 - bitPos;
            var thisWrite = bit < remBit ? bit : remBit;
            var mask = (byte)((0xFF >> remBit) | (0xFF << (bitPos + thisWrite)));
            var bytePos = _curBit >> 3;
            var tempByte = (byte)(mask & _bytes[bytePos]);
            var thisBit = (byte)((bits - bit) & 7);
            var thisByte = (bits - bit) >> 3;
            var thisData = data[thisByte];
            var nextByte = (((bits - 1) >> 3) > thisByte) ? data[thisByte + 1] : (byte)0;
            thisData = (byte)((nextByte << (8 - thisBit)) | (thisData >> thisBit));
            _bytes[bytePos] = (byte)(~mask & (thisData << bitPos) | tempByte);
            _curBit += thisWrite;
            bit -= thisWrite;
            if (_maxBit < _curBit) _maxBit = _curBit;
        }
        return true;
    }
}
