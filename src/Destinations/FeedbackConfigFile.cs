using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using static UserFeedback.ModLang;

namespace UserFeedback;

/// <summary>
/// assets/&lt;modid&gt;/config/userfeedback.json, shipped by any mod that wants feedback. Plain JSON, so the mod
/// does not depend on UserFeedback: without it installed the file is simply never read.
/// </summary>
public class FeedbackConfigFile
{
    /// <summary>Where reports about this mod can go. The player picks one, saving on their computer is always offered too.</summary>
    public List<DestinationConfig> Destinations = new();

    /// <summary>
    /// Extra files to include in reports about this mod, relative to the VintagestoryData folder, like "ModConfig/mymod.json",
    /// "ModData/mymod/" or "Logs/mymod-*.log". See ExtraFiles for which ones are allowed.
    /// </summary>
    public List<string> Files = new();

    /// <summary>
    /// Short prefixes, like a logger name or a Harmony patch class, whose log lines go into this mod's log extract too.
    /// See LogMatchRules for which ones are allowed.
    /// </summary>
    public List<string> LogMatch = new();

    /// <summary>The mod's own wording for the panel. Null when it uses the standard one. See docs/panel.md.</summary>
    public PanelConfig? Panel;
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

    /// <summary>Which kinds of report it takes, "bug" and/or "feedback". Missing means both.</summary>
    public List<string>? For;

    public bool Takes(string kind) => For == null || For.Contains(kind);

    /// <summary>The UserFeedback relay, added by RelayRegistry. Never read from a mod's file.</summary>
    [JsonIgnore]
    public bool Relay { get; init; }

    /// <summary>The name the player sees in the "Send to" list. Only call it on a destination the registry checked.</summary>
    public string Describe()
    {
        if (!string.IsNullOrWhiteSpace(Label)) return Label!;

        return Type switch
        {
            DestinationRegistry.GitHub => L("route-github", Repo),
            DestinationRegistry.WebPage => L("route-webpage", new Uri(Url!).Host),
            _ => L("route-http", new Uri(Url!).Host),
        };
    }

    /// <summary>For mod authors: "Name (type target, bug only)"</summary>
    public string Summary() => $"{Describe()} ({Type} {Target}{(For != null ? ", " + string.Join(" and ", For) + " only" : "")})";

    /// <summary>The repo for github, the url otherwise</summary>
    public string Target => Type == DestinationRegistry.GitHub ? Repo! : Url!;
}
