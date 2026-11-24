using UserFeedbackInterop;
using Vintagestory.API.Datastructures;
using Xunit;

namespace UserFeedback.Tests;

/// <summary>api/UserFeedbackApi.cs, the file modders copy: what it sends is what UserFeedback reads</summary>
public class ApiHelperTests
{
    [Fact]
    public void BugReportRequestIsRead()
    {
        var data = new TreeAttribute();
        data.SetString("recipe", "polymorph:clay-bowl");

        OpenRequest request = OpenRequest.From(UserFeedbackApi.Request("polymorph", UserFeedbackApi.Bug, "It broke", data));

        Assert.Equal("polymorph", request.ModId);
        Assert.Equal(FeedbackReport.Bug, request.Kind);
        Assert.Equal("It broke", request.Text);
        Assert.Equal("polymorph:clay-bowl", (string?)request.Data!["recipe"]);
    }

    [Theory]
    [InlineData(UserFeedbackApi.Feedback)]
    [InlineData(UserFeedbackApi.Works)]
    [InlineData(null)]
    public void KindsMatch(string? kind)
    {
        Assert.Equal(kind, OpenRequest.From(UserFeedbackApi.Request("polymorph", kind, null, null)).Kind);
    }

    [Fact]
    public void EventNamesMatch()
    {
        Assert.Equal(FeedbackEvents.Collect, UserFeedbackApi.CollectEvent);
        Assert.Equal(FeedbackEvents.Open, UserFeedbackApi.OpenEvent);
        Assert.Equal(FeedbackEvents.Sent, UserFeedbackApi.SentEvent);
        Assert.Equal(Core.ModId, UserFeedbackApi.ModId);
    }

    [Fact]
    public void SentIsReadForItsModOnly()
    {
        TreeAttribute sent = FeedbackEvents.SentData("polymorph", FeedbackReport.Feedback, FeedbackEvents.Opened);

        SentReport? mine = UserFeedbackApi.ReadSent(sent, "polymorph");
        Assert.NotNull(mine);
        Assert.Equal("feedback", mine!.Kind);
        Assert.True(mine.Opened);
        Assert.Null(UserFeedbackApi.ReadSent(sent, "othermod"));
    }
}
