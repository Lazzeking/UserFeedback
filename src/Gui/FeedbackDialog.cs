using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using static UserFeedback.ModLang;

namespace UserFeedback;

/// <summary>
/// The F8 panel, in one of two shapes picked in KindSelectorDialog. Feedback: which mod, a rating, some text, the
/// screenshot. Bug: which mod and which others play a part, a description, the screenshot, game details, logs and the
/// files mods ask for. Both end with where the report goes. Pauses singleplayer games like the handbook.
/// A mod can replace the wording, and the four faces, from its userfeedback.json. The report stays the same.
/// </summary>
public class FeedbackDialog : ModDialog
{

    private const double Width = 480;
    private const double Gap = 10;
    private const string LocalOnly = "local";
    private const double TextHeight = 130;
    private const double FeedbackTextHeight = 90;
    private const double ScrollbarWidth = 20;
    private const double SummaryHeight = 66;
    private const double FeedbackSummaryHeight = 40;

    private readonly UserFeedbackConfig config;
    private readonly DestinationRegistry registry;
    private readonly RelayRegistry relay;
    private readonly List<Mod> mods;
    private readonly string? lookingAt;
    private readonly string? holding;
    private readonly GameContext context;
    private readonly ModContributions contributions;
    // Mods that change UserFeedback's code, see PatchAudit
    private readonly List<string> patchedBy;
    private readonly Task<LogSnapshot> logs;
    private LogSnapshot? readLogs;

    private BitmapRef? screenshot;
    private string kind;
    private string selectedModId;
    // Kept here rather than read from the dropdown, which only exists in a bug report
    private string[] involvedIds = Array.Empty<string>();
    private List<DestinationConfig> routes = new();
    private string selectedRoute = LocalOnly;
    // Whether the player picked a destination themselves, so a relay bucket arriving later doesn't change their choice
    private bool routeChosen;
    private string? rating;
    private string text = "";
    private bool attachScreenshot;
    // What the switches say. They only count in a bug report, use Details and FullLogs.
    private bool includeDetails;
    private bool includeFullLogs;
    private bool publicAllowed;
    // Whether the "may be shown publicly" row is wanted, and whether the panel is built with it.
    // The game can't hide an element, so the panel is built again when they differ.
    private bool publicWanted;
    private bool publicComposed;
    // Requested files the player switched off in the review
    private readonly HashSet<string> excludedFiles = new();
    // RequestedFiles' last answer, for the reported mods in Key
    private (string Key, List<RequestedFile> Files)? requested;
    private bool pausedByUs;
    // Which mod's own panel the window was built with, "" for the standard one. See UpdatePanel.
    private string composedPanel = "";

    // Like the handbook: only a singleplayer world that isn't opened to LAN can be paused
    private bool CanPause => capi.IsSinglePlayer && !capi.OpenedToLan;

    private bool IsBug => kind == FeedbackReport.Bug;
    // Feedback is kept light: a rating, text and the screenshot, never details, logs or files
    private bool Details => IsBug && includeDetails;
    private bool FullLogs => IsBug && includeFullLogs;
    // Feedback needs a rating and a bug a description, so an empty report can't be sent by a stray click
    private bool CanSend => IsBug ? text.Trim().Length > 0 : rating != null;


    /// <param name="kind">FeedbackReport.Bug or FeedbackReport.Feedback</param>
    /// <param name="preselect">The mod to pick, instead of guessing from what the player looks at</param>
    /// <param name="prefill">Text a mod put in the text box, which the player can change</param>
    /// <param name="attached">Data the mod sent when opening it, for preselect's data in this report only</param>
    /// <param name="recentErrors">To pick the mod of a recent error when the player doesn't look at or hold a mod's thing</param>
    public FeedbackDialog(ICoreClientAPI capi, UserFeedbackConfig config, DestinationRegistry registry, RelayRegistry relay,
        BitmapRef? screenshot, string kind, string? preselect = null, string? prefill = null,
        Newtonsoft.Json.Linq.JObject? attached = null, RecentErrors? recentErrors = null) : base(capi)
    {
        this.kind = kind == FeedbackReport.Bug ? FeedbackReport.Bug : FeedbackReport.Feedback;
        this.config = config;
        this.registry = registry;
        this.relay = relay;
        this.screenshot = screenshot;
        attachScreenshot = screenshot != null && config.AttachScreenshotByDefault;
        includeDetails = config.IncludeDetailsByDefault;

        mods = ModGuess.PickableMods(capi);
        lookingAt = ModGuess.LookingAtCode(capi);
        holding = ModGuess.HoldingCode(capi);
        selectedModId = preselect != null && mods.Any(mod => mod.Info.ModID == preselect)
            ? preselect
            : ModGuess.Guess(capi, mods) ?? recentErrors?.Guess(mods, registry.LogMatchFor) ?? "";
        context = GameContext.Collect(capi);
        contributions = ModContributions.Collect(capi);
        // Kept with preselect: if the player picks another mod, it's no longer about this report and is left out
        if (attached != null && preselect != null) contributions.Attach(preselect, attached);
        patchedBy = PatchAudit.LogForeignPatches(capi);

        // The logs are read in the background as they are now, the summary updates once they are in
        bool singleplayer = capi.IsSinglePlayer;
        logs = Task.Run(() => LogSnapshot.Read(singleplayer));
        logs.ContinueWith(task => capi.Event.EnqueueMainThreadTask(() =>
        {
            if (task.IsCompletedSuccessfully) readLogs = task.Result;
            if (IsOpened()) UpdateSummary();
        }, Core.ModId + "-logs"));

        // Too long would be cut by the text box anyway, this keeps what's kept and what's shown the same
        if (prefill != null) text = prefill.Length > config.MaxTextLength ? prefill[..config.MaxTextLength] : prefill;

        Compose();
        UpdateInvolved();
        UpdateRoutes();
        if (text.Length > 0) SingleComposer.GetTextArea("text").SetValue(text);
        UpdateSummary();
    }

    private void Compose()
    {
        CairoFont label = CairoFont.WhiteSmallText();
        bool bug = IsBug;
        PanelConfig? panel = registry.PanelFor(selectedModId);
        composedPanel = panel == null ? "" : selectedModId;
        PanelKindText? side = bug ? panel?.Bug : panel?.Feedback;
        double half = (Width - Gap) / 2;
        double y = GuiStyle.TitleBarHeight + 5;

        // Which kind this is, and a way out if the wrong one was picked. The heading keeps clear of the switch
        // button, so a long one ("Something broke in Example Mod") wraps, and the next row starts under that.
        string heading = Shown(bug ? "kind-bug-heading" : "kind-feedback-heading", side?.Heading, side?.HeadingKey);
        CairoFont headingFont = CairoFont.WhiteSmallishText();
        double headingWidth = Width - 200;
        double headingHeight = Math.Max(22, new TextDrawUtil().GetMultilineTextHeight(headingFont, heading, headingWidth));
        ElementBounds kindLabel = ElementBounds.Fixed(0, y + 3, headingWidth, headingHeight);
        ElementBounds switchButton = ElementBounds.Fixed(0, y, 0, 0).WithAlignment(EnumDialogArea.RightFixed).WithFixedPadding(8, 3);
        y += Math.Max(32, headingHeight + 8);

        // Only when other mods patch UserFeedback, so the player knows to check the report before sending
        string warning = patchedBy.Count > 0 ? L("patched-by", string.Join(", ", patchedBy)) : "";
        CairoFont warningFont = CairoFont.WhiteDetailText().WithColor(GuiStyle.WarningTextColor);
        double warningHeight = warning.Length > 0 ? new TextDrawUtil().GetMultilineTextHeight(warningFont, warning, Width) : 0;
        ElementBounds warningText = ElementBounds.Fixed(0, y, Width, warningHeight);
        if (warningHeight > 0) y += warningHeight + 5;

        // "Other mods involved" is for bugs that only happen with both, feedback is about one mod
        ElementBounds modLabel = ElementBounds.Fixed(0, y, bug ? half : Width, 25);
        ElementBounds involvedLabel = ElementBounds.Fixed(half + Gap, y, half, 25);
        y += 25;
        ElementBounds modDropDown = ElementBounds.Fixed(0, y, bug ? half : Width, 30);
        ElementBounds involvedDropDown = ElementBounds.Fixed(half + Gap, y, half, 30);
        y += 30;

        // A bug has no rating, so the rows below only move down for feedback
        ElementBounds ratingLabel = ElementBounds.Fixed(0, y + 15, Width, 25);
        double ratingY = y + 40;
        // With icons: square icon buttons with the name under them. Without: plain text buttons.
        // A mod's own face replaces the drawing for that rating. The value sent stays great, good, meh or bad.
        string?[] icons = FeedbackReport.Ratings.Select(code => RatingIcons.Get(capi, code, panel?.Face(code)?.Icon)).ToArray();
        string[] ratingLabels = FeedbackReport.Ratings.Select(code => Shown("rating-" + code, panel?.Face(code)?.Label, panel?.Face(code)?.LabelKey)).ToArray();
        bool withIcons = icons.Any(icon => icon != null);
        double column = (Width - Gap * (FeedbackReport.Ratings.Length - 1)) / FeedbackReport.Ratings.Length;
        ElementBounds[] ratingButtons = FeedbackReport.Ratings
            .Select((_, i) => withIcons
                ? ElementBounds.Fixed(i * (column + Gap) + (column - RatingIcons.ButtonSize) / 2, ratingY, RatingIcons.ButtonSize, RatingIcons.ButtonSize)
                : ElementBounds.Fixed(i * (column + Gap), ratingY, column, 30))
            .ToArray();
        ElementBounds[] ratingNames = FeedbackReport.Ratings
            .Select((_, i) => ElementBounds.Fixed(i * (column + Gap), ratingY + RatingIcons.ButtonSize + 4, column, 20))
            .ToArray();
        if (!bug) y = ratingY + (withIcons ? RatingIcons.ButtonSize + 24 : 30);

        ElementBounds textLabel = ElementBounds.Fixed(0, y += 15, Width, 25);
        y += 25;
        // The text area grows with its text, so it sits in a clipped window with a scrollbar, like the vanilla sign editor
        double textHeight = bug ? TextHeight : FeedbackTextHeight;
        ElementBounds textArea = ElementBounds.Fixed(0, 0, Width - ScrollbarWidth - 6, textHeight);
        ElementBounds textClip = textArea.ForkBoundingParent().WithFixedPosition(0, y);
        ElementBounds textScrollbar = ElementBounds.Fixed(Width - ScrollbarWidth, y, ScrollbarWidth, textHeight);

        ElementBounds screenshotSwitch = ElementBounds.Fixed(0, y += textHeight + 15, 30, 30);
        ElementBounds screenshotLabel = ElementBounds.Fixed(40, y + 5, Width - 40, 25);
        // Details, the files review and the full logs are only in a bug report, so only then do the rows below move down
        ElementBounds detailsSwitch = ElementBounds.Fixed(0, y + 35, 30, 30);
        ElementBounds detailsLabel = ElementBounds.Fixed(40, y + 40, Width - 40 - 130, 25);
        ElementBounds reviewButton = ElementBounds.Fixed(0, y + 37, 0, 0).WithAlignment(EnumDialogArea.RightFixed).WithFixedPadding(8, 3);
        ElementBounds fullLogsSwitch = ElementBounds.Fixed(0, y + 70, 30, 30);
        ElementBounds fullLogsLabel = ElementBounds.Fixed(40, y + 75, Width - 40, 25);
        if (bug) y += 70;
        // Up to three lines of the small font at this width, a feedback summary is shorter
        double summaryHeight = bug ? SummaryHeight : FeedbackSummaryHeight;
        ElementBounds summary = ElementBounds.Fixed(0, y += 40, Width, summaryHeight);

        ElementBounds routeLabel = ElementBounds.Fixed(0, y += summaryHeight + 10, Width, 25);
        ElementBounds routeDropDown = ElementBounds.Fixed(0, y += 25, Width, 30);
        // Only when the relay is chosen and the mod has a public page there
        publicComposed = publicWanted;
        ElementBounds publicSwitch = ElementBounds.Fixed(0, y + 40, 30, 30);
        ElementBounds publicLabel = ElementBounds.Fixed(40, y + 45, Width - 40, 25);
        if (publicComposed) y += 40;

        ElementBounds cancelButton = ElementBounds.Fixed(0, y += 50, 0, 0).WithFixedPadding(10, 4);
        ElementBounds previewButton = ElementBounds.Fixed(0, y, 0, 0).WithAlignment(EnumDialogArea.CenterFixed).WithFixedPadding(10, 4);
        ElementBounds sendButton = ElementBounds.Fixed(0, y, 0, 0).WithAlignment(EnumDialogArea.RightFixed).WithFixedPadding(10, 4);
        // A rewritten panel credits UserFeedback, smaller, at the bottom right under the buttons
        ElementBounds credit = ElementBounds.Fixed(0, y + 30, Width, 14);

        ElementBounds background = NewBackground(kindLabel, switchButton, modLabel, modDropDown, textLabel, textClip, textScrollbar,
            screenshotSwitch, screenshotLabel, summary, routeLabel, routeDropDown, cancelButton, previewButton, sendButton);
        if (warningHeight > 0) background.WithChildren(warningText);
        if (bug) background.WithChildren(involvedLabel, involvedDropDown, detailsSwitch, detailsLabel, reviewButton, fullLogsSwitch, fullLogsLabel);
        if (publicComposed) background.WithChildren(publicSwitch, publicLabel);
        if (!bug) background.WithChildren(ratingLabel);
        if (!bug) background.WithChildren(ratingButtons);
        if (!bug && withIcons) background.WithChildren(ratingNames);
        if (panel != null) background.WithChildren(credit);

        // In the title bar, where the handbook has its own pause button
        ElementBounds pauseButton = ElementBounds.Fixed(Width - 150, -15, 110, 22);

        string[] modValues = new[] { "" }.Concat(mods.Select(mod => mod.Info.ModID)).ToArray();
        string[] modLabels = new[] { L("mod-unknown") }.Concat(mods.Select(ModGuess.Label)).ToArray();

        GuiComposer composer = StartCompo("dialog", Shown("dialog-title", panel?.Title, panel?.TitleKey), background)
                .AddIf(CanPause)
                    .AddToggleButton(Lang.Get("Pause game"), CairoFont.WhiteDetailText(), OnTogglePause, pauseButton, "pause")
                .EndIf()
                .AddStaticText(heading, headingFont, kindLabel)
                .AddSmallButton(Shown(bug ? "switch-to-feedback" : "switch-to-bug", side?.Switch, side?.SwitchKey), SwitchKind, switchButton, EnumButtonStyle.Small)
                .AddIf(warningHeight > 0)
                    .AddStaticText(warning, warningFont, warningText)
                .EndIf()
                .AddStaticText(Shown("which-mod", panel?.WhichMod, panel?.WhichModKey), label, modLabel)
                .AddDropDown(modValues, modLabels, Math.Max(0, Array.IndexOf(modValues, selectedModId)), OnModSelected, modDropDown, "mod")
                .AddIf(bug)
                    .AddStaticText(Shown("also-involved", side?.Involved, side?.InvolvedKey), label, involvedLabel)
                    .AddMultiSelectDropDown(new[] { "" }, new[] { "" }, -1, (_, _) => OnInvolvedChanged(), involvedDropDown, "involved")
                .EndIf()
                .AddIf(!bug)
                    .AddStaticText(Shown("how-is-it", side?.Prompt, side?.PromptKey), label, ratingLabel)
                .EndIf();

        if (!bug) AddRatings(composer, icons, ratingLabels, ratingButtons, ratingNames);

        composer
                .AddStaticText(Shown(bug ? "describe-bug" : "tell-more", side?.Text, side?.TextKey), label, textLabel)
                .BeginClip(textClip)
                    .AddTextArea(textArea, OnTextChanged, CairoFont.TextInput().WithFontSize(16), "text")
                .EndClip()
                .AddVerticalScrollbar(OnTextScrolled, textScrollbar, "textscroll")
                .AddSwitch(on => { attachScreenshot = on; UpdateSummary(); }, screenshotSwitch, "screenshot")
                .AddStaticText(screenshot != null ? Shown("attach-screenshot", panel?.Screenshot, panel?.ScreenshotKey) : L("no-screenshot"), label, screenshotLabel)
                .AddIf(bug)
                    .AddSwitch(on => { includeDetails = on; UpdateSummary(); }, detailsSwitch, "details")
                    .AddStaticText(Shown("include-details", side?.Details, side?.DetailsKey), label, detailsLabel)
                    .AddSmallButton(Shown("review-files", side?.Review, side?.ReviewKey), () => { ReviewFiles(null); return true; }, reviewButton, EnumButtonStyle.Small, "reviewfiles")
                    .AddSwitch(on => { includeFullLogs = on; UpdateSummary(); }, fullLogsSwitch, "fulllogs")
                    .AddStaticText(Shown("include-full-logs", side?.FullLogs, side?.FullLogsKey), label, fullLogsLabel)
                .EndIf()
                .AddDynamicText("", CairoFont.WhiteDetailText(), summary, "summary")
                .AddStaticText(Shown("send-to", panel?.SendTo, panel?.SendToKey), label, routeLabel)
                .AddDropDown(new[] { LocalOnly }, new[] { L("route-local") }, 0, (value, _) => { selectedRoute = value; routeChosen = true; UpdatePublicRow(); }, routeDropDown, "route")
                .AddIf(publicComposed)
                    .AddSwitch(on => publicAllowed = on, publicSwitch, "public")
                    .AddStaticText(Shown("public-allowed", panel?.PublicAllowed, panel?.PublicAllowedKey), label, publicLabel)
                .EndIf()
                .AddSmallButton(Shown("cancel", panel?.Cancel, panel?.CancelKey), () => TryClose(), cancelButton)
                .AddSmallButton(Shown("preview", panel?.Preview, panel?.PreviewKey), Preview, previewButton)
                .AddSmallButton(Shown("send", panel?.Send, panel?.SendKey), Send, sendButton, EnumButtonStyle.Normal, "send")
                .AddIf(panel != null)
                    .AddStaticText(L("panel-credit"), CairoFont.WhiteDetailText().WithFontSize(12).WithColor(new double[] { 0.7, 0.7, 0.7, 1 }), EnumTextOrientation.Right, credit)
                .EndIf()
            .EndChildElements();

        SingleComposer = composer.Compose();
        RestoreControls(textHeight);
    }

    // The four faces: icon buttons with their name under them, or plain text buttons when no face has a drawing
    private void AddRatings(GuiComposer composer, string?[] icons, string[] labels, ElementBounds[] buttons, ElementBounds[] names)
    {
        bool withIcons = icons.Any(icon => icon != null);
        for (int i = 0; i < FeedbackReport.Ratings.Length; i++)
        {
            string code = FeedbackReport.Ratings[i];
            string key = "rating-" + code;
            if (withIcons)
            {
                // toggleable: true is what makes it stay pressed, the same as AddToggleButton does
                composer.AddInteractiveElement(new GuiElementToggleButton(capi, icons[i] ?? "", icons[i] == null ? labels[i] : "",
                    CairoFont.WhiteSmallText(), on => SelectRating(code, on), buttons[i], toggleable: true), key);
                composer.AddStaticText(labels[i], RatingIcons.LabelFont(code), EnumTextOrientation.Center, names[i]);
            }
            else
            {
                composer.AddToggleButton(labels[i], CairoFont.WhiteSmallText(), on => SelectRating(code, on), buttons[i], key);
            }
        }
    }

    // A freshly built panel shows what the player already chose
    private void RestoreControls(double textHeight)
    {
        GuiElementTextArea area = SingleComposer.GetTextArea("text");
        area.SetMaxLength(config.MaxTextLength);
        area.OnCursorMoved = OnTextCursorMoved;
        SingleComposer.GetScrollbar("textscroll").SetHeights((float)textHeight, (float)textHeight);
        SingleComposer.GetSwitch("screenshot").SetValue(attachScreenshot);
        SingleComposer.GetSwitch("screenshot").Enabled = screenshot != null;
        SingleComposer.GetSwitch("details")?.SetValue(includeDetails);
        SingleComposer.GetSwitch("fulllogs")?.SetValue(includeFullLogs);
        SingleComposer.GetToggleButton("pause")?.SetValue(config.PauseInSingleplayer);
        SingleComposer.GetSwitch("public")?.SetValue(publicAllowed);
        UpdateSendEnabled();
    }

    // The mod's words when it set them, the standard line otherwise. A lang key wins when the player has that translation.
    private static string Shown(string langKey, string? text, string? key)
    {
        if (key != null && Lang.HasTranslation(key)) return Lang.Get(key);
        return text ?? L(langKey);
    }

    /// <summary>The picked mod changed: builds the panel again when its wording differs from the one on screen</summary>
    private void UpdatePanel()
    {
        if (PanelState() == composedPanel) return;
        if (IsOpened()) capi.Event.EnqueueMainThreadTask(() => { if (PanelState() != composedPanel) Recompose(); }, Core.ModId + "-panel");
        else Recompose();
    }

    private string PanelState() => registry.PanelFor(selectedModId) != null ? selectedModId : "";

    private void UpdateSendEnabled()
    {
        SingleComposer.GetButton("send").Enabled = CanSend;
    }

    // From the button's own click, so the panel is built again on the next frame. Text, mod and choices stay.
    // The destinations can differ by kind, the chosen one stays if it takes the new kind too.
    private bool SwitchKind()
    {
        kind = IsBug ? FeedbackReport.Feedback : FeedbackReport.Bug;
        capi.Event.EnqueueMainThreadTask(() =>
        {
            Recompose();
            UpdateRoutes(keepSelection: true);
        }, Core.ModId + "-switchkind");
        return true;
    }


    private void OnTextChanged(string value)
    {
        text = value;
        UpdateSendEnabled();
        SingleComposer.GetScrollbar("textscroll").SetNewTotalHeight((float)SingleComposer.GetTextArea("text").Bounds.fixedHeight);
    }

    private void OnTextScrolled(float value)
    {
        GuiElementTextArea area = SingleComposer.GetTextArea("text");
        area.Bounds.fixedY = -value;
        area.Bounds.CalcWorldBounds();
    }

    // Keeps the line being typed in view, the same way the vanilla command block editor does
    private void OnTextCursorMoved(double x, double y)
    {
        double lineHeight = SingleComposer.GetTextArea("text").Font.GetFontExtents().Height;
        GuiElementScrollbar scrollbar = SingleComposer.GetScrollbar("textscroll");
        scrollbar.EnsureVisible(x, y);
        scrollbar.EnsureVisible(x, y + lineHeight + 5);
    }

    private void OnInvolvedChanged()
    {
        GuiElementDropDown dropDown = SingleComposer.GetDropDown("involved");
        involvedIds = dropDown.SelectedValues.ToArray();
        // The relay refuses reports naming more, and past 20 the list says little anyway
        if (involvedIds.Length > UploadLimits.MaxInvolved)
        {
            involvedIds = involvedIds.Take(UploadLimits.MaxInvolved).ToArray();
            capi.ShowChatMessage(L("too-many-involved", UploadLimits.MaxInvolved));
            // From the dropdown's own event, so its selection is put back on the next frame
            capi.Event.EnqueueMainThreadTask(() => SingleComposer.GetDropDown("involved")?.SetSelectedValue(involvedIds), Core.ModId + "-involved");
        }
        UpdateSummary();
    }

    private void OnModSelected(string modId, bool selected)
    {
        selectedModId = modId;
        routeChosen = false;
        UpdateInvolved();
        UpdateRoutes(keepSelection: false);
        UpdateSummary();
        UpdatePanel();
    }

    // What the reported mods added through the collect event, the chosen mod's first, within the size limits
    private Dictionary<string, Newtonsoft.Json.Linq.JToken> ReportModData(out List<string> leftOut)
    {
        var ids = ReportMods().Select(mod => mod.Info.ModID).ToHashSet();
        IEnumerable<string> ordered = new[] { selectedModId }.Concat(involvedIds).Where(ids.Contains);
        return contributions.For(ordered, out leftOut);
    }

    /// <summary>
    /// Every file the reported mods ask for in their own userfeedback.json. Looking them up reads the disk, so the
    /// result is kept until the reported mods change; fresh looks again, as reviewing before sending does.
    /// </summary>
    private List<RequestedFile> RequestedFiles(bool fresh = false)
    {
        if (!Details) return new();

        List<Mod> reported = ReportMods();
        string key = string.Join(",", reported.Select(mod => mod.Info.ModID));
        if (!fresh && requested?.Key == key) return requested.Value.Files;

        var requests = new List<(string, string)>();
        foreach (Mod mod in reported)
        {
            string id = mod.Info.ModID;
            requests.AddRange(registry.FilesFor(id).Select(pattern => (id, pattern)));
        }
        List<RequestedFile> files = requests.Count == 0 ? new() : ExtraFiles.Resolve(requests, capi.Logger);
        requested = (key, files);
        return files;
    }

    // The requested files the player didn't switch off
    private List<string> IncludedFiles()
    {
        return RequestedFiles().Select(file => file.Path).Where(path => !excludedFiles.Contains(path)).ToList();
    }

    /// <summary>
    /// Shows every requested file. With onConfirm it is the last step before sending, which only goes ahead on its "Send".
    /// Without files it calls onConfirm right away.
    /// </summary>
    private void ReviewFiles(Action? onConfirm)
    {
        List<RequestedFile> files = RequestedFiles(fresh: true);
        if (files.Count == 0)
        {
            onConfirm?.Invoke();
            return;
        }

        new FileReviewDialog(capi, files, excludedFiles, id => mods.FirstOrDefault(mod => mod.Info.ModID == id)?.Info.Name ?? id,
            UpdateSummary, onConfirm).TryOpen();
    }

    /// <summary>The mod the report is about and the others the player marked as involved</summary>
    private List<Mod> ReportMods()
    {
        var ids = new HashSet<string>(IsBug ? involvedIds : Array.Empty<string>()) { selectedModId };
        return mods.Where(mod => ids.Contains(mod.Info.ModID)).ToList();
    }

    // One line telling the player what the report will hold, so nothing leaves their computer unseen
    private void UpdateSummary()
    {
        var parts = new List<string>();
        if (attachScreenshot && screenshot != null) parts.Add(L("summary-screenshot"));
        // Only in a bug report
        GuiElementTextButton? review = SingleComposer.GetButton("reviewfiles");
        if (Details)
        {
            parts.Add(L("summary-details"));
            parts.Add(readLogs == null
                ? L("summary-logs-reading")
                : L("summary-logs", new ModLogFilter(ReportMods(), registry.LogMatchFor).Matching(readLogs).Count));

            List<string> modData = ReportModData(out _).Keys.ToList();
            if (modData.Count > 0) parts.Add(L("summary-mod-data", string.Join(", ", modData)));
            int requested = RequestedFiles().Count;
            int files = IncludedFiles().Count;
            if (requested > 0) parts.Add(L("summary-files", files, requested));
            if (review != null) review.Enabled = requested > 0;
        }
        else if (review != null)
        {
            review.Enabled = false;
        }
        if (FullLogs) parts.Add(L("summary-full-logs"));

        string prefix = IsBug ? "summary-bug" : "summary-feedback";
        string line = parts.Count == 0 ? L(prefix + "-only") : L(prefix, string.Join(", ", parts));
        SingleComposer.GetDynamicText("summary").SetNewText(line);
    }

    // "Other mods involved" lists every mod except the one the report is about, keeping what was already picked
    private void UpdateInvolved()
    {
        involvedIds = involvedIds.Where(id => id != selectedModId).ToArray();
        GuiElementDropDown? dropDown = SingleComposer.GetDropDown("involved");
        if (dropDown == null) return; // feedback has no such list

        List<Mod> others = mods.Where(mod => mod.Info.ModID != selectedModId).ToList();
        dropDown.SetList(others.Select(mod => mod.Info.ModID).ToArray(), others.Select(ModGuess.Label).ToArray());
        dropDown.SetSelectedValue(involvedIds);
    }

    // The destinations follow the chosen mod and kind: the relay first when it takes the mod's reports of this kind
    // (its registry says which), then the mod's own that take this kind, saving on this computer always last.
    // "Not sure" never gets the relay, it can't know whose report that is.
    private void UpdateRoutes(bool keepSelection = false)
    {
        DestinationConfig? previous = SelectedRoute();
        bool wasLocal = selectedRoute == LocalOnly;
        // When the relay splits its list, only the picked mod's bucket is asked for. OnRelayChanged adds the relay once it's in.
        relay.Prefetch(selectedModId);

        routes = new List<DestinationConfig>();
        if (relay.Has(selectedModId, kind)) routes.Add(relay.Destination);
        routes.AddRange(registry.DestinationsFor(selectedModId, kind).Where(route => !relay.IsRelayUrl(route.Url)));

        string? select = null;
        if (keepSelection && wasLocal) select = LocalOnly;
        else if (keepSelection && previous != null && routes.IndexOf(previous) is int index and >= 0) select = index.ToString();
        FillRoutes(select);
        UpdatePublicRow();
    }

    /// <summary>Puts routes in the "Send to" list and picks select, or the first one</summary>
    private void FillRoutes(string? select)
    {
        string[] values = routes.Select((_, i) => i.ToString()).Append(LocalOnly).ToArray();
        string[] names = routes.Select(route => route.Describe()).Append(L("route-local")).ToArray();
        int index = Math.Max(0, select == null ? 0 : Array.IndexOf(values, select));

        GuiElementDropDown dropDown = SingleComposer.GetDropDown("route");
        dropDown.SetList(values, names);
        dropDown.SetSelectedIndex(index);
        selectedRoute = values[index];
    }

    // Whether the author could use this report on a public page: the relay is chosen and the mod has one there
    private bool CanBePublic(DestinationConfig? route) => route?.Relay == true && relay.IsPublic(selectedModId);

    private void UpdatePublicRow()
    {
        publicWanted = CanBePublic(SelectedRoute());
        if (publicWanted == publicComposed) return;

        // Usually called from a dropdown's own event, which still uses the dropdown afterwards, so it waits a frame
        if (IsOpened()) capi.Event.EnqueueMainThreadTask(() => { if (publicWanted != publicComposed) Recompose(); }, Core.ModId + "-recompose");
        else Recompose();
    }

    // Builds the panel again with the same choices, text and rating
    private void Recompose()
    {
        Recompose(Compose);

        UpdateInvolved();
        FillRoutes(selectedRoute);
        if (text.Length > 0) SingleComposer.GetTextArea("text").SetValue(text);
        if (rating != null) SingleComposer.GetToggleButton("rating-" + rating)?.SetValue(true);
        UpdateSendEnabled();
        UpdateSummary();
    }

    private void SelectRating(string code, bool on)
    {
        rating = on ? code : null;
        // Toggle buttons that act like radio buttons
        foreach (string other in FeedbackReport.Ratings)
        {
            if (other != code) SingleComposer.GetToggleButton("rating-" + other).SetValue(false);
        }
        UpdateSendEnabled();
    }

    private DestinationConfig? SelectedRoute()
    {
        return int.TryParse(selectedRoute, out int index) && index < routes.Count ? routes[index] : null;
    }

    private FeedbackReport BuildReport(DestinationConfig? route)
    {
        Mod? mod = mods.FirstOrDefault(m => m.Info.ModID == selectedModId);
        List<string> dataLeftOut = new();
        Dictionary<string, Newtonsoft.Json.Linq.JToken> modData = Details ? ReportModData(out dataLeftOut) : new();
        return new FeedbackReport
        {
            CreatedUtc = DateTime.UtcNow.ToString("o"),
            ModId = mod?.Info.ModID,
            ModName = mod?.Info.Name,
            ModVersion = mod?.Info.Version,
            AlsoInvolved = ReportMods()
                .Where(m => m.Info.ModID != selectedModId)
                .Select(m => new ModRef { ModId = m.Info.ModID, Name = m.Info.Name, Version = m.Info.Version })
                .ToList(),
            Kind = kind,
            Rating = IsBug ? null : rating,
            Text = text.Trim(),
            GameVersion = GameVersion.ShortGameVersion,
            Singleplayer = capi.IsSinglePlayer,
            LookingAt = lookingAt,
            Holding = holding,
            Destination = route == null ? null : new ReportDestination
            {
                Type = route.Type!,
                Target = route.Target,
            },
            PublicAllowed = publicAllowed && CanBePublic(route),
            Context = Details ? context : null,
            ModData = modData,
            LeftOut = dataLeftOut.Select(ReportFiles.LeftOutModData).ToList(),
            ExtraFiles = IncludedFiles(),
        };
    }

    // Uploads must fit what servers accept. A GitHub issue, a web page or a local save keep everything in the folder.
    private ReportLogs BuildLogs(DestinationConfig? route)
    {
        long? budget = route?.Type == DestinationRegistry.Http ? UploadLimits.MaxUploadBytes : null;
        return new ReportLogs(logs, Details ? new ModLogFilter(ReportMods(), registry.LogMatchFor) : null, FullLogs,
            IncludedFiles(), new Sanitizer(capi), budget);
    }

    private void TellLeftOut(FeedbackReport report)
    {
        if (report.LeftOut.Count > 0) capi.ShowChatMessage(L("left-out", string.Join(", ", report.LeftOut)));
    }

    // Writes exactly what Send would, into the preview folder, and opens it. The panel stays open.
    private bool Preview()
    {
        try
        {
            DestinationConfig? route = SelectedRoute();
            FeedbackReport report = BuildReport(route);
            ReportWriter.Save(capi, ReportWriter.PreviewFolder, report, attachScreenshot ? screenshot : null,
                handOverScreenshot: false, BuildLogs(route), _ =>
                {
                    TellLeftOut(report);
                    ReportWriter.OpenFolder(ReportWriter.PreviewFolder);
                });
        }
        catch (Exception e)
        {
            capi.Logger.Error("[{0}] Could not write the preview: {1}", Core.ModId, e);
            capi.ShowChatMessage(L("save-failed"));
        }
        return true;
    }

    // Send goes through the file review (when mods ask for files), then the first-upload question, then sends
    private bool Send()
    {
        if (!CanSend)
        {
            capi.ShowChatMessage(L(IsBug ? "description-needed" : "rating-needed"));
            return true;
        }

        DestinationConfig? route = SelectedRoute();
        ReviewFiles(() => ConfirmSite(route));
        return true;
    }

    private void ConfirmSite(DestinationConfig? route)
    {
        // The first upload to a site is confirmed, the report leaves the computer for someone else's server
        if (route?.Type == DestinationRegistry.Http)
        {
            string host = new Uri(route.Url!).Host;
            ConsentDialog.AskOnce(capi, config, host, L(route.Relay ? "consent-relay" : "consent", host),
                route.Relay ? relay.PrivacyUrl : null, () => SaveAndSend(route));
            return;
        }

        SaveAndSend(route);
    }

    private void SaveAndSend(DestinationConfig? route)
    {
        FeedbackReport report = BuildReport(route);
        string folder = ReportWriter.NewReportFolder(report.ModId);

        BitmapRef? attached = attachScreenshot ? screenshot : null;
        if (attached != null) screenshot = null; // the writer disposes it once saved

        try
        {
            ReportWriter.Save(capi, folder, report, attached, handOverScreenshot: true, BuildLogs(route), ok =>
            {
                if (!ok) capi.ShowChatMessage(L("save-partly-failed"));
                TellLeftOut(report);

                if (route == null)
                {
                    capi.ShowChatMessage(L("saved"));
                    FeedbackEvents.PushSent(capi, report.ModId, report.Kind, FeedbackEvents.Saved);
                    return;
                }
                ReportSender.Send(capi, route, report, folder, status =>
                {
                    // A 403 from the relay means the mod stopped taking reports there since the list was fetched
                    if (route.Relay && status == 403) relay.Drop(report.ModId, report.Kind);
                }, outcome => FeedbackEvents.PushSent(capi, report.ModId, report.Kind, outcome));
            });
        }
        catch (Exception e)
        {
            capi.Logger.Error("[{0}] Could not save the feedback report: {1}", Core.ModId, e);
            capi.ShowChatMessage(L("save-failed"));
            FeedbackEvents.PushSent(capi, report.ModId, report.Kind, FeedbackEvents.Failed);
            screenshot ??= attached;
            return; // stay open so the text isn't lost
        }

        TryClose();
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        relay.Changed += OnRelayChanged;
        if (CanPause && config.PauseInSingleplayer) Pause(true);
    }

    // The list or the picked mod's bucket arrived while the panel is open: the relay may now be offered.
    // It becomes the choice unless the player already picked a destination.
    private void OnRelayChanged()
    {
        if (IsOpened()) UpdateRoutes(keepSelection: routeChosen);
    }

    private void OnTogglePause(bool on)
    {
        Pause(on);
        // Remembered for next time, like the handbook does
        config.PauseInSingleplayer = on;
        capi.StoreModConfig(config, Core.ConfigFile);
    }

    private void Pause(bool on)
    {
        capi.PauseGame(on);
        pausedByUs = on;
    }

    public override void OnGuiClosed()
    {
        // Only resume what this panel paused, not a pause from somewhere else
        if (pausedByUs) Pause(false);
        relay.Changed -= OnRelayChanged;
        base.OnGuiClosed();
        screenshot?.Dispose();
        screenshot = null;
    }

}
