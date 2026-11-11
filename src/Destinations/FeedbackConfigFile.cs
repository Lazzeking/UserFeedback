using System;
using System.Collections.Generic;
using Vintagestory.API.Config;

namespace UserFeedback;

/// <summary>
/// assets/&lt;modid&gt;/config/userfeedback.json, shipped by any mod that wants feedback. Plain JSON, so the mod
/// does not depend on UserFeedback: without it installed the file is simply never read.
/// </summary>
public class FeedbackConfigFile
{
    /// <summary>Where reports about this mod can go. The player picks one, saving on their computer is always offered too.</summary>
    public List<DestinationConfig> Destinations = new();
}

public class DestinationConfig
{
    /// <summary>github, webpage or http</summary>
    public string? Type;

    /// <summary>github: "owner/repo"</summary>
    public string? Repo;

    /// <summary>github: labels added to the issue, if the repo has them</summary>
    public List<string>? Labels;

    /// <summary>webpage: the page the report is pasted into. http: the endpoint reports are uploaded to.</summary>
    public string? Url;

    /// <summary>Optional name shown to the player instead of the generated one, for example "Our Discord's #bugs form"</summary>
    public string? Label;

    /// <summary>The name the player sees in the "Send to" list. Only call it on a destination the registry checked.</summary>
    public string Describe()
    {
        if (!string.IsNullOrWhiteSpace(Label)) return Label!;

        return Type switch
        {
            DestinationRegistry.GitHub => Lang.Get($"{Core.ModId}:route-github", Repo),
            DestinationRegistry.WebPage => Lang.Get($"{Core.ModId}:route-webpage", new Uri(Url!).Host),
            _ => Lang.Get($"{Core.ModId}:route-http", new Uri(Url!).Host),
        };
    }

    /// <summary>The repo for github, the url otherwise</summary>
    public string Target => Type == DestinationRegistry.GitHub ? Repo! : Url!;
}
