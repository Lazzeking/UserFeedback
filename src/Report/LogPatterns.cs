using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UserFeedback;

/// <summary>
/// How a mod shows up in a log, shared by the report's log extract (ModLogFilter), the "works for me" check
/// (WorksLogMatcher), the F8 guess (RecentErrors) and the "logMatch" rules: its id, name and file as whole words, and
/// the namespaces of its code. Namespaces of the game and of libraries say nothing about which mod it was.
/// </summary>
public static class LogPatterns
{
    private static readonly string[] SharedRoots =
    {
        "Vintagestory", "System", "Microsoft", "HarmonyLib", "Newtonsoft", "ProtoBuf", "Cairo", "SkiaSharp", "OpenTK", "MonoMod", "Mono",
    };

    /// <summary>The text as a whole word, so a modid like "ore" doesn't match "more"</summary>
    public static string Word(string text) => @"(?<!\w)" + Regex.Escape(text) + @"(?!\w)";

    /// <summary>"Polymorph." followed by more of a type name, not after another identifier or dot</summary>
    public static string Namespace(string prefix) => @"(?<![\w.])" + Regex.Escape(prefix) + @"\.(?=[A-Za-z_])";

    /// <summary>Whether a namespace's first segment is the game's or a library's, like "System" or "VintagestoryLib"</summary>
    public static bool IsShared(string root) =>
        SharedRoots.Contains(root, StringComparer.OrdinalIgnoreCase) || root.StartsWith("Vintagestory", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether text is the start of one of those namespaces, like "Newt" or "harmony"</summary>
    public static bool StartsShared(string text) => SharedRoots.Any(root => root.StartsWith(text, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The modid, the name when it's 4 characters or more, and the file name when it's a .zip or .dll: a folder mod's
    /// name can be anything ("net10.0" for a dev build), only archive and dll names are distinctive.
    /// </summary>
    public static List<string> IdentityTokens(string modId, string? name, string? fileName)
    {
        var tokens = new List<string> { Word(modId) };
        if (name is { Length: >= 4 }) tokens.Add(Word(name));
        if (fileName != null && (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            tokens.Add(Word(fileName));
        }
        return tokens;
    }
}
