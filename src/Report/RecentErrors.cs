using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace UserFeedback;

/// <summary>
/// Remembers the client's newest errors, so F8 can pick the mod a recent error is about when the player doesn't look at
/// or hold anything of a mod. It reads the log, not an event, so a mod can't use it to steer reports to itself; it uses
/// the same matching as the log extract, "logMatch" included.
/// </summary>
public class RecentErrors
{
    /// <summary>Older errors are likely not what the player wants to report</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    private const int Kept = 20;
    /// <summary>A whole stack trace isn't needed to tell the mod, and keeps matching cheap</summary>
    private const int MaxText = 4000;

    private readonly object sync = new();
    private readonly LinkedList<(DateTime Utc, string Text)> errors = new();
    private ILogger? logger;

    public void Attach(ILogger logger)
    {
        Detach();
        this.logger = logger;
        logger.EntryAdded += OnEntry;
    }

    public void Detach()
    {
        if (logger != null) logger.EntryAdded -= OnEntry;
        logger = null;
    }

    // Can come from any thread
    private void OnEntry(EnumLogType type, string message, object[] args)
    {
        if (type != EnumLogType.Error && type != EnumLogType.Fatal) return;

        string text;
        try
        {
            text = args.Length > 0 ? string.Format(message, args) : message;
        }
        catch (FormatException)
        {
            text = message;
        }
        if (text.Length > MaxText) text = text[..MaxText];

        lock (sync)
        {
            errors.AddLast((DateTime.UtcNow, text));
            while (errors.Count > Kept) errors.RemoveFirst();
        }
    }

    /// <summary>The mod of the newest recent error that names exactly one of these mods, or null</summary>
    public string? Guess(IEnumerable<Mod> pickable, System.Func<string, IEnumerable<string>> logMatch)
    {
        List<(DateTime Utc, string Text)> snapshot;
        DateTime now = DateTime.UtcNow;
        lock (sync) snapshot = errors.Where(error => now - error.Utc <= MaxAge).ToList();
        // Usually so: then no matcher is built for each mod
        if (snapshot.Count == 0) return null;

        var candidates = pickable
            .Where(mod => mod.Info.ModID != Core.ModId)
            .Select(mod => (mod.Info.ModID, (System.Func<string, bool>)new ModLogFilter(new[] { mod }, logMatch).Matches))
            .ToList();
        return Pick(snapshot, now, candidates);
    }

    /// <summary>
    /// Newest first, the first error within MaxAge that exactly one mod matches. An error naming several mods says
    /// nothing about which one to report, so it's passed over.
    /// </summary>
    public static string? Pick(IEnumerable<(DateTime Utc, string Text)> errors, DateTime nowUtc, IReadOnlyList<(string ModId, System.Func<string, bool> Matches)> mods)
    {
        foreach ((DateTime utc, string text) in errors.OrderByDescending(error => error.Utc))
        {
            if (nowUtc - utc > MaxAge) break;

            List<string> named = mods.Where(mod => mod.Matches(text)).Select(mod => mod.ModId).Take(2).ToList();
            if (named.Count == 1) return named[0];
        }
        return null;
    }
}
