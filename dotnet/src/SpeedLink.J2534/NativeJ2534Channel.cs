using System.Runtime.InteropServices;
using System.Text;
using SpeedLink.Core;

namespace SpeedLink.J2534;

/// <summary>SAE J2534-1 v04.04 binding to a registered manufacturer driver.</summary>
public sealed class NativeJ2534Channel : IJ2534Channel
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public uint ProtocolId, RxStatus, TxFlags, Timestamp, DataSize, ExtraDataIndex;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4128)] public byte[] Data;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint OpenFn(nint name, out uint device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint CloseFn(uint device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ConnectFn(uint device, uint protocol, uint flags, uint baud, out uint channel);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint DisconnectFn(uint channel);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReadFn(uint channel, ref Message message, ref uint count, uint timeout);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint WriteFn(uint channel, ref Message message, ref uint count, uint timeout);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint FilterFn(uint channel, uint type, ref Message mask, ref Message pattern, ref Message flow, out uint filter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint StopFilterFn(uint channel, uint filter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint VersionFn(uint device, StringBuilder firmware, StringBuilder dll, StringBuilder api);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ErrorFn(StringBuilder error);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint IoctlFn(uint channel, uint code, nint input, nint output);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryExW(string path, nint reserved, uint flags);

    private readonly string libraryPath;
    private nint module;
    private uint device, channel, filter, requestId;
    private bool opened, connected, filtered, disposed;
    private OpenFn? open;
    private CloseFn? close;
    private ConnectFn? connect;
    private DisconnectFn? disconnect;
    private ReadFn? read;
    private WriteFn? write;
    private FilterFn? startFilter;
    private StopFilterFn? stopFilter;
    private VersionFn? version;
    private ErrorFn? error;
    private IoctlFn? ioctl;
    public string? CleanupWarning { get; private set; }
    public long WriteRequests { get; private set; }
    public bool IsOpen => opened && !disposed;
    public string AdapterName { get; }
    public bool Experimental { get; }

    public NativeJ2534Channel(J2534Driver driver)
    {
        libraryPath = Path.GetFullPath(driver.LibraryPath);
        // Trust the registered installation, never a DLL supplied by a ROM or backup.
        var registered = J2534Drivers.Discover(driver.Kind).FirstOrDefault(item => item.LibraryPath.Equals(libraryPath, StringComparison.OrdinalIgnoreCase));
        if (registered is null)
            throw new TransportException("The selected J2534 driver is no longer registered for this adapter. Refresh devices.");
        AdapterName = registered.AdapterName;
        Experimental = registered.Experimental;
    }

    private T Bind<T>(string name, int bytes) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(module, name, out var address) &&
            !NativeLibrary.TryGetExport(module, $"_{name}@{bytes}", out address))
            throw new TransportException("J2534 driver export is missing: " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
    private void Check(uint code, string action)
    {
        if (code == 0)
            return;
        var text = new StringBuilder(256);
        error?.Invoke(text);
        throw new TransportException($"{action}: J2534 0x{code:X} {text}. Reconnect before continuing.");
    }
    private static Message Packet(uint id, ReadOnlySpan<byte> payload, uint flags = 0x40)
    {
        var result = new Message { ProtocolId = 6, TxFlags = flags, DataSize = (uint)payload.Length + 4, Data = new byte[4128] };
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result.Data, id);
        payload.CopyTo(result.Data.AsSpan(4));
        return result;
    }

    public J2534Version Open()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new TransportException("Use the 32-bit Windows SpeedLink build with the installed J2534 driver.");
        if (opened || module != 0)
            throw new TransportException("The J2534 channel is already open.");
        try
        {
            // Restrict dependencies to the installation directory and Windows system.
            module = LoadLibraryExW(libraryPath, 0, 0x100 | 0x800);
            if (module == 0)
                throw new TransportException($"Cannot load the installed J2534 driver (Windows error {Marshal.GetLastWin32Error()}).");
            open = Bind<OpenFn>("PassThruOpen", 8);
            close = Bind<CloseFn>("PassThruClose", 4);
            connect = Bind<ConnectFn>("PassThruConnect", 20);
            disconnect = Bind<DisconnectFn>("PassThruDisconnect", 4);
            read = Bind<ReadFn>("PassThruReadMsgs", 16);
            write = Bind<WriteFn>("PassThruWriteMsgs", 16);
            startFilter = Bind<FilterFn>("PassThruStartMsgFilter", 24);
            stopFilter = Bind<StopFilterFn>("PassThruStopMsgFilter", 8);
            version = Bind<VersionFn>("PassThruReadVersion", 16);
            error = Bind<ErrorFn>("PassThruGetLastError", 4);
            ioctl = Bind<IoctlFn>("PassThruIoctl", 16);
            Check(open(0, out device), "Open J2534");
            opened = true;
            var fw = new StringBuilder(256);
            var dll = new StringBuilder(256);
            var api = new StringBuilder(256);
            Check(version(device, fw, dll, api), "Read J2534 version");
            return new(fw.ToString(), dll.ToString(), api.ToString());
        }
        catch { Dispose(); throw; }
    }

    public void Connect(uint bitrate, uint requestId, uint responseId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!opened || connected || bitrate != 500000 || requestId != 0x7E0 || responseId != 0x7E8)
            throw new TransportException("Unsupported J2534 connection configuration.");
        Check(connect!(device, 6, 0, bitrate, out channel), "Connect CAN 500 kbit/s");
        connected = true;
        this.requestId = requestId;
        var mask = Packet(0xFFFFFFFF, []);
        var pattern = Packet(responseId, []);
        var flow = Packet(requestId, []);
        Check(startFilter!(channel, 3, ref mask, ref pattern, ref flow, out filter), "Install ISO-TP flow-control filter");
        filtered = true;
    }

    public void ClearReceive()
    {
        EnsureConnected();
        Check(ioctl!(channel, 8, 0, 0), "Clear stale J2534 receive messages");
    }
    public void Write(byte[] request)
    {
        DiagnosticProtocol.CheckReadOnly(request, 3968);
        EnsureConnected();
        var packet = Packet(requestId, request);
        uint count = 1;
        WriteRequests++;
        Check(write!(channel, ref packet, ref count, 1000), "Send read-only diagnostic request");
        if (count != 1)
            throw new TransportException("J2534 did not accept exactly one request. No retry was attempted.");
    }
    public J2534Frame? Read(int timeoutMilliseconds)
    {
        EnsureConnected();
        if (timeoutMilliseconds is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        var packet = Packet(0, []);
        uint count = 1;
        var status = read!(channel, ref packet, ref count, (uint)timeoutMilliseconds);
        if (status is 9 or 16)
            return null;
        Check(status, "Receive J2534 response");
        if (count == 0)
            return null;
        if (count != 1 || packet.ProtocolId != 6)
            throw new TransportException("Invalid J2534 receive message count or protocol.");
        if ((packet.RxStatus & 0x0B) != 0)
            return new(0, packet.RxStatus, []);
        uint end = packet.ExtraDataIndex == 0 ? packet.DataSize : packet.ExtraDataIndex;
        if (packet.DataSize is < 5 or > 4128 || end < 5 || end > packet.DataSize || end > 4099)
            throw new TransportException("Malformed J2534 response bounds.");
        uint id = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(packet.Data);
        return new(id, packet.RxStatus, packet.Data.AsSpan(4, (int)end - 4).ToArray());
    }
    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!connected || !filtered)
            throw new TransportException("The J2534 channel is not ready.");
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        var warnings = new List<string>();
        try
        {
            if (filtered && stopFilter is not null && stopFilter(channel, filter) != 0)
                warnings.Add("J2534 filter cleanup failed.");
            if (connected && disconnect is not null && disconnect(channel) != 0)
                warnings.Add("J2534 disconnect failed.");
            if (opened && close is not null && close(device) != 0)
                warnings.Add("J2534 close failed.");
        }
        finally
        {
            if (module != 0)
                NativeLibrary.Free(module);
            module = 0;
            filtered = connected = opened = false;
            CleanupWarning = warnings.Count == 0 ? null : string.Join(' ', warnings);
        }
    }
}
