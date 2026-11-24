using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using static UserFeedback.ModLang;

namespace UserFeedback;

/// <summary>
/// The UserFeedback relay (RelayUrl in the player's config): which mods take reports through it, and which count
/// "works for me" results (formatVersion 2's "works" list).
///
/// While the list is small the relay sends all of it, fetched at most once a day (a 304 when unchanged) and cached, so
/// the relay doesn't learn which mods a player has. When it grows big the relay splits it into buckets by the start of
/// sha256(modid), and only the bucket of the mod picked in the panel is fetched, so the relay learns "one of the ~100+
/// mods in that bucket", never the player's whole mod list. Buckets are never fetched for every installed mod: that set
/// of prefixes would itself point at the player's mods.
/// When a fetch fails the cached list stays in use.
/// </summary>
public partial class RelayRegistry
{
    /// <summary>The registry format this version asks for (?format=2) and reads</summary>
    public const int FormatVersion = 2;

    private const int MaxPrefixLength = 4;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);
    /// <summary>After a failed fetch, the next try waits at least this long</summary>
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);
    /// <summary>
    /// Buckets not used for this long are dropped from the cache, except those of mods in WorksOptIn: the automatic check
    /// reads them, and never fetches buckets on its own for mods the player didn't pick
    /// </summary>
    private static readonly TimeSpan BucketKeep = TimeSpan.FromDays(7);
    private static readonly Regex PrefixPattern = new("^[0-9a-f]{1,4}$");

    // Compressed answers are welcome; the 1 MB limit counts the unpacked size, so a small gzip can't unpack into something huge
    private static readonly HttpClient Http = ServerHttp.CreateClient(TimeSpan.FromSeconds(15), 1024 * 1024, decompress: true);

    private readonly ICoreClientAPI capi;
    private readonly UserFeedbackConfig config;
    private readonly object sync = new();
    private Index? index;
    private readonly Dictionary<string, Bucket> buckets = new();
    private readonly HashSet<string> fetchingBuckets = new();
    private readonly Dictionary<string, DateTime> bucketAttempts = new();
    private bool fetching;
    private DateTime lastAttemptUtc = DateTime.MinValue;

    /// <summary>Raised on the main thread when the list or a bucket arrives, so an open panel can offer the relay</summary>
    public event Action? Changed;

    /// <summary>The checked RelayUrl without a trailing slash, null when the relay is off or the setting is wrong</summary>
    public string? BaseUrl { get; }

    /// <summary>Why RelayUrl isn't used, for ".uf destinations". Null when it's fine or switched off.</summary>
    public string? Problem { get; }

    public string? Host => BaseUrl == null ? null : new Uri(BaseUrl).Host;
    public string? ReportsUrl => BaseUrl == null ? null : BaseUrl + "/reports";
    public string? WorksUrl => BaseUrl == null ? null : BaseUrl + "/works";
    private string? RegistryUrl => BaseUrl == null ? null : BaseUrl + "/registry";

    private static string CacheFile => Path.Combine(ReportWriter.RootFolder, "relay-registry-v2.json");

    public RelayRegistry(ICoreClientAPI capi, UserFeedbackConfig config)
    {
        this.capi = capi;
        this.config = config;

        string url = (config.RelayUrl ?? "").Trim().TrimEnd('/');
        if (url.Length == 0) return; // switched off: no fetch, no relay in "Send to"

        string? error = DestinationRegistry.CheckUrl(url);
        if (error == null && new Uri(url) is { Query.Length: > 0 } or { Fragment.Length: > 0 }) error = "can't have a ? or # part";
        if (error != null)
        {
            Problem = $"RelayUrl in ModConfig/{Core.ConfigFile} {error}, so the relay is off";
            capi.Logger.Warning("[{0}] {1}", Core.ModId, Problem);
            return;
        }

        BaseUrl = url;
        LoadCache();
    }

    /// <summary>When the list in use was fetched, null when there is none yet</summary>
    public DateTime? FetchedUtc
    {
        get { lock (sync) return index?.FetchedUtc; }
    }

    /// <summary>Whether the relay splits its list into buckets, for ".uf destinations"</summary>
    public bool Bucketed
    {
        get { lock (sync) return index?.PrefixLength != null; }
    }

    /// <summary>How many buckets are cached, for ".uf destinations"</summary>
    public int CachedBuckets
    {
        get { lock (sync) return buckets.Count; }
    }

    /// <summary>
    /// Every modid known to take reports or "works for me" results: the whole list, or the mods in the buckets fetched so far
    /// </summary>
    public IReadOnlyCollection<string> ModIds
    {
        get
        {
            lock (sync)
            {
                if (index == null) return Array.Empty<string>();
                return index.PrefixLength == null ? index.Mods.Keys.ToList() : buckets.Values.SelectMany(bucket => bucket.Mods.Keys).ToList();
            }
        }
    }

    /// <summary>
    /// Whether reports about modId, of this kind when given, can go through the relay. Never for "not sure", the relay
    /// can't know whose report that is. With buckets it's false until modId's bucket is in, see <see cref="Prefetch"/>.
    /// </summary>
    public bool Has(string? modId, string? kind = null)
    {
        RelayMod? mod = Lookup(modId);
        return mod is { Reports: true } && (kind == null || mod.Takes(kind));
    }

    /// <summary>Whether modId's author has a public page on the relay, so the player can allow their report to be used there</summary>
    public bool IsPublic(string? modId) => Lookup(modId) is { Reports: true, Public: true };

    /// <summary>The kinds modId takes when it's limited, for ".uf destinations". Null when it takes both or no reports at all.</summary>
    public IReadOnlyList<string>? KindsOf(string modId) => Lookup(modId) is { Reports: true } mod ? mod.Kinds : null;

    /// <summary>
    /// Whether modId's author counts "works for me" results. Such a mod may take no reports at all. With buckets it's false
    /// until modId's bucket is in, like <see cref="Has"/>.
    /// </summary>
    public bool CountsWorks(string? modId) => Lookup(modId)?.Works == true;

    /// <summary>
    /// Whether the list in use can answer for modId: the whole list is in, or modId's bucket is. False while there's no
    /// list yet or the bucket is still to come, so a panel can say "checking" instead of "not on the relay".
    /// </summary>
    public bool Knows(string? modId)
    {
        if (string.IsNullOrEmpty(modId)) return false;
        lock (sync) return ModsHolding(modId) != null;
    }

    /// <summary>
    /// The relay's privacy page, only when it's https:// on the relay's own host, so a changed registry can't link elsewhere.
    /// Also refuses characters that would need escaping in VTML.
    /// </summary>
    public string? PrivacyUrl
    {
        get
        {
            string? url;
            lock (sync) url = index?.PrivacyUrl;
            if (url == null || Host == null || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)) return null;
            string safe = uri.AbsoluteUri;
            return safe.IndexOfAny(new[] { '"', '\'', '<', '>', '&', ' ' }) < 0 ? safe : null;
        }
    }

    /// <summary>The relay as a destination in "Send to". An ordinary http upload to {RelayUrl}/reports.</summary>
    public DestinationConfig Destination => new()
    {
        Type = DestinationRegistry.Http,
        Url = ReportsUrl,
        Label = L("route-relay"),
        Relay = true,
    };

    /// <summary>Whether url is the relay's upload address, so a mod listing it as its own http destination isn't shown twice</summary>
    public bool IsRelayUrl(string? url)
    {
        return ReportsUrl != null && url != null && string.Equals(url.TrimEnd('/'), ReportsUrl, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The relay refused a report of this kind with 403: the mod was paused, blocked, or no longer has a destination for
    /// that kind since the list was fetched. The kind is left out until the next fetch, and reports once it takes none.
    /// </summary>
    public void Drop(string? modId, string kind)
    {
        Change(modId, mod =>
        {
            if (!mod.Reports) return;
            // Kinds null means both, so the one left is the other
            mod.Kinds = (mod.Kinds ?? new List<string> { FeedbackReport.Bug, FeedbackReport.Feedback }).Where(k => k != kind).ToList();
            if (mod.Kinds.Count > 0) return;
            mod.Reports = false;
            mod.Kinds = null;
        });
    }

    /// <summary>The relay refused a "works for me" result with 403: the counter is off until the next fetch says otherwise</summary>
    public void DropWorks(string? modId)
    {
        Change(modId, mod => mod.Works = false);
    }

    // Changes modId's cached entry, and forgets it once it takes neither reports nor results
    private void Change(string? modId, Action<RelayMod> change)
    {
        if (string.IsNullOrEmpty(modId)) return;
        lock (sync)
        {
            Dictionary<string, RelayMod>? mods = ModsHolding(modId);
            if (mods == null || !mods.TryGetValue(modId, out RelayMod? mod)) return;

            change(mod);
            if (!mod.Reports && !mod.Works) mods.Remove(modId);
            SaveCache();
        }
    }

    /// <summary>Fetches the list in the background when the cached one is a day old or missing. Cheap to call often.</summary>
    public void Refresh()
    {
        string? url = RegistryUrl;
        if (url == null) return;

        string? etag;
        lock (sync)
        {
            DateTime now = DateTime.UtcNow;
            // A list "from the future" after the clock went back counts as old
            bool fresh = index != null && IsFresh(index.FetchedUtc, now);
            if (fetching || fresh || now - lastAttemptUtc < RetryAfterFailure) return;
            fetching = true;
            lastAttemptUtc = now;
            etag = index?.ETag;
        }

        Task.Run(async () =>
        {
            try
            {
                Response response = await Get($"{url}?format={FormatVersion}", etag);
                if (response.NotModified)
                {
                    lock (sync)
                    {
                        if (index != null) index = index with { FetchedUtc = DateTime.UtcNow };
                        SaveCache();
                    }
                    return;
                }

                Index? fetched = response.Json == null ? null : ParseIndex(response.Json, DateTime.UtcNow, response.ETag);
                if (fetched == null)
                {
                    capi.Logger.Warning("[{0}] The relay registry at {1} has a format this version doesn't know, keeping the cached list", Core.ModId, url);
                    return;
                }

                lock (sync)
                {
                    // Another prefix length makes every cached bucket meaningless
                    if (fetched.PrefixLength != index?.PrefixLength) buckets.Clear();
                    index = fetched;
                    SaveCache();
                }
                capi.Logger.Notification("[{0}] Relay registry fetched: {1}", Core.ModId,
                    fetched.PrefixLength is int length ? $"split into buckets of {length} hex character(s)" : $"{fetched.Mods.Values.Count(mod => mod.Reports)} mod(s) take reports through it, {fetched.Mods.Values.Count(mod => mod.Works)} count \"works for me\"");
                RaiseChanged();
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[{0}] Could not fetch the relay registry from {1}, keeping the cached list: {2}", Core.ModId, url, e.Message);
            }
            finally
            {
                lock (sync) fetching = false;
            }
        });
    }

    /// <summary>
    /// When the relay uses buckets, fetches modId's bucket in the background if it isn't cached or is a day old.
    /// Only call it for the mod the player picked, never for every installed mod.
    /// </summary>
    public void Prefetch(string? modId)
    {
        string? url = RegistryUrl;
        if (url == null || string.IsNullOrEmpty(modId)) return;

        string prefix;
        lock (sync)
        {
            if (index?.PrefixLength is not int length) return;
            prefix = Prefix(modId, length);

            DateTime now = DateTime.UtcNow;
            if (buckets.TryGetValue(prefix, out Bucket? cached) && IsFresh(cached.FetchedUtc, now)) return;
            if (fetchingBuckets.Contains(prefix)) return;
            if (bucketAttempts.TryGetValue(prefix, out DateTime last) && now - last < RetryAfterFailure) return;
            fetchingBuckets.Add(prefix);
            bucketAttempts[prefix] = now;
        }

        Task.Run(async () =>
        {
            try
            {
                Response response = await Get($"{url}/{prefix}?format={FormatVersion}", null);
                Bucket? bucket = response.Json == null ? null : ParseBucket(response.Json, prefix, DateTime.UtcNow);
                if (bucket == null)
                {
                    capi.Logger.Warning("[{0}] The relay registry bucket {1} has a format this version doesn't know", Core.ModId, prefix);
                    return;
                }

                lock (sync)
                {
                    // The list may have changed its prefix length meanwhile
                    if (index?.PrefixLength != prefix.Length) return;
                    buckets[prefix] = bucket;
                    SaveCache();
                }
                RaiseChanged();
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[{0}] Could not fetch relay registry bucket {1}: {2}", Core.ModId, prefix, e.Message);
            }
            finally
            {
                lock (sync) fetchingBuckets.Remove(prefix);
            }
        });
    }

    /// <summary>The first length hex characters of sha256 of the lowercased modid, which the relay computes the same way</summary>
    public static string Prefix(string modId, int length)
    {
        return Hashing.Sha256Hex(modId.ToLowerInvariant())[..length];
    }

    private RelayMod? Lookup(string? modId)
    {
        if (string.IsNullOrEmpty(modId)) return null;
        lock (sync) return ModsHolding(modId)?.GetValueOrDefault(modId);
    }

    // The list that would hold modId: the whole list, or modId's bucket. Null while that isn't in. Call it under the lock.
    private Dictionary<string, RelayMod>? ModsHolding(string modId)
    {
        if (index == null) return null;
        if (index.PrefixLength is not int length) return index.Mods;
        return buckets.TryGetValue(Prefix(modId, length), out Bucket? bucket) ? bucket.Mods : null;
    }

    // Under a day old. A time in the future (the clock was changed) counts as stale, so it's fetched again.
    private static bool IsFresh(DateTime fetchedUtc, DateTime now) => fetchedUtc <= now && now - fetchedUtc < MaxAge;

    private void RaiseChanged()
    {
        capi.Event.EnqueueMainThreadTask(() => Changed?.Invoke(), Core.ModId + "-relay");
    }

    // --- Fetching ------------------------------------------------------------------------------------------------------

    private record Response(bool NotModified, JObject? Json, string? ETag);

    private static async Task<Response> Get(string url, string? etag)
    {
        using HttpRequestMessage request = ServerHttp.NewRequest(HttpMethod.Get, url);
        if (etag != null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        using HttpResponseMessage response = await Http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotModified) return new Response(true, null, etag);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"answered {(int)response.StatusCode}");

        JObject json = JObject.Parse(await response.Content.ReadAsStringAsync());
        return new Response(false, json, response.Headers.ETag?.ToString());
    }

    // --- Cache ---------------------------------------------------------------------------------------------------------

    // Kept in formatVersion 2's shape, so it's read back by the same code as the relay's answers
    private void SaveCache()
    {
        try
        {
            if (index == null) return;
            var file = new JObject
            {
                ["relayUrl"] = BaseUrl,
                ["fetchedUtc"] = index.FetchedUtc,
                ["etag"] = index.ETag,
                ["registry"] = IndexJson(index),
                ["buckets"] = new JObject(buckets.Select(pair => new JProperty(pair.Key, new JObject
                {
                    ["fetchedUtc"] = pair.Value.FetchedUtc,
                    ["registry"] = CompactJson(pair.Value.Mods, new JObject { ["formatVersion"] = 2, ["prefix"] = pair.Key }),
                }))),
            };
            AtomicFile.WriteAllText(CacheFile, file.ToString(Formatting.Indented));
        }
        catch (Exception e)
        {
            capi.Logger.Warning("[{0}] Could not save the relay registry: {1}", Core.ModId, e.Message);
        }
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return;

            JObject file = JObject.Parse(File.ReadAllText(CacheFile));
            // A list from another relay doesn't count, after RelayUrl changed
            if (file.Value<string>("relayUrl") != BaseUrl || file["registry"] is not JObject registry) return;

            index = ParseIndex(registry, file.Value<DateTime>("fetchedUtc"), file.Value<string>("etag"));
            if (index?.PrefixLength is not int length || file["buckets"] is not JObject saved) return;

            DateTime now = DateTime.UtcNow;
            foreach (JProperty entry in saved.Properties())
            {
                if (entry.Name.Length != length || !PrefixPattern.IsMatch(entry.Name) || entry.Value is not JObject value) continue;
                DateTime fetched = value.Value<DateTime>("fetchedUtc");
                if (value["registry"] is not JObject json) continue;
                if (now - fetched > BucketKeep && !config.WorksOptIn.Any(modId => Prefix(modId, length) == entry.Name)) continue;
                Bucket? bucket = ParseBucket(json, entry.Name, fetched);
                if (bucket != null) buckets[entry.Name] = bucket;
            }
        }
        catch (Exception e)
        {
            capi.Logger.Warning("[{0}] Could not read the cached relay registry, it is fetched again: {1}", Core.ModId, e.Message);
            index = null;
            buckets.Clear();
        }
    }

    /// <summary>The list as a whole (PrefixLength null), or only how it's split into buckets</summary>
    private record Index(DateTime FetchedUtc, string? ETag, string? PrivacyUrl, int? PrefixLength, Dictionary<string, RelayMod> Mods);

    private record Bucket(DateTime FetchedUtc, Dictionary<string, RelayMod> Mods);
}
