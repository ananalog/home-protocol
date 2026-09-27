using System.Buffers.Binary;
using System.Text;

namespace Home.Protocol;

/// <summary>Body of a message or a nested struct.</summary>
public interface IProtoBody
{
    void Write(TlvWriter w);
}

/// <summary>A request or response body bound to a message type.</summary>
public interface IProtoMessage : IProtoBody
{
    MsgType Type { get; }
    bool IsResponse { get; }
}

public sealed class ProtoException(ErrorCode code, string message) : Exception(message)
{
    public ErrorCode Code { get; } = code;
}

/// <summary>Growable TLV writer. Encoding is canonical: minimal varints, fields in the order written.</summary>
public sealed class TlvWriter(int capacity = 256)
{
    private byte[] _buf = new byte[Math.Max(capacity, 16)];
    private int _len;

    public int Length => _len;
    public ReadOnlySpan<byte> Span => _buf.AsSpan(0, _len);
    public byte[] ToArray() => _buf.AsSpan(0, _len).ToArray();

    private void Ensure(int more)
    {
        if (_len + more <= _buf.Length) return;
        Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + more));
    }

    public void Raw(ReadOnlySpan<byte> data)
    {
        Ensure(data.Length);
        data.CopyTo(_buf.AsSpan(_len));
        _len += data.Length;
    }

    public static int VarintSize(uint v)
    {
        var n = 1;
        while (v >= 0x80) { v >>= 7; n++; }
        return n;
    }

    private static int PutVarint(Span<byte> dst, uint v)
    {
        var n = 0;
        while (v >= 0x80) { dst[n++] = (byte)(v | 0x80); v >>= 7; }
        dst[n++] = (byte)v;
        return n;
    }

    private void Head(int tag, int len)
    {
        Span<byte> tmp = stackalloc byte[6];
        tmp[0] = (byte)tag;
        var k = 1 + PutVarint(tmp[1..], (uint)len);
        Raw(tmp[..k]);
    }

    private void Le(int tag, ulong v, int n)
    {
        Span<byte> tmp = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(tmp, v);
        Head(tag, n);
        Raw(tmp[..n]);
    }

    public void U8(int tag, byte v) => Le(tag, v, 1);
    public void U16(int tag, ushort v) => Le(tag, v, 2);
    public void U32(int tag, uint v) => Le(tag, v, 4);
    public void U64(int tag, ulong v) => Le(tag, v, 8);
    public void I32(int tag, int v) => Le(tag, (uint)v, 4);
    public void I64(int tag, long v) => Le(tag, (ulong)v, 8);
    public void F32(int tag, float v) => Le(tag, BitConverter.SingleToUInt32Bits(v), 4);
    public void Bool(int tag, bool v) => Le(tag, v ? 1u : 0u, 1);

    public void Str(int tag, string v, int max)
    {
        var bytes = Encoding.UTF8.GetBytes(v);
        if (bytes.Length > max) throw new ProtoException(ErrorCode.InvalidValue, $"string longer than {max} bytes: '{v}'");
        Head(tag, bytes.Length);
        Raw(bytes);
    }

    public void Bytes(int tag, ReadOnlySpan<byte> v, int max)
    {
        if (v.Length > max) throw new ProtoException(ErrorCode.InvalidValue, $"bytes longer than {max}");
        Head(tag, v.Length);
        Raw(v);
    }

    public void Struct(int tag, IProtoBody body)
    {
        Ensure(2);
        _buf[_len++] = (byte)tag;
        _buf[_len++] = 0; // reserved length byte
        var start = _len;
        body.Write(this);
        var n = _len - start;
        var vs = VarintSize((uint)n);
        if (vs > 1)
        {
            Ensure(vs - 1);
            Array.Copy(_buf, start, _buf, start + vs - 1, n);
            _len += vs - 1;
        }
        PutVarint(_buf.AsSpan(start - 1), (uint)n);
    }
}

/// <summary>Reads TLV fields one by one. Throws <see cref="ProtoException"/> on malformed input.</summary>
public ref struct TlvReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _pos = 0;

    public bool Next(out int tag, out ReadOnlySpan<byte> value)
    {
        value = default;
        tag = 0;
        if (_pos >= _data.Length) return false;
        tag = _data[_pos++];
        uint n = 0;
        var shift = 0;
        while (true)
        {
            if (_pos >= _data.Length || shift > 28) throw new ProtoException(ErrorCode.BadRequest, "bad varint");
            var b = _data[_pos++];
            n |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        if (n > _data.Length - _pos) throw new ProtoException(ErrorCode.BadRequest, "field exceeds message");
        value = _data.Slice(_pos, (int)n);
        _pos += (int)n;
        return true;
    }
}

/// <summary>Scalar decoders; sizes must match exactly.</summary>
public static class Tlv
{
    private static ReadOnlySpan<byte> Want(ReadOnlySpan<byte> v, int n) =>
        v.Length == n ? v : throw new ProtoException(ErrorCode.BadRequest, $"expected {n} bytes, got {v.Length}");

    public static byte U8(ReadOnlySpan<byte> v) => Want(v, 1)[0];
    public static ushort U16(ReadOnlySpan<byte> v) => BinaryPrimitives.ReadUInt16LittleEndian(Want(v, 2));
    public static uint U32(ReadOnlySpan<byte> v) => BinaryPrimitives.ReadUInt32LittleEndian(Want(v, 4));
    public static ulong U64(ReadOnlySpan<byte> v) => BinaryPrimitives.ReadUInt64LittleEndian(Want(v, 8));
    public static int I32(ReadOnlySpan<byte> v) => BinaryPrimitives.ReadInt32LittleEndian(Want(v, 4));
    public static long I64(ReadOnlySpan<byte> v) => BinaryPrimitives.ReadInt64LittleEndian(Want(v, 8));
    public static float F32(ReadOnlySpan<byte> v) => BitConverter.UInt32BitsToSingle(U32(v));
    public static bool Bool(ReadOnlySpan<byte> v) => U8(v) != 0;
    public static string Str(ReadOnlySpan<byte> v) => Encoding.UTF8.GetString(v);
    public static byte[] Bytes(ReadOnlySpan<byte> v) => v.ToArray();

    public static void Unknown(int tag)
    {
        if ((tag & 0x80) != 0) throw new ProtoException(ErrorCode.Unsupported, $"unknown critical tag {tag & 0x7F}");
    }
}
