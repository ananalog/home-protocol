using System.Buffers;
using Home.Protocol;
using Xunit;

namespace Home.Protocol.Tests;

public class VectorTests
{
    static string VectorDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "schema.yaml"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "testdata", "vectors");
    }

    public static IEnumerable<object[]> Vectors() =>
        File.ReadAllLines(Path.Combine(VectorDir(), "index.txt"))
            .Where(l => l.Length > 0)
            .Select(l => new object[] { l.Split(' ')[0] });

    [Theory]
    [MemberData(nameof(Vectors))]
    public void DecodeEncodeRoundTrip(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(VectorDir(), name + ".bin"));
        var msg = Codec.Decode(bytes);
        var again = Codec.Encode(msg.Header.Type, msg.Header.Flags, msg.Header.ReqId, msg.Body);
        Assert.Equal(bytes, again);
    }

    [Fact]
    public void HelloFields()
    {
        var msg = Codec.Decode(File.ReadAllBytes(Path.Combine(VectorDir(), "hello_req.bin")));
        var hello = Assert.IsType<HelloReq>(msg.Body);
        Assert.Equal("a1b2c3d4e5f6", hello.DeviceId);
        Assert.Equal(Model.Co2Egg, hello.Model);
        Assert.Equal("Спальня CO2", hello.Name);
        Assert.True(hello.PendingVerify);
    }

    [Fact]
    public void ErrorBody()
    {
        var msg = Codec.Decode(File.ReadAllBytes(Path.Combine(VectorDir(), "error_resp.bin")));
        Assert.True(msg.Header.IsError);
        var err = Assert.IsType<ErrorBody>(msg.Body);
        Assert.Equal(ErrorCode.InvalidValue, err.Code);
    }

    [Fact]
    public void TcpFrameSkipsGarbageAndSplitsStream()
    {
        var a = Codec.Encode(new PingReq { TimeMs = 1 }, 1);
        var b = Codec.Encode(new PingReq { TimeMs = 2 }, 2);
        var bad = TcpFrame.Encode(a);
        bad[^1] ^= 0xFF; // broken crc
        var stream = new byte[] { 1, 2, (byte)'H' }.Concat(bad).Concat(TcpFrame.Encode(a)).Concat(TcpFrame.Encode(b)).ToArray();
        var seq = new ReadOnlySequence<byte>(stream);
        Assert.True(TcpFrame.TryDecode(ref seq, out var m1));
        Assert.Equal(a, m1);
        Assert.True(TcpFrame.TryDecode(ref seq, out var m2));
        Assert.Equal(b, m2);
        Assert.False(TcpFrame.TryDecode(ref seq, out _));
    }

    [Fact]
    public void BleRoundTrip()
    {
        var msg = File.ReadAllBytes(Path.Combine(VectorDir(), "ota_data_req.bin"));
        var rx = new BleReassembler();
        byte[]? got = null;
        foreach (var f in BleFragments.Split(msg, 244)) got = rx.Feed(f) ?? got;
        Assert.Equal(msg, got);
    }

    [Fact]
    public void UnknownTags()
    {
        Assert.Throws<ProtoException>(() => SetReq.Read(new byte[] { 0x01, 0x01, 0x05, 0xFF, 0x01, 0x00 }));
        Assert.Equal((byte)5, SetReq.Read(new byte[] { 0x01, 0x01, 0x05, 0x7F, 0x01, 0x00 }).Point);
    }

    [Fact]
    public void StringLimitIsEnforced()
    {
        var p = new PointDef { Id = 1, Key = new string('x', 25) };
        Assert.Throws<ProtoException>(() => Codec.Encode(new DescribeResp { Points = { p } }, 1));
    }

    [Fact]
    public void CrcKnownValue() => Assert.Equal(0x29B1, Crc16.Compute("123456789"u8));
}
