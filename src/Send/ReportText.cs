using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UserFeedback;

/// <summary>
/// The report as Markdown, for a GitHub issue or for pasting into a mod author's page.
/// In English whatever the player's language, it's read by the author.
/// </summary>
public static class ReportText
{
    public static string Title(FeedbackReport report)
    {
        string about = report.ModName ?? "a mod";
        string rating = report.Rating != null ? $" ({Capitalize(report.Rating)})" : "";
        string start = FirstWords(report.Text, 60);
        return start.Length > 0 ? $"[Feedback{rating}] {start}" : $"[Feedback{rating}] {about}";
    }

    /// <summary>The report without its log, then as many of the newest log lines as fit in maxLength</summary>
    public static string Body(FeedbackReport report, string? log, int maxLength, bool attachmentsNote)
    {
        var text = new StringBuilder();

        if (report.Rating != null) text.AppendLine($"**Rating:** {Capitalize(report.Rating)}  ");
        text.AppendLine($"**Mod:** {Describe(report.ModName, report.ModId, report.ModVersion)}  ");
        if (report.AlsoInvolved.Count > 0)
        {
            text.AppendLine($"**Also involved:** {string.Join(", ", report.AlsoInvolved.Select(m => Describe(m.Name, m.ModId, m.Version)))}  ");
        }
        text.AppendLine();
        text.AppendLine(report.Text.Length > 0 ? report.Text : "_No text._");
        text.AppendLine();

        if (report.Context is GameContext context)
        {
            text.AppendLine("<details><summary>Game details</summary>");
            text.AppendLine();
            text.AppendLine($"- Game: {report.GameVersion}, {(report.Singleplayer ? "singleplayer" : "multiplayer")}, {context.GameMode}, language {context.Language}");
            if (context.Position != null) text.AppendLine($"- Position from spawn: {context.Position}");
            if (context.Date != null) text.AppendLine($"- Date: {context.Date}, {context.Season}");
            if (context.Temperature != null) text.AppendLine($"- Temperature {context.Temperature} °C, rainfall {context.Rainfall}");
            if (report.LookingAt != null) text.AppendLine($"- Looking at: `{report.LookingAt}`");
            if (report.Holding != null) text.AppendLine($"- Holding: `{report.Holding}`");
            foreach ((string key, string value) in context.System) text.AppendLine($"- {key}: {value}");
            text.AppendLine($"- Mods ({context.Mods.Count}): {ModList(context.Mods, 1500)}");
            text.AppendLine();
            text.AppendLine("</details>");
            text.AppendLine();
        }

        var files = new List<string>();
        if (report.HasScreenshot) files.Add("`screenshot.png`");
        if (report.LogEntries > 0) files.Add("`log.txt`");
        if (report.HasFullLogs) files.Add("`logs.zip`");
        string footer = attachmentsNote && files.Count > 0
            ? $"_Sent with UserFeedback. The player's report folder also has {string.Join(", ", files)}, they can drag them in here._"
            : "_Sent with UserFeedback._";

        if (!string.IsNullOrWhiteSpace(log))
        {
            const string open = "<details><summary>Log lines about these mods</summary>\n\n```\n";
            const string close = "```\n\n</details>\n\n";
            int room = maxLength - text.Length - footer.Length - open.Length - close.Length - 40;
            string excerpt = NewestLines(log, room);
            if (excerpt.Length > 0)
            {
                text.Append(open);
                if (excerpt.Length < log.Length) text.AppendLine("(older lines cut, see log.txt)");
                text.AppendLine(excerpt.Replace("```", "'''"));
                text.Append(close);
            }
        }

        text.Append(footer);
        return text.ToString();
    }

    // Big mod lists would push everything else out of a GitHub link, the full list is in report.json
    private static string ModList(List<string> mods, int max)
    {
        var shown = new List<string>();
        int length = 0;
        foreach (string mod in mods)
        {
            if (length + mod.Length + 2 > max) break;
            shown.Add(mod);
            length += mod.Length + 2;
        }
        string list = string.Join(", ", shown);
        return shown.Count < mods.Count ? $"{list} and {mods.Count - shown.Count} more (see report.json)" : list;
    }

    private static string Describe(string? name, string? id, string? version)
    {
        return id == null ? "not sure" : $"{name} {version} (`{id}`)";
    }

    private static string NewestLines(string log, int room)
    {
        if (room <= 0) return "";
        if (log.Length <= room) return log.TrimEnd();

        // Keep the end, starting at a whole line
        string tail = log[^room..];
        int newline = tail.IndexOf('\n');
        return (newline >= 0 ? tail[(newline + 1)..] : "").TrimEnd();
    }

    private static string FirstWords(string text, int max)
    {
        string line = text.Split('\n')[0].Trim();
        if (line.Length <= max) return line;

        int space = line.LastIndexOf(' ', max);
        return line[..(space > 20 ? space : max)] + "…";
    }

    private static string Capitalize(string word) => word.Length == 0 ? word : char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..];
}
