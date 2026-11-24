using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace UserFeedback.Tests;

/// <summary>The helpers several parts share: log patterns, server answers, report attachments, small utilities</summary>
public class SharedHelpersTests
{
    private static bool Matches(List<string> tokens, string line) => new Regex(string.Join("|", tokens), RegexOptions.IgnoreCase).IsMatch(line);

    [Fact]
    public void AFolderModsFolderNameIsNotAToken()
    {
        // A dev build's folder is "net10.0": every log line naming .NET would count for it
        List<string> tokens = LogPatterns.IdentityTokens("polymorph", "Polymorph", "net10.0");
        Assert.False(Matches(tokens, "Running on net10.0"));
        Assert.True(Matches(tokens, "[polymorph] failed"));
    }

    [Fact]
    public void ZipAndDllNamesAreTokens()
    {
        Assert.True(Matches(LogPatterns.IdentityTokens("poly", null, "Polymorph_1.2.zip"), "Loading Polymorph_1.2.zip"));
        Assert.True(Matches(LogPatterns.IdentityTokens("poly", null, "Polymorph.dll"), "Could not load Polymorph.dll"));
    }

    [Theory]
    [InlineData("System", true)]
    [InlineData("VintagestoryLib", true)]
    [InlineData("harmonylib", true)]
    [InlineData("Polymorph", false)]
    [InlineData("MonoBlocks", false)]
    public void SharedNamespaces(string root, bool shared)
    {
        Assert.Equal(shared, LogPatterns.IsShared(root));
    }

    [Fact]
    public void ALoggerStartingLikeALibraryIsKept()
    {
        // Only the library's own namespace is refused, not every word starting with its letters
        Assert.Null(LogMatchRules.Refusal("MonoBlocks"));
        Assert.NotNull(LogMatchRules.Refusal("MonoMod.Patch"));
        Assert.NotNull(LogMatchRules.Refusal("Newt"));
    }

    [Fact]
    public void ServerReplyReadsTheFieldsOnce()
    {
        ServerReply reply = ServerReply.Parse("""{ "id": "R-12", "message": "<b>Thanks</b>\nfor it", "followUrl": "https://relay.example/f/abc" }""");
        Assert.Equal("R-12", reply.SafeId);
        Assert.Equal("Thanks for it", reply.SafeMessage);
        Assert.Equal("https://relay.example/f/abc", reply.FollowUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void AnAnswerThatIsntAnObjectHasNoFields(string body)
    {
        Assert.Equal(new ServerReply(null, null, null), ServerReply.Parse(body));
    }

    [Fact]
    public void AttachmentsListWhatTheFolderHolds()
    {
        var report = new FeedbackReport { HasScreenshot = true, LogEntries = 3, ExtraFiles = new() { "ModConfig/a.json" } };
        Assert.Equal(new[] { ReportFiles.Screenshot, ReportFiles.Log, ReportFiles.Files }, ReportText.Attachments(report));
        Assert.Empty(ReportText.Attachments(new FeedbackReport()));
    }

    [Fact]
    public void SmallHelpers()
    {
        Assert.Equal("512 B", Sizes.Format(512));
        Assert.Matches(@"^1[.,]5 KB$", Sizes.Format(1536)); // the player's locale decides the separator
        Assert.Equal(64, Hashing.Sha256Hex("polymorph").Length);
        Assert.True(ModIdRules.IsValid("poly_morph2"));
        Assert.False(ModIdRules.IsValid("Poly"));
        Assert.False(ModIdRules.IsValid(null));
    }
}
