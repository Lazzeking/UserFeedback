using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using static UserFeedback.ModLang;

namespace UserFeedback;

/// <summary>The registry's JSON (formatVersion 2): reading answers, and writing the cache in the same shape</summary>
public partial class RelayRegistry
{
    // --- Reading the answers -------------------------------------------------------------------------------------------

    /// <summary>
    /// The registry answer, or null for a format this version doesn't know. Unknown fields are ignored.
    /// formatVersion 2: "buckets": { "prefixLength": 1–4 }, or the compact list ("mods", "public", "kinds", "works").
    /// </summary>
    private static Index? ParseIndex(JObject json, DateTime fetchedUtc, string? etag)
    {
        string? privacy = json["privacyUrl"] is JValue { Type: JTokenType.String } text ? text.Value<string>() : null;

        switch (Version(json))
        {
            case 2:
                if (json["buckets"] is JObject split)
                {
                    if (split["prefixLength"] is not JValue { Type: JTokenType.Integer } value) return null;
                    long length = value.Value<long>();
                    if (length < 1 || length > MaxPrefixLength) return null;
                    return new Index(fetchedUtc, etag, privacy, (int)length, new());
                }
                return new Index(fetchedUtc, etag, privacy, null, ParseCompact(json, _ => true));

            default:
                return null;
        }
    }

    /// <summary>A bucket answer, keeping only the modids that really belong in it</summary>
    private static Bucket? ParseBucket(JObject json, string prefix, DateTime fetchedUtc)
    {
        if (Version(json) != 2) return null;
        if (json["prefix"] is not JValue { Type: JTokenType.String } given || given.Value<string>() != prefix) return null;
        return new Bucket(fetchedUtc, ParseCompact(json, modId => Prefix(modId, prefix.Length) == prefix));
    }

    private static long Version(JObject json)
    {
        return json["formatVersion"] is JValue { Type: JTokenType.Integer } version ? version.Value<long>() : 0;
    }

    // "mods": every modid that takes reports, "public": the ones with a public page, "kinds": the ones limited to some
    // kinds, "works": the ones counting "works for me" results, which needn't be in "mods". Internal so tests can call it.
    internal static Dictionary<string, RelayMod> ParseCompact(JObject json, Func<string, bool> belongs)
    {
        var mods = new Dictionary<string, RelayMod>();
        foreach (string modId in Strings(json["mods"]).Where(id => ModIdRules.IsValid(id) && belongs(id)))
        {
            mods[modId] = new RelayMod { Reports = true };
        }
        // "public" and "kinds" are about reports, so only for mods in "mods"
        foreach (string modId in Strings(json["public"]))
        {
            if (mods.TryGetValue(modId, out RelayMod? mod)) mod.Public = true;
        }
        if (json["kinds"] is JObject kinds)
        {
            foreach (JProperty entry in kinds.Properties())
            {
                if (mods.TryGetValue(entry.Name, out RelayMod? mod)) mod.Kinds = ParseKinds(entry.Value);
            }
        }
        foreach (string modId in Strings(json["works"]).Where(id => ModIdRules.IsValid(id) && belongs(id)))
        {
            if (!mods.TryGetValue(modId, out RelayMod? mod)) mods[modId] = mod = new RelayMod();
            mod.Works = true;
        }
        return mods;
    }

    private static IEnumerable<string> Strings(JToken? token)
    {
        return token is JArray list
            ? list.OfType<JValue>().Where(value => value.Type == JTokenType.String).Select(value => value.Value<string>()!)
            : Enumerable.Empty<string>();
    }

    // Missing means both kinds. Kinds this version doesn't know are skipped, so a list with only newer ones takes none of ours.
    private static List<string>? ParseKinds(JToken? token)
    {
        if (token is not JArray) return null;
        return Strings(token).Where(kind => kind == FeedbackReport.Bug || kind == FeedbackReport.Feedback).Distinct().ToList();
    }

    // --- Writing the cache in the same shape ------------------------------------------------------------------------

    private static JObject IndexJson(Index index)
    {
        var json = new JObject { ["formatVersion"] = 2, ["privacyUrl"] = index.PrivacyUrl };
        if (index.PrefixLength is int length)
        {
            json["buckets"] = new JObject { ["prefixLength"] = length };
            return json;
        }
        return CompactJson(index.Mods, json);
    }

    internal static JObject CompactJson(Dictionary<string, RelayMod> mods, JObject json)
    {
        var reports = mods.Where(pair => pair.Value.Reports).ToList();
        json["mods"] = new JArray(reports.Select(pair => pair.Key));
        json["public"] = new JArray(reports.Where(pair => pair.Value.Public).Select(pair => pair.Key));
        json["kinds"] = new JObject(reports.Where(pair => pair.Value.Kinds != null).Select(pair => new JProperty(pair.Key, new JArray(pair.Value.Kinds!))));
        json["works"] = new JArray(mods.Where(pair => pair.Value.Works).Select(pair => pair.Key));
        return json;
    }
}
