using System.Buffers;
using System.Buffers.Binary;

namespace Home.Protocol;

public readonly record struct Header(byte Ver, MsgType Type, Flags Flags, ushort ReqId)
{
    public const int Size = 6;
    public bool IsResponse => (Flags & Flags.Resp) != 0;
    public bool IsError => (Flags & Flags.Err) != 0;

    public void Write(Span<byte> dst)
    {
        dst[0] = Ver;
        dst[1] = (byte)Type;
        dst[2] = (byte)Flags;
        dst[3] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(dst[4..], ReqId);
    }

    public static Header Read(ReadOnlySpan<byte> msg)
    {
        if (msg.Length < Size) throw new ProtoException(ErrorCode.BadRequest, "message shorter than header");
        return new Header(msg[0], (MsgType)msg[1], (Flags)msg[2], BinaryPrimitives.ReadUInt16LittleEndian(msg[4..]));
    }
}

/// <summary>A decoded message: header + typed body (an <see cref="ErrorBody"/> when the ERR flag is set).</summary>
public sealed record Message(Header Header, IProtoBody Body);

public static class Codec
{
    /// <summary>Encodes a message (header + body). RESP flag is set automatically for responses.</summary>
    public static byte[] Encode(IProtoMessage body, ushort reqId, Flags extra = 0)
    {
        var flags = extra | (body.IsResponse ? Flags.Resp : 0);
        return Encode(body.Type, flags, reqId, body);
    }

    public static byte[] Encode(MsgType type, Flags flags, ushort reqId, IProtoBody body)
    {
        var w = new TlvWriter();
        Span<byte> h = stackalloc byte[Header.Size];
        new Header(ProtoInfo.HeaderVersion, type, flags, reqId).Write(h);
        w.Raw(h);
        body.Write(w);
        if (w.Length > ProtoInfo.MaxMessage) throw new ProtoException(ErrorCode.InvalidValue, $"message too large: {w.Length}");
        return w.ToArray();
    }

    public static byte[] EncodeError(MsgType type, ushort reqId, ErrorCode code, string? text = null)
    {
        var body = new ErrorBody { Code = code, Text = Truncate(text, 90) };
        return Encode(type, Flags.Resp | Flags.Err, reqId, body);
    }

    /// <summary>Cuts a string so that its UTF-8 form fits into maxBytes (without splitting characters).</summary>
    public static string? Truncate(string? s, int maxBytes)
    {
        if (s == null || System.Text.Encoding.UTF8.GetByteCount(s) <= maxBytes) return s;
        var n = s.Length;
        while (n > 0 && System.Text.Encoding.UTF8.GetByteCount(s.AsSpan(0, n)) > maxBytes) n--;
        if (n > 0 && char.IsHighSurrogate(s[n - 1])) n--;
        return s[..n];
    }

    public static Message Decode(ReadOnlySpan<byte> msg)
    {
        var h = Header.Read(msg);
        var body = msg[Header.Size..];
        IProtoBody decoded = h.IsError
            ? ErrorBody.Read(body)
            : (h.IsResponse ? Messages.ReadResponse(h.Type, body) : Messages.ReadRequest(h.Type, body))
              ?? throw new ProtoException(ErrorCode.Unsupported, $"unknown message type 0x{(byte)h.Type:X2}");
        return new Message(h, decoded);
    }
}

public static class Crc16
{
    /// <summary>CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF).</summary>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }
}

/// <summary>TCP framing: 'H' 'M' | len u16le | message | crc16 u16le.</summary>
public static class TcpFrame
{
    public const int Overhead = 6;

    public static byte[] Encode(ReadOnlySpan<byte> msg)
    {
        var f = new byte[msg.Length + Overhead];
        f[0] = (byte)'H';
        f[1] = (byte)'M';
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(2), (ushort)msg.Length);
        msg.CopyTo(f.AsSpan(4));
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(4 + msg.Length), Crc16.Compute(msg));
        return f;
    }

    /// <summary>
    /// Tries to take one frame from the buffer. Garbage and frames with a bad CRC are skipped.
    /// On success the buffer is advanced past the frame.
    /// </summary>
    public static bool TryDecode(ref ReadOnlySequence<byte> buffer, out byte[]? msg, int maxMessage = ProtoInfo.MaxMessage)
    {
        msg = null;
        while (true)
        {
            if (buffer.Length < 4) return false;
            Span<byte> head = stackalloc byte[4];
            buffer.Slice(0, 4).CopyTo(head);
            if (head[0] != 'H' || head[1] != 'M')
            {
                buffer = buffer.Slice(1);
                continue;
            }
            int n = BinaryPrimitives.ReadUInt16LittleEndian(head[2..]);
            if (n < Header.Size || n > maxMessage)
            {
                buffer = buffer.Slice(1);
                continue;
            }
            if (buffer.Length < n + Overhead) return false;
            var body = buffer.Slice(4, n).ToArray();
            Span<byte> crcBytes = stackalloc byte[2];
            buffer.Slice(4 + n, 2).CopyTo(crcBytes);
            if (BinaryPrimitives.ReadUInt16LittleEndian(crcBytes) != Crc16.Compute(body))
            {
                buffer = buffer.Slice(1);
                continue;
            }
            buffer = buffer.Slice(n + Overhead);
            msg = body;
            return true;
        }
    }
}

/// <summary>BLE fragmentation: hdr u8 (bit7 FIRST, bit6 LAST, bits0-3 seq) | payload.</summary>
public static class BleFragments
{
    public const byte First = 0x80, Last = 0x40;

    public static IEnumerable<byte[]> Split(byte[] msg, int mtuPayload)
    {
        var chunk = mtuPayload - 1;
        if (chunk < 1) throw new ArgumentOutOfRangeException(nameof(mtuPayload));
        var off = 0;
        var seq = 0;
        do
        {
            var n = Math.Min(chunk, msg.Length - off);
            var f = new byte[n + 1];
            f[0] = (byte)(seq & 0x0F);
            if (off == 0) f[0] |= First;
            if (off + n == msg.Length) f[0] |= Last;
            Array.Copy(msg, off, f, 1, n);
            yield return f;
            off += n;
            seq++;
        } while (off < msg.Length);
    }
}

public sealed class BleReassembler(int maxMessage = ProtoInfo.MaxMessage)
{
    private readonly List<byte> _buf = new();
    private int _nextSeq;
    private bool _active;

    /// <summary>Feeds a fragment; returns the whole message when the last fragment arrives.</summary>
    public byte[]? Feed(ReadOnlySpan<byte> frag)
    {
        if (frag.Length < 1) return null;
        var hdr = frag[0];
        var seq = hdr & 0x0F;
        if ((hdr & BleFragments.First) != 0)
        {
            _buf.Clear();
            _active = true;
        }
        else if (!_active || seq != _nextSeq)
        {
            _active = false;
            return null;
        }
        if (_buf.Count + frag.Length - 1 > maxMessage)
        {
            _active = false;
            return null;
        }
        _buf.AddRange(frag[1..]);
        _nextSeq = (seq + 1) & 0x0F;
        if ((hdr & BleFragments.Last) == 0) return null;
        _active = false;
        return _buf.ToArray();
    }
}
