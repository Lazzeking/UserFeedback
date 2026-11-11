using System.Collections.Generic;

namespace UserFeedback;

/// <summary>What gets saved as report.json, and later sent to the mod's author.</summary>
public class FeedbackReport
{
    public int FormatVersion = 1;
    public string CreatedUtc = "";

    public string? ModId;
    public string? ModName;
    public string? ModVersion;

    /// <summary>Other mods the player thinks play a part, for problems that only happen with both</summary>
    public List<ModRef> AlsoInvolved = new();

    /// <summary>great, good, meh, bad, or null if the player picked none</summary>
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
