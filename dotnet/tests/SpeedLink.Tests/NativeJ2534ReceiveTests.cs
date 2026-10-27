using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using SpeedLink.Core;
using SpeedLink.J2534;

namespace SpeedLink.Tests;

public sealed class NativeJ2534ReceiveTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(9u)]
    [InlineData(16u)]
    public void complete_native_reply_survives_timeout_like_status(uint status)
    {
        using var driver = new Driver();
        driver.Replies.Enqueue(Reply.Frame([0x41, 12, 0, 0]) with { Status = status });
        var frame = Assert.IsType<J2534Frame>(driver.Read(25));
        Assert.Equal(0x7E8u, frame.Id);
        Assert.Equal(new byte[] { 0x41, 12, 0, 0 }, frame.Data);
        Assert.Equal(1, driver.Reads);
        Assert.Empty(driver.Writes);
        Assert.Equal(4152, Marshal.SizeOf(Driver.MessageType));
        Assert.Equal(24, Marshal.OffsetOf(Driver.MessageType, "Data").ToInt32());
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(9u)]
    [InlineData(16u)]
    public void zero_returned_count_never_uses_buffer_contents(uint status)
    {
        using var driver = new Driver();
        driver.Replies.Enqueue(Reply.Frame([0x41, 12, 0, 0]) with { Status = status, Count = 0 });
        Assert.Null(driver.Read(25));
        Assert.Equal(1, driver.Stats.GetProperty("emptyReads").GetInt32());
        Assert.Equal(0u, driver.Stats.GetProperty("lastMessageCount").GetUInt32());
    }

    [Theory]
    [InlineData(7u)]
    [InlineData(8u)]
    [InlineData(18u)]
    public void fatal_native_error_is_not_hidden_by_reply_bytes(uint status)
    {
        using var driver = new Driver();
        driver.Replies.Enqueue(Reply.Frame([0x41, 12, 0, 0]) with { Status = status });
        Assert.Throws<TransportException>(() => driver.Read(25));
        Assert.Empty(driver.Writes);
    }

    [Theory]
    [InlineData(0u, 2u, 6u)]
    [InlineData(9u, 2u, 6u)]
    [InlineData(16u, 2u, 6u)]
    [InlineData(9u, 1u, 5u)]
    [InlineData(16u, 1u, 0u)]
    public void count_and_protocol_are_checked_even_on_timeout(uint status, uint count, uint protocol)
    {
        using var driver = new Driver();
        driver.Replies.Enqueue(Reply.Frame([0x41, 12, 0, 0]) with { Status = status, Count = count, Protocol = protocol });
        Assert.Throws<TransportException>(() => driver.Read(25));
    }

    [Fact]
    public void unchanged_output_and_input_count_cannot_become_a_reply()
    {
        using var driver = new Driver();
        driver.Replies.Enqueue(new(9, 1, 0, 0, 0, 0, new byte[4128]));
        Assert.Throws<TransportException>(() => driver.Read(25));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(8u)]
    [InlineData(6u)]
    [InlineData(3u)]
    public void iso15765_uses_full_data_size_and_records_extra_index_anomalies(uint extraIndex)
    {
        using var driver = new Driver();
        driver.Replies.Enqueue(Reply.Frame([0x41, 12, 0, 0]) with { ExtraIndex = extraIndex });
        Assert.Equal(new byte[] { 0x41, 12, 0, 0 }, driver.Read(25)!.Data);
        Assert.Equal(extraIndex is 0 or 8 ? 0 : 1, driver.Stats.GetProperty("extraDataIndexMismatches").GetInt32());
    }

    [Theory]
    [InlineData(4u, 0u)]
    [InlineData(4100u, 0u)]
    [InlineData(4129u, 0u)]
    [InlineData(8u, 9u)]
    public void malformed_native_lengths_are_rejected(uint size, uint extraIndex)
    {
        using var driver = new Driver();
        driver.Replies.Enqueue(Reply.Frame([0x41, 12, 0, 0]) with { Status = 9, Size = size, ExtraIndex = extraIndex });
        Assert.Throws<TransportException>(() => driver.Read(25));
    }

    [Fact]
    public void largest_iso15765_reply_is_preserved()
    {
        using var driver = new Driver();
        byte[] payload = Enumerable.Range(0, 4095).Select(i => (byte)i).ToArray();
        driver.Replies.Enqueue(Reply.Frame(payload) with { Status = 9 });
        Assert.Equal(payload, driver.Read(25)!.Data);
    }

    [Fact]
    public void notifications_pending_and_timeout_data_complete_one_request()
    {
        using var driver = new Driver();
        driver.Replies.Enqueue(Reply.Frame([]) with { Status = 9, RxStatus = 1 });
        driver.Replies.Enqueue(Reply.Frame([]) with { Status = 16, RxStatus = 2 });
        driver.Replies.Enqueue(Reply.Frame([]) with { RxStatus = 8 });
        driver.Replies.Enqueue(Reply.Frame([0x7F, 1, 0x78]) with { Status = 9 });
        driver.Replies.Enqueue(Reply.Frame([0x41, 12, 0, 0]) with { Status = 16 });
        using var link = Link(driver);
        Assert.Equal(new byte[] { 0x41, 12, 0, 0 }, link.Exchange([1, 12]));
        Assert.Single(driver.Writes);
        Assert.Equal(new byte[] { 0, 0, 7, 0xE0, 1, 12 }, driver.Writes[0]);
        Assert.Equal(5, driver.Reads);
        Assert.Equal(4, driver.Stats.GetProperty("messagesWithTimeout").GetInt32());
    }

    [Fact]
    public void malformed_timeout_reply_faults_connection_without_resending()
    {
        using var driver = new Driver();
        driver.Replies.Enqueue(Reply.Frame([0x41, 12, 0, 0]) with { Status = 9, Size = 4 });
        using var link = Link(driver);
        Assert.Throws<TransportException>(() => link.Exchange([1, 12]));
        Assert.Throws<TransportException>(() => link.Exchange([1, 12]));
        Assert.Single(driver.Writes);
        Assert.Equal(1, driver.Reads);
    }

    [Fact]
    public void timeout_padding_error_is_still_rejected_without_resending()
    {
        using var driver = new Driver();
        driver.Replies.Enqueue(Reply.Frame([0x41, 12, 0, 0]) with { Status = 9, RxStatus = 16 });
        using var link = Link(driver);
        Assert.Contains("padding", Assert.Throws<TransportException>(() => link.Exchange([1, 12])).Message);
        Assert.Single(driver.Writes);
    }

    [Fact]
    public void receive_diagnostics_include_metadata_and_reset_between_exchanges()
    {
        var sink = new Sink();
        using var driver = new Driver(sink);
        driver.Replies.Enqueue(Reply.Frame([0x63, 0xDE, 0xAD, 0xBE, 0xEF]) with { Status = 9, ExtraIndex = 6 });
        driver.Read(25);
        var details = Assert.Single(sink.Events, e => e.Kind == "driver.receive").Details;
        Assert.Equal("0x9", details.GetProperty("status").GetString());
        Assert.Equal(1u, details.GetProperty("messageCount").GetUInt32());
        Assert.True(details.GetProperty("durationMs").GetDouble() >= 0);
        Assert.DoesNotContain("DEADBEEF", details.GetRawText());
        Assert.False(details.TryGetProperty("data", out _));
        Assert.Equal(1, driver.Stats.GetProperty("messagesWithTimeout").GetInt32());
        driver.ClearReceive();
        Assert.Equal(0, driver.Stats.GetProperty("readCalls").GetInt32());
        Assert.Equal(0, driver.Stats.GetProperty("messagesWithTimeout").GetInt32());
        Assert.Equal(0, driver.Stats.GetProperty("extraDataIndexMismatches").GetInt32());
        Assert.Equal(0u, driver.Stats.GetProperty("lastMessageCount").GetUInt32());
    }

    [Fact]
    public void full_two_pass_backup_accepts_timeout_data_without_duplicate_requests() => CheckFullBackup("L38KEH000", 530);
    [Fact]
    public void gen2_full_two_pass_backup_accepts_timeout_data_without_duplicate_requests() => CheckFullBackup("L3CGEE000", 2052);
    private static void CheckFullBackup(string calibration, int requests)
    {
        using var temp = new TempFolder();
        byte[] image = Fixture.Image(Fixture.Profiles.Single(p => p.Calibration == calibration));
        using var ecu = new TestEcu(image);
        using var driver = new Driver();
        int sequence = 0;
        driver.Respond = request =>
        {
            if (request[0] == 0x23)
                driver.Replies.Enqueue(Reply.Frame([]) with { Status = 9, RxStatus = 2 });
            return Reply.Frame(ecu.Exchange(request)) with { Status = ++sequence % 2 == 0 ? 9u : 16u };
        };
        using var link = Link(driver);
        var result = Fixture.Reader().ReadBackup(link, ReadWorkflow.Identify(link), temp.Path);
        Assert.Equal(image, Fixture.Store().Load(result.Path, "hardware").Data);
        Assert.Equal(requests, ecu.Requests.Count(request => request[0] == 0x23));
        Assert.Equal(ecu.Requests.Count, driver.Writes.Count);
    }

    [Fact]
    public void native_output_buffers_are_clean_for_each_poll()
    {
        using var driver = new Driver();
        driver.Replies.Enqueue(Reply.Frame([0x41, 12, 0, 0]));
        driver.Read(25); driver.Read(25);
        Assert.Equal(2, driver.ReadBuffers.Count);
        Assert.All(driver.ReadBuffers, buffer => Assert.Equal((0u, 0u, true), buffer));
    }

    private static J2534ReadTransport Link(Driver driver)
    {
        var link = new J2534ReadTransport(driver, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        link.Initialize();
        return link;
    }

    private sealed class Sink : IDiagnosticSink
    {
        public List<(string Kind, JsonElement Details)> Events { get; } = [];
        public void Record(string kind, object? details = null) => Events.Add((kind, JsonSerializer.SerializeToElement(details)));
        public void RegisterSecret(string? value) { }
    }

    private sealed record Reply(uint Status, uint Count, uint Protocol, uint RxStatus, uint Size, uint ExtraIndex, byte[] Data)
    {
        public static Reply Frame(byte[] payload)
        {
            var data = new byte[4128];
            BinaryPrimitives.WriteUInt32BigEndian(data, 0x7E8);
            payload.CopyTo(data, 4);
            return new(0, 1, 6, 0, (uint)payload.Length + 4, 0, data);
        }
    }

    // Bind managed driver delegates to the actual native-channel implementation.
    // This keeps the production driver discovery/registration contract intact and
    // tests the P/Invoke boundary's output handling without a DLL, registry or ECU.
    private sealed class Driver : IJ2534Channel
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly Type ChannelType = typeof(NativeJ2534Channel);
        public static readonly Type MessageType = ChannelType.GetNestedType("Message", BindingFlags.NonPublic)!;
        private readonly NativeJ2534Channel channel;
        public Queue<Reply> Replies { get; } = new();
        public List<byte[]> Writes { get; } = [];
        public List<(uint Protocol, uint Size, bool Zeroed)> ReadBuffers { get; } = [];
        public Func<byte[], Reply>? Respond { get; set; }
        public int Reads { get; private set; }
        public JsonElement Stats => JsonSerializer.SerializeToElement(channel.ReadDiagnostics);
        public object ReadDiagnostics => channel.ReadDiagnostics;
        public string? CleanupWarning => channel.CleanupWarning;

        public Driver(IDiagnosticSink? sink = null)
        {
            channel = (NativeJ2534Channel)RuntimeHelpers.GetUninitializedObject(ChannelType);
            Set("connected", true); Set("filtered", true); Set("requestId", 0x7E0u); Set("diagnostics", sink);
            var read = Function("read", [typeof(uint), MessageType.MakeByRefType(), typeof(uint).MakeByRefType(), typeof(uint)]);
            var il = read.GetILGenerator();
            var reply = il.DeclareLocal(typeof(Reply));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_S, (byte)4);
            foreach (var field in new[] { "ProtocolId", "DataSize", "Data" })
            {
                il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldfld, MessageType.GetField(field)!);
            }
            il.Emit(OpCodes.Call, typeof(Driver).GetMethod(nameof(Next))!); il.Emit(OpCodes.Stloc, reply);
            foreach (var (field, property) in new[] { ("ProtocolId", "Protocol"), ("RxStatus", "RxStatus"), ("DataSize", "Size"), ("ExtraDataIndex", "ExtraIndex"), ("Data", "Data") })
            {
                il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldloc, reply);
                il.Emit(OpCodes.Callvirt, typeof(Reply).GetProperty(property)!.GetMethod!);
                il.Emit(OpCodes.Stfld, MessageType.GetField(field)!);
            }
            il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldloc, reply);
            il.Emit(OpCodes.Callvirt, typeof(Reply).GetProperty("Count")!.GetMethod!); il.Emit(OpCodes.Stind_I4);
            il.Emit(OpCodes.Ldloc, reply); il.Emit(OpCodes.Callvirt, typeof(Reply).GetProperty("Status")!.GetMethod!); il.Emit(OpCodes.Ret);
            Bind("read", read);

            var write = Function("write", [typeof(uint), MessageType.MakeByRefType(), typeof(uint).MakeByRefType(), typeof(uint)]);
            il = write.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldfld, MessageType.GetField("Data")!);
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldfld, MessageType.GetField("DataSize")!);
            il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldarg_S, (byte)4);
            il.Emit(OpCodes.Call, typeof(Driver).GetMethod(nameof(Send))!); il.Emit(OpCodes.Ret);
            Bind("write", write);
            var ioctlField = ChannelType.GetField("ioctl", Hidden)!;
            Set("ioctl", Delegate.CreateDelegate(ioctlField.FieldType, this, nameof(Ioctl)));
        }

        private void Set(string name, object? value) => ChannelType.GetField(name, Hidden)!.SetValue(channel, value);
        private static DynamicMethod Function(string name, Type[] arguments) => new(name, typeof(uint), [typeof(Driver), .. arguments], ChannelType, true);
        private void Bind(string name, DynamicMethod method) => Set(name, method.CreateDelegate(ChannelType.GetField(name, Hidden)!.FieldType, this));
        public Reply Next(uint timeout, uint protocol, uint size, byte[] data)
        {
            Assert.InRange(timeout, 1u, 1000u); Reads++;
            ReadBuffers.Add((protocol, size, data.AsSpan().IndexOfAnyExcept((byte)0) < 0));
            return Replies.TryDequeue(out var reply) ? reply : new(9, 0, 0, 0, 0, 0, new byte[4128]);
        }
        public uint Send(byte[] bytes, uint size, ref uint count, uint timeout)
        {
            Assert.Equal(1u, count); Assert.Equal(1000u, timeout);
            Writes.Add(bytes.AsSpan(0, (int)size).ToArray()); count = 1;
            if (Respond is not null) Replies.Enqueue(Respond(bytes.AsSpan(4, (int)size - 4).ToArray()));
            return 0;
        }
        public uint Ioctl(uint id, uint code, nint input, nint output) { Assert.Equal(8u, code); return 0; }
        public J2534Version Open() => new("test", "test", "04.04");
        public void Connect(uint bitrate, uint requestId, uint responseId) { }
        public void ClearReceive() => channel.ClearReceive();
        public void Write(byte[] request) => channel.Write(request);
        public J2534Frame? Read(int timeoutMilliseconds) => channel.Read(timeoutMilliseconds);
        public void Dispose() => channel.Dispose();
    }
}
