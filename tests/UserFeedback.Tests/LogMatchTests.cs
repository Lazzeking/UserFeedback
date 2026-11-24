using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Xunit;

namespace UserFeedback.Tests;

/// <summary>"logMatch" in a mod's userfeedback.json (docs/for-modders.md, "More log lines")</summary>
public class LogMatchTests
{
    private static List<string> Check(string json, out List<string> problems)
    {
        FeedbackConfigFile file = JsonConvert.DeserializeObject<FeedbackConfigFile>(json)!;
        problems = new List<string>();
        return LogMatchRules.Check(file.LogMatch, problems);
    }

    [Fact]
    public void SpecificPrefixesAreKept()
    {
        List<string> kept = Check("""{ "logMatch": ["PolyBiome", "[poly]", "Polymorph.Patches"] }""", out List<string> problems);

        Assert.Empty(problems);
        Assert.Equal(new[] { "PolyBiome", "[poly]", "Polymorph.Patches" }, kept);
    }

    [Fact]
    public void NoLogMatchIsEmpty()
    {
        Assert.Empty(Check("{}", out List<string> problems));
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("[Error]")]
    [InlineData("error:")]
    [InlineData("Exce")]
    [InlineData("Exception")]
    [InlineData("Server")]
    [InlineData("Harmony")]
    [InlineData("Vintagestory.GameContent")]
    [InlineData("System.")]
    [InlineData("HarmonyLib")]
    [InlineData("Mods")]
    [InlineData("a.b")]
    [InlineData("[]--")]
    [InlineData("")]
    [InlineData(" Poly")]
    [InlineData("Poly\nBiome")]
    [InlineData("ThisPrefixIsMuchTooLongToBeAnythingSpecific")]
    public void BroadOrBrokenPrefixesAreRefused(string prefix)
    {
        List<string> kept = LogMatchRules.Check(new List<string> { prefix }, new List<string>());
        Assert.Empty(kept);
        Assert.NotNull(LogMatchRules.Refusal(prefix));
    }

    [Fact]
    public void AWordThatOnlyStartsLikeACommonOneIsKept()
    {
        Assert.Null(LogMatchRules.Refusal("ErrorTracker"));
        Assert.Null(LogMatchRules.Refusal("ServerSidePoly"));
    }

    [Fact]
    public void AtMostEightAreKeptAndDuplicatesCountOnce()
    {
        var prefixes = Enumerable.Range(1, 10).Select(i => $"Poly{i}x").Prepend("Poly1x").ToList();
        List<string> problems = new();

        List<string> kept = LogMatchRules.Check(prefixes, problems);

        Assert.Equal(LogMatchRules.MaxPrefixes, kept.Count);
        Assert.Single(problems);
    }

    [Theory]
    [InlineData("[poly] biome table built", true)]
    [InlineData("at PolyBiomeGen.Patch.Prefix()", true)]
    [InlineData("12:00 [Error] PolyBiome failed", true)]
    [InlineData("at MyPolyBiome.Run()", false)]
    [InlineData("polybiome lowercase", false)]
    public void APrefixMatchesCaseAndWordStart(string line, bool matches)
    {
        // Same options as ModLogFilter, which ignores case for modids and names
        var regex = new Regex(string.Join("|", new[] { "PolyBiome", "[poly]" }.Select(LogMatchRules.Pattern)), RegexOptions.IgnoreCase);
        Assert.Equal(matches, regex.IsMatch(line));
    }
}
