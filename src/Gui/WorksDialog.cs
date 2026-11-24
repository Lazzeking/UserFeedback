using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using static UserFeedback.ModLang;

namespace UserFeedback;

/// <summary>
/// "Works for me", the third choice after F8: tells a mod's author, through the relay, that the mod runs fine for this
/// player. One click, no text, no files, once per mod version and game version.
/// Under it, the per-mod switch for the automatic check after joining a world (section 4), saved as WorksOptIn.
/// Both only for mods whose author counts results, which the relay's registry lists in "works".
/// </summary>
public class WorksDialog : ModDialog
{
    /// <summary>What KindSelectorDialog passes for this choice. Not a report kind, nothing is written to a report folder.</summary>
    public const string Choice = "works";

    private const double Width = 420;

    private readonly UserFeedbackConfig config;
    private readonly RelayRegistry relay;
    private readonly WorksSent sent;
    private readonly List<Mod> mods;
    private string selectedModId;
    private bool sending;
    // Whether the switch is in the composed panel: only while the picked mod counts results
    private bool optInComposed;


    public WorksDialog(ICoreClientAPI capi, UserFeedbackConfig config, RelayRegistry relay, WorksSent sent, string? preselect) : base(capi)
    {
        this.config = config;
        this.relay = relay;
        this.sent = sent;

        mods = ModGuess.PickableMods(capi);
        // The mod asked for or looked at, else the first one that counts results, else the first one
        selectedModId = new[] { preselect, ModGuess.Guess(capi, mods) }.FirstOrDefault(id => id != null && mods.Any(mod => mod.Info.ModID == id))
            ?? mods.FirstOrDefault(mod => relay.CountsWorks(mod.Info.ModID))?.Info.ModID
            ?? mods.FirstOrDefault()?.Info.ModID
            ?? "";

        Compose();
        relay.Prefetch(selectedModId);
        Update();
    }

    private void Compose()
    {
        CairoFont label = CairoFont.WhiteSmallText();
        CairoFont hintFont = CairoFont.WhiteDetailText();
        bool optIn = optInComposed = WantsOptIn();
        double y = GuiStyle.TitleBarHeight + 5;

        ElementBounds modLabel = ElementBounds.Fixed(0, y, Width, 25);
        ElementBounds modDropDown = ElementBounds.Fixed(0, y += 28, Width, 30);
        ElementBounds status = ElementBounds.Fixed(0, y += 42, Width, 80);

        // The switch's text and the hint under it, measured since they wrap. The hint is rich text for the privacy link.
        string? privacyUrl = relay.PrivacyUrl;
        string optInText = L("works-optin");
        string hintPlain = L("works-optin-hint");
        string hintLink = ConsentDialog.PrivacyPlain(privacyUrl);
        string hintVtml = privacyUrl == null ? hintPlain : $"{hintPlain} {ConsentDialog.PrivacyVtml(privacyUrl)}";
        var measure = new TextDrawUtil();
        double optInHeight = Math.Max(25, measure.GetMultilineTextHeight(label, optInText, Width - 40));
        double hintHeight = measure.GetMultilineTextHeight(hintFont, (hintPlain + " " + hintLink).Trim(), Width - 40) + 5;

        ElementBounds optInSwitch = ElementBounds.Fixed(0, y += 88, 30, 30);
        ElementBounds optInLabel = ElementBounds.Fixed(40, y + 5, Width - 40, optInHeight);
        ElementBounds optInHint = ElementBounds.Fixed(40, y + 5 + optInHeight + 2, Width - 40, hintHeight);
        if (optIn) y += 5 + optInHeight + 2 + hintHeight + 12;
        else y += 2; // without the switch, the buttons sit right under the status line

        ElementBounds closeButton = ElementBounds.Fixed(0, y, 0, 0).WithFixedPadding(10, 4);
        ElementBounds worksButton = ElementBounds.Fixed(0, y, 0, 0).WithAlignment(EnumDialogArea.RightFixed).WithFixedPadding(10, 4);

        ElementBounds background = NewBackground(modLabel, modDropDown, status, closeButton, worksButton);
        if (optIn) background.WithChildren(optInSwitch, optInLabel, optInHint);

        string[] values = mods.Select(mod => mod.Info.ModID).ToArray();
        string[] names = mods.Select(ModGuess.Label).ToArray();

        SingleComposer = StartCompo("works", L("works-title"), background)
                .AddStaticText(L("which-mod"), label, modLabel)
                .AddDropDown(values, names, Math.Max(0, Array.IndexOf(values, selectedModId)), OnModSelected, modDropDown, "mod")
                .AddDynamicText("", label, status, "status")
                .AddIf(optIn)
                    .AddSwitch(OnOptIn, optInSwitch, "optin")
                    .AddStaticText(optInText, label, optInLabel)
                    .AddRichtext(hintVtml, hintFont, optInHint)
                .EndIf()
                .AddSmallButton(L("close"), () => TryClose(), closeButton)
                .AddSmallButton(L("works-button"), OnWorks, worksButton, EnumButtonStyle.Normal, "works")
            .EndChildElements()
            .Compose();

        SingleComposer.GetSwitch("optin")?.SetValue(config.WorksOptIn.Contains(selectedModId));
    }

    // The switch is offered for an installed mod whose author counts results, with versions the relay takes
    private bool WantsOptIn() => relay.BaseUrl != null && relay.CountsWorks(selectedModId) && Result() != null;

    private void Recompose()
    {
        Recompose(Compose);
        Update();
    }

    private void OnOptIn(bool on)
    {
        if (!on)
        {
            // Nothing is sent and works-sent.json keeps its entries, they're only on this computer
            SetOptIn(selectedModId, false);
            return;
        }

        string? host = relay.Host;
        if (host == null) return;

        // Automatic results go to the relay, so the relay question comes first; no means the switch goes back off
        string modId = selectedModId;
        ConsentDialog.AskOnce(capi, config, host, L("consent-works-auto", host), relay.PrivacyUrl,
            onYes: () => SetOptIn(modId, true),
            onNo: () =>
            {
                if (IsOpened() && selectedModId == modId) SingleComposer.GetSwitch("optin")?.SetValue(false);
            });
    }

    private void SetOptIn(string modId, bool on)
    {
        config.WorksOptIn.Remove(modId);
        if (on) config.WorksOptIn.Add(modId);
        capi.StoreModConfig(config, Core.ConfigFile);
    }

    private void OnModSelected(string modId, bool selected)
    {
        selectedModId = modId;
        // With buckets the answer for this mod may still have to come, Update says "checking" until then
        relay.Prefetch(modId);
        Update();
    }

    private Mod? SelectedMod => mods.FirstOrDefault(mod => mod.Info.ModID == selectedModId);

    private WorksResult? Result()
    {
        Mod? mod = SelectedMod;
        return mod == null ? null : WorksResult.Manual(mod.Info.ModID, mod.Info.Version, GameVersion.ShortGameVersion, capi.IsSinglePlayer);
    }

    // The line under the picker says what the button would do, or why it can't; the button only works when it can
    private void Update()
    {
        if (WantsOptIn() != optInComposed)
        {
            // Often called from the dropdown's own event, which still uses the dropdown afterwards, so it waits a frame
            if (IsOpened()) capi.Event.EnqueueMainThreadTask(() => { if (IsOpened() && WantsOptIn() != optInComposed) Recompose(); }, Core.ModId + "-works-recompose");
            else Recompose();
            return;
        }
        string name = SelectedMod?.Info.Name ?? selectedModId;
        WorksResult? result = Result();

        string text;
        bool ready = false;
        if (relay.BaseUrl == null) text = L("works-relay-off");
        else if (!relay.CountsWorks(selectedModId)) text = L(relay.Knows(selectedModId) ? "works-not-counted" : "works-checking", name);
        else if (result == null) text = L("works-cant-count", name);
        else if (sending) text = L("works-sending");
        else if (sent.HasManual(result.Key)) text = L("works-counted", name);
        else
        {
            text = L("works-ready", name);
            ready = true;
        }

        SingleComposer.GetDynamicText("status").SetNewText(text);
        SingleComposer.GetButton("works").Enabled = ready;
        SingleComposer.GetSwitch("optin")?.SetValue(config.WorksOptIn.Contains(selectedModId));
    }

    private bool OnWorks()
    {
        WorksResult? result = Result();
        string? url = relay.WorksUrl;
        string? host = relay.Host;
        if (result == null || url == null || host == null || sending || sent.HasManual(result.Key) || !relay.CountsWorks(result.ModId)) return true;

        // The same question as before the first report through the relay, asked once for the relay's host
        ConsentDialog.AskOnce(capi, config, host, L("consent-works", host), relay.PrivacyUrl, () => Send(result, url));
        return true;
    }

    private void Send(WorksResult result, string url)
    {
        sending = true;
        Update();
        WorksSender.Send(capi, url, result, answer => OnAnswer(result, answer));
    }

    // Every outcome ends with a chat line, the panel may be closed by then
    private void OnAnswer(WorksResult result, WorksAnswer answer)
    {
        sending = false;
        string name = capi.ModLoader.GetMod(result.ModId)?.Info.Name ?? result.ModId;

        string line;
        if (answer.Counted)
        {
            sent.MarkManual(result.Key, DateTime.UtcNow);
            if (sent.Problem != null) capi.Logger.Warning("[{0}] {1}", Core.ModId, sent.Problem);
            line = L("works-thanks", name);
        }
        else
        {
            // The author switched the counter off, or the mod was paused or blocked, since the list was fetched
            if (answer.Status == 403) relay.DropWorks(result.ModId);
            line = answer.Status switch
            {
                null or >= 500 => L("works-no-answer"),
                429 when answer.RetryMinutes is int minutes => L("works-rate-limited-wait", minutes),
                429 => L("works-rate-limited"),
                _ => L("works-refused", answer.Status),
            };
            if (answer.Message != null) line += "\n" + L("http-says", relay.Host ?? "", answer.Message);
        }

        capi.ShowChatMessage(line);
        if (IsOpened()) Update();
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        relay.Changed += OnRelayChanged;
    }

    public override void OnGuiClosed()
    {
        relay.Changed -= OnRelayChanged;
        base.OnGuiClosed();
    }

    // The list or the picked mod's bucket arrived: the mod may count results after all, or not
    private void OnRelayChanged()
    {
        if (IsOpened()) Update();
    }

}
