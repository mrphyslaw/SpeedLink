using SpeedLink.Core;
namespace SpeedLink.Tests;

public class RecoveryChecks
{
    private static RecoveryModel AtTransfer()
    {
        var model = new RecoveryModel(Fixture.Rom, Fixture.Rom);
        foreach (var value in new[] { "5085", "6701123477", "6702", "F100B2", "740FFF" })
        {
            model.Next();
            model.Accept(Fixture.Hex(value));
        }
        return model;
    }
    [Fact]
    public void test_complete_span_page_alignment_and_last_marker()
    {
        var rom = Fixture.Rom;
        var blocks = RecoveryModel.Transfers(rom).ToArray();
        Assert.Equal(256, blocks.Length);
        Assert.Equal(0x8000, blocks[0].Address);
        Assert.Equal(0x100000, blocks[^1].Address + blocks[^1].Data.Length);
        Assert.Equal(rom[0x8000..], blocks.SelectMany(b => b.Data).ToArray());
        Assert.Equal(Fixture.Hex("A55A"), blocks[^1].Data[^2..]);
        Assert.All(blocks, b => { Assert.Equal(0, b.Address % 128); Assert.Equal(3968, b.Data.Length); });
    }
    [Fact]
    public void test_checksum_valid_tuning_edit_is_accepted()
    {
        var image = Fixture.Rom;
        image[0x50000] ^= 1;
        image = Fixture.Inspector().CorrectedCopy(image);
        RecoveryModel.Validate(image, Fixture.Rom);
        Assert.NotEqual(RomInspector.Hash(Fixture.Rom), RomInspector.Hash(image));
    }
    [Fact]
    public void test_bad_images_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => RecoveryModel.Validate(Fixture.Rom[..^1], Fixture.Rom));
        foreach (var at in new[] { 0x50000, 0x8002, 0xFFFFE, 0x2A7D8, 0x6000 })
        {
            var image = Fixture.Rom;
            image[at] ^= 1;
            Assert.Throws<InvalidDataException>(() => RecoveryModel.Validate(image, Fixture.Rom));
        }
    }
    [Fact]
    public void test_unrecognized_matching_boot_is_rejected()
    {
        var image = Fixture.Rom;
        image[0x6000] ^= 1;
        Assert.Throws<InvalidDataException>(() => RecoveryModel.Validate(image, image));
    }
    [Fact]
    public void test_no_partial_recovery_request()
    {
        foreach (var (at, size) in new[] { (0, 0x100000), (0x8000, 0xF0000), (0x9000, 0xF7000) })
            Assert.Throws<InvalidDataException>(() => RecoveryModel.Download(at, size));
    }
    [Fact]
    public void test_native_key_vectors()
    {
        foreach (var (seed, key) in new[] { ("000077", "7AD840"), ("000177", "45ED0E"), ("7FFF77", "9ED7E5"), ("800077", "E5CAEF"), ("FFFF77", "01C54A") })
            Assert.Equal(Fixture.Hex(key), SecurityAccess.SeedKey(Fixture.Hex(seed)));
        foreach (var data in new[] { Array.Empty<byte>(), new byte[1], new byte[4] })
            Assert.Throws<InvalidDataException>(() => SecurityAccess.SeedKey(data));
    }
    [Fact]
    public void test_pending_does_not_resend_or_advance()
    {
        var model = AtTransfer();
        var request = model.Next();
        for (int i = 0; i < 3; i++)
            model.Accept(Fixture.Hex("7F3678"));
        Assert.Equal(0, model.Acknowledged);
        Assert.Equal(RecoveryStage.Transfer, model.Stage);
        Assert.Throws<InvalidDataException>(() => model.Next());
        model.Accept(Fixture.Hex("76"));
        Assert.Equal(3968, model.Acknowledged);
        Assert.NotEqual(request, model.Next());
    }
    [Fact]
    public void test_ambiguous_transfer_timeouts_block_further_requests()
    {
        foreach (var lost in new[] { 0, 1, 127, 254, 255 })
        {
            var model = AtTransfer();
            for (int i = 0; i < lost; i++)
            {
                model.Next();
                model.Accept(Fixture.Hex("76"));
            }
            model.Next();
            model.Timeout();
            Assert.Equal(lost * 3968, model.Acknowledged);
            Assert.Equal(RecoveryStage.Interrupted, model.Stage);
            Assert.Throws<InvalidDataException>(() => model.Next());
        }
    }
    [Fact]
    public void test_negative_or_malformed_response_halts()
    {
        foreach (var value in new[] { "", "7F36", "7F3478", "7F3672", "7600" })
        {
            var model = AtTransfer();
            model.Next();
            Assert.Throws<InvalidDataException>(() => model.Accept(Fixture.Hex(value)));
            Assert.Equal(0, model.Acknowledged);
            Assert.Equal(RecoveryStage.Failed, model.Stage);
            Assert.Throws<InvalidDataException>(() => model.Next());
        }
    }
    [Fact]
    public void test_already_unlocked_zero_seed_skips_key()
    {
        var model = new RecoveryModel(Fixture.Rom, Fixture.Rom);
        Assert.Equal(Fixture.Hex("1085"), model.Next());
        model.Accept(Fixture.Hex("5085"));
        Assert.Equal(Fixture.Hex("2701"), model.Next());
        model.Accept(Fixture.Hex("6701000000"));
        Assert.Equal(Fixture.Hex("B100B200"), model.Next());
    }
    [Fact]
    public void test_reset_ack_is_not_recovery_verification()
    {
        var model = AtTransfer();
        for (int i = 0; i < 256; i++)
        {
            model.Next();
            model.Accept(Fixture.Hex("76"));
        }
        Assert.Equal(RecoveryStage.Exit, model.Stage);
        Assert.Equal(Fixture.Hex("37"), model.Next());
        model.Accept(Fixture.Hex("77"));
        Assert.Equal(Fixture.Hex("1101"), model.Next());
        model.Accept(Fixture.Hex("51"));
        Assert.Equal(RecoveryStage.Verify, model.Stage);
        Assert.Equal(0xF8000, model.Acknowledged);
        Assert.Throws<InvalidDataException>(() => model.Next());
    }
}
