using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;

namespace UserFeedback;

/// <summary>
/// The event bus API, for mods that want more than their userfeedback.json. Works by event name only,
/// so a mod using it doesn't reference UserFeedback.dll and keeps working without it.
/// </summary>
public static class FeedbackEvents
{
    /// <summary>
    /// Pushed on the client when F8 is pressed, with an empty TreeAttribute. A mod adds a subtree named after its modid
    /// with any values it wants in the report. Only the subtrees of the mods a report is about are kept.
    /// Files can't be asked for here: anyone on the event bus could use any modid, so files are only taken from a mod's
    /// own userfeedback.json, where it's known which mod asked.
    /// </summary>
    public const string Collect = "userfeedback:collect";

    /// <summary>
    /// Push it on the client to open the panel, from a player's action in your mod (a button, a hotkey). Either a StringAttribute holding a
    /// modid, or a TreeAttribute with "modid", optionally "kind" ("bug" or "feedback", which skips the "bug or
    /// feedback?" question, or "works" for the "works for me" panel), "text" (put in the text box, the player can
    /// still change it; not used by "works") and "data" (a TreeAttribute added to that one report's data for the mod, like
    /// what <see cref="Collect"/> adds, within the same limits). With "works" nothing is sent until the player clicks the button there.
    /// It's ignored for <see cref="OpenRequest.Cooldown"/> after the last time a mod opened it, so a mod can't keep
    /// popping it up on its own.
    /// </summary>
    public const string Open = "userfeedback:open";

    /// <summary>
    /// Pushed on the client by UserFeedback after a bug or feedback report was handled, with a TreeAttribute holding
    /// "modid" (the mod the report is about), "kind" ("bug" or "feedback") and "outcome": "saved" (only kept on the
    /// player's computer), "opened" (the player's browser was opened on GitHub or the author's page, where the player
    /// still sends it), "delivered" (uploaded and accepted) or "failed" (not saved, or not accepted). Never the report's
    /// contents. Any mod can push an event of this name, so treat it as a hint, like for showing a "thanks".
    /// </summary>
    public const string Sent = "userfeedback:sent";

    public const string Saved = "saved";
    public const string Opened = "opened";
    public const string Delivered = "delivered";
    public const string Failed = "failed";

    /// <summary>The <see cref="Sent"/> payload</summary>
    public static TreeAttribute SentData(string modId, string kind, string outcome)
    {
        var tree = new TreeAttribute();
        tree.SetString("modid", modId);
        tree.SetString("kind", kind);
        tree.SetString("outcome", outcome);
        return tree;
    }

    /// <summary>Pushes <see cref="Sent"/>. One mod's broken listener doesn't stop UserFeedback.</summary>
    public static void PushSent(ICoreClientAPI capi, string? modId, string kind, string outcome)
    {
        if (string.IsNullOrEmpty(modId)) return;
        try
        {
            capi.Event.PushEvent(Sent, SentData(modId, kind, outcome));
        }
        catch (Exception e)
        {
            capi.Logger.Warning("[{0}] A mod failed while handling {1}: {2}", Core.ModId, Sent, e);
        }
    }
}
