using Vintagestory.API.Datastructures;
using Xunit;

namespace UserFeedback.Tests;

/// <summary>What userfeedback:open takes from a mod (docs/for-modders.md, "Open the panel from your mod")</summary>
public class OpenRequestTests
{
    private static OpenRequest Tree(string? modId, string? kind, string? text = null)
    {
        var tree = new TreeAttribute();
        if (modId != null) tree.SetString("modid", modId);
        if (kind != null) tree.SetString("kind", kind);
        if (text != null) tree.SetString("text", text);
        return OpenRequest.From(tree);
    }

    [Theory]
    [InlineData("bug")]
    [InlineData("feedback")]
    [InlineData("works")]
    public void KnownKindsSkipTheQuestion(string kind)
    {
        Assert.Equal(kind, Tree("polymorph", kind).Kind);
    }

    [Fact]
    public void UnknownKindAsksTheQuestion()
    {
        Assert.Null(Tree("polymorph", "note").Kind);
    }

    [Fact]
    public void WorksKeepsTheModId()
    {
        OpenRequest request = Tree("polymorph", "works");
        Assert.Equal("polymorph", request.ModId);
    }

    [Fact]
    public void StringAttributeIsTheModId()
    {
        OpenRequest request = OpenRequest.From(new StringAttribute("polymorph"));
        Assert.Equal("polymorph", request.ModId);
        Assert.Null(request.Kind);
    }

    [Fact]
    public void BlankTextIsNoText()
    {
        Assert.Null(Tree("polymorph", "bug", "   ").Text);
    }
}
