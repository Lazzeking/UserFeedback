using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace UserFeedback.Tests;

/// <summary>The request body's rules and works-sent.json</summary>
public class WorksTests
{
    [Fact]
    public void ManualIsAlwaysCleanAndHasNoChangeFields()
    {
        JObject json = WorksResult.Manual("polymorph", "3.0.0", "1.22.7", singleplayer: false)!.ToJson();

        Assert.Equal(1, json.Value<int>("FormatVersion"));
        Assert.Equal("polymorph", json.Value<string>("ModId"));
        Assert.Equal("3.0.0", json.Value<string>("ModVersion"));
        Assert.Equal("1.22.7", json.Value<string>("GameVersion"));
        Assert.Equal("manual", json.Value<string>("Source"));
        Assert.Equal("clean", json.Value<string>("Result"));
        Assert.False(json.Value<bool>("Singleplayer"));
        Assert.Null(json["Replaces"]);
        Assert.Null(json["OtherModsChanged"]);
    }

    [Theory]
    [InlineData("Polymorph", "3.0.0", "1.22.7")] // modid must be lower case
    [InlineData("p", "3.0.0", "1.22.7")]         // and at least 2 characters
    [InlineData("polymorph", "", "1.22.7")]
    [InlineData("polymorph", "-3.0", "1.22.7")]  // versions start with a letter or digit
    [InlineData("polymorph", "3.0.0 beta", "1.22.7")]
    [InlineData("polymorph", "3.0.0", "1.22.7-rc.1-with-a-very-long-suffix")] // at most 32 characters
    public void StrangeIdsAndVersionsAreSkipped(string modId, string modVersion, string gameVersion)
    {
        Assert.Null(WorksResult.Manual(modId, modVersion, gameVersion, false));
    }

    [Fact]
    public void VersionsWithPlusAndDashAreFine()
    {
        Assert.NotNull(WorksResult.Manual("my_mod", "1.0.0-rc.2+build5", "1.22.0-pre.3", true));
    }

    [Fact]
    public void AutoChangeCarriesReplacesAndOtherModsChanged()
    {
        JObject json = WorksResult.Auto("polymorph", "3.0.0", "1.22.7", "clean", true, replaces: "errors", otherModsChanged: true)!.ToJson();

        Assert.Equal("auto", json.Value<string>("Source"));
        Assert.Equal("clean", json.Value<string>("Result"));
        Assert.Equal("errors", json.Value<string>("Replaces"));
        Assert.True(json.Value<bool>("OtherModsChanged"));
        Assert.True(json.Value<bool>("Singleplayer"));
    }

    [Fact]
    public void AutoFirstResultHasNoOtherModsChanged()
    {
        JObject json = WorksResult.Auto("polymorph", "3.0.0", "1.22.7", "errors", false, otherModsChanged: true)!.ToJson();

        Assert.Null(json["Replaces"]);
        Assert.Null(json["OtherModsChanged"]);
    }

    [Theory]
    [InlineData("clean", "clean")] // Replaces must differ from Result
    [InlineData("broken", null)]
    [InlineData("clean", "maybe")]
    public void AutoRefusesWhatTheRelayWouldRefuse(string result, string? replaces)
    {
        Assert.Null(WorksResult.Auto("polymorph", "3.0.0", "1.22.7", result, false, replaces));
    }

    [Fact]
    public void KeyIsModAndBothVersions()
    {
        Assert.Equal("polymorph@3.0.0@1.22.7", WorksResult.Manual("polymorph", "3.0.0", "1.22.7", false)!.Key);
    }

    [Fact]
    public void ManualIsRememberedAcrossLoads()
    {
        string path = TempFile();
        try
        {
            var sent = new WorksSent(path);
            Assert.False(sent.HasManual("polymorph@3.0.0@1.22.7"));

            sent.MarkManual("polymorph@3.0.0@1.22.7", new DateTime(2026, 11, 24, 10, 20, 0, DateTimeKind.Utc));
            var again = new WorksSent(path);

            Assert.True(again.HasManual("polymorph@3.0.0@1.22.7"));
            // A new version is a new pair
            Assert.False(again.HasManual("polymorph@3.0.1@1.22.7"));
            JObject file = JObject.Parse(File.ReadAllText(path));
            Assert.Equal(1, file.Value<int>("version"));
            Assert.NotNull(file["sent"]!["polymorph@3.0.0@1.22.7"]!["manual"]!["at"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BrokenFileIsReportedAndLeftAlone()
    {
        string path = TempFile();
        try
        {
            File.WriteAllText(path, "{ not json");
            var sent = new WorksSent(path);
            Assert.NotNull(sent.Problem);

            sent.MarkManual("polymorph@3.0.0@1.22.7", DateTime.UtcNow);
            Assert.Equal("{ not json", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"works-sent-test-{Guid.NewGuid():N}.json");
}

/// <summary>What the automatic check sends, from works-sent.json</summary>
public class WorksDecisionTests : IDisposable
{
    private const string Key = "polymorph@3.0.0@1.22.7";
    private readonly string path = Path.Combine(Path.GetTempPath(), $"works-decide-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(path);

    private WorksResult? Decide(WorksSent sent, string result, string fingerprint) => sent.Decide("polymorph", "3.0.0", "1.22.7", result, fingerprint, true);

    private static void Count(WorksSent sent, WorksResult result, string fingerprint)
    {
        sent.SetPending(result.Key, result.ToJson(), fingerprint);
        sent.MarkAuto(result.Key, result.Result, fingerprint, DateTime.UtcNow);
    }

    [Fact]
    public void FirstResultIsSentAsItIs()
    {
        WorksResult first = Decide(new WorksSent(path), "errors", "A")!;
        Assert.Equal("errors", first.Result);
        Assert.Null(first.Replaces);
    }

    [Fact]
    public void SameResultSendsNothing()
    {
        var sent = new WorksSent(path);
        Count(sent, Decide(sent, "clean", "A")!, "A");
        Assert.Null(Decide(sent, "clean", "A"));
        Assert.Null(Decide(sent, "clean", "B"));
    }

    [Fact]
    public void ChangeAfterOtherModsChanged()
    {
        var sent = new WorksSent(path);
        Count(sent, Decide(sent, "errors", "A")!, "A");

        WorksResult change = Decide(sent, "clean", "B")!;
        Assert.Equal("errors", change.Replaces);
        Assert.True(change.OtherModsChanged);
    }

    [Fact]
    public void ChangeWithSameMods()
    {
        var sent = new WorksSent(path);
        Count(sent, Decide(sent, "clean", "A")!, "A");

        WorksResult change = Decide(sent, "errors", "A")!;
        Assert.Equal("clean", change.Replaces);
        Assert.False(change.OtherModsChanged);
    }

    [Fact]
    public void FingerprintStaysTheOneOfTheLastResultSent()
    {
        // errors with A, still errors after changing to B (nothing sent), clean with B: the mods did change since the errors
        var sent = new WorksSent(path);
        Count(sent, Decide(sent, "errors", "A")!, "A");
        Assert.Null(Decide(sent, "errors", "B"));

        Assert.True(Decide(sent, "clean", "B")!.OtherModsChanged);
    }

    [Fact]
    public void NotCountedUntilTheRelayTakesIt()
    {
        var sent = new WorksSent(path);
        WorksResult first = Decide(sent, "errors", "A")!;
        sent.SetPending(Key, first.ToJson(), "A");

        // Still a first result next time: the pending one wasn't counted
        Assert.Null(Decide(new WorksSent(path), "errors", "A")!.Replaces);
        Assert.Equal("A", new WorksSent(path).GetPending(Key)!.Value.Fingerprint);
    }

    [Fact]
    public void PendingIsDroppedAfterTheSecondFailure()
    {
        var sent = new WorksSent(path);
        sent.SetPending(Key, Decide(sent, "clean", "A")!.ToJson(), "A");

        Assert.True(sent.FailPending(Key));
        Assert.Contains(Key, new WorksSent(path).PendingKeys);
        Assert.False(sent.FailPending(Key));
        Assert.Empty(new WorksSent(path).PendingKeys);
    }

    [Fact]
    public void CountedRemovesPending()
    {
        var sent = new WorksSent(path);
        Count(sent, Decide(sent, "clean", "A")!, "A");
        Assert.Empty(sent.PendingKeys);
        Assert.Equal("clean", new WorksSent(path).LastAuto(Key));
    }

    [Fact]
    public void PruneKeepsInstalledAndRecent()
    {
        var sent = new WorksSent(path);
        DateTime now = new(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);
        sent.MarkManual("old@1.0@1.21.0", now.AddDays(-100));
        sent.MarkManual("recent@1.0@1.21.0", now.AddDays(-10));
        sent.MarkManual("installed@1.0@1.22.7", now.AddDays(-100));

        sent.Prune(new HashSet<string> { "installed@1.0@1.22.7" }, now);
        var again = new WorksSent(path);

        Assert.False(again.HasManual("old@1.0@1.21.0"));
        Assert.True(again.HasManual("recent@1.0@1.21.0"));
        Assert.True(again.HasManual("installed@1.0@1.22.7"));
    }
}
