using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace UserFeedback;

/// <summary>
/// Client side commands, run with a "." instead of a "/": ".userfeedback", or ".uf" for short.
/// </summary>
public class Commands
{
    public Commands(ICoreClientAPI capi, Core core, DestinationRegistry registry)
    {
        capi.ChatCommands.Create(Core.ModId)
            .WithAlias("uf")
            .WithDescription("UserFeedback tools")
            .RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("open")
                .WithDescription("Open the feedback panel, like F8")
                .HandleWith(_ =>
                {
                    core.OpenDialog();
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
                        return TextCommandResult.Error(Lang.Get($"{Core.ModId}:folder-failed", ReportWriter.RootFolder, e.Message));
                    }
                })
            .EndSubCommand()
            .BeginSubCommand("destinations")
                .WithDescription("For mod authors: list every mod's feedback destinations and mistakes in their userfeedback.json")
                .HandleWith(_ => ListDestinations(registry))
            .EndSubCommand();
    }

    private static TextCommandResult ListDestinations(DestinationRegistry registry)
    {
        var lines = new List<string>();
        foreach ((string modId, FeedbackConfigFile file) in registry.Files.OrderBy(pair => pair.Key))
        {
            lines.Add($"{modId}:");
            lines.AddRange(file.Destinations.Select(d => $"  {d.Describe()} ({d.Type} {d.Target})"));
        }
        if (lines.Count == 0) lines.Add(Lang.Get($"{Core.ModId}:no-destinations"));
        lines.AddRange(registry.Problems.Select(problem => "! " + problem));
        return TextCommandResult.Success(string.Join("\n", lines));
    }
}
