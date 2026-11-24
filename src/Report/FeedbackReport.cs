using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UserFeedback;

/// <summary>What gets saved as report.json, and later sent to the mod's author.</summary>
public class FeedbackReport
{
    public const string Bug = "bug";
    public const string Feedback = "feedback";

    /// <summary>The faces of a rating, best first. A mod can redraw them; the value in a report stays one of these.</summary>
    public static readonly string[] Ratings = { "great", "good", "meh", "bad" };

    public int FormatVersion = 1;
    public string CreatedUtc = "";

    public string? ModId;
    public string? ModName;
    public string? ModVersion;

    /// <summary>Other mods the player thinks play a part, for problems that only happen with both</summary>
    public List<ModRef> AlsoInvolved = new();

    /// <summary>bug or feedback, picked by the player before the panel opens. Reports from before it was added are feedback.</summary>
    public string Kind = Feedback;

    /// <summary>great, good, meh, bad, or null if the player picked none. Always null for a bug.</summary>
    public string? Rating;
    public string Text = "";

    public string GameVersion = "";
    public bool Singleplayer;
    public string? LookingAt;
    public string? Holding;

    public bool HasScreenshot;

    /// <summary>Where, when and with what the player plays. Null when they chose not to include game details.</summary>
    public GameContext? Context;

    /// <summary>How many log entries about the mods are in log.txt</summary>
    public int LogEntries;

    /// <summary>Whether logs.zip holds the player's complete log files</summary>
    public bool HasFullLogs;

    /// <summary>What the reported mods added through the userfeedback:collect event, by modid</summary>
    public Dictionary<string, JToken> ModData = new();

    /// <summary>Files the reported mods asked for, relative to VintagestoryData, packed in files.zip</summary>
    public List<string> ExtraFiles = new();

    /// <summary>Files left out so the upload stays within UploadLimits.MaxUploadBytes, like "logs.zip"</summary>
    public List<string> LeftOut = new();

    /// <summary>
    /// The player allows the author to use the report on the mod's public page on the relay. Only offered when the
    /// relay is chosen and the mod has a public page there, false otherwise. The player can change it later on the follow page.
    /// </summary>
    public bool PublicAllowed;

    /// <summary>Where the player chose to send it, null when it is only saved on their computer</summary>
    public ReportDestination? Destination;
}

public class ModRef
{
    public string ModId = "";
    public string Name = "";
    public string Version = "";
}

public class ReportDestination
{
    public string Type = "";
    /// <summary>The repo for github, the url otherwise</summary>
    public string Target = "";
}
