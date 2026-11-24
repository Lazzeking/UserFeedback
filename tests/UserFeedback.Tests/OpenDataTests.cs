using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using Xunit;

namespace UserFeedback.Tests;

/// <summary>"data" on userfeedback:open: added to that one report's data for the mod, within collect's limits</summary>
public class OpenDataTests
{
    private static OpenRequest Open(TreeAttribute data)
    {
        var tree = new TreeAttribute();
        tree.SetString("modid", "polymorph");
        tree.SetString("kind", "bug");
        tree["data"] = data;
        return OpenRequest.From(tree);
    }

    [Fact]
    public void DataIsRead()
    {
        var data = new TreeAttribute();
        data.SetString("recipe", "polymorph:clay-bowl");
        data.SetInt("attempts", 3);

        OpenRequest request = Open(data);

        Assert.Null(request.DataProblem);
        Assert.Equal("polymorph:clay-bowl", (string?)request.Data!["recipe"]);
        Assert.Equal(3, (int)request.Data["attempts"]!);
    }

    [Fact]
    public void NoDataIsNull()
    {
        var tree = new TreeAttribute();
        tree.SetString("modid", "polymorph");
        Assert.Null(OpenRequest.From(tree).Data);
        Assert.Null(OpenRequest.From(new StringAttribute("polymorph")).Data);
    }

    [Fact]
    public void DataThatIsNotATreeIsLeftOut()
    {
        var tree = new TreeAttribute();
        tree.SetString("modid", "polymorph");
        tree.SetString("data", "just text");

        OpenRequest request = OpenRequest.From(tree);

        Assert.Null(request.Data);
        Assert.NotNull(request.DataProblem);
    }

    [Fact]
    public void FilesAreTakenOut()
    {
        var data = new TreeAttribute();
        data.SetString("files", "ModConfig/other.json");
        data.SetString("recipe", "x");

        OpenRequest request = Open(data);

        Assert.Null(request.Data!["files"]);
        Assert.Equal("x", (string?)request.Data["recipe"]);
        Assert.NotNull(request.DataProblem);
    }

    [Fact]
    public void TooBigDataIsLeftOut()
    {
        var data = new TreeAttribute();
        data.SetString("dump", new string('a', ModContributions.MaxModBytes + 1));

        OpenRequest request = Open(data);

        Assert.Null(request.Data);
        Assert.NotNull(request.DataProblem);
    }

    [Fact]
    public void AttachedDataReplacesCollectedKeysAndKeepsTheRest()
    {
        var contributions = new ModContributions();
        contributions.Data["polymorph"] = JObject.Parse("""{ "biome": "tundra", "recipe": "old" }""");

        contributions.Attach("polymorph", JObject.Parse("""{ "recipe": "new" }"""));

        Dictionary<string, JToken> kept = contributions.For(new[] { "polymorph" }, out List<string> leftOut);
        Assert.Empty(leftOut);
        Assert.Equal("tundra", (string?)kept["polymorph"]["biome"]);
        Assert.Equal("new", (string?)kept["polymorph"]["recipe"]);
    }

    [Fact]
    public void WhenBothAreTooBigOnlyTheAttachedDataIsKept()
    {
        var contributions = new ModContributions();
        contributions.Data["polymorph"] = new JObject { ["dump"] = new string('a', ModContributions.MaxModBytes - 100) };

        contributions.Attach("polymorph", new JObject { ["recipe"] = new string('b', 200) });

        JToken kept = contributions.Data["polymorph"];
        Assert.Null(kept["dump"]);
        Assert.NotNull(kept["recipe"]);
    }

    [Fact]
    public void AttachedDataReplacesCollectDataThatWasTooBig()
    {
        var contributions = new ModContributions();
        contributions.TooBig.Add("polymorph");

        contributions.Attach("polymorph", JObject.Parse("""{ "recipe": "x" }"""));

        Assert.Contains("polymorph", contributions.For(new[] { "polymorph" }, out _).Keys);
    }

    [Fact]
    public void AttachedDataIsOnlyInReportsAboutThatMod()
    {
        var contributions = new ModContributions();
        contributions.Attach("polymorph", JObject.Parse("""{ "recipe": "x" }"""));

        Assert.Empty(contributions.For(new[] { "othermod" }, out _));
    }
}

/// <summary>userfeedback:sent: what was done with a report, never what's in it</summary>
public class SentEventTests
{
    [Fact]
    public void SentHoldsOnlyModKindAndOutcome()
    {
        TreeAttribute tree = FeedbackEvents.SentData("polymorph", FeedbackReport.Bug, FeedbackEvents.Delivered);

        Assert.Equal(3, tree.Count);
        Assert.Equal("polymorph", tree.GetString("modid"));
        Assert.Equal("bug", tree.GetString("kind"));
        Assert.Equal("delivered", tree.GetString("outcome"));
    }
}
