using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using static UserFeedback.ModLang;

namespace UserFeedback;

/// <summary>
/// Client side commands, run with a "." instead of a "/": ".userfeedback", or ".uf" for short.
/// </summary>
public class Commands
{
    public Commands(ICoreClientAPI capi, Core core, DestinationRegistry registry, RelayRegistry relay, WorksCheck worksCheck)
    {
        capi.ChatCommands.Create(Core.ModId)
            .WithAlias("uf")
            .WithDescription("UserFeedback tools")
            .RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("open")
                .WithDescription("Open the feedback panel, like F8. With bug, feedback or works it skips the question.")
                .WithArgs(capi.ChatCommands.Parsers.OptionalWordRange("kind", FeedbackReport.Bug, FeedbackReport.Feedback, WorksDialog.Choice))
                .HandleWith(args =>
                {
                    core.OpenDialog(kind: args.Parsers[0].IsMissing ? null : (string)args[0]);
                    return TextCommandResult.Success();
                })
            .EndSubCommand()
            .BeginSubCommand("folder")
                .WithDescription("Open the folder with your saved reports")
                .HandleWith(_ =>
                {
                    try
                    {
                        ReportWriter.OpenFolder(ReportWriter.RootFolder);
                        return TextCommandResult.Success();
                    }
                    catch (Exception e)
                    {
                        return TextCommandResult.Error(L("folder-failed", ReportWriter.RootFolder, e.Message));
                    }
                })
            .EndSubCommand()
            .BeginSubCommand("destinations")
                .WithDescription("For mod authors: list every mod's feedback destinations and mistakes in their userfeedback.json")
                .HandleWith(_ => ListDestinations(capi, registry, relay))
            .EndSubCommand()
            .BeginSubCommand("check")
                .WithDescription("For mod authors: what a bug report about this mod would hold now, and why something is missing. Sends nothing.")
                .WithArgs(capi.ChatCommands.Parsers.Word("modid"))
                .HandleWith(args => TextCommandResult.Success(SetupCheck.Run(capi, registry, relay, ((string)args[0]).ToLowerInvariant())))
            .EndSubCommand()
            .BeginSubCommand("workscheck")
                .WithDescription("Run the \"works for me\" check now instead of 3 minutes after joining. Results already counted aren't sent again. The outcome is in the client log.")
                .HandleWith(_ => worksCheck.Run() is string why
                    ? TextCommandResult.Error($"Nothing checked: {why}.")
                    : TextCommandResult.Success("Checking the logs, results go out over the next minute. The client log says what was found."))
            .EndSubCommand();
    }

    private static TextCommandResult ListDestinations(ICoreClientAPI capi, DestinationRegistry registry, RelayRegistry relay)
    {
        var lines = new List<string> { RelayLine(capi, relay) };
        foreach ((string modId, FeedbackConfigFile file) in registry.Files.OrderBy(pair => pair.Key))
        {
            lines.Add($"{modId}:");
            lines.AddRange(file.Destinations.Select(d => "  " + d.Summary()));
            if (file.LogMatch.Count > 0) lines.Add($"  log lines also starting with: {string.Join(", ", file.LogMatch)}");
            if (file.Panel != null)
            {
                string faces = string.Join(", ", FeedbackReport.Ratings.Where(code => file.Panel.Face(code)?.Icon != null));
                lines.Add(faces.Length == 0 ? "  panel: its own wording" : $"  panel: its own wording, custom faces: {faces}");
            }
        }
        if (registry.Files.Count == 0) lines.Add(L("no-destinations"));
        lines.AddRange(registry.Problems.Select(problem => "! " + problem));
        foreach ((string who, List<string> methods) in PatchAudit.ForeignPatches(capi))
        {
            lines.Add($"! {who} changes UserFeedback's code with Harmony: {string.Join(", ", methods.Distinct())}");
        }
        if (relay.Problem != null) lines.Add("! " + relay.Problem);
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    // Whether the relay is on, how old its list is and which of the loaded mods are on it
    private static string RelayLine(ICoreClientAPI capi, RelayRegistry relay)
    {
        if (relay.BaseUrl == null) return "UserFeedback relay: off";
        if (relay.FetchedUtc is not DateTime fetched) return $"UserFeedback relay ({relay.Host}): no list fetched yet";

        string installed = string.Join(", ", relay.ModIds.Where(id => capi.ModLoader.GetMod(id) != null).OrderBy(id => id)
            .Select(id => Describe(relay, id)));
        string list = relay.Bucketed
            // Only the buckets of mods picked in the panel are fetched, so mods not picked yet can't be shown
            ? $"split into buckets, {relay.CachedBuckets} fetched, which hold {relay.ModIds.Count} mod(s)"
            : $"{relay.ModIds.Count} mod(s)";
        return $"UserFeedback relay ({relay.Host}): list from {fetched.ToLocalTime():yyyy-MM-dd HH:mm}, {list}, "
            + (installed.Length > 0 ? $"of yours: {installed}" : relay.Bucketed ? "none of yours picked yet" : "none of yours");
    }

    // "polymorph (bug only, works for me)", "othermod (works for me only)"
    private static string Describe(RelayRegistry relay, string modId)
    {
        var notes = new List<string>();
        if (!relay.Has(modId)) notes.Add("no reports");
        else if (relay.KindsOf(modId) is { } kinds) notes.Add(kinds.Count == 0 ? "no kinds this version knows" : string.Join(" and ", kinds) + " only");
        if (relay.CountsWorks(modId)) notes.Add("counts works for me");
        return notes.Count == 0 ? modId : $"{modId} ({string.Join(", ", notes)})";
    }
}
