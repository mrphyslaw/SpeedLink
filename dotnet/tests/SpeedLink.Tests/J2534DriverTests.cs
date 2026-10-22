using SpeedLink.J2534;

namespace SpeedLink.Tests;

public class J2534DriverTests
{
    [Theory]
    [InlineData("Mongoose-Plus ISO/CAN3", true, true)]
    [InlineData("MongoosePlus ISO3", true, true)]
    [InlineData("Mongoose+ ISO/CAN 3", true, true)]
    [InlineData("Drew Technologies Mongoose-Plus ISO/CAN3", true, true)]
    [InlineData("mongoose-plus iso/can3", true, true)]
    [InlineData("Mongoose-Plus ISO/CAN3", false, false)]
    [InlineData("MongoosePro ISO/CAN", true, false)]
    [InlineData("Mongoose-Plus ISO/CAN2", true, false)]
    [InlineData("Mongoose-Plus Ford3", true, false)]
    [InlineData("Mongoose-Plus GM3", true, false)]
    [InlineData("Openport 2.0", true, false)]
    public void experimental_discovery_requires_the_candidate_model_and_iso15765(string name, bool iso15765, bool expected)
        => Assert.Equal(expected, J2534Drivers.AcceptsRegistration(J2534AdapterKind.MongoosePlusIsoCan3, name, "Drew Technologies", iso15765));

    [Fact]
    public void candidate_selection_preserves_existing_tactrix_discovery()
    {
        Assert.True(J2534Drivers.AcceptsRegistration(J2534AdapterKind.Tactrix, "Openport 2.0", "", false));
        Assert.True(J2534Drivers.AcceptsRegistration(J2534AdapterKind.Tactrix, "Other registered name", "Tactrix", false));
        Assert.False(J2534Drivers.AcceptsRegistration(J2534AdapterKind.Tactrix, "Mongoose-Plus ISO/CAN3", "Drew Technologies", true));
        Assert.False(J2534Drivers.AcceptsRegistration((J2534AdapterKind)99, "Mongoose-Plus ISO/CAN3", "Drew Technologies", true));
    }

    [Fact]
    public void registered_identity_keeps_candidate_status_separate_from_tactrix()
    {
        var candidate = new J2534Driver("Mongoose-Plus ISO/CAN3", "registered.dll", J2534AdapterKind.MongoosePlusIsoCan3);
        Assert.True(candidate.Experimental);
        Assert.Equal("Mongoose-Plus ISO/CAN3 (experimental)", candidate.AdapterName);
        Assert.False(new J2534Driver("Openport 2.0", "registered.dll").Experimental);
    }
}
