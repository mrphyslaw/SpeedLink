using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using SpeedLink.Core;
[assembly: InternalsVisibleTo("SpeedLink.Tests")]
namespace SpeedLink.Hardware;

/// <summary>Exclusive Windows COM handle with bounded synchronous I/O on the app worker.</summary>
/// <remarks>DCB/COMMTIMEOUTS ABI: learn.microsoft.com/windows/win32/api/winbase/ .</remarks>
public sealed class SerialConnection : ISerialConnection
{
    private readonly SafeFileHandle handle;
    private readonly List<string> errors = [];
    private int baud;
    private string? fault;
    public const string Backend = "Win32 COM / SafeFileHandle";
    public long Writes
    {
        get; private set;
    }
    public long BytesWritten
    {
        get; private set;
    }
    public string[] DriverErrors => errors.ToArray();
    public int ReadBufferSize => 65536;
    internal static (int Dcb, int Timeouts, int Status) InteropLayout => (Marshal.SizeOf<Dcb>(), Marshal.SizeOf<Timeouts>(), Marshal.SizeOf<ComStatus>());
    public SerialConnection(string name, int baud)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("SpeedLink serial I/O requires Windows.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^COM[1-9][0-9]*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("Select a COM port.", nameof(name));
        if (InteropLayout != (28, 20, 12))
            throw new InvalidOperationException("Unexpected Windows serial ABI.");
        handle = Native.CreateFile("\\\\.\\" + name, 0xC0000000, 0, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw Error("Open " + name, error);
        }
        try
        {
            Check(Native.SetupComm(handle, 65536, 8192), "Configure serial queues");
            var timeouts = new Timeouts { ReadTotalConstant = 100, WriteTotalConstant = 2000 };
            Check(Native.SetCommTimeouts(handle, ref timeouts), "Configure serial timeouts");
            BaudRate = baud;
            DiscardInput();
        }
        catch { handle.Dispose(); throw; }
    }
    public static string[] GetPorts()
    {
        if (!OperatingSystem.IsWindows())
            return [];
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (key is null)
            return [];
        var names = new List<string>();
        foreach (var value in key.GetValueNames())
            if (key.GetValue(value) is string name && name.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                names.Add(name);
        return names.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public bool IsOpen => !handle.IsClosed && !handle.IsInvalid;
    public int BaudRate
    {
        get => baud;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            var state = new Dcb { Length = (uint)Marshal.SizeOf<Dcb>() };
            Check(Native.GetCommState(handle, ref state), "Read COM settings");
            state.Baud = (uint)value;
            state.Flags = 0x1011; // binary, DTR enabled, RTS enabled; no flow control.
            state.ByteSize = 8;
            state.Parity = 0;
            state.StopBits = 0;
            state.XonChar = 17;
            state.XoffChar = 19;
            state.ErrorChar = 0;
            state.EofChar = 26;
            Check(Native.SetCommState(handle, ref state), "Set COM settings");
            baud = value;
        }
    }
    private ComStatus Status()
    {
        if (fault is not null)
            throw new TransportException(fault);
        Check(Native.ClearCommError(handle, out var flags, out var status), "Read serial status");
        if (flags != 0)
        {
            var label = $"COM error 0x{flags:X}";
            errors.Add(label);
            fault = "Serial driver reported: " + label + ". Reconnect before continuing.";
            throw new TransportException(fault);
        }
        return status;
    }
    public int BytesToRead => checked((int)Status().InputBytes);
    public int Read(byte[] buffer, int offset, int count)
    {
        if ((uint)offset > (uint)buffer.Length || (uint)count > (uint)(buffer.Length - offset))
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0)
            return 0;
        Status();
        var output = offset == 0 ? buffer : new byte[count];
        Check(Native.ReadFile(handle, output, (uint)count, out var read, IntPtr.Zero), "Read COM data");
        Status();
        if (read > (uint)count)
            throw new IOException("Serial driver returned an invalid byte count.");
        if (offset != 0)
            output.AsSpan(0, (int)read).CopyTo(buffer.AsSpan(offset));
        return (int)read;
    }
    public int Write(byte[] data)
    {
        Status();
        Writes++;
        Check(Native.WriteFile(handle, data, (uint)data.Length, out var written, IntPtr.Zero), "Write COM data");
        if (written > (uint)data.Length)
            throw new IOException("Serial driver returned an invalid write count.");
        BytesWritten += written;
        return (int)written;
    }
    public void Flush()
    {
        // Poll the output queue as the reference reader does, with a deadline
        // so a stalled driver cannot block the app worker indefinitely.
        var timer = Stopwatch.StartNew();
        while (Status().OutputBytes != 0)
        {
            if (timer.Elapsed >= TimeSpan.FromSeconds(2))
                throw new TransportException("Serial output did not drain. Reconnect before continuing.");
            Thread.Sleep(50);
        }
    }
    public void DiscardInput()
    {
        Check(Native.PurgeComm(handle, 0x000A), "Clear COM input");
        Check(Native.ClearCommError(handle, out _, out _), "Clear previous COM status");
    }
    public void Dispose() => handle.Dispose();
    private static IOException Error(string action, int error) => new(action + ": " + new Win32Exception(error).Message + " (" + error + ").", new Win32Exception(error));
    private static void Check(bool success, string action)
    {
        if (!success)
            throw Error(action, Marshal.GetLastPInvokeError());
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Dcb
    {
        public uint Length, Baud, Flags;
        public ushort Reserved, XonLimit, XoffLimit;
        public byte ByteSize, Parity, StopBits, XonChar, XoffChar, ErrorChar, EofChar, EventChar;
        public ushort Reserved1;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Timeouts
    {
        public uint ReadInterval, ReadTotalMultiplier, ReadTotalConstant, WriteTotalMultiplier, WriteTotalConstant;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ComStatus
    {
        public uint Flags, InputBytes, OutputBytes;
    }
    private static class Native
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetupComm(SafeFileHandle file, uint input, uint output);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetCommState(SafeFileHandle file, ref Dcb state);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetCommState(SafeFileHandle file, ref Dcb state);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetCommTimeouts(SafeFileHandle file, ref Timeouts timeouts);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool ClearCommError(SafeFileHandle file, out uint errors, out ComStatus status);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool PurgeComm(SafeFileHandle file, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool ReadFile(SafeFileHandle file, [Out] byte[] buffer, uint count, out uint read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool WriteFile(SafeFileHandle file, byte[] buffer, uint count, out uint written, IntPtr overlapped);
    }
}
