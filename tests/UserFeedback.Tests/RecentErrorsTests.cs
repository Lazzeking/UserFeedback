using System;
using System.Collections.Generic;
using Xunit;

namespace UserFeedback.Tests;

/// <summary>F8 picks the mod of a recent error when nothing the player looks at or holds names one</summary>
public class RecentErrorsTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly List<(string, Func<string, bool>)> Mods = new()
    {
        ("polymorph", text => text.Contains("Polymorph")),
        ("othermod", text => text.Contains("OtherMod")),
    };

    [Fact]
    public void NewestErrorAboutOneModWins()
    {
        var errors = new[]
        {
            (Now.AddMinutes(-3), "Exception in OtherMod.Tick"),
            (Now.AddMinutes(-1), "Exception in Polymorph.Biomes"),
        };
        Assert.Equal("polymorph", RecentErrors.Pick(errors, Now, Mods));
    }

    [Fact]
    public void ErrorsNamingSeveralModsArePassedOver()
    {
        var errors = new[]
        {
            (Now.AddMinutes(-3), "Exception in OtherMod.Tick"),
            (Now.AddMinutes(-1), "Polymorph called OtherMod and failed"),
        };
        Assert.Equal("othermod", RecentErrors.Pick(errors, Now, Mods));
    }

    [Fact]
    public void OldErrorsAndUnknownErrorsPickNothing()
    {
        var errors = new[]
        {
            (Now - RecentErrors.MaxAge - TimeSpan.FromSeconds(1), "Exception in Polymorph.Biomes"),
            (Now.AddSeconds(-10), "Vintagestory.Client failed to load a texture"),
        };
        Assert.Null(RecentErrors.Pick(errors, Now, Mods));
    }
}
