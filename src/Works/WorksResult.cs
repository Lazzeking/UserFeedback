using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace UserFeedback;

/// <summary>
/// One "works for me" result for one mod, the JSON body of POST {RelayUrl}/works. The relay refuses a body that breaks the rules checked here.
/// Only made through <see cref="Manual"/> and <see cref="Auto"/>, which refuse what the relay would answer 400 to.
/// </summary>
public class WorksResult
{
    public const int FormatVersion = 1;
    public const string SourceAuto = "auto";
    public const string SourceManual = "manual";
    public const string Clean = "clean";
    public const string Errors = "errors";

    private static readonly Regex VersionPattern = new("^[A-Za-z0-9][A-Za-z0-9._+-]{0,31}$");

    public string ModId { get; }
    public string ModVersion { get; }
    public string GameVersion { get; }
    public string Source { get; }
    public string Result { get; }
    /// <summary>Only on a change of an automatic result: the result it replaces</summary>
    public string? Replaces { get; }
    /// <summary>Only with Replaces: the other installed mods changed since the replaced result</summary>
    public bool? OtherModsChanged { get; }
    public bool Singleplayer { get; }

    /// <summary>"modid@modversion@gameversion": one result per player for each, the key in works-sent.json</summary>
    public string Key => VersionKey(ModId, ModVersion, GameVersion);

    private WorksResult(string modId, string modVersion, string gameVersion, string source, string result, string? replaces, bool? otherModsChanged, bool singleplayer)
    {
        ModId = modId;
        ModVersion = modVersion;
        GameVersion = gameVersion;
        Source = source;
        Result = result;
        Replaces = replaces;
        OtherModsChanged = otherModsChanged;
        Singleplayer = singleplayer;
    }

    public static string VersionKey(string modId, string modVersion, string gameVersion) => $"{modId}@{modVersion}@{gameVersion}";

    /// <summary>Whether the relay takes this modid and these versions. A mod with a strange version string is skipped.</summary>
    public static bool CanCount(string? modId, string? modVersion, string? gameVersion)
    {
        return modId != null && modVersion != null && gameVersion != null
            && ModIdRules.IsValid(modId) && VersionPattern.IsMatch(modVersion) && VersionPattern.IsMatch(gameVersion);
    }

    /// <summary>The "Works for me" button: always clean. Null when the mod can't be counted.</summary>
    public static WorksResult? Manual(string modId, string modVersion, string gameVersion, bool singleplayer)
    {
        return CanCount(modId, modVersion, gameVersion) ? new WorksResult(modId, modVersion, gameVersion, SourceManual, Clean, null, null, singleplayer) : null;
    }

    /// <summary>
    /// The automatic check. replaces is the earlier result when this one changes it, then otherModsChanged says whether
    /// the other mods changed in between. Null when the mod can't be counted or the combination is one the relay refuses.
    /// </summary>
    public static WorksResult? Auto(string modId, string modVersion, string gameVersion, string result, bool singleplayer, string? replaces = null, bool otherModsChanged = false)
    {
        if (!CanCount(modId, modVersion, gameVersion) || !IsResult(result)) return null;
        if (replaces != null && (!IsResult(replaces) || replaces == result)) return null;
        return new WorksResult(modId, modVersion, gameVersion, SourceAuto, result, replaces, replaces != null ? otherModsChanged : null, singleplayer);
    }

    private static bool IsResult(string value) => value == Clean || value == Errors;

    public JObject ToJson()
    {
        var json = new JObject
        {
            ["FormatVersion"] = FormatVersion,
            ["ModId"] = ModId,
            ["ModVersion"] = ModVersion,
            ["GameVersion"] = GameVersion,
            ["Source"] = Source,
            ["Result"] = Result,
        };
        if (Replaces != null)
        {
            json["Replaces"] = Replaces;
            json["OtherModsChanged"] = OtherModsChanged == true;
        }
        json["Singleplayer"] = Singleplayer;
        return json;
    }
}
