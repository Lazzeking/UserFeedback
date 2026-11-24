using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace UserFeedback;

/// <summary>
/// ".uf check &lt;modid&gt;": for mod authors, what a bug report about their mod would hold right now and why something
/// is missing. Reads the same things the panel does (userfeedback.json, the relay list, the files, the logs, the
/// collect event), sends nothing.
/// </summary>
public static class SetupCheck
{
    public static string Run(ICoreClientAPI capi, DestinationRegistry registry, RelayRegistry relay, string modId)
    {
        Mod? mod = capi.ModLoader.GetMod(modId);
        if (mod == null) return $"No mod \"{modId}\" is loaded. Mod ids are lower case, like the one in modinfo.json.";

        var lines = new List<string> { $"{mod.Info.Name} ({modId}) {mod.Info.Version}" };
        registry.Files.TryGetValue(modId, out FeedbackConfigFile? file);

        lines.Add(file == null
            ? $"userfeedback.json: none used (looked for assets/{modId}/{DestinationRegistry.AssetPath} in the mod's own files)"
            : "userfeedback.json: read");
        lines.AddRange(registry.Problems.Where(problem => problem.StartsWith(modId + ":", StringComparison.Ordinal)).Select(problem => "! " + problem));

        Destinations(lines, file, relay, modId);
        Files(lines, capi, registry, modId);
        Logs(lines, capi, registry, mod);
        Data(lines, capi, modId);
        if (file?.Panel != null) lines.Add("Panel: its own wording");

        return string.Join("\n", lines);
    }

    private static void Destinations(List<string> lines, FeedbackConfigFile? file, RelayRegistry relay, string modId)
    {
        lines.Add("Destinations (saving on the player's computer is always offered):");
        foreach (DestinationConfig d in file?.Destinations ?? new())
        {
            lines.Add("  " + d.Summary());
        }

        if (relay.BaseUrl == null)
        {
            lines.Add("  UserFeedback relay: switched off in this player's settings");
            return;
        }
        if (!relay.Knows(modId))
        {
            // With buckets only the picked mods' buckets are fetched; this one is now on its way
            relay.Prefetch(modId);
            lines.Add($"  UserFeedback relay ({relay.Host}): list not in yet, run this again in a moment");
            return;
        }

        var kinds = new[] { FeedbackReport.Bug, FeedbackReport.Feedback }.Where(kind => relay.Has(modId, kind)).ToList();
        lines.Add(kinds.Count == 0
            ? $"  UserFeedback relay ({relay.Host}): doesn't take reports about {modId}"
            : $"  UserFeedback relay ({relay.Host}): takes {string.Join(" and ", kinds)} reports");
        lines.Add(relay.CountsWorks(modId) ? "Works for me: counted on the relay" : "Works for me: not counted on the relay");
    }

    private static void Files(List<string> lines, ICoreClientAPI capi, DestinationRegistry registry, string modId)
    {
        List<string> patterns = registry.FilesFor(modId);
        if (patterns.Count == 0) return;

        lines.Add("Files (bug reports with game details):");
        foreach (string pattern in patterns)
        {
            List<RequestedFile> found = ExtraFiles.Resolve(new[] { (modId, pattern) }, capi.Logger);
            lines.Add(found.Count == 0
                ? $"  ! {pattern}: nothing found, or only files that aren't allowed (text files in ModConfig, ModData or Logs, at most {ExtraFiles.MaxFileBytes / 1024 / 1024} MB each)"
                : $"  {pattern}: {found.Count} file(s), {Sizes.Format(found.Sum(f => f.Size))}");
            foreach (RequestedFile notOwn in found.Where(f => !f.NamedAfterMod).Take(3))
            {
                lines.Add($"    {notOwn.Path} doesn't name {modId}, so the player is warned it may be another mod's");
            }
        }

        // All together, like a real report: the limits apply to the sum
        int total = ExtraFiles.Resolve(patterns.Select(p => (modId, p)), capi.Logger).Count;
        if (total >= ExtraFiles.MaxFiles) lines.Add($"  ! {ExtraFiles.MaxFiles} files is the most a report takes, the rest are left out");
    }

    private static void Logs(List<string> lines, ICoreClientAPI capi, DestinationRegistry registry, Mod mod)
    {
        string modId = mod.Info.ModID;
        List<string> logMatch = registry.LogMatchFor(modId);
        LogSnapshot snapshot;
        try
        {
            snapshot = LogSnapshot.Read(capi.IsSinglePlayer);
        }
        catch (Exception e)
        {
            lines.Add($"Log lines: ! could not read the logs: {e.Message}");
            return;
        }

        int standard = new ModLogFilter(new[] { mod }).Matching(snapshot).Count;
        List<LogEntry> all = new ModLogFilter(new[] { mod }, _ => logMatch).Matching(snapshot);
        string line = $"Log lines (bug reports with game details): {all.Count} about {modId} in this session's logs";
        if (all.Count >= ModLogFilter.MaxEntries) line += $", only the newest {ModLogFilter.MaxEntries} are kept";
        lines.Add(line);

        if (logMatch.Count > 0)
        {
            lines.Add($"  {all.Count - standard} of them only through logMatch ({string.Join(", ", logMatch)})");
        }
        else if (all.Count == 0)
        {
            lines.Add("  None name the mod's id, name, file or code. If it logs under another name, list it in \"logMatch\".");
        }
    }

    private static void Data(List<string> lines, ICoreClientAPI capi, string modId)
    {
        // The same event F8 pushes, so a listener that only works on F8 shows here too
        ModContributions contributions = ModContributions.Collect(capi);
        if (contributions.TooBig.Contains(modId))
        {
            lines.Add($"Data from userfeedback:collect: ! over {ModContributions.MaxModBytes / 1024} KB, so it's left out of reports");
        }
        else if (contributions.Data.TryGetValue(modId, out JToken? data))
        {
            int keys = data is JObject obj ? obj.Count : 1;
            lines.Add($"Data from userfeedback:collect: {keys} value(s), {Sizes.Format(ModContributions.EncodedSize(data))} of {ModContributions.MaxModBytes / 1024} KB");
        }
        else
        {
            lines.Add("Data from userfeedback:collect: none");
        }
    }

}
