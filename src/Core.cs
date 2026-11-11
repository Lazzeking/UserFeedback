using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace UserFeedback;

/// <summary>
/// Client only: F8 takes a screenshot, then opens the feedback panel. The mod is not needed on the server
/// (requiredOnServer is false), so players can use it in any multiplayer world.
/// </summary>
public class Core : ModSystem
{
    public const string ModId = "userfeedback";
    public const string ConfigFile = ModId + ".json";
    public const string HotkeyCode = ModId + "-open";

    /// <summary>This mod's version from modinfo.json, sent as part of the User-Agent of uploads</summary>
    public static string Version { get; private set; } = "0";

    private static Harmony? harmony;

    private ICoreClientAPI? capi;
    private ScreenshotCapture? capture;
    private DestinationRegistry? registry;
    private FeedbackDialog? dialog;
    private bool capturing;

    public UserFeedbackConfig Config { get; private set; } = new();

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void Start(ICoreAPI api)
    {
        PatchOnce(api);
    }

    public override void StartClientSide(ICoreClientAPI capi)
    {
        this.capi = capi;
        Version = Mod.Info.Version;
        Config = LoadConfig(capi);
        capture = new ScreenshotCapture(capi);
        registry = new DestinationRegistry(capi);

        capi.Input.RegisterHotKey(HotkeyCode, Lang("hotkey"), GlKeys.F8, HotkeyType.GUIOrOtherControls);
        capi.Input.SetHotKeyHandler(HotkeyCode, _ =>
        {
            if (dialog?.IsOpened() == true) dialog.TryClose();
            else OpenDialog();
            return true;
        });

        new Commands(capi, this, registry);
    }

    /// <summary>Takes the screenshot on the next frame, then opens the panel.</summary>
    public void OpenDialog()
    {
        if (capi == null || capture == null || registry == null || capturing || dialog?.IsOpened() == true) return;

        capturing = true;
        capture.Request(screenshot =>
        {
            capturing = false;
            dialog = new FeedbackDialog(capi, Config, registry, screenshot);
            dialog.TryOpen();
        });
    }

    public override void Dispose()
    {
        dialog?.TryClose();
        capture?.Dispose();
        harmony?.UnpatchAll(harmony.Id);
        harmony = null;
    }

    private static string Lang(string key) => Vintagestory.API.Config.Lang.Get($"{ModId}:{key}");

    // Patch only the first time, in case Start runs more than once in this process
    private void PatchOnce(ICoreAPI api)
    {
        if (harmony != null) return;

        harmony = new Harmony(ModId);
        harmony.PatchAllUncategorized(typeof(Core).Assembly);
    }

    // If another mod changed what a category patches so one patch fails, the whole category is taken back out
    // and the rest of the mod keeps working. fallback says what the player gets instead. Unused until the mod has patches.
    private bool PatchCategory(ICoreAPI api, string category, string fallback)
    {
        try
        {
            harmony!.PatchCategory(typeof(Core).Assembly, category);
            return true;
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not apply the {0} patches, so {1}: {2}", category, fallback, e.Message);
            foreach (MethodBase original in harmony!.GetPatchedMethods().ToList())
            {
                Patches? info = Harmony.GetPatchInfo(original);
                if (info == null) continue;

                foreach (Patch patch in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers))
                {
                    if (patch.owner == harmony.Id && InCategory(patch.PatchMethod.DeclaringType, category))
                    {
                        harmony.Unpatch(original, patch.PatchMethod);
                    }
                }
            }
            return false;
        }
    }

    private static bool InCategory(Type? type, string category)
    {
        return type != null && type.GetCustomAttributes<HarmonyPatchCategory>(false).Any(attribute => attribute.info.category == category);
    }

    private UserFeedbackConfig LoadConfig(ICoreAPI api)
    {
        UserFeedbackConfig? config;
        try
        {
            config = api.LoadModConfig<UserFeedbackConfig>(ConfigFile);
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Failed to read {0}, using default settings: {1}", ConfigFile, e.Message);
            return new UserFeedbackConfig();
        }

        // Writes the file on first start and adds settings that newer versions introduced
        config ??= new UserFeedbackConfig();
        api.StoreModConfig(config, ConfigFile);
        return config;
    }
}
