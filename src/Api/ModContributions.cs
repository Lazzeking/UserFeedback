using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;

namespace UserFeedback;

/// <summary>
/// What mods added through <see cref="FeedbackEvents.Collect"/>, by modid. Any mod can write under any modid, so this
/// is limited in size: a mod can't make another mod's reports too big for the relay (which takes report.json up to
/// 256 KB) by stuffing data under its modid.
/// </summary>
public class ModContributions
{
    /// <summary>Above this, a mod's data is left out whole: cutting JSON would only leave something misleading</summary>
    public const int MaxModBytes = UploadLimits.MaxModDataBytes;
    /// <summary>All the data in one report together. The mod the report is about comes first.</summary>
    public const int MaxTotalBytes = UploadLimits.MaxModDataTotalBytes;

    public Dictionary<string, JToken> Data = new();
    /// <summary>Modids whose data was over MaxModBytes</summary>
    public HashSet<string> TooBig = new();

    /// <summary>
    /// Adds the data a mod sent with <see cref="FeedbackEvents.Open"/> to its data for this one report. Its keys replace
    /// the same keys from collect. When both together are over MaxModBytes, only the opened data is kept: it was sent for
    /// this report. data must already be within MaxModBytes.
    /// </summary>
    public void Attach(string modId, JObject data)
    {
        JToken merged = data;
        if (Data.TryGetValue(modId, out JToken? collected) && collected is JObject collectedObject)
        {
            var both = (JObject)collectedObject.DeepClone();
            both.Merge(data, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
            if (EncodedSize(both) <= MaxModBytes) merged = both;
        }
        Data[modId] = merged;
        TooBig.Remove(modId);
    }

    /// <summary>
    /// The data of these mods, in this order, as long as the total stays within MaxTotalBytes.
    /// leftOut has the modids whose data was there but too big.
    /// </summary>
    public Dictionary<string, JToken> For(IEnumerable<string> modIds, out List<string> leftOut)
    {
        var kept = new Dictionary<string, JToken>();
        leftOut = new List<string>();
        int total = 0;
        foreach (string modId in modIds.Distinct())
        {
            if (TooBig.Contains(modId))
            {
                leftOut.Add(modId);
                continue;
            }
            if (!Data.TryGetValue(modId, out JToken? data)) continue;

            int size = EncodedSize(data);
            if (total + size > MaxTotalBytes)
            {
                leftOut.Add(modId);
                continue;
            }
            total += size;
            kept[modId] = data;
        }
        return kept;
    }

    /// <summary>
    /// The size the relay sees, which re-encodes with PHP's json_encode: by default that writes "/" as "\/" and every
    /// non-ASCII character as \uXXXX. Counting the same way is an upper bound, so data within the limits here is
    /// never refused there, whichever flags it uses.
    /// </summary>
    public static int EncodedSize(JToken data)
    {
        int size = 0;
        foreach (char c in data.ToString(Formatting.None))
        {
            size += c > 127 ? 6 : c == '/' ? 2 : 1;
        }
        return size;
    }

    /// <summary>
    /// Applies the limits again to data that changed since, like after personal details were replaced with longer
    /// placeholders. Returns the modids taken out, in the order given.
    /// </summary>
    public static List<string> Enforce(Dictionary<string, JToken> data, IEnumerable<string> order)
    {
        var removed = new List<string>();
        int total = 0;
        foreach (string modId in order.Where(data.ContainsKey).ToList())
        {
            int size = EncodedSize(data[modId]);
            if (size > MaxModBytes || total + size > MaxTotalBytes)
            {
                data.Remove(modId);
                removed.Add(modId);
                continue;
            }
            total += size;
        }
        return removed;
    }

    /// <summary>
    /// A mod's data tree as JSON, the same for collect and open: "files" taken out (files only come from the mod's own
    /// userfeedback.json, where it's known which mod asked), empty is no data, over MaxModBytes is left out whole.
    /// The mod's own tree isn't changed.
    /// </summary>
    public static ModDataRead Read(ITreeAttribute tree)
    {
        bool hadFiles = tree.HasAttribute("files");
        if (hadFiles)
        {
            tree = (ITreeAttribute)tree.Clone();
            tree.RemoveAttribute("files");
        }
        if (tree.Count == 0) return new ModDataRead(null, hadFiles, false, null);

        try
        {
            if (JToken.Parse(tree.ToJsonToken()) is not JObject data) return new ModDataRead(null, hadFiles, false, null);
            return EncodedSize(data) > MaxModBytes ? new ModDataRead(null, hadFiles, true, null) : new ModDataRead(data, hadFiles, false, null);
        }
        catch (Exception e)
        {
            return new ModDataRead(null, hadFiles, false, e.Message);
        }
    }

    /// <summary>
    /// Cleans each mod's data of personal details, like the logs; placeholders like &lt;player&gt; keep it valid JSON.
    /// They can be longer than what they replace, so the limits are applied again: the relay refuses a whole report with
    /// one mod's data over them. Returns the modids taken out, in data's order (the picked mod first).
    /// </summary>
    public static List<string> Clean(Dictionary<string, JToken> data, Sanitizer sanitizer)
    {
        List<string> order = data.Keys.ToList();
        var removed = new List<string>();
        foreach (string modId in order)
        {
            try
            {
                data[modId] = JToken.Parse(sanitizer.Clean(data[modId].ToString(Formatting.None)));
            }
            catch (Exception)
            {
                data.Remove(modId);
                removed.Add(modId);
            }
        }
        removed.AddRange(Enforce(data, order));
        return removed;
    }

    public static ModContributions Collect(ICoreClientAPI capi)
    {
        var contributions = new ModContributions();
        var tree = new TreeAttribute();

        try
        {
            capi.Event.PushEvent(FeedbackEvents.Collect, tree);
        }
        catch (Exception e)
        {
            // One mod's broken listener shouldn't stop the report
            capi.Logger.Warning("[{0}] A mod failed while adding to the report: {1}", Core.ModId, e);
        }

        foreach ((string modId, IAttribute value) in tree)
        {
            if (value is not ITreeAttribute modTree) continue;

            ModDataRead read = Read(modTree);
            if (read.HadFiles)
            {
                capi.Logger.Warning("[{0}] {1} asked for files through the {2} event, which isn't possible. List them under \"files\" in its userfeedback.json instead.",
                    Core.ModId, modId, FeedbackEvents.Collect);
            }
            if (read.Error != null)
            {
                capi.Logger.Warning("[{0}] Could not read what {1} added to the report: {2}", Core.ModId, modId, read.Error);
            }
            else if (read.TooBig)
            {
                capi.Logger.Warning("[{0}] The data added for {1} is over {2} KB, so it's left out of reports.", Core.ModId, modId, MaxModBytes / 1024);
                contributions.TooBig.Add(modId);
            }
            else if (read.Data != null)
            {
                contributions.Data[modId] = read.Data;
            }
        }

        return contributions;
    }
}

/// <summary>What <see cref="ModContributions.Read"/> made of a mod's data tree</summary>
/// <param name="Data">Null when there was none, or it was left out</param>
/// <param name="HadFiles">The tree asked for "files", which were taken out</param>
/// <param name="TooBig">Over MaxModBytes, so left out</param>
/// <param name="Error">Why it couldn't be read</param>
public record ModDataRead(JObject? Data, bool HadFiles, bool TooBig, string? Error);
