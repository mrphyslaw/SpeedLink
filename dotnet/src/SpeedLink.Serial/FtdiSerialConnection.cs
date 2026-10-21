using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using SpeedLink.Core;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace SpeedLink.Hardware;

internal interface IFtdiChannel : IDisposable
{
    bool IsOpen
    {
        get;
    }
    int BaudRate
    {
        get; set;
    }
    byte Latency
    {
        get; set;
    }
    uint LineStatus
    {
        get;
    }
    int BytesToRead
    {
        get;
    }
    int BytesToWrite
    {
        get;
    }
    int Read(byte[] data, int offset, int count);
    int Write(byte[] data, int offset, int count);
    void DiscardInput();
}

/// <summary>Bounded, exclusive access through the installed FTDI D2XX driver.</summary>
/// <remarks>Uses FTDI's public API. No vendor application or driver is bundled.</remarks>
public sealed class FtdiSerialConnection : ISerialConnection
{
    public const string Backend = "FTDI D2XX / SafeHandle";
    private readonly IFtdiChannel channel;
    private readonly List<string> errors = [];
    private string? fault;
    private bool disposed;
    public byte OriginalLatency
    {
        get;
    }
    public bool LatencyRestored
    {
        get; private set;
    }
    public string? CleanupWarning
    {
        get; private set;
    }
    public long Writes
    {
        get; private set;
    }
    public string[] DriverErrors => errors.ToArray();
    public bool IsOpen => !disposed && channel.IsOpen;
    public int BaudRate
    {
        get => channel.BaudRate; set => channel.BaudRate = value;
    }

    public FtdiSerialConnection(string port, int baud) : this(new NativeFtdiChannel(port, baud)) { }

    internal FtdiSerialConnection(IFtdiChannel channel)
    {
        this.channel = channel;
        bool captured = false;
        try
        {
            OriginalLatency = channel.Latency;
            captured = true;
            channel.Latency = 1;
            if (channel.Latency != 1)
                throw new IOException("FTDI latency readback did not match.");
            DiscardInput();
        }
        catch
        {
            try
            {
                if (captured)
                    channel.Latency = OriginalLatency;
            }
            finally { channel.Dispose(); }
            throw;
        }
    }

    private void CheckLine()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (fault is not null)
            throw new TransportException(fault);
        // D2XX returns modem status in the low byte and UART line status above it.
        var flags = channel.LineStatus & 0x1E00u;
        if (flags == 0)
            return;
        var label = $"FTDI line error 0x{flags:X}";
        errors.Add(label);
        fault = label + ". Reconnect before continuing.";
        throw new TransportException(fault);
    }

    public int BytesToRead
    {
        get
        {
            CheckLine();
            return channel.BytesToRead;
        }
    }

    public int Read(byte[] data, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(data);
        if ((uint)offset > (uint)data.Length || (uint)count > (uint)(data.Length - offset))
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0)
            return 0;
        CheckLine();
        int actual = channel.Read(data, offset, count);
        CheckLine();
        if ((uint)actual > (uint)count)
            throw new IOException("Invalid FTDI read count.");
        return actual;
    }

    public int Write(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        CheckLine();
        Writes++;
        int total = 0;
        while (total < data.Length)
        {
            int count = Math.Min(512, data.Length - total);
            int actual = channel.Write(data, total, count);
            if (actual != count)
                throw new IOException("Incomplete FTDI write. No further bytes were sent.");
            total += actual;
        }
        return total;
    }

    public void Flush()
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            CheckLine();
            if (channel.BytesToWrite == 0)
                return;
            if (watch.Elapsed >= TimeSpan.FromSeconds(2))
                throw new TransportException("FTDI output did not drain.");
            Thread.Sleep(5);
        }
    }

    public void DiscardInput()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        channel.DiscardInput();
        _ = channel.LineStatus; // Drain setup status; a previously latched fault remains fatal.
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        try
        {
            channel.Latency = OriginalLatency;
            LatencyRestored = channel.Latency == OriginalLatency;
            if (!LatencyRestored)
                throw new IOException("FTDI latency restoration did not match.");
        }
        catch (IOException error) { CleanupWarning = error.Message; }
        finally
        {
            try
            {
                channel.Dispose();
            }
            catch (IOException error) { CleanupWarning = CleanupWarning is null ? error.Message : CleanupWarning + " " + error.Message; }
        }
    }

    private sealed class NativeFtdiChannel : IFtdiChannel
    {
        private readonly FtdiHandle handle;
        private int baud;
        public bool IsOpen => !handle.IsClosed && !handle.IsInvalid;

        public NativeFtdiChannel(string port, int baud)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("FTDI access requires Windows.");
            if (!Regex.IsMatch(port, "^COM[1-9][0-9]*$", RegexOptions.IgnoreCase))
                throw new ArgumentException("Select a COM port.", nameof(port));
            if (baud <= 0)
                throw new ArgumentOutOfRangeException(nameof(baud));
            string serial = FindDevice(port);
            Check(Native.FT_OpenEx(serial, 1, out var opened), "Open FTDI adapter");
            handle = new FtdiHandle(opened);
            try
            {
                Check(Native.FT_GetComPortNumber(handle, out int number), "Get FTDI COM port");
                if (!port.Equals("COM" + number, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The opened FTDI device does not match the selected COM port.");
                Check(Native.FT_SetDataCharacteristics(handle, 8, 0, 0), "Set FTDI 8N1");
                Check(Native.FT_SetFlowControl(handle, 0, 17, 19), "Set FTDI UART flow control");
                Check(Native.FT_SetDtr(handle), "Set FTDI DTR");
                Check(Native.FT_SetRts(handle), "Set FTDI RTS");
                Check(Native.FT_SetTimeouts(handle, 100, 2000), "Set FTDI timeouts");
                BaudRate = baud;
            }
            catch { handle.Dispose(); throw; }
        }

        private static string FindDevice(string port)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException();
            Check(Native.FT_CreateDeviceInfoList(out uint count), "Enumerate FTDI devices");
            if (count > 128)
                throw new IOException("Unexpected FTDI device count.");
            var matches = new List<string>();
            using var bus = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\FTDIBUS");
            if (bus is null)
                throw new IOException("The FTDI USB driver is not available.");
            foreach (uint index in Enumerable.Range(0, (int)count).Select(value => (uint)value))
            {
                var serial = new StringBuilder(16);
                var description = new StringBuilder(64);
                Check(Native.FT_GetDeviceInfoDetail(index, out _, out _, out uint id, out _, serial, description, out _), "Read FTDI device identity");
                if (id != 0x04036015 || serial.Length == 0)
                    continue;
                string prefix = "VID_0403+PID_6015+" + serial;
                foreach (string keyName in bus.GetSubKeyNames().Where(name => name.Equals(prefix, StringComparison.OrdinalIgnoreCase) || name.Equals(prefix + "A", StringComparison.OrdinalIgnoreCase)))
                {
                    using var settings = bus.OpenSubKey(keyName + @"\0000\Device Parameters");
                    if (settings?.GetValue("PortName") is string found && found.Equals(port, StringComparison.OrdinalIgnoreCase))
                        matches.Add(serial.ToString());
                }
            }
            var unique = matches.Distinct(StringComparer.Ordinal).ToArray();
            if (unique.Length != 1)
                throw new IOException("The selected COM port does not uniquely match an available vLinker FS FTDI interface.");
            return unique[0];
        }

        public int BaudRate
        {
            get => baud;
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                Check(Native.FT_SetBaudRate(handle, (uint)value), "Set FTDI baud");
                baud = value;
            }
        }
        public byte Latency
        {
            get
            {
                Check(Native.FT_GetLatencyTimer(handle, out byte value), "Get FTDI latency");
                return value;
            }
            set => Check(Native.FT_SetLatencyTimer(handle, value), "Set FTDI latency");
        }
        public uint LineStatus
        {
            get
            {
                Check(Native.FT_GetModemStatus(handle, out uint value), "Get FTDI line status");
                return value;
            }
        }
        public int BytesToRead
        {
            get
            {
                Check(Native.FT_GetQueueStatus(handle, out uint count), "Get FTDI receive queue");
                return checked((int)count);
            }
        }
        public int BytesToWrite
        {
            get
            {
                Check(Native.FT_GetStatus(handle, out _, out uint count, out _), "Get FTDI output queue");
                return checked((int)count);
            }
        }
        public int Read(byte[] data, int offset, int count) => Transfer(data, offset, count, true);
        public int Write(byte[] data, int offset, int count) => Transfer(data, offset, count, false);
        private int Transfer(byte[] data, int offset, int count, bool read)
        {
            var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var pointer = IntPtr.Add(pinned.AddrOfPinnedObject(), offset);
                uint actual;
                uint status = read ? Native.FT_Read(handle, pointer, (uint)count, out actual) : Native.FT_Write(handle, pointer, (uint)count, out actual);
                Check(status, read ? "FTDI read" : "FTDI write");
                return checked((int)actual);
            }
            finally { pinned.Free(); }
        }
        public void DiscardInput() => Check(Native.FT_Purge(handle, 1), "Clear FTDI input");
        public void Dispose()
        {
            handle.Dispose();
            if (handle.CloseStatus is uint status)
                Check(status, "Close FTDI adapter");
        }
    }

    private sealed class FtdiHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public uint? CloseStatus
        {
            get; private set;
        }
        public FtdiHandle(IntPtr value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle()
        {
            CloseStatus = Native.FT_Close(handle);
            return CloseStatus == 0;
        }
    }
    private static void Check(uint status, string action)
    {
        if (status != 0)
            throw new IOException($"{action}: FT_STATUS {status}. Reconnect before continuing.");
    }
    private static class Native
    {
        private const string Library = "ftd2xx.dll";
        [DllImport(Library)] internal static extern uint FT_CreateDeviceInfoList(out uint count);
        [DllImport(Library, CharSet = CharSet.Ansi)] internal static extern uint FT_GetDeviceInfoDetail(uint index, out uint flags, out uint type, out uint id, out uint location, StringBuilder serial, StringBuilder description, out IntPtr opened);
        [DllImport(Library, CharSet = CharSet.Ansi)] internal static extern uint FT_OpenEx(string serial, uint flags, out IntPtr handle);
        [DllImport(Library)] internal static extern uint FT_Close(IntPtr handle);
        [DllImport(Library)] internal static extern uint FT_GetComPortNumber(FtdiHandle handle, out int port);
        [DllImport(Library)] internal static extern uint FT_SetBaudRate(FtdiHandle handle, uint baud);
        [DllImport(Library)] internal static extern uint FT_SetDataCharacteristics(FtdiHandle handle, byte bits, byte stop, byte parity);
        [DllImport(Library)] internal static extern uint FT_SetFlowControl(FtdiHandle handle, ushort flow, byte xon, byte xoff);
        [DllImport(Library)] internal static extern uint FT_SetDtr(FtdiHandle handle);
        [DllImport(Library)] internal static extern uint FT_SetRts(FtdiHandle handle);
        [DllImport(Library)] internal static extern uint FT_SetTimeouts(FtdiHandle handle, uint read, uint write);
        [DllImport(Library)] internal static extern uint FT_GetLatencyTimer(FtdiHandle handle, out byte latency);
        [DllImport(Library)] internal static extern uint FT_SetLatencyTimer(FtdiHandle handle, byte latency);
        [DllImport(Library)] internal static extern uint FT_GetModemStatus(FtdiHandle handle, out uint status);
        [DllImport(Library)] internal static extern uint FT_GetQueueStatus(FtdiHandle handle, out uint count);
        [DllImport(Library)] internal static extern uint FT_GetStatus(FtdiHandle handle, out uint rx, out uint tx, out uint events);
        [DllImport(Library)] internal static extern uint FT_Read(FtdiHandle handle, IntPtr data, uint count, out uint actual);
        [DllImport(Library)] internal static extern uint FT_Write(FtdiHandle handle, IntPtr data, uint count, out uint actual);
        [DllImport(Library)] internal static extern uint FT_Purge(FtdiHandle handle, uint mask);
    }
}
