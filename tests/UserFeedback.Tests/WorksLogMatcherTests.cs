using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UserFeedback.Tests;

/// <summary>The error matching of the automatic check, on log snippets from real games</summary>
public class WorksLogMatcherTests
{
    // From a test instance's client-main.log: a code mod failing to start, with the game's own lines about it
    private const string SpearTrajectoryFails = """
        12.11.2026 15:10:08 [Notification] Loaded Shaderprogramm for render pass sky.
        12.11.2026 15:10:08 [Error] [speartrajectory] An exception was thrown when trying to start the mod:
        12.11.2026 15:10:08 [Error] [speartrajectory] Exception: Object reference not set to an instance of an object.
           at HarmonyLib.Harmony.PatchAll()
           at SpearTrajectory.Systems.TrajectoryModSystem.StartClientSide_Patch1(TrajectoryModSystem this, ICoreClientAPI api)
           at Vintagestory.Common.ModLoader.TryRunModPhase(Mod mod, ModSystem system, ICoreAPI api, ModRunPhase phase) in VintagestoryLib\Common\API\ModLoader.cs:line 676
        12.11.2026 15:10:08 [Error] Failed to start system SpearTrajectory.Systems.TrajectoryModSystem
        12.11.2026 15:10:08 [Notification] Started 101 systems on Client:
        12.11.2026 15:10:08 [Notification]         FluffyClouds.FluffyCloudsModSystem
        """;

    // A content mod whose recipes need items another mod should add
    private const string ArmoryRecipes = """
        27.10.2026 10:28:53 [Warning] Failed resolving crafting recipe ingredient with code armory:part-battlehammerhead-steel in Grid recipe
        27.10.2026 10:28:53 [Error] Grid Recipe with output 'armory:part-battlehammerhead-steel' contains an ingredient that cannot be resolved: Item code armory:part-battlehammerhead-steel
        """;

    private static readonly WorksModInfo SpearTrajectory = new("speartrajectory", "Spear Trajectory", "speartrajectory_1.0.0.zip", new[] { "SpearTrajectory" });
    private static readonly WorksModInfo FluffyClouds = new("fluffyclouds", "Fluffy Clouds", "fluffyclouds.zip", new[] { "FluffyClouds" });

    private static List<string> Entries(string log) => LogSnapshot.Split("client-main.log", log).Select(entry => entry.Text).ToList();

    [Fact]
    public void ModFailingToStartHasErrors()
    {
        Assert.True(WorksLogMatcher.HasErrors(Entries(SpearTrajectoryFails), SpearTrajectory));
    }

    [Fact]
    public void ModOnlyListedInNotificationsIsClean()
    {
        Assert.False(WorksLogMatcher.HasErrors(Entries(SpearTrajectoryFails), FluffyClouds));
    }

    [Fact]
    public void StackFrameAloneCounts()
    {
        // No modid in the message, only the mod's code in the trace
        const string log = """
            24.11.2026 10:00:00 [Error] Exception thrown during handling event OnGameWorldBeingSaved. Will skip over.
            System.NullReferenceException: Object reference not set to an instance of an object.
               at FluffyClouds.Rendering.CloudRenderer.OnSave()
               at Vintagestory.Server.ServerEventManager.TriggerGameWorldBeingSaved()
            """;
        Assert.True(WorksLogMatcher.HasErrors(Entries(log), FluffyClouds));
        Assert.False(WorksLogMatcher.HasErrors(Entries(log), SpearTrajectory));
    }

    [Fact]
    public void ContentModsAssetDomainCounts()
    {
        var armory = new WorksModInfo("armory", "Armory", null, Array.Empty<string>());
        Assert.True(WorksLogMatcher.HasErrors(Entries(ArmoryRecipes), armory));
    }

    [Fact]
    public void WarningsDontCount()
    {
        const string log = "24.11.2026 10:00:00 [Warning] [speartrajectory] Something looks odd\n";
        Assert.False(WorksLogMatcher.HasErrors(Entries(log), SpearTrajectory));
    }

    [Fact]
    public void FatalCounts()
    {
        const string log = "24.11.2026 10:00:00 [Fatal] Spear Trajectory broke everything\n";
        Assert.True(WorksLogMatcher.HasErrors(Entries(log), SpearTrajectory));
    }

    [Fact]
    public void ModIdOnlyAsAWholeWord()
    {
        var ore = new WorksModInfo("ore", null, null, Array.Empty<string>());
        const string log = "24.11.2026 10:00:00 [Error] Could not load more textures\n";
        Assert.False(WorksLogMatcher.HasErrors(Entries(log), ore));
    }

    [Fact]
    public void ShortNamesAreNotMatched()
    {
        // "Ore" is under 4 characters, so only the modid "oremod" would count
        var mod = new WorksModInfo("oremod", "Ore", null, Array.Empty<string>());
        const string log = "24.11.2026 10:00:00 [Error] Ore block missing texture\n";
        Assert.False(WorksLogMatcher.HasErrors(Entries(log), mod));
    }

    [Fact]
    public void UserFeedbacksOwnLinesDontCountForOtherMods()
    {
        const string log = "24.11.2026 10:00:00 [Error] [userfeedback] Could not collect state from speartrajectory: timeout\n";
        Assert.False(WorksLogMatcher.HasErrors(Entries(log), SpearTrajectory));
        // but they do for UserFeedback itself
        var userFeedback = new WorksModInfo("userfeedback", "UserFeedback", null, new[] { "UserFeedback" });
        Assert.True(WorksLogMatcher.HasErrors(Entries(log), userFeedback));
    }

    [Fact]
    public void ZipFileNameCountsButFolderNameDoesnt()
    {
        const string log = """
            24.11.2026 10:00:00 [Error] An exception was thrown when trying to load assembly: speartrajectory_1.0.0.zip
            24.11.2026 10:00:01 [Error] Missing file in /home/me/bin/Debug/net10.0/assets
            """;
        Assert.True(WorksLogMatcher.HasErrors(Entries(log).Take(1), SpearTrajectory with { ModId = "zzz", Name = null, Namespaces = Array.Empty<string>() }));
        var folderMod = new WorksModInfo("zzz", null, "net10.0", Array.Empty<string>());
        Assert.False(WorksLogMatcher.HasErrors(Entries(log), folderMod));
    }

    [Fact]
    public void NamespaceNeedsATypeAfterIt()
    {
        // "Polymorph." at the end of a sentence isn't code
        var polymorph = new WorksModInfo("pm", null, null, new[] { "Polymorph" });
        Assert.False(WorksLogMatcher.HasErrors(Entries("24.11.2026 10:00:00 [Error] Please update Polymorph.\n"), polymorph));
        Assert.True(WorksLogMatcher.HasErrors(Entries("24.11.2026 10:00:00 [Error] Failed to start system Polymorph.Core\n"), polymorph));
    }

    [Fact]
    public void NamespacePrefixesSkipGameAndLibraries()
    {
        var prefixes = WorksLogMatcher.NamespacePrefixes(new[] { "Polymorph.Systems", "Polymorph", "HarmonyLib", "System.Runtime", "Vintagestory.GameContent", null, "Ab" }, new HashSet<string>());
        Assert.Equal(new[] { "Polymorph" }, prefixes);
    }

    [Fact]
    public void SharedFirstSegmentUsesTwoSegments()
    {
        // Two mods by one author, both under "Lazzeking."
        var prefixes = WorksLogMatcher.NamespacePrefixes(new[] { "Lazzeking.Timers.Gui", "Lazzeking.Timers", "Lazzeking" }, new HashSet<string> { "Lazzeking" });
        Assert.Equal(new[] { "Lazzeking.Timers" }, prefixes);

        var timers = new WorksModInfo("lazzekingtimers", null, null, prefixes);
        const string other = """
            24.11.2026 10:00:00 [Error] Exception
               at Lazzeking.Barrels.BarrelSystem.Start()
            """;
        const string own = """
            24.11.2026 10:00:00 [Error] Exception
               at Lazzeking.Timers.Gui.TimerHud.Draw()
            """;
        Assert.False(WorksLogMatcher.HasErrors(Entries(other), timers));
        Assert.True(WorksLogMatcher.HasErrors(Entries(own), timers));
    }

    [Fact]
    public void FingerprintIgnoresOrderButNotVersions()
    {
        string a = WorksLogMatcher.Fingerprint(new[] { ("b", "1.0"), ("a", "2.0") });
        string b = WorksLogMatcher.Fingerprint(new[] { ("a", "2.0"), ("b", "1.0") });
        string c = WorksLogMatcher.Fingerprint(new[] { ("a", "2.0"), ("b", "1.1") });
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(64, a.Length);
    }
}
