using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UserFeedback;

/// <summary>
/// A mod's own wording for the bug report and the feedback report, from "panel" in its userfeedback.json.
/// Anything left out stays the standard UserFeedback panel. The report is unchanged: a feedback rating is still
/// great, good, meh or bad, so the relay and the author's tools keep reading it.
/// </summary>
public class PanelConfig
{
    public string? Title;
    public string? TitleKey;

    public PanelKindText? Feedback;
    public PanelKindText? Bug;

    public string? WhichMod;
    public string? WhichModKey;
    public string? Screenshot;
    public string? ScreenshotKey;
    public string? SendTo;
    public string? SendToKey;
    public string? PublicAllowed;
    public string? PublicAllowedKey;
    public string? Cancel;
    public string? CancelKey;
    public string? Preview;
    public string? PreviewKey;
    public string? Send;
    public string? SendKey;

    /// <summary>The four faces, by great, good, meh or bad. A label replaces the name under the face, an icon the drawing.</summary>
    public Dictionary<string, PanelFace>? Ratings;

    public bool Customized =>
        Feedback?.Any == true || Bug?.Any == true || Ratings is { Count: > 0 }
        || PanelRules.AnySet(Title, TitleKey, WhichMod, WhichModKey, Screenshot, ScreenshotKey, SendTo, SendToKey,
            PublicAllowed, PublicAllowedKey, Cancel, CancelKey, Preview, PreviewKey, Send, SendKey);

    public PanelFace? Face(string code) => Ratings != null && Ratings.TryGetValue(code, out PanelFace? face) ? face : null;
}

/// <summary>Wording for one kind of report. Feedback uses heading, prompt, text and switch. A bug uses the rest too, and no prompt.</summary>
public class PanelKindText
{
    public string? Heading;
    public string? HeadingKey;
    public string? Prompt;
    public string? PromptKey;
    public string? Text;
    public string? TextKey;
    public string? Switch;
    public string? SwitchKey;
    public string? Involved;
    public string? InvolvedKey;
    public string? Details;
    public string? DetailsKey;
    public string? FullLogs;
    public string? FullLogsKey;
    public string? Review;
    public string? ReviewKey;

    public bool Any => PanelRules.AnySet(Heading, HeadingKey, Prompt, PromptKey, Text, TextKey, Switch, SwitchKey,
        Involved, InvolvedKey, Details, DetailsKey, FullLogs, FullLogsKey, Review, ReviewKey);
}

/// <summary>One of the four faces. Icon is an svg in the mod's own assets, like "polymorph:textures/icons/great.svg".</summary>
public class PanelFace
{
    public string? Label;
    public string? LabelKey;
    public string? Icon;
}

/// <summary>What a panel may say. A field that breaks a rule is left out and the standard wording is used there.</summary>
public static class PanelRules
{
    public const int MaxTitle = 40;
    public const int MaxHeading = 48;
    public const int MaxButton = 32;
    public const int MaxFace = 16;
    public const int MaxLine = 70;

    private static readonly Regex IconPattern = new("^[a-z0-9_]+:[a-z0-9_./-]+\\.svg$");

    /// <summary>
    /// Drops fields that break a rule, listing each in problems. Returns null when nothing usable is left,
    /// which is the standard panel.
    /// </summary>
    /// <param name="prefix">Put before each problem, like "panel "</param>
    public static PanelConfig? Check(string modId, PanelConfig? panel, List<string> problems, string prefix = "")
    {
        if (panel == null) return null;

        var check = new Checker(problems, modId, prefix);
        check.Pair("title", ref panel.Title, ref panel.TitleKey, MaxTitle);
        panel.Feedback = Kind(check, "feedback", panel.Feedback, bug: false);
        panel.Bug = Kind(check, "bug", panel.Bug, bug: true);
        check.Pair("whichMod", ref panel.WhichMod, ref panel.WhichModKey, MaxLine);
        check.Pair("screenshot", ref panel.Screenshot, ref panel.ScreenshotKey, MaxLine);
        check.Pair("sendTo", ref panel.SendTo, ref panel.SendToKey, MaxLine);
        check.Pair("publicAllowed", ref panel.PublicAllowed, ref panel.PublicAllowedKey, MaxLine);
        check.Pair("cancel", ref panel.Cancel, ref panel.CancelKey, MaxButton);
        check.Pair("preview", ref panel.Preview, ref panel.PreviewKey, MaxButton);
        check.Pair("send", ref panel.Send, ref panel.SendKey, MaxButton);
        panel.Ratings = Faces(check, panel.Ratings);

        return panel.Customized ? panel : null;
    }

    private static PanelKindText? Kind(Checker check, string name, PanelKindText? kind, bool bug)
    {
        if (kind == null) return null;

        check.Pair($"{name}.heading", ref kind.Heading, ref kind.HeadingKey, MaxHeading);
        check.Pair($"{name}.text", ref kind.Text, ref kind.TextKey, MaxLine);
        check.Pair($"{name}.switch", ref kind.Switch, ref kind.SwitchKey, MaxButton);

        // Feedback has a prompt above the faces; a bug has the involved mods, details, full logs and review instead
        if (bug)
        {
            check.Unused(name, "prompt", ref kind.Prompt, ref kind.PromptKey);
            check.Pair($"{name}.involved", ref kind.Involved, ref kind.InvolvedKey, MaxLine);
            check.Pair($"{name}.details", ref kind.Details, ref kind.DetailsKey, MaxLine);
            check.Pair($"{name}.fullLogs", ref kind.FullLogs, ref kind.FullLogsKey, MaxLine);
            check.Pair($"{name}.review", ref kind.Review, ref kind.ReviewKey, MaxButton);
        }
        else
        {
            check.Pair($"{name}.prompt", ref kind.Prompt, ref kind.PromptKey, MaxLine);
            check.Unused(name, "involved", ref kind.Involved, ref kind.InvolvedKey);
            check.Unused(name, "details", ref kind.Details, ref kind.DetailsKey);
            check.Unused(name, "fullLogs", ref kind.FullLogs, ref kind.FullLogsKey);
            check.Unused(name, "review", ref kind.Review, ref kind.ReviewKey);
        }

        return kind.Any ? kind : null;
    }

    private static Dictionary<string, PanelFace>? Faces(Checker check, Dictionary<string, PanelFace>? ratings)
    {
        if (ratings == null) return null;

        var kept = new Dictionary<string, PanelFace>();
        foreach ((string code, PanelFace? face) in ratings)
        {
            if (!FeedbackReport.Ratings.Contains(code))
            {
                check.Problem($"rating \"{code}\" isn't one of {string.Join(", ", FeedbackReport.Ratings)}");
                continue;
            }
            if (face == null)
            {
                check.Problem($"rating \"{code}\" is empty");
                continue;
            }

            check.Pair($"rating \"{code}\" label", ref face.Label, ref face.LabelKey, MaxFace);
            face.Icon = check.Icon(code, face.Icon);
            if (AnySet(face.Label, face.LabelKey, face.Icon)) kept[code] = face;
        }
        return kept.Count > 0 ? kept : null;
    }

    /// <summary>Whether any of these is set</summary>
    public static bool AnySet(params string?[] values) => values.Any(value => value != null);

    /// <summary>Checks one mod's fields, collecting a line in problems for each one left out</summary>
    private sealed class Checker(List<string> problems, string modId, string prefix)
    {
        public void Problem(string message) => Add(message);

        private void Add(string message) => problems.Add(prefix + message);

        /// <summary>A text and the lang key that can replace it, named field and field + "Key"</summary>
        public void Pair(string field, ref string? text, ref string? key, int max)
        {
            text = Text(field, text, max);
            key = Key(field + "Key", key);
        }

        /// <summary>A pair this kind of report has no place for</summary>
        public void Unused(string kind, string field, ref string? text, ref string? key)
        {
            if (text != null) Add($"{kind} doesn't use \"{field}\"");
            if (key != null) Add($"{kind} doesn't use \"{field}Key\"");
            text = key = null;
        }

        public string? Icon(string code, string? icon)
        {
            if (icon == null) return null;
            if (icon.StartsWith(modId + ":", StringComparison.Ordinal) && !icon.Contains("..") && IconPattern.IsMatch(icon)) return icon;
            Add($"rating \"{code}\" icon must be an svg in {modId}'s own assets, like \"{modId}:textures/icons/great.svg\"");
            return null;
        }

        // An empty or over-long text is left out. Null means the mod didn't set it.
        private string? Text(string field, string? text, int max)
        {
            if (text == null) return null;
            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                Add($"\"{field}\" is empty");
                return null;
            }
            if (trimmed.Length > max)
            {
                Add($"\"{field}\" is over {max} characters");
                return null;
            }
            return trimmed;
        }

        // A lang key from the mod's own domain, so the panel can be translated without borrowing another mod's texts
        private string? Key(string field, string? key)
        {
            if (key == null) return null;
            if (key.StartsWith(modId + ":", StringComparison.Ordinal) && key.Length > modId.Length + 1 && !key.Contains(' ')) return key;
            Add($"\"{field}\" must start with \"{modId}:\"");
            return null;
        }
    }
}
