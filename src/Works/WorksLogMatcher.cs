using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UserFeedback;

/// <summary>What the matcher knows about one mod: how its name, file and code show up in a log</summary>
/// <param name="Namespaces">The namespace prefixes of the mod's code, see <see cref="WorksLogMatcher.NamespacePrefixes"/></param>
public record WorksModInfo(string ModId, string? Name, string? FileName, IReadOnlyCollection<string> Namespaces);

/// <summary>
/// Whether a mod logged errors this session, for the automatic "works for me" check.
/// Only [Error] and [Fatal] entries count, and one counts for a mod when it:
/// - names the modid as a word (this is also how the game's own mod loader reports a mod that failed to start:
///   "[modid] An exception was thrown when trying to start the mod:", "[modid] Could not resolve some dependencies:"),
///   or the mod's name when it's 4 characters or more, or its file name,
/// - or has the mod's code in it: a stack frame "at Polymorph.Systems.X.Y(...)", or the game's "Failed to start system
///   Polymorph.Systems.X" and "Exception thrown when trying to create an instance of ModSystem Polymorph...".
/// Entries UserFeedback logged itself ("[userfeedback] ...") don't count for other mods: they name the mods it handles.
/// </summary>
public static class WorksLogMatcher
{
    // The level is on the entry's first line: "12.11.2026 15:10:08 [Error] ..."
    private static readonly Regex ErrorLevel = new(@"^[^\[\r\n]*\[(Error|Fatal)\]", RegexOptions.Compiled);
    private static readonly Regex FromUserFeedback = new(@"^[^\[\r\n]*\[(Error|Fatal)\]\s*\[" + Core.ModId + @"\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>True when an error entry is about this mod</summary>
    public static bool HasErrors(IEnumerable<string> entries, WorksModInfo mod)
    {
        Regex pattern = PatternFor(mod);
        bool isUserFeedback = mod.ModId == Core.ModId;
        foreach (string entry in entries)
        {
            if (!ErrorLevel.IsMatch(entry)) continue;
            if (!isUserFeedback && FromUserFeedback.IsMatch(entry)) continue;
            if (pattern.IsMatch(entry)) return true;
        }
        return false;
    }

    private static Regex PatternFor(WorksModInfo mod)
    {
        List<string> tokens = LogPatterns.IdentityTokens(mod.ModId, mod.Name, mod.FileName);
        tokens.AddRange(mod.Namespaces.Select(LogPatterns.Namespace));
        return new Regex(string.Join("|", tokens), RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// The prefixes to look for in a log for a mod whose code has these namespaces: their first segment ("Polymorph"),
    /// or their first two ("Lazzeking.Polymorph") when another loaded mod's code starts with the same first segment, so
    /// one author's mods don't take each other's errors (a namespace that is only the shared segment is then left out).
    /// Namespaces of the game and of libraries are left out.
    /// </summary>
    /// <param name="otherRoots">First segments of the namespaces of every other loaded code mod</param>
    public static IReadOnlyCollection<string> NamespacePrefixes(IEnumerable<string?> namespaces, ISet<string> otherRoots)
    {
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string ns in namespaces.Where(ns => !string.IsNullOrEmpty(ns)).Cast<string>())
        {
            string[] parts = ns.Split('.');
            if (LogPatterns.IsShared(parts[0])) continue;
            string prefix = parts[0];
            if (otherRoots.Contains(prefix))
            {
                // Types right in the shared namespace can't be told apart from the other mod's, so they're left out
                if (parts.Length == 1) continue;
                prefix += "." + parts[1];
            }
            if (prefix.Length >= 3) prefixes.Add(prefix);
        }
        return prefixes;
    }

    /// <summary>The first segment of a namespace, null for the game's and libraries'</summary>
    public static string? Root(string? ns)
    {
        if (string.IsNullOrEmpty(ns)) return null;
        string root = ns.Split('.')[0];
        return LogPatterns.IsShared(root) ? null : root;
    }

    /// <summary>
    /// SHA-256 (hex) of the other mods, "modid@version" sorted, one per line. Changes when the player adds, removes or
    /// updates another mod. Kept in works-sent.json, never sent: only whether it changed is.
    /// </summary>
    public static string Fingerprint(IEnumerable<(string ModId, string Version)> otherMods)
    {
        string text = string.Join("\n", otherMods.Select(mod => mod.ModId + "@" + mod.Version).OrderBy(line => line, StringComparer.Ordinal));
        return Hashing.Sha256Hex(text);
    }
}
