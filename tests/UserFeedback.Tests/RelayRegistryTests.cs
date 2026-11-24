using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Xunit;

namespace UserFeedback.Tests;

/// <summary>The registry's compact list (formatVersion 2), with the mods that count "works for me" results</summary>
public class RelayRegistryTests
{
    private static Dictionary<string, RelayMod> Parse(string json) => RelayRegistry.ParseCompact(JObject.Parse(json), _ => true);

    [Fact]
    public void ModsWithoutWorksTakeReportsOnly()
    {
        var mods = Parse("""{ "formatVersion": 2, "mods": ["polymorph"], "public": [], "kinds": {} }""");

        Assert.True(mods["polymorph"].Reports);
        Assert.False(mods["polymorph"].Works);
    }

    [Fact]
    public void ModInBothListsTakesBoth()
    {
        var mods = Parse("""{ "formatVersion": 2, "mods": ["polymorph"], "public": [], "kinds": {}, "works": ["polymorph"] }""");

        Assert.True(mods["polymorph"].Reports);
        Assert.True(mods["polymorph"].Works);
    }

    [Fact]
    public void ModOnlyInWorksTakesNoReports()
    {
        var mods = Parse("""{ "formatVersion": 2, "mods": [], "public": ["counter"], "kinds": { "counter": ["bug"] }, "works": ["counter"] }""");

        RelayMod mod = mods["counter"];
        Assert.False(mod.Reports);
        Assert.True(mod.Works);
        // "public" and "kinds" are about reports, so they don't apply to a mod that takes none
        Assert.False(mod.Public);
        Assert.Null(mod.Kinds);
    }

    [Fact]
    public void InvalidModIdsInWorksAreSkipped()
    {
        var mods = Parse("""{ "formatVersion": 2, "mods": [], "works": ["Polymorph", "x", 5, "fine_one"] }""");

        Assert.Equal(new[] { "fine_one" }, mods.Keys);
    }

    [Fact]
    public void WorksInABucketOnlyKeepsModsOfThatBucket()
    {
        string prefix = RelayRegistry.Prefix("polymorph", 1);
        string other = prefix == "0" ? "1" : "0";
        var json = JObject.Parse("""{ "formatVersion": 2, "mods": [], "works": ["polymorph"] }""");

        Assert.Contains("polymorph", RelayRegistry.ParseCompact(json, id => RelayRegistry.Prefix(id, 1) == prefix).Keys);
        Assert.Empty(RelayRegistry.ParseCompact(json, id => RelayRegistry.Prefix(id, 1) == other));
    }

    [Fact]
    public void CacheKeepsWorksAndWorksOnlyMods()
    {
        var mods = Parse("""
            { "formatVersion": 2, "mods": ["polymorph", "other"], "public": ["polymorph"], "kinds": { "other": ["bug"] },
              "works": ["polymorph", "counter"] }
            """);

        JObject saved = RelayRegistry.CompactJson(mods, new JObject { ["formatVersion"] = 2 });
        var back = RelayRegistry.ParseCompact(saved, _ => true);

        Assert.Equal(new[] { "polymorph", "other" }, saved["mods"]!.ToObject<string[]>());
        Assert.True(back["polymorph"] is { Reports: true, Works: true, Public: true });
        Assert.True(back["other"] is { Reports: true, Works: false });
        Assert.Equal(new[] { "bug" }, back["other"].Kinds);
        Assert.True(back["counter"] is { Reports: false, Works: true });
    }
}
