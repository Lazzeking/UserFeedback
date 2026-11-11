using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;

namespace UserFeedback;

/// <summary>Picks the log entries that are about some mods: their modid, name, zip file or code (namespace in a stack trace).</summary>
public class ModLogFilter
{
    /// <summary>The newest entries are kept when there are more</summary>
    public const int MaxEntries = 400;

    private readonly Regex? pattern;

    public ModLogFilter(IEnumerable<Mod> mods)
    {
        var tokens = new HashSet<string>();
        foreach (Mod mod in mods)
        {
            // Whole words only, so a modid like "ore" doesn't match "more"
            tokens.Add(Word(mod.Info.ModID));
            if (mod.Info.Name.Length >= 4) tokens.Add(Word(mod.Info.Name));
            if (!string.IsNullOrEmpty(mod.FileName)) tokens.Add(Word(mod.FileName));

            // "at TimeFliesBy.WallStreaks.At(...)": the first part of each mod system's namespace
            foreach (ModSystem system in mod.Systems)
            {
                string? root = system.GetType().Namespace?.Split('.')[0];
                if (root != null && root.Length >= 4 && !root.StartsWith("Vintagestory", StringComparison.Ordinal))
                {
                    tokens.Add(@"(?<!\w)" + Regex.Escape(root) + @"\.");
                }
            }
        }

        if (tokens.Count > 0)
        {
            pattern = new Regex(string.Join("|", tokens), RegexOptions.IgnoreCase);
        }
    }

    private static string Word(string text) => @"(?<!\w)" + Regex.Escape(text) + @"(?!\w)";

    public List<LogEntry> Matching(LogSnapshot snapshot)
    {
        if (pattern == null) return new();

        List<LogEntry> all = snapshot.Files.SelectMany(file => file.Entries).Where(entry => pattern.IsMatch(entry.Text)).ToList();
        return all.Count > MaxEntries ? all.GetRange(all.Count - MaxEntries, MaxEntries) : all;
    }

    /// <summary>The entries as one text file, grouped by the log they came from</summary>
    public static string Format(IEnumerable<LogEntry> entries)
    {
        var text = new StringBuilder();
        foreach (IGrouping<string, LogEntry> file in entries.GroupBy(entry => entry.File))
        {
            text.AppendLine($"===== {file.Key} =====");
            foreach (LogEntry entry in file) text.AppendLine(entry.Text);
            text.AppendLine();
        }
        return text.ToString();
    }
}
