using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace UserFeedback;

/// <summary>
/// Files a mod asks to include, from "files" in its own userfeedback.json. Paths are relative to the
/// VintagestoryData folder, may end in a folder (all its files) or use * and ? in the file name: "ModConfig/mymod.json",
/// "ModData/mymod/", "Logs/mymod-*.log".
/// Only text files inside ModConfig, ModData and Logs are taken, never chat or audit logs, and they are cleaned of personal
/// details like the logs. The player reviews every file before a report with them is sent, and every one is in files.zip.
/// </summary>
/// <param name="Path">Relative to the VintagestoryData folder</param>
/// <param name="ModId">The mod that asked for it</param>
/// <param name="NamedAfterMod">Whether the modid is in the path. When it isn't, the mod may be asking for another mod's files.</param>
public record RequestedFile(string Path, string ModId, long Size, bool NamedAfterMod);

public static class ExtraFiles
{
    public const int MaxFiles = UploadLimits.MaxExtraFiles;
    public const int MaxFileBytes = UploadLimits.MaxExtraFileBytes;
    public const int MaxTotalBytes = UploadLimits.MaxExtraFilesTotalBytes;

    private static readonly string[] AllowedFolders = { "ModConfig", "ModData", "Logs" };
    private static readonly string[] TextExtensions = { ".json", ".json5", ".txt", ".log", ".cfg", ".ini", ".yaml", ".yml", ".xml", ".csv", ".md", ".toml" };

    /// <summary>
    /// The files the mods' patterns name that may be included, at most MaxFiles, each with the first mod that asked for it.
    /// </summary>
    public static List<RequestedFile> Resolve(IEnumerable<(string ModId, string Pattern)> requests, ILogger logger)
    {
        string root = Path.GetFullPath(GamePaths.DataPath);
        var found = new Dictionary<string, RequestedFile>(StringComparer.Ordinal);
        long total = 0;

        foreach ((string modId, string pattern) in requests)
        {
            foreach (string full in Expand(root, pattern, logger))
            {
                if (found.Count >= MaxFiles) return Sorted(found);

                string relative = Path.GetRelativePath(root, full).Replace('\\', '/');
                if (found.ContainsKey(relative) || !Allowed(relative)) continue;

                long size = new FileInfo(full).Length;
                if (size > MaxFileBytes || total + size > MaxTotalBytes) continue;

                found[relative] = new RequestedFile(relative, modId, size, relative.Contains(modId, StringComparison.OrdinalIgnoreCase));
                total += size;
            }
        }
        return Sorted(found);
    }

    private static List<RequestedFile> Sorted(Dictionary<string, RequestedFile> found) => found.Values.OrderBy(file => file.Path, StringComparer.Ordinal).ToList();

    public static string FullPath(string relative) => Path.Combine(GamePaths.DataPath, relative);

    /// <summary>Packs the files, cleaned, into a zip. Files that vanished meanwhile are skipped.</summary>
    public static void WriteZip(string path, IEnumerable<string> files, Sanitizer sanitizer)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string relative in files)
        {
            string full = Path.Combine(GamePaths.DataPath, relative);
            if (!File.Exists(full)) continue;

            string text;
            using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                text = reader.ReadToEnd();
            }

            ZipArchiveEntry entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(sanitizer.Clean(text));
        }
    }

    private static IEnumerable<string> Expand(string root, string pattern, ILogger logger)
    {
        string clean = pattern.Trim().Replace('\\', '/');
        if (clean.Length == 0 || Path.IsPathRooted(clean) || clean.Split('/').Contains(".."))
        {
            logger.Warning("[{0}] Ignored the file \"{1}\": it must be a path inside the VintagestoryData folder", Core.ModId, pattern);
            yield break;
        }

        string full = Path.GetFullPath(Path.Combine(root, clean));
        if (!full.StartsWith(root, StringComparison.Ordinal)) yield break;

        if (Directory.Exists(full))
        {
            foreach (string file in SafeFiles(full, "*", SearchOption.AllDirectories)) yield return file;
            yield break;
        }

        string? folder = Path.GetDirectoryName(full);
        string name = Path.GetFileName(full);
        if (folder == null || !Directory.Exists(folder)) yield break;

        if (name.Contains('*') || name.Contains('?'))
        {
            foreach (string file in SafeFiles(folder, name, SearchOption.TopDirectoryOnly)) yield return file;
        }
        else if (File.Exists(full))
        {
            yield return full;
        }
    }

    private static IEnumerable<string> SafeFiles(string folder, string pattern, SearchOption option)
    {
        try
        {
            return Directory.GetFiles(folder, pattern, option);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static bool Allowed(string relative)
    {
        string[] parts = relative.Split('/');
        if (parts.Length < 2 || !AllowedFolders.Contains(parts[0], StringComparer.OrdinalIgnoreCase)) return false;
        if (!TextExtensions.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase)) return false;

        // Other players' messages, and logs of who did what on a server
        string name = parts[^1];
        return !Regex.IsMatch(name, "chat|audit", RegexOptions.IgnoreCase);
    }
}
