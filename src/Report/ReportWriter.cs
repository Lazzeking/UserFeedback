using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace UserFeedback;

/// <summary>What to do with the logs for one report</summary>
/// <param name="Filter">Picks the entries about the report's mods, null when the player left logs out</param>
/// <param name="Full">Also attach the complete log files</param>
public record ReportLogs(Task<LogSnapshot> Snapshot, ModLogFilter? Filter, bool Full, Sanitizer Sanitizer);

/// <summary>
/// Every report is saved in VintagestoryData/UserFeedback/&lt;date&gt;_&lt;modid&gt;/, whatever else happens to it:
/// report.json, screenshot.png, log.txt (the entries about the report's mods) and logs.zip (the full logs, if chosen).
/// </summary>
public static class ReportWriter
{
    public static string RootFolder => Path.Combine(GamePaths.DataPath, "UserFeedback");

    /// <summary>"Show report" writes here, replacing the previous preview</summary>
    public static string PreviewFolder => Path.Combine(RootFolder, "preview");

    public static string NewReportFolder(string? modId)
    {
        return Path.Combine(RootFolder, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{SafeName(modId ?? "unknown")}");
    }

    /// <summary>
    /// Creates the folder and a first report.json now, so a failure can still be shown in the open panel.
    /// The logs and the final report.json are written in the background, then onDone gets on the main thread
    /// whether all of it worked. With handOverScreenshot the screenshot is saved in the background too and
    /// disposed, otherwise it is saved right away and stays the caller's (the panel is still open for a preview).
    /// </summary>
    public static void Save(ICoreClientAPI capi, string folder, FeedbackReport report, BitmapRef? screenshot, bool handOverScreenshot, ReportLogs logs, Action<bool> onDone)
    {
        // A new preview replaces the last one, real reports always get a new folder
        if (folder == PreviewFolder && Directory.Exists(folder))
        {
            foreach (string old in Directory.GetFiles(folder)) File.Delete(old);
        }
        Directory.CreateDirectory(folder);

        report.HasScreenshot = screenshot != null;
        WriteReport(folder, report);

        bool screenshotOk = true;
        if (screenshot != null && !handOverScreenshot)
        {
            screenshotOk = SaveScreenshot(capi, screenshot, folder);
            screenshot = null;
        }

        // Reading, filtering, cleaning and packing the logs and encoding the PNG take long enough to stutter the game
        Task.Run(async () =>
        {
            bool ok = true;
            try
            {
                LogSnapshot snapshot = await logs.Snapshot;

                if (logs.Filter != null)
                {
                    var entries = logs.Filter.Matching(snapshot);
                    report.LogEntries = entries.Count;
                    File.WriteAllText(Path.Combine(folder, "log.txt"), logs.Sanitizer.Clean(ModLogFilter.Format(entries)));

                    if (report.Context != null)
                    {
                        report.Context.System = snapshot.System.ToDictionary(pair => pair.Key, pair => logs.Sanitizer.Clean(pair.Value));
                    }
                }

                if (logs.Full)
                {
                    WriteFullLogs(Path.Combine(folder, "logs.zip"), snapshot, logs.Sanitizer);
                    report.HasFullLogs = true;
                }

                WriteReport(folder, report);
            }
            catch (Exception e)
            {
                ok = false;
                capi.Logger.Error("[{0}] Could not write the logs of the feedback report in {1}: {2}", Core.ModId, folder, e);
            }

            if (screenshot != null)
            {
                ok &= SaveScreenshot(capi, screenshot, folder);
                screenshot.Dispose();
            }

            ok &= screenshotOk;
            capi.Event.EnqueueMainThreadTask(() => onDone(ok), Core.ModId + "-saved");
        });
    }

    private static bool SaveScreenshot(ICoreClientAPI capi, BitmapRef screenshot, string folder)
    {
        try
        {
            screenshot.Save(Path.Combine(folder, "screenshot.png"));
            return true;
        }
        catch (Exception e)
        {
            capi.Logger.Warning("[{0}] Could not save the screenshot in {1}: {2}", Core.ModId, folder, e.Message);
            return false;
        }
    }

    private static void WriteReport(string folder, FeedbackReport report)
    {
        File.WriteAllText(Path.Combine(folder, "report.json"), JsonConvert.SerializeObject(report, Formatting.Indented));
    }

    private static void WriteFullLogs(string path, LogSnapshot snapshot, Sanitizer sanitizer)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (LogFile file in snapshot.Files)
        {
            ZipArchiveEntry entry = zip.CreateEntry(file.Name, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            if (file.Truncated) writer.WriteLine($"[{Core.ModId}] Only the last {LogSnapshot.MaxBytesPerFile / 1024 / 1024} MB of this file are included.");
            writer.Write(sanitizer.Clean(file.Content));
        }
    }

    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        // UseShellExecute opens it with Explorer, Finder or xdg-open
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private static string SafeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}
