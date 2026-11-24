using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UserFeedback;

/// <summary>
/// works-sent.json in UserFeedback's data folder: which "works for me" results were already counted, so each player
/// counts once per mod version and game version, and what still waits to be sent.
/// Local only, never sent. Used on the main thread only.
/// </summary>
public class WorksSent
{
    /// <summary>A result that failed this many times (429, 5xx, no answer) is dropped</summary>
    public const int MaxTries = 2;
    /// <summary>Entries for versions no longer installed are dropped after this long</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(90);

    public static string DefaultPath => Path.Combine(ReportWriter.RootFolder, "works-sent.json");

    private readonly string path;
    private readonly Data data;

    /// <summary>Why the file couldn't be read or saved, for the log. Null when fine.</summary>
    public string? Problem { get; private set; }

    public WorksSent(string path)
    {
        this.path = path;
        data = Load();
    }

    // --- The button ----------------------------------------------------------------------------------------------------

    /// <summary>Whether the button already counted this mod version and game version</summary>
    public bool HasManual(string key) => data.Sent.TryGetValue(key, out Entry? entry) && entry.Manual != null;

    /// <summary>After the relay's 204 for the button</summary>
    public void MarkManual(string key, DateTime utc)
    {
        Get(key).Manual = new ManualEntry { At = utc };
        Save();
    }

    // --- The automatic check -------------------------------------------------------------------------------------------

    /// <summary>
    /// What to send for this check's result, or null when it's the same as the last one counted. A first result is sent
    /// as it is. A different one is sent as a change, with OtherModsChanged when the other mods aren't the ones installed
    /// when the replaced result was counted. That fingerprint stays the one of the last result sent, so "errors with some
    /// mods, still errors after changing them, clean after that" still says the other mods changed.
    /// Null as well when the mod or versions can't be counted.
    /// </summary>
    public WorksResult? Decide(string modId, string modVersion, string gameVersion, string result, string fingerprint, bool singleplayer)
    {
        string key = WorksResult.VersionKey(modId, modVersion, gameVersion);
        AutoEntry? last = data.Sent.TryGetValue(key, out Entry? entry) ? entry.Auto : null;
        if (last == null) return WorksResult.Auto(modId, modVersion, gameVersion, result, singleplayer);
        if (last.Result == result) return null;
        return WorksResult.Auto(modId, modVersion, gameVersion, result, singleplayer, replaces: last.Result, otherModsChanged: last.Fingerprint != fingerprint);
    }

    /// <summary>After the relay's 204 for an automatic result: it's the one to compare with from now on</summary>
    public void MarkAuto(string key, string result, string fingerprint, DateTime utc)
    {
        Get(key).Auto = new AutoEntry { Result = result, Fingerprint = fingerprint, At = utc };
        data.Pending.Remove(key);
        Save();
    }

    /// <summary>The last automatic result counted for this key, null when none</summary>
    public string? LastAuto(string key) => data.Sent.TryGetValue(key, out Entry? entry) ? entry.Auto?.Result : null;

    // --- Waiting to be sent --------------------------------------------------------------------------------------------

    /// <summary>A result to send, kept until the relay answers. Replaces an older one for the same key, which it supersedes.</summary>
    public void SetPending(string key, JObject body, string fingerprint)
    {
        data.Pending[key] = new PendingEntry { Body = body, Fingerprint = fingerprint };
        Save();
    }

    public void DropPending(string key)
    {
        if (data.Pending.Remove(key)) Save();
    }

    /// <summary>The relay didn't take it for now (429, 5xx, no answer). True when it's kept for the next check.</summary>
    public bool FailPending(string key)
    {
        if (!data.Pending.TryGetValue(key, out PendingEntry? pending)) return false;
        pending.Tries++;
        bool kept = pending.Tries < MaxTries;
        if (!kept) data.Pending.Remove(key);
        Save();
        return kept;
    }

    public IReadOnlyList<string> PendingKeys => data.Pending.Keys.ToList();

    /// <summary>The body and the fingerprint it was made with, null when nothing waits for this key</summary>
    public (JObject Body, string Fingerprint)? GetPending(string key)
    {
        return data.Pending.TryGetValue(key, out PendingEntry? pending) ? (pending.Body, pending.Fingerprint) : null;
    }

    // --- Housekeeping --------------------------------------------------------------------------------------------------

    /// <summary>Drops entries older than <see cref="Keep"/> whose key isn't one of the installed mod and game versions</summary>
    public void Prune(ISet<string> installedKeys, DateTime utc)
    {
        List<string> old = data.Sent
            .Where(pair => !installedKeys.Contains(pair.Key) && utc - Latest(pair.Value) > Keep)
            .Select(pair => pair.Key)
            .ToList();
        if (old.Count == 0) return;
        foreach (string key in old) data.Sent.Remove(key);
        Save();
    }

    private static DateTime Latest(Entry entry)
    {
        DateTime auto = entry.Auto?.At ?? DateTime.MinValue;
        DateTime manual = entry.Manual?.At ?? DateTime.MinValue;
        return auto > manual ? auto : manual;
    }

    private Entry Get(string key)
    {
        if (!data.Sent.TryGetValue(key, out Entry? entry)) data.Sent[key] = entry = new Entry();
        return entry;
    }

    private Data Load()
    {
        try
        {
            if (!File.Exists(path)) return new Data();
            Data? read = JsonConvert.DeserializeObject<Data>(File.ReadAllText(path));
            // A newer version's file is kept as it is and not overwritten, this one starts fresh in memory
            if (read == null || read.Version != Data.CurrentVersion) return new Data { ReadOnly = read != null };
            read.Sent ??= new();
            read.Pending ??= new();
            return read;
        }
        catch (Exception e)
        {
            // A broken file would make every result look new and count again, so it's left for the player to look at
            Problem = $"Could not read {path}, results already counted may be counted again: {e.Message}";
            return new Data { ReadOnly = true };
        }
    }

    private void Save()
    {
        if (data.ReadOnly) return;
        try
        {
            AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
            Problem = null;
        }
        catch (Exception e)
        {
            Problem = $"Could not save {path}: {e.Message}";
        }
    }

    private class Data
    {
        public const int CurrentVersion = 1;

        [JsonProperty("version")] public int Version = CurrentVersion;
        [JsonProperty("sent")] public Dictionary<string, Entry> Sent = new();
        [JsonProperty("pending")] public Dictionary<string, PendingEntry> Pending = new();
        [JsonIgnore] public bool ReadOnly;
    }

    private class Entry
    {
        [JsonProperty("auto", NullValueHandling = NullValueHandling.Ignore)] public AutoEntry? Auto;
        [JsonProperty("manual", NullValueHandling = NullValueHandling.Ignore)] public ManualEntry? Manual;
    }

    private class AutoEntry
    {
        [JsonProperty("result")] public string Result = "";
        /// <summary>SHA-256 of the other mods installed when this result was counted, see WorksLogMatcher.Fingerprint</summary>
        [JsonProperty("fingerprint")] public string Fingerprint = "";
        [JsonProperty("at")] public DateTime At;
    }

    private class ManualEntry
    {
        [JsonProperty("at")] public DateTime At;
    }

    private class PendingEntry
    {
        [JsonProperty("body")] public JObject Body = new();
        /// <summary>Saved as the auto entry's fingerprint once the relay counts it</summary>
        [JsonProperty("fingerprint")] public string Fingerprint = "";
        [JsonProperty("tries")] public int Tries;
    }
}
