// UserFeedbackApi.cs: copy this one file into your mod to use UserFeedback from code. It only uses the game's event bus,
// so your mod needs no reference to UserFeedback.dll and keeps working when UserFeedback isn't installed.
// Change the namespace to your own if you like. Free to copy, change and ship with your mod, no credit needed.
// Version 1, for UserFeedback 0.1.0 and later. See docs/for-modders.md.

using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace UserFeedbackInterop;

/// <summary>Opens UserFeedback's panel for your mod, adds your mod's state to reports and hears what happened to them.</summary>
public static class UserFeedbackApi
{
    public const string ModId = "userfeedback";

    public const string CollectEvent = "userfeedback:collect";
    public const string OpenEvent = "userfeedback:open";
    public const string SentEvent = "userfeedback:sent";

    public const string Bug = "bug";
    public const string Feedback = "feedback";
    public const string Works = "works";

    /// <summary>Whether UserFeedback is loaded, so you can hide a "Report a problem" button without it</summary>
    public static bool Installed(ICoreClientAPI capi) => capi.ModLoader.IsModEnabled(ModId);

    /// <summary>
    /// Opens the panel with your mod picked, asking the player "bug or feedback?". Only call it when the player asks,
    /// from a button or a hotkey: UserFeedback ignores it for 10 seconds after a mod last opened the panel.
    /// Returns false when UserFeedback isn't installed.
    /// </summary>
    public static bool Open(ICoreClientAPI capi, string modId) => Push(capi, Request(modId, null, null, null));

    /// <summary>Opens the feedback panel (a rating, text and a screenshot), with text the player can change</summary>
    public static bool OpenFeedback(ICoreClientAPI capi, string modId, string? text = null) =>
        Push(capi, Request(modId, Feedback, text, null));

    /// <summary>
    /// Opens a bug report, with text the player can change and data for this one report, like the exception or the
    /// state of what failed. The data goes in when the player leaves game details switched on, within 16 KB for your
    /// mod, cleaned of personal details, and the player sees it in Show report. Files can't be added here: list them
    /// under "files" in your userfeedback.json.
    /// </summary>
    public static bool OpenBugReport(ICoreClientAPI capi, string modId, string? text = null, ITreeAttribute? data = null) =>
        Push(capi, Request(modId, Bug, text, data));

    /// <summary>Opens the "works for me" panel. Nothing is sent until the player clicks its button.</summary>
    public static bool OpenWorksForMe(ICoreClientAPI capi, string modId) => Push(capi, Request(modId, Works, null, null));

    /// <summary>
    /// Calls fill whenever the player presses F8, to add your mod's state to the report: at most 16 KB, kept only in
    /// reports about your mod. Keep fill quick, it runs before the panel opens.
    /// </summary>
    public static void OnCollect(ICoreClientAPI capi, string modId, Action<ITreeAttribute> fill)
    {
        capi.Event.RegisterEventBusListener((string eventName, ref EnumHandling handling, IAttribute data) =>
        {
            if (data is ITreeAttribute tree) fill(tree.GetOrAddTreeAttribute(modId));
        }, filterByEventName: CollectEvent);
    }

    /// <summary>
    /// Calls handler after a bug or feedback report about your mod was saved or sent. It never carries the report's
    /// contents. Any mod can push this event, so use it for small things like a thank-you.
    /// </summary>
    public static void OnSent(ICoreClientAPI capi, string modId, Action<SentReport> handler)
    {
        capi.Event.RegisterEventBusListener((string eventName, ref EnumHandling handling, IAttribute data) =>
        {
            SentReport? report = ReadSent(data, modId);
            if (report != null) handler(report);
        }, filterByEventName: SentEvent);
    }

    internal static SentReport? ReadSent(IAttribute data, string modId)
    {
        if (data is not ITreeAttribute tree || tree.GetString("modid") != modId) return null;
        return new SentReport(tree.GetString("kind") ?? "", tree.GetString("outcome") ?? "");
    }

    internal static TreeAttribute Request(string modId, string? kind, string? text, ITreeAttribute? data)
    {
        var request = new TreeAttribute();
        request.SetString("modid", modId);
        if (kind != null) request.SetString("kind", kind);
        if (!string.IsNullOrWhiteSpace(text)) request.SetString("text", text);
        if (data != null) request["data"] = data;
        return request;
    }

    private static bool Push(ICoreClientAPI capi, TreeAttribute request)
    {
        if (!Installed(capi)) return false;
        capi.Event.PushEvent(OpenEvent, request);
        return true;
    }
}

/// <summary>What happened to a report about your mod</summary>
/// <param name="Kind">"bug" or "feedback"</param>
/// <param name="Outcome">"saved", "opened", "delivered" or "failed"</param>
public record SentReport(string Kind, string Outcome)
{
    /// <summary>Only kept on the player's computer</summary>
    public bool Saved => Outcome == "saved";
    /// <summary>The player's browser opened your GitHub issue form or page, where they still submit it</summary>
    public bool Opened => Outcome == "opened";
    /// <summary>Uploaded, and your server or the relay accepted it</summary>
    public bool Delivered => Outcome == "delivered";
    /// <summary>Not saved, or not accepted</summary>
    public bool Failed => Outcome == "failed";
}
