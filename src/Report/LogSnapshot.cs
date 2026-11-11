using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Config;

namespace UserFeedback;

/// <summary>One log message, with the stack trace or other lines that follow it.</summary>
public record LogEntry(string File, string Text);

public class LogFile
{
    public string Name = "";
    public string Content = "";
    /// <summary>True when only the end of a long file was read</summary>
    public bool Truncated;
    public List<LogEntry> Entries = new();
}

/// <summary>
/// The game's log files as they were when the panel opened. Read off the main thread, the game keeps writing to them meanwhile.
/// Chat and audit logs are never read: they hold other players' messages, not anything a mod author needs.
/// </summary>
public class LogSnapshot
{
    /// <summary>Only the end of longer files is read, that's where the current session is</summary>
    public const int MaxBytesPerFile = 4 * 1024 * 1024;

    private static readonly string[] ClientFiles = { "client-main.log", "client-debug.log", "client-crash.log" };
    // In singleplayer the server runs in the same game, so its logs show the server side of a mod
    private static readonly string[] ServerFiles = { "server-main.log", "server-debug.log", "server-crash.log", "server-worldgen.log" };

    // "11.11.2026 10:31:25 [Notification] ...", crash reports start with "Game Version: ..." instead
    private static readonly Regex EntryStart = new(@"^(\d{1,2}[./-]\d{1,2}[./-]\d{2,4}[ T]\d{1,2}:\d{2}:\d{2}(\.\d+)?\b|Game Version:)", RegexOptions.Compiled);

    // Lines from the top of client-main.log worth knowing about the player's computer
    private static readonly string[] SystemKeys =
    {
        "Game Version", "Operating System", "CPU", "CPU Cores", "Available RAM", "Graphics Card Vendor",
        "Graphics Card Version", "Graphics Card Renderer", "C# Framework",
    };

    public List<LogFile> Files = new();
    public Dictionary<string, string> System = new();

    public static LogSnapshot Read(bool singleplayer)
    {
        var snapshot = new LogSnapshot();
        IEnumerable<string> names = singleplayer ? ClientFiles.Concat(ServerFiles) : ClientFiles;

        foreach (string name in names)
        {
            string path = Path.Combine(GamePaths.Logs, name);
            if (!File.Exists(path)) continue;

            try
            {
                LogFile file = ReadTail(path, name);
                file.Entries = Split(name, file.Content);
                snapshot.Files.Add(file);
            }
            catch (Exception)
            {
                // A log we can't read just isn't included
            }
        }

        snapshot.System = ReadSystem(Path.Combine(GamePaths.Logs, "client-main.log"));
        return snapshot;
    }

    private static LogFile ReadTail(string path, string name)
    {
        // The game has the file open for writing, so share it
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        bool truncated = stream.Length > MaxBytesPerFile;
        if (truncated) stream.Seek(-MaxBytesPerFile, SeekOrigin.End);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string content = reader.ReadToEnd();
        if (truncated)
        {
            // Drop the line cut in half by the seek
            int newline = content.IndexOf('\n');
            content = newline >= 0 ? content[(newline + 1)..] : "";
        }
        return new LogFile { Name = name, Content = content, Truncated = truncated };
    }

    private static List<LogEntry> Split(string file, string content)
    {
        var entries = new List<LogEntry>();
        var current = new StringBuilder();

        foreach (string raw in content.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (EntryStart.IsMatch(line) && current.Length > 0)
            {
                entries.Add(new LogEntry(file, current.ToString().TrimEnd()));
                current.Clear();
            }
            if (line.Length > 0 || current.Length > 0) current.AppendLine(line);
        }
        if (current.Length > 0) entries.Add(new LogEntry(file, current.ToString().TrimEnd()));

        return entries;
    }

    private static Dictionary<string, string> ReadSystem(string path)
    {
        var system = new Dictionary<string, string>();
        if (!File.Exists(path)) return system;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            // The details are logged in the first few dozen lines of a session
            for (int i = 0; i < 60 && reader.ReadLine() is string line; i++)
            {
                int start = line.IndexOf("] ", StringComparison.Ordinal);
                if (start < 0) continue;

                string text = line[(start + 2)..];
                int colon = text.IndexOf(": ", StringComparison.Ordinal);
                if (colon < 0) continue;

                string key = text[..colon];
                if (SystemKeys.Contains(key)) system[key] = text[(colon + 2)..];
            }
        }
        catch (Exception)
        {
        }
        return system;
    }
}
