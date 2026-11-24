using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace UserFeedback;

/// <summary>
/// The automatic "works for me" check. A few minutes after the player is in a
/// world, it reads this session's logs for the mods the player switched on in the panel (WorksOptIn), and tells the relay
/// "clean" or "errors" for each, but only when it's new or changed since the last result counted. Never shows anything:
/// it only writes to the log.
/// Nothing at all happens while the relay is off or the player hasn't accepted it in the consent question.
/// </summary>
public class WorksCheck
{
    /// <summary>After the world is ready: mods have loaded, assets and the first ticks have run</summary>
    public const int DelayMs = 180_000;
    private const int MinGapMs = 5_000;
    private const int MaxGapMs = 15_000;

    // This session's logs, which the game starts fresh each time. Crash logs are left out: they keep older sessions.
    private static readonly string[] CheckedFiles = { "client-main.log", "client-debug.log", "server-main.log", "server-debug.log" };

    private readonly ICoreClientAPI capi;
    private readonly UserFeedbackConfig config;
    private readonly RelayRegistry relay;
    private readonly WorksSent sent;
    private bool scheduled;
    private bool running;

    public WorksCheck(ICoreClientAPI capi, UserFeedbackConfig config, RelayRegistry relay, WorksSent sent)
    {
        this.capi = capi;
        this.config = config;
        this.relay = relay;
        this.sent = sent;
    }

    /// <summary>
    /// Once the player is in the world. The check runs once per session, after <see cref="DelayMs"/>. Leaving the world
    /// earlier drops the callback with it, so that session isn't checked.
    /// </summary>
    public void Schedule()
    {
        if (scheduled) return;
        scheduled = true;
        capi.World.RegisterCallback(_ => Run(), DelayMs);
    }

    /// <summary>Runs the check now, for ".uf works check". The same rules: what's unchanged isn't sent again.</summary>
    /// <returns>Why nothing is checked, or null when it started</returns>
    public string? Run()
    {
        if (running) return "a check is already running";
        string? url = relay.WorksUrl;
        string? host = relay.Host;
        if (url == null || host == null) return "the relay is off";
        if (!config.ConsentedHosts.Contains(host)) return "the relay question wasn't accepted yet";

        string gameVersion = GameVersion.ShortGameVersion;
        bool singleplayer = capi.IsSinglePlayer;
        List<Candidate> candidates = Candidates(gameVersion);

        sent.Prune(capi.ModLoader.Mods.Select(mod => WorksResult.VersionKey(mod.Info.ModID, mod.Info.Version, gameVersion)).ToHashSet(), DateTime.UtcNow);
        var queue = new List<Send>();
        QueueRetries(queue, candidates.Select(candidate => candidate.Key).ToHashSet());
        if (candidates.Count == 0 && queue.Count == 0) return config.WorksOptIn.Count == 0 ? "no mod is switched on" : "none of the switched-on mods can be checked";

        running = true;
        // The logs can be a few MB, read and searched off the main thread
        Task.Run(() =>
        {
            List<string> entries = LogSnapshot.Read(singleplayer).Files
                .Where(file => CheckedFiles.Contains(file.Name))
                .SelectMany(file => file.Entries)
                .Select(entry => entry.Text)
                .ToList();
            return candidates.Select(candidate => WorksLogMatcher.HasErrors(entries, candidate.Info) ? WorksResult.Errors : WorksResult.Clean).ToList();
        }).ContinueWith(task => capi.Event.EnqueueMainThreadTask(() =>
        {
            running = false;
            if (!task.IsCompletedSuccessfully)
            {
                capi.Logger.Warning("[{0}] The \"works for me\" check couldn't read the logs: {1}", Core.ModId, task.Exception?.GetBaseException().Message);
                SendAll(url, queue);
                return;
            }

            var summary = new List<string>();
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                string result = task.Result[i];
                WorksResult? toSend = sent.Decide(candidate.Info.ModId, candidate.ModVersion, gameVersion, result, candidate.Fingerprint, singleplayer);
                string key = candidate.Key;
                if (toSend == null)
                {
                    // Same as the last result counted: an older one still waiting is out of date
                    sent.DropPending(key);
                    summary.Add($"{candidate.Info.ModId} {result} (unchanged)");
                    continue;
                }

                sent.SetPending(key, toSend.ToJson(), candidate.Fingerprint);
                queue.Add(new Send(key, toSend.ModId));
                summary.Add($"{candidate.Info.ModId} {result} ({Describe(toSend)})");
            }

            if (sent.Problem != null) capi.Logger.Warning("[{0}] {1}", Core.ModId, sent.Problem);
            capi.Logger.Notification("[{0}] \"Works for me\" check: {1}", Core.ModId, summary.Count > 0 ? string.Join(", ", summary) : "only retries");
            SendAll(url, queue);
        }, Core.ModId + "-works-check"));
        return null;
    }

    // "first", "was errors", "was errors, other mods changed"
    private static string Describe(WorksResult toSend)
    {
        if (toSend.Replaces == null) return "first";
        return toSend.OtherModsChanged == true ? $"was {toSend.Replaces}, other mods changed" : $"was {toSend.Replaces}";
    }

    /// <param name="Key">WorksResult.VersionKey of the mod and game version checked</param>
    private record Candidate(WorksModInfo Info, string ModVersion, string Fingerprint, string Key);

    private record Send(string Key, string ModId);

    // The switched-on mods that are on this client and that the relay's cached list says count results. Mods only on
    // the server (multiplayer) aren't here and can't be checked. Buckets aren't fetched for this, the panel did when
    // the player switched a mod on, and those stay cached.
    private List<Candidate> Candidates(string gameVersion)
    {
        List<Mod> loaded = capi.ModLoader.Mods.ToList();
        var candidates = new List<Candidate>();
        // Reading every mod's types is slow, so it's done once, and only when a mod can be checked
        Dictionary<Mod, List<string?>>? namespaces = null;
        foreach (string modId in config.WorksOptIn.Distinct())
        {
            Mod? mod = loaded.FirstOrDefault(m => m.Info.ModID == modId);
            if (mod == null || !relay.CountsWorks(modId) || !WorksResult.CanCount(modId, mod.Info.Version, gameVersion)) continue;

            namespaces ??= loaded.ToDictionary(m => m, m => Namespaces(m).ToList());
            var otherRoots = loaded.Where(m => m != mod).SelectMany(m => namespaces[m]).Select(WorksLogMatcher.Root).OfType<string>().ToHashSet();
            var info = new WorksModInfo(modId, mod.Info.Name, mod.FileName, WorksLogMatcher.NamespacePrefixes(namespaces[mod], otherRoots));
            string fingerprint = WorksLogMatcher.Fingerprint(loaded.Where(m => m != mod).Select(m => (m.Info.ModID, m.Info.Version)));
            candidates.Add(new Candidate(info, mod.Info.Version, fingerprint, WorksResult.VersionKey(modId, mod.Info.Version, gameVersion)));
        }
        return candidates;
    }

    // The namespaces in the assemblies of a mod's systems. A content mod has none.
    private static IEnumerable<string?> Namespaces(Mod mod)
    {
        return mod.Systems.Select(system => system.GetType().Assembly).Distinct().SelectMany(Types).Select(type => type.Namespace).Distinct();
    }

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(type => type != null).Cast<Type>();
        }
    }

    // Results that waited since an earlier session, tried once more. Not for a key this check decides again (its new
    // result replaces the old one), and dropped for mods switched off or no longer counting results.
    private void QueueRetries(List<Send> queue, ISet<string> checkedKeys)
    {
        foreach (string key in sent.PendingKeys)
        {
            if (checkedKeys.Contains(key) || sent.GetPending(key) is not { } pending) continue;
            string? modId = pending.Body.Value<string>("ModId");
            if (modId == null || !config.WorksOptIn.Contains(modId) || !relay.CountsWorks(modId))
            {
                sent.DropPending(key);
                continue;
            }
            queue.Add(new Send(key, modId));
        }
    }

    // One request per mod, 5 to 15 seconds apart, so no request shows more than one of the player's mods
    private void SendAll(string url, List<Send> queue)
    {
        void Next(int index)
        {
            if (index >= queue.Count) return;
            capi.World.RegisterCallback(_ =>
            {
                SendOne(url, queue[index]);
                Next(index + 1);
            }, Random.Shared.Next(MinGapMs, MaxGapMs));
        }
        Next(0);
    }

    private void SendOne(string url, Send item)
    {
        if (sent.GetPending(item.Key) is not { } pending) return;
        WorksSender.Send(capi, url, item.ModId, pending.Body, answer => OnAnswer(item, pending.Body.Value<string>("Result") ?? "", pending.Fingerprint, answer));
    }

    // Section 9's table. Nothing is shown to the player for automatic results.
    private void OnAnswer(Send item, string result, string fingerprint, WorksAnswer answer)
    {
        switch (answer.Status)
        {
            case 204:
                sent.MarkAuto(item.Key, result, fingerprint, DateTime.UtcNow);
                break;
            case 400 or 413:
                sent.DropPending(item.Key);
                capi.Logger.Warning("[{0}] The relay refused the \"works for me\" result for {1} ({2}): {3}", Core.ModId, item.ModId, answer.Status, answer.Message ?? "no reason given");
                break;
            case 403:
                // The author switched the counter off, or the mod was paused or blocked
                sent.DropPending(item.Key);
                relay.DropWorks(item.ModId);
                break;
            default:
                // 429, 5xx, no answer: once more at the next check, then dropped
                bool kept = sent.FailPending(item.Key);
                capi.Logger.Notification("[{0}] The relay didn't take the \"works for me\" result for {1} now ({2}), {3}", Core.ModId, item.ModId,
                    answer.Status?.ToString() ?? "no answer", kept ? "trying again next time" : "dropped after a second try");
                break;
        }
        if (sent.Problem != null) capi.Logger.Warning("[{0}] {1}", Core.ModId, sent.Problem);
    }
}
