using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;

namespace UserFeedback;

/// <summary>
/// Blanks personal details out of log text before it is saved: the home folder, the player's name and uid,
/// the server they connect to, ip addresses, emails and anything that looks like a password or token.
/// Built on the main thread, Clean can then run on any thread.
/// </summary>
public class Sanitizer
{
    private readonly List<(Regex Pattern, string Replacement)> rules = new();

    public Sanitizer(ICoreClientAPI capi)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 3) Literal(home, "~");
        // Other home folders, for example the Flatpak's view of it
        rules.Add((new Regex(@"(/home/|/Users/|[A-Za-z]:\\Users\\)[^/\\\s""']+"), "$1<user>"));

        IClientPlayer? player = capi.World?.Player;
        if (player != null)
        {
            Word(player.PlayerUID, "<player-uid>");
            Word(player.PlayerName, "<player>");
        }
        Word(capi.Settings.String["playername"], "<player>");

        // Every part of the domain starts with a letter and it isn't a file, so "timefliesby@0.1.0" and "mod@Some-v1.0.zip" stay
        rules.Add((new Regex(@"[\w.+-]+@[A-Za-z][\w-]*(\.[A-Za-z][\w-]*)+(?<!\.(zip|dll|cs|json|png|svg|txt|log))(?![\w.])"), "<email>"));
        rules.Add((new Regex(@"(?i)\b(session ?key|sessionkey|mptoken|token|password|passwd|api_?key|secret)\b(\s*[:=]\s*""?)[^\s"",;]+"), "$1$2<hidden>"));
    }

    // a.b.c.d is an ip address when it has a :port, or when every part fits one and the first is 10 or more.
    // That leaves version numbers like "Kernel 7.0.0.42" and "Version=10.0.0.0" (after "=") alone, and loopback.
    private static readonly Regex Ipv4 = new(@"(?<![\w.=])(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(:\d+)?(?![\w.])");

    private static string CleanIp(Match match)
    {
        int[] parts = Enumerable.Range(1, 4).Select(i => int.Parse(match.Groups[i].Value)).ToArray();
        bool hasPort = match.Groups[5].Success;
        if (parts.Any(part => part > 255) || parts[0] == 127 || parts.All(part => part == 0)) return match.Value;
        if (!hasPort && parts[0] < 10) return match.Value;
        return "<ip>" + match.Groups[5].Value;
    }

    /// <summary>The cleaned text. Server names are found in the text itself, from the game's "Connecting to ..." line.</summary>
    public string Clean(string text)
    {
        foreach ((Regex pattern, string replacement) in rules)
        {
            text = pattern.Replace(text, replacement);
        }
        text = Ipv4.Replace(text, CleanIp);

        foreach (string host in ServerHosts(text))
        {
            text = text.Replace(host, "<server>", StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    private static IEnumerable<string> ServerHosts(string text)
    {
        return Regex.Matches(text, @"Connecting to (\S+?)\.\.\.")
            .Select(match => match.Groups[1].Value.Split(':')[0])
            .Where(host => host.Length > 2 && host != "<ip>" && host != "localhost")
            .Distinct();
    }

    private void Literal(string value, string replacement)
    {
        rules.Add((new Regex(Regex.Escape(value), RegexOptions.IgnoreCase), replacement));
    }

    private void Word(string? value, string replacement)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 3) return;
        rules.Add((new Regex(@"(?<![\w])" + Regex.Escape(value) + @"(?![\w])", RegexOptions.IgnoreCase), replacement));
    }
}
