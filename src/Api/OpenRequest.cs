using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;

namespace UserFeedback;

/// <summary>What a mod asked for through <see cref="FeedbackEvents.Open"/></summary>
/// <param name="Data">The "data" subtree, already within <see cref="ModContributions.MaxModBytes"/></param>
/// <param name="DataProblem">Why "data" was left out or changed, for the log</param>
public record OpenRequest(string? ModId, string? Kind, string? Text, JObject? Data = null, string? DataProblem = null)
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    public static OpenRequest From(IAttribute? data)
    {
        if (data is StringAttribute modId) return new OpenRequest(modId.value, null, null);
        if (data is not ITreeAttribute tree) return new OpenRequest(null, null, null);

        string? kind = tree.GetString("kind");
        if (kind != FeedbackReport.Bug && kind != FeedbackReport.Feedback && kind != WorksDialog.Choice) kind = null;
        string? text = tree.GetString("text");
        JObject? extra = ReadData(tree, out string? problem);
        return new OpenRequest(tree.GetString("modid"), kind, string.IsNullOrWhiteSpace(text) ? null : text, extra, problem);
    }

    private static JObject? ReadData(ITreeAttribute tree, out string? problem)
    {
        problem = null;
        if (!tree.HasAttribute("data")) return null;
        if (tree["data"] is not ITreeAttribute dataTree)
        {
            problem = "\"data\" isn't a TreeAttribute, so it's left out";
            return null;
        }

        ModDataRead read = ModContributions.Read(dataTree);
        problem = read.Error != null ? $"\"data\" could not be read, so it's left out: {read.Error}"
            : read.TooBig ? $"\"data\" is over {ModContributions.MaxModBytes / 1024} KB, so it's left out"
            : read.HadFiles ? "\"files\" can't be asked for here, it's left out. List them in userfeedback.json instead."
            : null;
        return read.Data;
    }
}
