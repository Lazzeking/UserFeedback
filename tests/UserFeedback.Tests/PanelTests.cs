using System.Collections.Generic;
using Newtonsoft.Json;
using Xunit;

namespace UserFeedback.Tests;

/// <summary>A mod's own wording for the panel (docs/panel.md). A bad field is left out, the rest stays.</summary>
public class PanelTests
{
    private const string Sample = """
        {
          "panel": {
            "title": "Polymorph",
            "titleKey": "polymorph:panel-title",
            "feedback": { "heading": "How are the biomes?", "prompt": "How is Polymorph treating you?", "text": "What should change?" },
            "bug": { "heading": "Something broke", "text": "What broke?" },
            "ratings": {
              "great": { "label": "Love them", "icon": "polymorph:textures/icons/great.svg" },
              "bad": { "label": "Not for me" }
            }
          }
        }
        """;

    private static PanelConfig? Check(string json, out List<string> problems, string modId = "polymorph")
    {
        PanelConfig? panel = JsonConvert.DeserializeObject<FeedbackConfigFile>(json)!.Panel;
        problems = new List<string>();
        return PanelRules.Check(modId, panel, problems);
    }

    [Fact]
    public void APanelKeepsItsWordingAndFaces()
    {
        PanelConfig? panel = Check(Sample, out List<string> problems);

        Assert.Empty(problems);
        Assert.Equal("Polymorph", panel!.Title);
        Assert.Equal("polymorph:panel-title", panel.TitleKey);
        Assert.Equal("How are the biomes?", panel.Feedback!.Heading);
        Assert.Equal("What broke?", panel.Bug!.Text);
        Assert.Equal("Love them", panel.Face("great")!.Label);
        Assert.Equal("polymorph:textures/icons/great.svg", panel.Face("great")!.Icon);
        Assert.Equal("Not for me", panel.Face("bad")!.Label);
        Assert.True(panel.Customized);
    }

    [Fact]
    public void NoPanelIsTheStandardOne()
    {
        PanelConfig? panel = Check("""{ "destinations": [] }""", out List<string> problems);

        Assert.Null(panel);
        Assert.Empty(problems);
    }

    [Fact]
    public void AnEmptyPanelIsTheStandardOne()
    {
        Assert.Null(Check("""{ "panel": {} }""", out List<string> problems));
        Assert.Empty(problems);
    }

    [Fact]
    public void ALongTitleIsLeftOut()
    {
        PanelConfig? panel = Check($$"""{ "panel": { "title": "{{new string('x', 41)}}", "send": "Go" } }""", out List<string> problems);

        Assert.Null(panel!.Title);
        Assert.Equal("Go", panel.Send);
        Assert.Contains(problems, problem => problem.Contains("title"));
    }

    [Fact]
    public void AKeyFromAnotherModIsLeftOut()
    {
        PanelConfig? panel = Check("""{ "panel": { "title": "Polymorph", "titleKey": "other:title" } }""", out List<string> problems);

        Assert.Equal("Polymorph", panel!.Title);
        Assert.Null(panel.TitleKey);
        Assert.Contains(problems, problem => problem.Contains("polymorph:"));
    }

    [Fact]
    public void AnUnknownFaceIsLeftOut()
    {
        PanelConfig? panel = Check("""{ "panel": { "ratings": { "lovely": { "label": "Lovely" }, "good": { "label": "Fine" } } } }""", out List<string> problems);

        Assert.Null(panel!.Face("lovely"));
        Assert.Equal("Fine", panel.Face("good")!.Label);
        Assert.Contains(problems, problem => problem.Contains("lovely"));
    }

    [Fact]
    public void AnIconOutsideTheModIsLeftOut()
    {
        PanelConfig? panel = Check("""{ "panel": { "ratings": { "great": { "label": "Love them", "icon": "other:textures/icons/great.svg" } } } }""", out List<string> problems);

        Assert.Equal("Love them", panel!.Face("great")!.Label);
        Assert.Null(panel.Face("great")!.Icon);
        Assert.Contains(problems, problem => problem.Contains("icon"));
    }

    [Fact]
    public void ABugPromptIsLeftOut()
    {
        PanelConfig? panel = Check("""{ "panel": { "bug": { "prompt": "How is it?", "text": "What broke?" } } }""", out List<string> problems);

        Assert.Null(panel!.Bug!.Prompt);
        Assert.Equal("What broke?", panel.Bug.Text);
        Assert.Contains(problems, problem => problem.Contains("prompt"));
    }

    [Fact]
    public void FeedbackDetailsAreLeftOut()
    {
        PanelConfig? panel = Check("""{ "panel": { "feedback": { "prompt": "How is it?", "details": "Include logs" } } }""", out List<string> problems);

        Assert.Equal("How is it?", panel!.Feedback!.Prompt);
        Assert.Null(panel.Feedback.Details);
        Assert.Contains(problems, problem => problem.Contains("details"));
    }
}
