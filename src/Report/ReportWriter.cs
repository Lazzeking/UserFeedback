using System;
using System.IO;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace UserFeedback;

/// <summary>What to do with the logs for one report</summary>
/// <param name="Filter">Picks the entries about the report's mods, null when the player left logs out</param>
/// <param name="Full">Also attach the complete log files</param>
/// <param name="ExtraFiles">Files the reported mods asked for, relative to VintagestoryData</param>
/// <param name="MaxTotalBytes">For uploads: logs.zip, then files.zip are left out until the report fits. Null keeps everything.</param>
public record ReportLogs(Task<LogSnapshot> Snapshot, ModLogFilter? Filter, bool Full, List<string> ExtraFiles, Sanitizer Sanitizer, long? MaxTotalBytes);

/// <summary>
/// Every report is saved in VintagestoryData/UserFeedback/&lt;date&gt;_&lt;modid&gt;/, whatever else happens to it:
/// report.json, screenshot.png, log.txt (the entries about the report's mods), files.zip (files the mods asked for)
/// and logs.zip (the full logs, if chosen).
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
            Directory.Delete(folder, recursive: true);
        }
        Directory.CreateDirectory(folder);

        report.HasScreenshot = screenshot != null;
        WriteReport(folder, report);

        // Saved now when the caller keeps it (a preview, the panel stays open), else in the background, then disposed
        bool screenshotOk = true;
        BitmapRef? background = handOverScreenshot ? screenshot : null;
        if (screenshot != null && !handOverScreenshot) screenshotOk = SaveScreenshot(capi, screenshot, folder);

        // Reading, filtering, cleaning and packing the logs and encoding the PNG take long enough to stutter the game
        Task.Run(async () =>
        {
            bool ok = screenshotOk;
            try
            {
                LogSnapshot snapshot = await logs.Snapshot;
                WriteLog(folder, report, logs, snapshot);
                CleanModData(report, logs.Sanitizer);
                WriteZips(folder, report, logs, snapshot);
            }
            catch (Exception e)
            {
                ok = false;
                capi.Logger.Error("[{0}] Could not write the logs of the feedback report in {1}: {2}", Core.ModId, folder, e);
            }

            if (background != null)
            {
                ok &= SaveScreenshot(capi, background, folder);
                background.Dispose();
            }

            if (logs.MaxTotalBytes is long budget) FitInto(folder, report, budget);

            try
            {
                WriteReport(folder, report);
            }
            catch (Exception e)
            {
                ok = false;
                capi.Logger.Error("[{0}] Could not write report.json in {1}: {2}", Core.ModId, folder, e);
            }

            capi.Event.EnqueueMainThreadTask(() => onDone(ok), Core.ModId + "-saved");
        });
    }

    // log.txt with the entries about the report's mods, and the system details from the logs for the game details
    private static void WriteLog(string folder, FeedbackReport report, ReportLogs logs, LogSnapshot snapshot)
    {
        if (logs.Filter != null)
        {
            List<LogEntry> entries = logs.Filter.Matching(snapshot);
            report.LogEntries = entries.Count;
            string log = KeepNewest(logs.Sanitizer.Clean(ModLogFilter.Format(entries)), UploadLimits.MaxLogBytes);
            File.WriteAllText(Path.Combine(folder, ReportFiles.Log), log, new UTF8Encoding(false));
        }
        if (report.Context != null)
        {
            report.Context.System = snapshot.System.ToDictionary(pair => pair.Key, pair => logs.Sanitizer.Clean(pair.Value));
        }
    }

    // What mods added can hold the same personal details as their logs. Data that no longer fits is listed in LeftOut.
    private static void CleanModData(FeedbackReport report, Sanitizer sanitizer)
    {
        foreach (string modId in ModContributions.Clean(report.ModData, sanitizer))
        {
            string entry = ReportFiles.LeftOutModData(modId);
            if (!report.LeftOut.Contains(entry)) report.LeftOut.Add(entry);
        }
    }

    private static void WriteZips(string folder, FeedbackReport report, ReportLogs logs, LogSnapshot snapshot)
    {
        if (logs.ExtraFiles.Count > 0)
        {
            ExtraFiles.WriteZip(Path.Combine(folder, ReportFiles.Files), logs.ExtraFiles, logs.Sanitizer);
        }
        if (logs.Full)
        {
            WriteFullLogs(Path.Combine(folder, ReportFiles.FullLogs), snapshot, logs.Sanitizer);
            report.HasFullLogs = true;
        }
    }

    // Screenshots wider than MaxScreenshotWidth are scaled down, a 4K PNG alone would be too big to upload
    private static bool SaveScreenshot(ICoreClientAPI capi, BitmapRef screenshot, string folder)
    {
        string path = Path.Combine(folder, ReportFiles.Screenshot);
        try
        {
            if (screenshot is BitmapExternal external && external.bmp.Width > UploadLimits.MaxScreenshotWidth)
            {
                SKBitmap original = external.bmp;
                int width = UploadLimits.MaxScreenshotWidth;
                int height = (int)Math.Round(original.Height * (double)width / original.Width);
                using SKBitmap scaled = original.Resize(new SKImageInfo(width, height, original.ColorType, original.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell));
                using FileStream file = File.Create(path);
                scaled.Encode(file, SKEncodedImageFormat.Png, 100);
            }
            else
            {
                screenshot.Save(path);
            }
            return true;
        }
        catch (Exception e)
        {
            capi.Logger.Warning("[{0}] Could not save the screenshot in {1}: {2}", Core.ModId, folder, e.Message);
            return false;
        }
    }

    // Moves the full logs, then the mods' files, into not-sent/ until the report fits. report.json is written after this and can
    // still grow a little, so keep some room for it.
    private static void FitInto(string folder, FeedbackReport report, long budget)
    {
        long Size() => Directory.GetFiles(folder).Where(f => Path.GetFileName(f) != ReportFiles.Report).Sum(f => new FileInfo(f).Length)
            + UploadLimits.MaxReportJsonBytes;

        foreach (string name in new[] { ReportFiles.FullLogs, ReportFiles.Files })
        {
            if (Size() <= budget) return;

            string path = Path.Combine(folder, name);
            if (!File.Exists(path)) continue;

            // Kept for the player, only the files right in the folder are uploaded
            string notSent = Path.Combine(folder, "not-sent");
            Directory.CreateDirectory(notSent);
            File.Move(path, Path.Combine(notSent, name), overwrite: true);
            report.LeftOut.Add(name);
            if (name == ReportFiles.FullLogs) report.HasFullLogs = false;
            else report.ExtraFiles.Clear();
        }
    }

    /// <summary>The end of the text within maxBytes, from a whole line on, with a note that older lines were cut</summary>
    private static string KeepNewest(string text, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;

        string note = $"[{Core.ModId}] Older entries were cut to keep this file under {maxBytes / 1024 / 1024} MB.\n";
        int start = Math.Max(0, text.Length - maxBytes);
        while (Encoding.UTF8.GetByteCount(text.AsSpan(start)) + note.Length > maxBytes) start += 4096;

        int newline = text.IndexOf('\n', start);
        return note + (newline >= 0 ? text[(newline + 1)..] : "");
    }

    private static void WriteReport(string folder, FeedbackReport report)
    {
        UploadLimits.Fit(report);
        File.WriteAllText(Path.Combine(folder, ReportFiles.Report), JsonConvert.SerializeObject(report, Formatting.Indented));
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
        Shell.Open(folder);
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
