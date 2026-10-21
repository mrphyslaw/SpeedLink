using System.Text;
using SpeedLink.Core;
namespace SpeedLink.Tests;

public class ParserTests
{
    [Fact] public void test_single_and_echo() => Assert.Equal(Fixture.Hex("5087"), DiagnosticProtocol.ParseElm("10 87\r\n50 87\r>", "1087").Single());
    [Fact]
    public void test_multiline_wrap_padding()
    {
        var data = Enumerable.Range(0, 256).Select(i => (byte)i).Concat(new byte[] { 0x63 }).ToArray();
        Assert.Equal(data, DiagnosticProtocol.ParseElm(Fixture.Multiline(data)).Single());
        Assert.Equal(Fixture.Hex("4902014142434445"), DiagnosticProtocol.ParseElm("008\r0:49020141424344\r1:45000000000000\r>").Single());
    }
    [Fact]
    public void test_bad_messages_rejected()
    {
        foreach (var value in new[] { "NO DATA>", "BUFFER FULL>", "CAN ERROR>", "?>", "008\r0:49020141424344>", "008\r1:49020141424344>", "008\r0:49020141424344\r0:45>", "508>", "5087F>", "0:49020141424344>", "008\r0:GG>", "008\r0:4902014142434445>" })
            Assert.Throws<TransportException>(() => DiagnosticProtocol.ParseElm(value));
    }
    [Fact] public void test_pending_then_final() => Assert.Equal(Fixture.Hex("5087"), DiagnosticProtocol.FinalResponse(Fixture.Hex("1087"), [Fixture.Hex("7F1078"), Fixture.Hex("5087")]));
    [Fact]
    public void test_pending_without_final_or_bad_negative()
    {
        foreach (var packets in new[] { new[] { "7F1078" }, new[] { "7F" }, new[] { "7F2778" }, new[] { "5087", "5087" }, new[] { "5087", "7F1078" } })
            Assert.Throws<TransportException>(() => DiagnosticProtocol.FinalResponse(Fixture.Hex("1087"), packets.Select(Fixture.Hex).ToArray()));
    }
    [Fact]
    public void test_negative_response_code()
    {
        var error = Assert.Throws<NegativeResponseException>(() => DiagnosticProtocol.FinalResponse(Fixture.Hex("2701"), [Fixture.Hex("7F2735")]));
        Assert.Equal(0x35, error.Code);
    }
    [Fact]
    public void test_live_programming_blocked_before_io()
    {
        var serial = new FakeSerial();
        using var link = new ElmTransport(serial, new TestClock());
        link.Initialize();
        int before = serial.Writes.Count;
        foreach (var value in new[] { "1085", "B100B200", "340000000000000000", "3674657374", "37", "1101", "3B00", "04", "23000FFFFF0002" })
            Assert.Throws<TransportException>(() => link.Exchange(Fixture.Hex(value)));
        Assert.Equal(before, serial.Writes.Count);
    }
    [Fact]
    public void test_adapter_init_and_multiframe_diagnostics()
    {
        var serial = new FakeSerial();
        var link = new ElmTransport(serial, new TestClock());
        link.Initialize();
        var response = link.Exchange(Fixture.Hex("0902"));
        Assert.Equal("JM1GG12L061000001", Encoding.ASCII.GetString(response.AsSpan(3)));
        Assert.Equal("0902\r", Encoding.ASCII.GetString(serial.Writes[^1]));
        link.Dispose();
        Assert.False(serial.IsOpen);
    }
    [Fact]
    public void test_missing_prompt_invalidates_connection_no_retry()
    {
        var serial = new FakeSerial { NoPrompt = true };
        using var link = new ElmTransport(serial, new TestClock());
        Assert.Throws<TransportException>(() => link.Command("ATI", 0));
        Assert.Throws<TransportException>(() => link.Command("ATI", 0));
        Assert.Single(serial.Writes);
    }
}
public class VLinkerTests
{
    private static (VLinkerReadTransport Link, FakeSerial Serial) Connect()
    {
        var serial = new FakeSerial { Segmented = true };
        var link = new VLinkerReadTransport(serial, new TestClock());
        link.Initialize();
        return (link, serial);
    }
    [Fact]
    public void test_initialize_long_read_and_restore_baud()
    {
        var (link, serial) = Connect();
        var response = link.Exchange(Fixture.Hex("23000480000F80"));
        Assert.Equal(new byte[] { 0x63 }.Concat(Enumerable.Range(0, 3968).Select(i => (byte)(i % 256))).ToArray(), response);
        Assert.Equal(2000000, link.Baud);
        link.Dispose();
        Assert.Equal(115200, link.Baud);
        Assert.False(serial.IsOpen);
    }
    [Fact]
    public void test_all_programming_and_out_of_profile_reads_blocked_before_io()
    {
        var (link, serial) = Connect();
        using (link)
        {
            int before = serial.Writes.Count;
            foreach (var value in new[] { "1085", "B100B200", "340000000000000000", "36616263", "37", "1101", "04", "22F1A0", "23000080000FFF", "23000FFFFF0002" })
                Assert.Throws<TransportException>(() => link.Exchange(Fixture.Hex(value)));
            Assert.Equal(before, serial.Writes.Count);
        }
    }
    [Fact]
    public void stock_logger_queries_use_segmented_framing_and_nrc_does_not_desynchronize()
    {
        var (link, serial) = Connect();
        using (link)
        {
            serial.ResponseOverride = request => request[1] == 0x17 ? [0x7F, 0x22, 0x31] : [0x62, 0, 0x0C, 0x1F, 0x40];
            int before = serial.Writes.Count;
            var response = link.Exchange([0x22, 0, 0x0C]);
            Assert.Equal(2000, LoggerDefinition.Speed6.Decode("engine_rpm", response));
            Assert.Throws<NegativeResponseException>(() => link.Exchange([0x22, 0x17, 0x46]));
            Assert.Equal(response, link.Exchange([0x22, 0, 0x0C]));
            Assert.Equal(before + 6, serial.Writes.Count);
            Assert.False(link.Failed);
        }
    }
    [Fact]
    public void test_error_invalidates_connection_and_does_not_retry()
    {
        var (link, serial) = Connect();
        using (link)
        {
            serial.BadReply = true;
            Assert.Throws<TransportException>(() => link.Exchange(Fixture.Hex("010C")));
            int before = serial.Writes.Count;
            Assert.Throws<TransportException>(() => link.Exchange(Fixture.Hex("010C")));
            Assert.Equal(before, serial.Writes.Count);
        }
    }
    [Fact]
    public void test_wrong_adapter_is_rejected()
    {
        var serial = new FakeSerial { Firmware = "other device" };
        using var link = new VLinkerReadTransport(serial, new TestClock());
        Assert.Throws<TransportException>(link.Initialize);
        Assert.False(link.Ready);
    }
}
