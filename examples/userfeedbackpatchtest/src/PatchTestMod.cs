using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace UserFeedbackPatchTest;

/// <summary>
/// Does what a careless or malicious mod might, so UserFeedback's reactions can be checked:
/// 1. A Harmony postfix on UserFeedback's ReportText.Title. The F8 panel should warn about this mod, and
///    ".uf destinations" and client-main.log should list ReportText.Title.
/// 2. Data over 16 KB under another mod's modid (userfeedbackexample) in userfeedback:collect. With that mod picked
///    in a bug report, the data should be left out and listed in LeftOut as ModData.userfeedbackexample.
/// 3. A small subtree under its own modid, which should be kept as usual.
/// </summary>
public class PatchTestMod : ModSystem
{
    // The same as the modid, so UserFeedback can name the mod instead of showing a raw Harmony id
    private const string HarmonyId = "userfeedbackpatchtest";

    private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI capi)
    {
        // Found by name, so this mod doesn't need UserFeedback.dll and loads fine without it
        MethodInfo? title = AccessTools.Method("UserFeedback.ReportText:Title");
        if (title == null)
        {
            Mod.Logger.Warning("UserFeedback.ReportText.Title not found, is UserFeedback installed? Nothing patched.");
        }
        else
        {
            harmony = new Harmony(HarmonyId);
            harmony.Patch(title, postfix: new HarmonyMethod(typeof(PatchTestMod), nameof(TitlePostfix)));
            Mod.Logger.Notification("Patched UserFeedback.ReportText.Title, the F8 panel should now warn about this mod.");
        }

        capi.Event.RegisterEventBusListener((string _, ref EnumHandling handling, IAttribute data) =>
        {
            if (data is not ITreeAttribute tree) return;

            tree.GetOrAddTreeAttribute(Mod.Info.ModID).SetString("note", "small data under its own modid, should be kept");
            // 20 KB, over the 16 KB a mod may add, and under someone else's modid
            tree.GetOrAddTreeAttribute("userfeedbackexample").SetString("padding", new string('x', 20 * 1024));
        }, filterByEventName: "userfeedback:collect");
    }

    // Visible in a GitHub issue's title, so the change itself can be seen too
    private static void TitlePostfix(ref string __result)
    {
        __result = "[patched by test mod] " + __result;
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
    }
}
