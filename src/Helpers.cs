using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UserFeedback;

/// <summary>Opens a file, folder or web page the way the player's system would</summary>
public static class Shell
{
    /// <summary>UseShellExecute opens it with Explorer, Finder or xdg-open, a web page in the default browser</summary>
    public static void Open(string pathOrUrl) => Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
}

public static class Sizes
{
    /// <summary>"512 B", "14.2 KB", "1.5 MB"</summary>
    public static string Format(long bytes) =>
        bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1024.0 / 1024.0:0.#} MB";
}

public static class Hashing
{
    /// <summary>SHA-256 of the text's UTF-8 bytes, as lowercase hex</summary>
    public static string Sha256Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

public static class ModIdRules
{
    private static readonly Regex Pattern = new("^[a-z0-9_]{2,64}$");

    /// <summary>A modid as the relay accepts it: 2 to 64 lowercase letters, digits or underscores</summary>
    public static bool IsValid(string? modId) => modId != null && Pattern.IsMatch(modId);
}

public static class AtomicFile
{
    /// <summary>Writes a temporary file, then moves it over path, so a crash halfway leaves the old file, not a broken one</summary>
    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
