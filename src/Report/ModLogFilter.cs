using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;

namespace UserFeedback;

/// <summary>
/// Picks the log entries that are about some mods: their modid, name, zip file or code (namespace in a stack trace),
/// and the prefixes each one lists under "logMatch" in its userfeedback.json.
/// </summary>
public class ModLogFilter
{
    /// <summary>The newest entries are kept when there are more</summary>
    public const int MaxEntries = 400;

    private readonly Regex? pattern;

    /// <param name="logMatch">A mod's checked "logMatch" prefixes, by modid (see LogMatchRules)</param>
    public ModLogFilter(IEnumerable<Mod> mods, System.Func<string, IEnumerable<string>>? logMatch = null)
    {
        var tokens = new HashSet<string>();
        foreach (Mod mod in mods)
        {
            if (logMatch != null)
            {
                foreach (string prefix in logMatch(mod.Info.ModID)) tokens.Add(LogMatchRules.Pattern(prefix));
            }

            tokens.UnionWith(LogPatterns.IdentityTokens(mod.Info.ModID, mod.Info.Name, mod.FileName));

            // "at TimeFliesBy.WallStreaks.At(...)": the first part of each mod system's namespace
            foreach (ModSystem system in mod.Systems)
            {
                string? root = system.GetType().Namespace?.Split('.')[0];
                if (root != null && root.Length >= 4 && !LogPatterns.IsShared(root)) tokens.Add(LogPatterns.Namespace(root));
            }
        }

        if (tokens.Count > 0)
        {
            pattern = new Regex(string.Join("|", tokens), RegexOptions.IgnoreCase);
        }
    }

    /// <summary>Whether one log text is about these mods</summary>
    public bool Matches(string text) => pattern != null && pattern.IsMatch(text);

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
