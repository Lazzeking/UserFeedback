using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UserFeedback;

/// <summary>
/// "logMatch" in a mod's userfeedback.json: short prefixes, like a logger name or a Harmony patch class, whose log lines
/// go into that mod's log extract next to the lines naming its modid, name, file or namespace. A prefix that would pick up
/// lines of the game or of every mod is refused, so one mod's report can't carry the whole log.
/// </summary>
public static class LogMatchRules
{
    public const int MaxPrefixes = 8;
    public const int MinLength = 4;
    public const int MaxLength = 40;

    // Words in lines of the game and of any mod. A prefix that is only one of these (or the start of one) is refused.
    private static readonly string[] CommonWords =
    {
        "error", "errors", "warning", "warnings", "exception", "notification", "notice", "debug", "verbose", "information",
        "fatal", "event", "events", "server", "client", "mods", "modsystem", "modloader", "harmony", "patch", "patches",
        "null", "failed", "failure", "loading", "loaded", "game", "world", "player", "block", "item", "entity",
        "asset", "assets", "texture", "shape", "chunk", "thread", "main", "system", "stack", "trace", "crash",
        "unhandled", "object", "reference", "string", "value", "config", "network", "packet", "message", "audit",
        "chat", "info", "true", "false", "time", "done", "start", "started", "stop", "stopped",
    };

    /// <summary>The usable prefixes, in order. Each refused one is listed in problems.</summary>
    public static List<string> Check(List<string>? prefixes, List<string> problems)
    {
        var kept = new List<string>();
        if (prefixes == null) return kept;

        foreach (string? prefix in prefixes)
        {
            string? why = Refusal(prefix);
            if (why != null)
            {
                problems.Add($"logMatch \"{prefix}\" {why}");
                continue;
            }
            if (kept.Contains(prefix!)) continue;
            if (kept.Count == MaxPrefixes)
            {
                problems.Add($"logMatch has more than {MaxPrefixes} prefixes, \"{prefix}\" and after are left out");
                break;
            }
            kept.Add(prefix!);
        }
        return kept;
    }

    /// <summary>Why a prefix can't be used, null when it can</summary>
    public static string? Refusal(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return "is empty";
        if (prefix.Length > MaxLength) return $"is longer than {MaxLength} characters";
        if (prefix.Any(char.IsControl)) return "holds a line break or control character";
        if (prefix.Trim().Length != prefix.Length) return "starts or ends with a space";

        string core = new string(prefix.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (core.Length < MinLength) return $"needs at least {MinLength} letters or digits";
        if (CommonWords.Any(word => word.StartsWith(core, StringComparison.Ordinal)))
        {
            return "is a word found in the lines of the game and of every mod";
        }

        // "System.", "Vintagestory.Server", "HarmonyLib.Harmony": the start of a namespace shared by everything
        if (LogPatterns.IsShared(prefix.Split('.')[0]) || LogPatterns.StartsShared(core))
        {
            return "matches the game's or .NET's own code";
        }
        return null;
    }

    /// <summary>
    /// The regex for one checked prefix: case-sensitive, and not inside a longer word ("Poly" doesn't match "MyPoly"),
    /// but it may go on ("PolyBiome" matches "PolyBiomeGen").
    /// </summary>
    public static string Pattern(string prefix) => @"(?<!\w)(?-i:" + Regex.Escape(prefix) + ")";
}
