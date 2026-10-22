using SpeedLink.Core;

namespace SpeedLink.Tests;

public class TunerHelpTests
{
    public static IEnumerable<object[]> Profiles => TuningDefinition.Known.Select(d => new object[] { d.Calibration });

    [Theory, MemberData(nameof(Profiles))]
    public void EveryMapHasUnitsAndHelpAndCriticalCorrectionsDescribeTheirArithmetic(string calibration)
    {
        var definition = TuningDefinition.Known.Single(d => d.Calibration == calibration);
        Assert.All(definition.Tables, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Units), t.Name);
            Assert.False(string.IsNullOrWhiteSpace(t.Description), t.Name);
            Assert.DoesNotContain("unit not stated", t.Units);
            Assert.DoesNotContain("UI label", t.Units);
        });
        TuningTable Map(string name) => definition.Tables.Single(t => t.Name == name);
        var offset = Map("Knock Retard Additive");
        Assert.Equal("ECU knock-signal units", offset.Units);
        Assert.Contains("does not directly remove degrees", offset.Description);
        Assert.Contains("base reference × Knock Retard Multiplier + this offset", offset.Description);
        Assert.Contains("not accumulated spark retard", Map("Knock Retard Multiplier").Description);
        foreach (var table in definition.Tables.Where(t => t.Name.StartsWith("MAP Scale Additive - ", StringComparison.Ordinal)))
            Assert.Contains("Offset subtracted", table.Description);
        foreach (var table in definition.Tables.Where(t => t.Name.StartsWith("Injector Base Pulsewidth - ", StringComparison.Ordinal)))
        {
            Assert.Equal("encoded pulse coefficient", table.Units);
            Assert.Contains("not milliseconds", table.Description);
        }
        Assert.Contains("0 adds no enrichment", Map("Warmup Enrichment").Description);
        Assert.Contains("not a multiplier", Map("Volumetric Efficiency - VVT Modifier").Description);
        Assert.All(definition.Tables.Where(t => t.Category == "DTC Controls"), t => Assert.Contains("Disabled (0) clears it", t.Description));
    }
}
