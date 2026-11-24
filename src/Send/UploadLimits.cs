using System.Linq;

namespace UserFeedback;

/// <summary>
/// What a report may weigh, so it fits what servers accept. The relay checks the same numbers,
/// see "Sizes" in docs/http-protocol.md, so change both together.
/// Every such number is here; the classes that apply one name it after themselves (ExtraFiles.MaxFiles…).
/// </summary>
public static class UploadLimits
{
    /// <summary>All files of an uploaded report together. logs.zip, then files.zip are left out to fit.</summary>
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    /// <summary>report.json; the relay refuses a larger one</summary>
    public const int MaxReportJsonBytes = 256 * 1024;

    /// <summary>log.txt keeps its newest entries within this</summary>
    public const int MaxLogBytes = 2 * 1024 * 1024;

    /// <summary>Each log in logs.zip: only its end is read. The relay takes entries up to 5 MB.</summary>
    public const int MaxFullLogBytes = 4 * 1024 * 1024;

    /// <summary>files.zip: the files mods ask for in their userfeedback.json</summary>
    public const int MaxExtraFiles = 50;
    public const int MaxExtraFileBytes = 2 * 1024 * 1024;
    public const int MaxExtraFilesTotalBytes = 8 * 1024 * 1024;

    /// <summary>ModData: one mod's data, and all mods' together, counted as PHP's json_encode writes it</summary>
    public const int MaxModDataBytes = 16 * 1024;
    public const int MaxModDataTotalBytes = 64 * 1024;

    /// <summary>Wider screenshots are scaled down. A 4K PNG alone can be over 10 MB, 1920 wide stays around 2–3 MB.</summary>
    public const int MaxScreenshotWidth = 1920;

    /// <summary>The highest MaxTextLength the config can set</summary>
    public const int MaxTextLength = 5000;

    // report.json fields the relay checks. Over any of them it refuses the whole report.
    public const int MaxInvolved = 20;
    public const int MaxModList = 1000;
    public const int MaxModListEntry = 100;
    public const int MaxVersion = 32;
    public const int MaxLeftOut = 20;

    /// <summary>
    /// Brings report.json within the relay's limits, so an unusual setup (a 1000-mod pack, a long version string)
    /// can't get a whole report refused. Applied to every report, the same rules suit any server.
    /// </summary>
    public static void Fit(FeedbackReport report)
    {
        report.ModVersion = Cut(report.ModVersion, MaxVersion);
        report.GameVersion = Cut(report.GameVersion, MaxVersion)!;
        report.AlsoInvolved = report.AlsoInvolved.Take(MaxInvolved).ToList();
        foreach (ModRef mod in report.AlsoInvolved) mod.Version = Cut(mod.Version, MaxVersion)!;
        report.LeftOut = report.LeftOut.Distinct().Take(MaxLeftOut).ToList();

        if (report.Context is GameContext context)
        {
            var mods = context.Mods.Select(mod => Cut(mod, MaxModListEntry)!).ToList();
            // The last entry says how many more there were, so the list doesn't look complete
            if (mods.Count > MaxModList) mods = mods.Take(MaxModList - 1).Append($"… and {mods.Count - (MaxModList - 1)} more").ToList();
            context.Mods = mods;
        }
    }

    private static string? Cut(string? text, int max) => text == null || text.Length <= max ? text : text[..max];
}
