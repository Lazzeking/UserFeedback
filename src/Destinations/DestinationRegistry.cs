using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

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

    /// <summary>Where a report about modId can go, as its own userfeedback.json says. Empty when it has none.</summary>
    public List<DestinationConfig> DestinationsFor(string modId)
    {
        return Files.TryGetValue(modId, out FeedbackConfigFile? file) ? file.Destinations : new();
    }

    private Dictionary<string, FeedbackConfigFile> Load()
    {
        var loaded = new Dictionary<string, FeedbackConfigFile>();

        foreach (IAsset asset in capi.Assets.GetMany(AssetPath))
        {
            // The asset's domain is the mod it belongs to, for nearly every mod the same as its modid
            string owner = asset.Location.Domain;
            string where = asset.Location.ToString();

            FeedbackConfigFile? file;
            try
            {
                file = asset.ToObject<FeedbackConfigFile>();
            }
            catch (Exception e)
            {
                Problem(where, $"not valid JSON: {e.Message}");
                continue;
            }
            if (file == null) continue;

            file.Destinations = (file.Destinations ?? new()).Where((d, i) => Check(where, i + 1, d)).ToList();

            if (file.Destinations.Count == 0)
            {
                Problem(where, "has no usable destinations, so it is ignored");
                continue;
            }
            loaded[owner] = file;
        }

        capi.Logger.Notification("[{0}] Feedback destinations from {1} mod(s): {2}", Core.ModId, loaded.Count, string.Join(", ", loaded.Keys));
        return loaded;
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

        if (error != null) Problem(where, $"destination {number} {error}");
        return error == null;
    }

    // https only, except to this computer so authors can try their own endpoint locally
    private static string? CheckUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return "needs a full \"url\", like https://example.com/feedback";
        if (uri.Scheme == Uri.UriSchemeHttps) return null;
        if (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) return null;
        return "needs an https:// url";
    }

    private void Problem(string where, string message)
    {
        problems.Add($"{where}: {message}");
        capi.Logger.Warning("[{0}] {1}: {2}", Core.ModId, where, message);
    }
}
