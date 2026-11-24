using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace UserFeedback;

/// <summary>
/// Reads every mod's config/userfeedback.json once, checks it and answers where reports about a mod can go.
/// Mistakes in a file are logged and listed by ".uf destinations", the rest of the file still works.
/// </summary>
public class DestinationRegistry
{
    public const string GitHub = "github";
    public const string WebPage = "webpage";
    public const string Http = "http";
    public const string AssetPath = "config/userfeedback.json";

    private static readonly Regex RepoPattern = new(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");

    private readonly ICoreClientAPI capi;
    private Dictionary<string, FeedbackConfigFile>? files;
    private readonly List<string> problems = new();

    public DestinationRegistry(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    /// <summary>Modid of every mod that ships a userfeedback.json, with its checked contents.</summary>
    public IReadOnlyDictionary<string, FeedbackConfigFile> Files => files ??= Load();

    public IReadOnlyList<string> Problems
    {
        get
        {
            files ??= Load();
            return problems;
        }
    }

    /// <summary>Where a report of this kind about modId can go, as its own userfeedback.json says. Empty when it has none.</summary>
    public List<DestinationConfig> DestinationsFor(string modId, string kind) =>
        FileOf(modId)?.Destinations.Where(d => d.Takes(kind)).ToList() ?? new();

    /// <summary>modId's own panel wording, null when it uses the standard panel or has no file</summary>
    public PanelConfig? PanelFor(string modId) => FileOf(modId)?.Panel;

    /// <summary>The extra files modId's userfeedback.json asks for</summary>
    public List<string> FilesFor(string modId) => FileOf(modId)?.Files ?? new();

    /// <summary>The checked "logMatch" prefixes of modId's userfeedback.json</summary>
    public List<string> LogMatchFor(string modId) => FileOf(modId)?.LogMatch ?? new();

    private FeedbackConfigFile? FileOf(string modId) => Files.GetValueOrDefault(modId);

    /// <summary>
    /// Reads assets/&lt;modid&gt;/config/userfeedback.json from each mod's own files only. Any mod can ship assets for another
    /// mod's domain, which would let it send that mod's reports to itself, so copies coming from elsewhere are ignored.
    /// </summary>
    private Dictionary<string, FeedbackConfigFile> Load()
    {
        var loaded = new Dictionary<string, FeedbackConfigFile>();

        foreach (Mod mod in capi.ModLoader.Mods)
        {
            string modId = mod.Info.ModID;
            string? assets = AssetsFolder(mod);
            if (assets == null) continue;

            string path = Path.Combine(assets, modId, "config", "userfeedback.json");
            if (!File.Exists(path)) continue;

            string where = $"{modId}:{AssetPath}";
            FeedbackConfigFile? file;
            try
            {
                file = JsonUtil.ToObject<FeedbackConfigFile>(File.ReadAllText(path), modId);
            }
            catch (Exception e)
            {
                Problem(where, $"not valid JSON: {e.Message}");
                continue;
            }
            if (file == null) continue;

            file.Destinations = (file.Destinations ?? new()).Where((d, i) => Check(where, i + 1, d)).ToList();
            file.Files ??= new();
            var problems = new List<string>();
            file.LogMatch = LogMatchRules.Check(file.LogMatch, problems);
            file.Panel = PanelRules.Check(modId, file.Panel, problems, prefix: "panel ");
            foreach (string problem in problems) Problem(where, problem);
            if (file.Panel != null)
            {
                DropMissingIcons(where, file.Panel);
                if (!file.Panel.Customized) file.Panel = null;
            }
            if (file.Destinations.Count == 0 && file.Files.Count == 0 && file.LogMatch.Count == 0 && file.Panel == null)
            {
                Problem(where, "has no usable destinations, files, logMatch or panel, so it is ignored");
                continue;
            }
            loaded[modId] = file;
        }

        WarnAboutForeignCopies();
        capi.Logger.Notification("[{0}] Feedback destinations from {1} mod(s): {2}", Core.ModId, loaded.Count, string.Join(", ", loaded.Keys));
        return loaded;
    }

    // A userfeedback.json the game would use that comes from another mod than the one it's named after
    private void WarnAboutForeignCopies()
    {
        foreach (IAsset asset in capi.Assets.GetMany(AssetPath, null, false))
        {
            string domain = asset.Location.Domain;
            string? origin = asset.Origin?.OriginPath;
            Mod? owner = capi.ModLoader.GetMod(domain);
            if (origin == null || (owner != null && SameFolder(origin, AssetsFolder(owner)))) continue;

            Mod? from = capi.ModLoader.Mods.FirstOrDefault(mod => SameFolder(origin, AssetsFolder(mod)));
            string who = from != null ? $"{from.Info.Name} ({from.Info.ModID})" : origin;
            Problem(asset.Location.ToString(), owner == null
                ? $"comes from {who}, but no mod {domain} is loaded, so it is ignored"
                : $"is also shipped by {who}, which may be trying to receive {domain}'s reports. Only {domain}'s own file is used.");
        }
    }

    /// <summary>The folder holding the mod's assets, unpacked for zips. Null for mods without assets.</summary>
    private static string? AssetsFolder(Mod mod)
    {
        string? folder = (mod as ModContainer)?.FolderPath;
        return folder == null ? null : Path.Combine(folder, "assets");
    }

    private static bool SameFolder(string a, string? b)
    {
        if (b == null) return false;
        return string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.Ordinal);
    }

    // An icon the mod named but didn't ship falls back to the standard face. A panel that then says nothing is the standard one.
    private void DropMissingIcons(string where, PanelConfig panel)
    {
        if (panel.Ratings == null) return;

        foreach ((string code, PanelFace face) in panel.Ratings)
        {
            if (face.Icon == null) continue;

            int colon = face.Icon.IndexOf(':');
            var location = new AssetLocation(face.Icon[..colon], face.Icon[(colon + 1)..]);
            if (capi.Assets.TryGet(location) != null) continue;

            Problem(where, $"panel rating \"{code}\" icon \"{face.Icon}\" was not found, the standard face is used");
            face.Icon = null;
        }

        panel.Ratings = panel.Ratings.Where(pair => pair.Value.Label != null || pair.Value.LabelKey != null || pair.Value.Icon != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (panel.Ratings.Count == 0) panel.Ratings = null;
    }

    private bool Check(string where, int number, DestinationConfig? d)
    {
        string? error = d == null ? "is empty" : d.Type switch
        {
            GitHub => d.Repo != null && RepoPattern.IsMatch(d.Repo) ? null : "needs \"repo\": \"owner/repository\"",
            WebPage or Http => CheckUrl(d.Url),
            null => "has no \"type\"",
            _ => $"has an unknown type \"{d.Type}\" (github, webpage or http), maybe it needs a newer UserFeedback",
        };
        error ??= CheckFor(d!.For);

        if (error != null) Problem(where, $"destination {number} {error}");
        return error == null;
    }

    // A typo like "bugs" would silently hide the destination, so it's a mistake rather than ignored
    private static string? CheckFor(List<string>? kinds)
    {
        if (kinds == null) return null;
        if (kinds.Count == 0) return "has an empty \"for\", leave it out to take both kinds";
        string? unknown = kinds.FirstOrDefault(k => k != FeedbackReport.Bug && k != FeedbackReport.Feedback);
        return unknown == null ? null : $"has \"{unknown}\" in \"for\", which takes \"bug\" and/or \"feedback\"";
    }

    // https only, except to this computer so authors can try their own endpoint locally.
    // Never to the player's own network: a mod could otherwise make the game post to their router or other devices.
    internal static string? CheckUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return "needs a full \"url\", like https://example.com/feedback";
        if (uri.IsLoopback) return uri.Scheme is "http" or "https" ? null : "needs an https:// url";
        if (uri.Scheme != Uri.UriSchemeHttps) return "needs an https:// url";
        if (IsLocalNetwork(uri)) return "points into the player's local network, which isn't allowed";
        return null;
    }

    private static bool IsLocalNetwork(Uri uri)
    {
        string host = uri.Host;
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) || !host.Contains('.') && uri.HostNameType == UriHostNameType.Dns)
        {
            return true;
        }
        if (!IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? ip)) return false;

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.Equals(IPAddress.IPv6Any);
        }
        byte[] b = ip.GetAddressBytes();
        return b[0] == 10 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    private void Problem(string where, string message)
    {
        problems.Add($"{where}: {message}");
        capi.Logger.Warning("[{0}] {1}: {2}", Core.ModId, where, message);
    }
}
