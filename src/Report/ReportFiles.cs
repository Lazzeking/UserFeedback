namespace UserFeedback;

/// <summary>The files of a report folder. Their names are part of the upload format (docs/http-protocol.md), so they don't change.</summary>
public static class ReportFiles
{
    public const string Report = "report.json";
    public const string Screenshot = "screenshot.png";
    /// <summary>The log lines about the reported mods</summary>
    public const string Log = "log.txt";
    /// <summary>The complete logs, when the player ticked them</summary>
    public const string FullLogs = "logs.zip";
    /// <summary>The files mods ask for in their userfeedback.json</summary>
    public const string Files = "files.zip";
    /// <summary>The relay's private follow link, never sent</summary>
    public const string FollowUrl = "follow-url.txt";

    /// <summary>How a mod's data that didn't fit is named in the report's LeftOut</summary>
    public static string LeftOutModData(string modId) => "ModData." + modId;
}
