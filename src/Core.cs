using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using static UserFeedback.ModLang;

namespace UserFeedback;

/// <summary>
/// Client only: F8 takes a screenshot, asks "bug, feedback or works for me?", then opens the feedback panel or the
/// "works for me" one. The mod is not needed on the server
/// (requiredOnServer is false), so players can use it in any multiplayer world.
/// </summary>
public class Core : ModSystem
{
    public const string ModId = "userfeedback";
    public const string ConfigFile = ModId + ".json";
    public const string HotkeyCode = ModId + "-open";

    /// <summary>This mod's version from modinfo.json, sent as part of the User-Agent of uploads</summary>
    public static string Version { get; private set; } = "0";

    private ICoreClientAPI? capi;
    private ScreenshotCapture? capture;
    private DestinationRegistry? registry;
    private RelayRegistry? relay;
    private readonly RecentErrors recentErrors = new();
    private KindSelectorDialog? selector;
    private FeedbackDialog? dialog;
    private WorksDialog? worksDialog;
    private WorksSent? worksSent;
    private WorksCheck? worksCheck;
    private bool capturing;
    // When a mod last opened the panel through the event, for OpenRequest.Cooldown
    private DateTime lastModOpen = DateTime.MinValue;

    public UserFeedbackConfig Config { get; private set; } = new();

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI capi)
    {
        this.capi = capi;
        // As early as possible, so errors while the world loads count too
        recentErrors.Attach(capi.Logger);
        Version = Mod.Info.Version;
        Config = LoadConfig(capi);
        capture = new ScreenshotCapture(capi);
        registry = new DestinationRegistry(capi);
        relay = new RelayRegistry(capi, Config);
        relay.Refresh();
        worksSent = new WorksSent(WorksSent.DefaultPath);
        if (worksSent.Problem != null) capi.Logger.Warning("[{0}] {1}", ModId, worksSent.Problem);
        worksCheck = new WorksCheck(capi, Config, relay, worksSent);
        // The automatic "works for me" check, a few minutes after the player is in the world
        capi.Event.LevelFinalize += worksCheck.Schedule;

        capi.Input.RegisterHotKey(HotkeyCode, L("hotkey"), GlKeys.F8, HotkeyType.GUIOrOtherControls);
        capi.Input.SetHotKeyHandler(HotkeyCode, _ =>
        {
            if (selector?.IsOpened() == true) selector.TryClose();
            else if (dialog?.IsOpened() == true) dialog.TryClose();
            else if (worksDialog?.IsOpened() == true) worksDialog.TryClose();
            else OpenDialog();
            return true;
        });

        new Commands(capi, this, registry, relay, worksCheck);

        // Other mods can open the panel with their mod picked, by event name so they don't need this dll
        capi.Event.RegisterEventBusListener((string _, ref EnumHandling handling, IAttribute data) => OpenFromMod(OpenRequest.From(data)),
            filterByEventName: FeedbackEvents.Open);
    }

    // Only for a player's action, like a button or a mod's own hotkey: the panel never pops up by itself (no prompts),
    // so a mod can't open it again shortly after a mod last opened it
    private void OpenFromMod(OpenRequest request)
    {
        if (capi == null) return;

        string from = request.ModId ?? "a mod";
        if (DateTime.UtcNow - lastModOpen < OpenRequest.Cooldown)
        {
            capi.Logger.Warning("[{0}] Ignored {1} from {2}: a mod opened the panel less than {3} seconds ago.",
                ModId, FeedbackEvents.Open, from, OpenRequest.Cooldown.TotalSeconds);
            return;
        }

        if (request.DataProblem != null)
        {
            capi.Logger.Warning("[{0}] {1} from {2}: {3}", ModId, FeedbackEvents.Open, from, request.DataProblem);
        }

        lastModOpen = DateTime.UtcNow;
        OpenDialog(request.ModId, request.Kind, request.Text, request.Data);
    }

    /// <summary>
    /// Takes the screenshot on the next frame, asks whether it's a bug or feedback unless kind is given, then opens the
    /// panel, with preselect picked if it's a loaded mod, text in the text box and data added to preselect's data for
    /// that report.
    /// </summary>
    public void OpenDialog(string? preselect = null, string? kind = null, string? text = null, JObject? data = null)
    {
        if (capi == null || capture == null || registry == null || relay == null || worksSent == null || capturing
            || selector?.IsOpened() == true || dialog?.IsOpened() == true || worksDialog?.IsOpened() == true) return;

        // A long session may outlive a day, the list then comes in for the next time the panel opens
        relay.Refresh();

        // "Works for me" attaches nothing, so no screenshot. When the mod doesn't count results or the relay is off,
        // the panel says so, so an author testing their button learns why.
        if (kind == WorksDialog.Choice)
        {
            worksDialog = new WorksDialog(capi, Config, relay, worksSent, preselect);
            worksDialog.TryOpen();
            return;
        }

        capturing = true;
        capture.Request(screenshot =>
        {
            capturing = false;
            if (kind != null)
            {
                OpenPanel(kind);
                return;
            }

            // The selector holds the screenshot until the panel takes it, or frees it when closed without a choice
            selector = new KindSelectorDialog(capi, OpenPanel, () => screenshot?.Dispose(), offerWorks: relay.BaseUrl != null);
            selector.TryOpen();

            void OpenPanel(string chosen)
            {
                if (chosen == WorksDialog.Choice)
                {
                    // One click, nothing attached
                    screenshot?.Dispose();
                    worksDialog = new WorksDialog(capi, Config, relay, worksSent, preselect);
                    worksDialog.TryOpen();
                    return;
                }
                dialog = new FeedbackDialog(capi, Config, registry, relay, screenshot, chosen, preselect, text, data, recentErrors);
                dialog.TryOpen();
            }
        });
    }

    public override void Dispose()
    {
        recentErrors.Detach();
        selector?.TryClose();
        dialog?.TryClose();
        worksDialog?.TryClose();
        capture?.Dispose();
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
        // Servers refuse longer texts, see UploadLimits
        config.MaxTextLength = Math.Clamp(config.MaxTextLength, 100, UploadLimits.MaxTextLength);
        // A hand-edited file may hold one twice, or null
        config.WorksOptIn = (config.WorksOptIn ?? new()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        config.ConsentedHosts ??= new();
        api.StoreModConfig(config, ConfigFile);
        return config;
    }
}
