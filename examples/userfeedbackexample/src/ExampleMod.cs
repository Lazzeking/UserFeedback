using System;
using UserFeedbackInterop;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace UserFeedbackExample;

/// <summary>
/// Opens this mod's own feedback panel or bug report, adds its state to reports and thanks the player once a report is
/// handled. Everything goes through UserFeedbackApi.cs, copied from api/ (linked here), which uses events by name: this
/// mod does not reference UserFeedback.dll and still loads when UserFeedback is not installed.
/// </summary>
public class ExampleMod : ModSystem
{
    /// <summary>The prefix listed under "logMatch" in this mod's userfeedback.json, so these lines join its log extract</summary>
    private const string LogPrefix = "ExampleProbe";

    private int timesOpened;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI capi)
    {
        string modId = Mod.Info.ModID;

        capi.ChatCommands.Create("example")
            .WithDescription("Open this mod's feedback panel, with its own wording. \".example bug\" opens a bug report with data attached.")
            .RequiresPrivilege(Privilege.chat)
            .WithArgs(capi.ChatCommands.Parsers.OptionalWord("kind"))
            .HandleWith(args => args.Parsers[0].IsMissing ? OpenFeedback(capi) : OpenBugReport(capi));

        // Added to every report about this mod, on F8 too
        UserFeedbackApi.OnCollect(capi, modId, mine => mine.SetInt("timesOpened", timesOpened));

        // Only says thanks. Any mod could push this event, so nothing important depends on it.
        UserFeedbackApi.OnSent(capi, modId, report => capi.ShowChatMessage(
            report.Delivered ? $"Example Mod: thanks, your {report.Kind} report arrived."
            : report.Opened ? $"Example Mod: thanks! Finish sending your {report.Kind} report in the browser."
            : report.Saved ? $"Example Mod: your {report.Kind} report is saved on your computer."
            : $"Example Mod: your {report.Kind} report didn't get through ({report.Outcome})."));
    }

    private TextCommandResult OpenFeedback(ICoreClientAPI capi)
    {
        timesOpened++;
        return UserFeedbackApi.OpenFeedback(capi, Mod.Info.ModID)
            ? TextCommandResult.Success()
            : TextCommandResult.Error("UserFeedback isn't installed.");
    }

    // What a "Report this error" button would do: some text the player can change, and data for this one report
    private TextCommandResult OpenBugReport(ICoreClientAPI capi)
    {
        if (!UserFeedbackApi.Installed(capi)) return TextCommandResult.Error("UserFeedback isn't installed.");
        timesOpened++;

        Exception error = FakeError();
        // Doesn't name the modid, so only "logMatch" gets it into the report's log lines
        capi.Logger.Warning("{0}: the sample recipe failed to load: {1}", LogPrefix, error.Message);

        var data = new TreeAttribute();
        data.SetString("recipe", "userfeedbackexample:sample-recipe");
        data.SetString("exception", error.ToString());
        data.SetInt("attempts", 3);

        UserFeedbackApi.OpenBugReport(capi, Mod.Info.ModID, $"The sample recipe failed to load: {error.Message}", data);
        return TextCommandResult.Success();
    }

    private static Exception FakeError()
    {
        try
        {
            throw new InvalidOperationException("ingredient 'game:clay-blue' not found");
        }
        catch (Exception e)
        {
            return e;
        }
    }
}
