using SpeedLink.Hardware;
namespace SpeedLink.Tests;

public class FtdiConnectionTests
{
    [Fact]
    public void latency_is_restored_and_handle_closed_once()
    {
        var channel = new Channel();
        var serial = new FtdiSerialConnection(channel);
        Assert.Equal(1, channel.Latency);
        serial.Dispose();
        serial.Dispose();
        Assert.Equal(16, channel.Latency);
        Assert.True(serial.LatencyRestored);
        Assert.Null(serial.CleanupWarning);
        Assert.False(serial.IsOpen);
        Assert.Equal(1, channel.Closes);
    }
    [Fact]
    public void uart_error_latches_and_blocks_further_io()
    {
        var channel = new Channel();
        using var serial = new FtdiSerialConnection(channel);
        channel.Flags = 0x6200;
        Assert.Throws<SpeedLink.Core.TransportException>(() => serial.Read(new byte[1], 0, 1));
        channel.Flags = 0x6000;
        Assert.Throws<SpeedLink.Core.TransportException>(() => serial.Write([1]));
        Assert.Equal(0, channel.ReadCalls);
        Assert.Empty(channel.Output);
        Assert.Single(serial.DriverErrors);
    }
    [Fact]
    public void partial_write_stops_before_any_remaining_bytes()
    {
        var channel = new Channel { ShortWrite = true };
        using var serial = new FtdiSerialConnection(channel);
        Assert.Throws<IOException>(() => serial.Write(new byte[1500]));
        Assert.Equal(new[] { 512 }, channel.WriteSizes);
        Assert.Equal(511, channel.Output.Count);
    }
    [Fact]
    public void bounded_write_chunks_preserve_byte_order()
    {
        var channel = new Channel();
        using var serial = new FtdiSerialConnection(channel);
        var data = Enumerable.Range(0, 1100).Select(i => (byte)i).ToArray();
        Assert.Equal(data.Length, serial.Write(data));
        Assert.Equal(new[] { 512, 512, 76 }, channel.WriteSizes);
        Assert.Equal(data, channel.Output);
    }
    [Fact]
    public void invalid_read_count_is_rejected()
    {
        var channel = new Channel { InvalidReadCount = true };
        using var serial = new FtdiSerialConnection(channel);
        Assert.Throws<IOException>(() => serial.Read(new byte[8], 0, 8));
    }
    [Fact]
    public void setup_failure_restores_latency_and_closes()
    {
        var channel = new Channel { FailLowLatency = true };
        Assert.Throws<IOException>(() => new FtdiSerialConnection(channel));
        Assert.False(channel.IsOpen);
        Assert.Equal(16, channel.Latency);
        Assert.Equal(1, channel.Closes);
    }
    [Fact]
    public void removal_during_cleanup_does_not_leave_handle_open()
    {
        var channel = new Channel();
        var serial = new FtdiSerialConnection(channel);
        channel.FailRestore = true;
        serial.Dispose();
        Assert.False(serial.IsOpen);
        Assert.False(serial.LatencyRestored);
        Assert.NotNull(serial.CleanupWarning);
        Assert.Equal(1, channel.Closes);
    }
    [Fact]
    public void read_bounds_are_checked_before_driver_io()
    {
        var channel = new Channel();
        using var serial = new FtdiSerialConnection(channel);
        Assert.Throws<ArgumentOutOfRangeException>(() => serial.Read(new byte[2], 1, 2));
        Assert.Equal(0, channel.ReadCalls);
    }
    [Fact]
    public void close_failure_is_reported_after_latency_restoration()
    {
        var channel = new Channel { FailClose = true };
        var serial = new FtdiSerialConnection(channel);
        serial.Dispose();
        Assert.True(serial.LatencyRestored);
        Assert.False(serial.IsOpen);
        Assert.Contains("Close failed", serial.CleanupWarning);
        serial.Dispose();
        Assert.Equal(1, channel.Closes);
    }

    private sealed class Channel : IFtdiChannel
    {
        public bool IsOpen { get; private set; } = true;
        public int BaudRate { get; set; } = 115200;
        private byte latency = 16;
        public byte Latency
        {
            get => latency;
            set
            {
                if ((FailLowLatency && value == 1) || (FailRestore && value == 16))
                    throw new IOException("Device unavailable.");
                latency = value;
            }
        }
        public uint Flags = 0x6000;
        public uint LineStatus => Flags;
        public int BytesToRead => 0;
        public int BytesToWrite => 0;
        public int ReadCalls, Closes;
        public bool ShortWrite, InvalidReadCount, FailLowLatency, FailRestore, FailClose;
        public List<byte> Output = [];
        public List<int> WriteSizes = [];
        public int Read(byte[] data, int offset, int count)
        {
            ReadCalls++;
            return InvalidReadCount ? count + 1 : 0;
        }
        public int Write(byte[] data, int offset, int count)
        {
            WriteSizes.Add(count);
            int actual = ShortWrite ? count - 1 : count;
            Output.AddRange(data.AsSpan(offset, actual).ToArray());
            return actual;
        }
        public void DiscardInput()
        {
        }
        public void Dispose()
        {
            Closes++;
            IsOpen = false;
            if (FailClose)
                throw new IOException("Close failed.");
        }
    }
}
