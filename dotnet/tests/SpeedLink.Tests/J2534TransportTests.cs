using SpeedLink.Core;
using SpeedLink.J2534;

namespace SpeedLink.Tests;

public class J2534TransportTests
{
    private static J2534ReadTransport Link(Channel channel)
    {
        var result = new J2534ReadTransport(channel, TimeSpan.FromMilliseconds(15), TimeSpan.FromMilliseconds(30));
        result.Initialize();
        return result;
    }
    public static IEnumerable<object[]> Forbidden => new[]
    {
        "1085", "B100B200", "3400008000000F8000", "36616263", "37", "1101", "04", "3B00",
        "22F1A0", "23001000000001", "23000FFFFF0002", "23000080000FFF", "23000000000000", "27021234", ""
    }.Select(hex => new object[] { hex });

    [Theory, MemberData(nameof(Forbidden))]
    public void programming_and_invalid_reads_are_blocked_before_any_driver_io(string hex)
    {
        var channel = new Channel();
        using var link = Link(channel);
        Assert.Throws<TransportException>(() => link.Exchange(Convert.FromHexString(hex)));
        Assert.Equal(0, channel.Clears);
        Assert.Empty(channel.Writes);
        Assert.Equal(0, channel.Reads);
    }
    [Fact]
    public void uses_physical_500k_addressing_and_closes_once()
    {
        var channel = new Channel();
        var link = Link(channel);
        Assert.Equal((500000u, 0x7E0u, 0x7E8u), channel.Configuration);
        Assert.Equal("hardware", link.Mode);
        Assert.Equal(3968, link.ReadChunkSize);
        Assert.Contains("firmware test", link.Adapter);
        link.Dispose();
        link.Dispose();
        Assert.Equal(1, channel.Closes);
    }
    [Fact]
    public void stock_logger_query_and_nrc_keep_the_channel_synchronized()
    {
        var channel = new Channel();
        channel.Replies.Enqueue(new(0x7E8, 0, [0x62, 0, 0x0C, 0x1F, 0x40]));
        channel.Replies.Enqueue(new(0x7E8, 0, [0x7F, 0x22, 0x31]));
        channel.Replies.Enqueue(new(0x7E8, 0, [0x62, 0, 0x0C, 0x1F, 0x40]));
        using var link = Link(channel);
        var response = link.Exchange([0x22, 0, 0x0C]);
        Assert.Equal(2000, LoggerDefinition.Speed6.Decode("engine_rpm", response));
        Assert.Throws<NegativeResponseException>(() => link.Exchange([0x22, 0x17, 0x46]));
        Assert.Equal(response, link.Exchange([0x22, 0, 0x0C]));
        Assert.Equal(3, channel.Writes.Count);
    }
    [Fact]
    public void pending_and_driver_notifications_do_not_cause_a_resend()
    {
        var channel = new Channel();
        channel.Replies.Enqueue(new(0x7E8, 2, []));
        channel.Replies.Enqueue(new(0x7E0, 1, [1, 12]));
        channel.Replies.Enqueue(new(0x7E8, 8, []));
        channel.Replies.Enqueue(new(0x123, 0, [0]));
        channel.Replies.Enqueue(new(0x7E8, 0, [0x7F, 1, 0x78]));
        channel.Replies.Enqueue(new(0x7E8, 0, [0x41, 12, 0, 0]));
        using var link = Link(channel);
        Assert.Equal(new byte[] { 0x41, 12, 0, 0 }, link.Exchange([1, 12]));
        Assert.Single(channel.Writes);
    }
    [Fact]
    public void timeout_latches_failure_without_retry()
    {
        var channel = new Channel();
        using var link = Link(channel);
        Assert.Contains("No request was resent", Assert.Throws<TransportException>(() => link.Exchange([1, 12])).Message);
        Assert.Throws<TransportException>(() => link.Exchange([1, 12]));
        Assert.Single(channel.Writes);
        Assert.Equal(1, channel.Clears);
    }
    [Fact]
    public void negative_identity_response_can_be_followed_by_calibration_read()
    {
        var channel = new Channel();
        channel.Replies.Enqueue(new(0x7E8, 0, [0x7F, 9, 0x11]));
        channel.Replies.Enqueue(new(0x7E8, 0, [0x49, 4, 1, .. "UNKNOWN"u8.ToArray()]));
        using var link = Link(channel);
        var identity = ReadWorkflow.Identify(link);
        Assert.Null(identity.Vin);
        Assert.Equal("UNKNOWN", identity.Calibration);
        Assert.NotNull(identity.VinStatus);
        Assert.Equal(2, channel.Writes.Count);
    }
    [Theory]
    [InlineData(0x10, "410C0000")]
    [InlineData(0, "420C0000")]
    [InlineData(0, "7F2378")]
    [InlineData(0, "")]
    public void malformed_reply_latches_failure(int status, string hex)
    {
        var channel = new Channel();
        channel.Replies.Enqueue(new(0x7E8, (uint)status, Convert.FromHexString(hex)));
        using var link = Link(channel);
        Assert.Throws<TransportException>(() => link.Exchange([1, 12]));
        Assert.Throws<TransportException>(() => link.Exchange([1, 12]));
        Assert.Single(channel.Writes);
    }
    [Fact]
    public void pending_deadline_is_bounded_without_retry()
    {
        var channel = new Channel();
        channel.Replies.Enqueue(new(0x7E8, 0, [0x7F, 1, 0x78]));
        using var link = Link(channel);
        Assert.Throws<TransportException>(() => link.Exchange([1, 12]));
        Assert.Single(channel.Writes);
    }
    [Fact]
    public void oversized_driver_pdu_is_rejected()
    {
        var channel = new Channel();
        channel.Replies.Enqueue(new(0x7E8, 0, new byte[4096]));
        using var link = Link(channel);
        Assert.Throws<TransportException>(() => link.Exchange([1, 12]));
        Assert.Single(channel.Writes);
    }
    [Fact]
    public void initialization_failure_releases_driver_and_cannot_be_reused()
    {
        var channel = new Channel { FailConnect = true };
        var link = new J2534ReadTransport(channel);
        Assert.Throws<TransportException>(link.Initialize);
        Assert.Equal(1, channel.Closes);
        Assert.Throws<ObjectDisposedException>(link.Initialize);
        link.Dispose();
        Assert.Equal(1, channel.Closes);
    }
    [Fact]
    public void driver_failure_is_not_retried_or_reused()
    {
        var channel = new Channel { FailRead = true };
        using var link = Link(channel);
        Assert.Throws<IOException>(() => link.Exchange([1, 12]));
        Assert.Throws<TransportException>(() => link.Exchange([1, 12]));
        Assert.Single(channel.Writes);
    }

    private sealed class Channel : IJ2534Channel
    {
        public Queue<J2534Frame> Replies = new();
        public List<byte[]> Writes = [];
        public int Clears, Reads, Closes;
        public bool FailConnect, FailRead;
        public (uint, uint, uint) Configuration;
        public string? CleanupWarning => null;
        public J2534Version Open() => new("test", "test", "04.04");
        public void Connect(uint bitrate, uint requestId, uint responseId)
        {
            Configuration = (bitrate, requestId, responseId);
            if (FailConnect)
                throw new TransportException("Channel setup failed.");
        }
        public void ClearReceive() => Clears++;
        public void Write(byte[] request) => Writes.Add(request.ToArray());
        public J2534Frame? Read(int timeoutMilliseconds)
        {
            Reads++;
            if (FailRead)
                throw new IOException("Driver removed.");
            if (Replies.TryDequeue(out var result))
                return result;
            Thread.Sleep(1);
            return null;
        }
        public void Dispose() => Closes++;
    }
}
