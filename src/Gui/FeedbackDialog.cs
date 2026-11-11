using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace UserFeedback;

/// <summary>
/// The F8 panel: which mod (and which others play a part), a rating, some text, what to attach (the screenshot taken
/// just before it opened, game details, logs) and where the report goes. Pauses singleplayer games like the handbook.
/// </summary>
public class FeedbackDialog : GuiDialog
{
    public static readonly string[] Ratings = { "great", "good", "meh", "bad" };

    private const double Width = 480;
    private const double Gap = 10;
    private const string LocalOnly = "local";
    private const double TextHeight = 130;
    private const double ScrollbarWidth = 20;
    private const double SummaryHeight = 66;

    private readonly UserFeedbackConfig config;
    private readonly DestinationRegistry registry;
    private readonly List<Mod> mods;
    private readonly string? lookingAt;
    private readonly string? holding;
    private readonly GameContext context;
    private readonly Task<LogSnapshot> logs;
    private LogSnapshot? readLogs;

    private BitmapRef? screenshot;
    private string selectedModId;
    private List<DestinationConfig> routes = new();
    private string selectedRoute = LocalOnly;
    private string? rating;
    private string text = "";
    private bool attachScreenshot;
    private bool includeDetails = true;
    private bool includeFullLogs;
    private bool pausedByUs;

    // Like the handbook: only a singleplayer world that isn't opened to LAN can be paused
    private bool CanPause => capi.IsSinglePlayer && !capi.OpenedToLan;

    public override string? ToggleKeyCombinationCode => null; // Core handles F8, it has to take the screenshot first
    public override bool PrefersUngrabbedMouse => true;

    public FeedbackDialog(ICoreClientAPI capi, UserFeedbackConfig config, DestinationRegistry registry, BitmapRef? screenshot) : base(capi)
    {
        this.config = config;
        this.registry = registry;
        this.screenshot = screenshot;
        attachScreenshot = screenshot != null && config.AttachScreenshotByDefault;

        mods = ModGuess.PickableMods(capi);
        lookingAt = ModGuess.LookingAtCode(capi);
        holding = ModGuess.HoldingCode(capi);
        selectedModId = ModGuess.Guess(capi, mods) ?? "";
        context = GameContext.Collect(capi);

        // The logs are read in the background as they are now, the summary updates once they are in
        bool singleplayer = capi.IsSinglePlayer;
        logs = Task.Run(() => LogSnapshot.Read(singleplayer));
        logs.ContinueWith(task => capi.Event.EnqueueMainThreadTask(() =>
        {
            if (task.IsCompletedSuccessfully) readLogs = task.Result;
            if (IsOpened()) UpdateSummary();
        }, Core.ModId + "-logs"));

        Compose();
        UpdateInvolved();
        UpdateRoutes();
        UpdateSummary();
    }

    private void Compose()
    {
        CairoFont label = CairoFont.WhiteSmallText();
        double half = (Width - Gap) / 2;
        double y = GuiStyle.TitleBarHeight + 5;

        ElementBounds modLabel = ElementBounds.Fixed(0, y, half, 25);
        ElementBounds involvedLabel = ElementBounds.Fixed(half + Gap, y, half, 25);
        y += 25;
        ElementBounds modDropDown = ElementBounds.Fixed(0, y, half, 30);
        ElementBounds involvedDropDown = ElementBounds.Fixed(half + Gap, y, half, 30);

        ElementBounds ratingLabel = ElementBounds.Fixed(0, y += 45, Width, 25);
        y += 25;
        // With icons: square icon buttons with the name under them. Without: plain text buttons.
        string?[] icons = Ratings.Select(code => RatingIcons.Get(capi, code)).ToArray();
        bool withIcons = icons.Any(icon => icon != null);
        double column = (Width - Gap * (Ratings.Length - 1)) / Ratings.Length;
        ElementBounds[] ratingButtons = Ratings
            .Select((_, i) => withIcons
                ? ElementBounds.Fixed(i * (column + Gap) + (column - RatingIcons.ButtonSize) / 2, y, RatingIcons.ButtonSize, RatingIcons.ButtonSize)
                : ElementBounds.Fixed(i * (column + Gap), y, column, 30))
            .ToArray();
        ElementBounds[] ratingNames = Ratings
            .Select((_, i) => ElementBounds.Fixed(i * (column + Gap), y + RatingIcons.ButtonSize + 4, column, 20))
            .ToArray();
        y += withIcons ? RatingIcons.ButtonSize + 24 : 30;

        ElementBounds textLabel = ElementBounds.Fixed(0, y += 15, Width, 25);
        y += 25;
        // The text area grows with its text, so it sits in a clipped window with a scrollbar, like the vanilla sign editor
        ElementBounds textArea = ElementBounds.Fixed(0, 0, Width - ScrollbarWidth - 6, TextHeight);
        ElementBounds textClip = textArea.ForkBoundingParent().WithFixedPosition(0, y);
        ElementBounds textScrollbar = ElementBounds.Fixed(Width - ScrollbarWidth, y, ScrollbarWidth, TextHeight);

        ElementBounds screenshotSwitch = ElementBounds.Fixed(0, y += TextHeight + 15, 30, 30);
        ElementBounds screenshotLabel = ElementBounds.Fixed(40, y + 5, Width - 40, 25);
        ElementBounds detailsSwitch = ElementBounds.Fixed(0, y += 35, 30, 30);
        ElementBounds detailsLabel = ElementBounds.Fixed(40, y + 5, Width - 40, 25);
        ElementBounds fullLogsSwitch = ElementBounds.Fixed(0, y += 35, 30, 30);
        ElementBounds fullLogsLabel = ElementBounds.Fixed(40, y + 5, Width - 40, 25);
        // Up to three lines of the small font at this width
        ElementBounds summary = ElementBounds.Fixed(0, y += 40, Width, SummaryHeight);

        ElementBounds routeLabel = ElementBounds.Fixed(0, y += SummaryHeight + 10, Width, 25);
        ElementBounds routeDropDown = ElementBounds.Fixed(0, y += 25, Width, 30);

        ElementBounds cancelButton = ElementBounds.Fixed(0, y += 50, 0, 0).WithFixedPadding(10, 4);
        ElementBounds previewButton = ElementBounds.Fixed(0, y, 0, 0).WithAlignment(EnumDialogArea.CenterFixed).WithFixedPadding(10, 4);
        ElementBounds sendButton = ElementBounds.Fixed(0, y, 0, 0).WithAlignment(EnumDialogArea.RightFixed).WithFixedPadding(10, 4);

        ElementBounds background = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        background.BothSizing = ElementSizing.FitToChildren;
        background.WithChildren(modLabel, involvedLabel, modDropDown, involvedDropDown, ratingLabel, textLabel, textClip, textScrollbar,
            screenshotSwitch, screenshotLabel, detailsSwitch, detailsLabel, fullLogsSwitch, fullLogsLabel, summary, routeLabel, routeDropDown, cancelButton, previewButton, sendButton);
        background.WithChildren(ratingButtons);
        if (withIcons) background.WithChildren(ratingNames);

        // In the title bar, where the handbook has its own pause button
        ElementBounds pauseButton = ElementBounds.Fixed(Width - 150, -15, 110, 22);

        string[] modValues = new[] { "" }.Concat(mods.Select(mod => mod.Info.ModID)).ToArray();
        string[] modLabels = new[] { L("mod-unknown") }.Concat(mods.Select(ModName)).ToArray();

        GuiComposer composer = capi.Gui.CreateCompo(Core.ModId + "-dialog", ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(background)
            .AddDialogTitleBar(L("dialog-title"), () => TryClose())
            .BeginChildElements(background)
                .AddIf(CanPause)
                    .AddToggleButton(Lang.Get("Pause game"), CairoFont.WhiteDetailText(), OnTogglePause, pauseButton, "pause")
                .EndIf()
                .AddStaticText(L("which-mod"), label, modLabel)
                .AddStaticText(L("also-involved"), label, involvedLabel)
                .AddDropDown(modValues, modLabels, Math.Max(0, Array.IndexOf(modValues, selectedModId)), OnModSelected, modDropDown, "mod")
                .AddMultiSelectDropDown(new[] { "" }, new[] { "" }, -1, (_, _) => UpdateSummary(), involvedDropDown, "involved")
                .AddStaticText(L("how-is-it"), label, ratingLabel);

        for (int i = 0; i < Ratings.Length; i++)
        {
            string code = Ratings[i];
            string key = "rating-" + code;
            if (withIcons)
            {
                // toggleable: true is what makes it stay pressed, the same as AddToggleButton does
                composer.AddInteractiveElement(new GuiElementToggleButton(capi, icons[i] ?? "", icons[i] == null ? L(key) : "",
                    CairoFont.WhiteSmallText(), on => SelectRating(code, on), ratingButtons[i], toggleable: true), key);
                composer.AddStaticText(L(key), RatingIcons.LabelFont(code), EnumTextOrientation.Center, ratingNames[i]);
            }
            else
            {
                composer.AddToggleButton(L(key), CairoFont.WhiteSmallText(), on => SelectRating(code, on), ratingButtons[i], key);
            }
        }

        composer
                .AddStaticText(L("tell-more"), label, textLabel)
                .BeginClip(textClip)
                    .AddTextArea(textArea, OnTextChanged, CairoFont.TextInput().WithFontSize(16), "text")
                .EndClip()
                .AddVerticalScrollbar(OnTextScrolled, textScrollbar, "textscroll")
                .AddSwitch(on => { attachScreenshot = on; UpdateSummary(); }, screenshotSwitch, "screenshot")
                .AddStaticText(L(screenshot != null ? "attach-screenshot" : "no-screenshot"), label, screenshotLabel)
                .AddSwitch(on => { includeDetails = on; UpdateSummary(); }, detailsSwitch, "details")
                .AddStaticText(L("include-details"), label, detailsLabel)
                .AddSwitch(on => { includeFullLogs = on; UpdateSummary(); }, fullLogsSwitch, "fulllogs")
                .AddStaticText(L("include-full-logs"), label, fullLogsLabel)
                .AddDynamicText("", CairoFont.WhiteDetailText(), summary, "summary")
                .AddStaticText(L("send-to"), label, routeLabel)
                .AddDropDown(new[] { LocalOnly }, new[] { L("route-local") }, 0, (value, _) => selectedRoute = value, routeDropDown, "route")
                .AddSmallButton(L("cancel"), () => TryClose(), cancelButton)
                .AddSmallButton(L("preview"), Preview, previewButton)
                .AddSmallButton(L("send"), Send, sendButton)
            .EndChildElements();

        SingleComposer = composer.Compose();

        GuiElementTextArea area = SingleComposer.GetTextArea("text");
        area.SetMaxLength(config.MaxTextLength);
        area.OnCursorMoved = OnTextCursorMoved;
        SingleComposer.GetScrollbar("textscroll").SetHeights((float)TextHeight, (float)TextHeight);
        SingleComposer.GetSwitch("screenshot").SetValue(attachScreenshot);
        SingleComposer.GetSwitch("screenshot").Enabled = screenshot != null;
        SingleComposer.GetSwitch("details").SetValue(includeDetails);
        SingleComposer.GetSwitch("fulllogs").SetValue(includeFullLogs);
        SingleComposer.GetToggleButton("pause")?.SetValue(config.PauseInSingleplayer);
    }

    private static string ModName(Mod mod) => $"{mod.Info.Name} ({mod.Info.Version})";

    private void OnTextChanged(string value)
    {
        text = value;
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

    private void OnModSelected(string modId, bool selected)
    {
        selectedModId = modId;
        UpdateInvolved();
        UpdateRoutes();
        UpdateSummary();
    }

    /// <summary>The mod the report is about and the others the player marked as involved</summary>
    private List<Mod> ReportMods()
    {
        var ids = new HashSet<string>(SingleComposer.GetDropDown("involved").SelectedValues) { selectedModId };
        return mods.Where(mod => ids.Contains(mod.Info.ModID)).ToList();
    }

    // One line telling the player what the report will hold, so nothing leaves their computer unseen
    private void UpdateSummary()
    {
        var parts = new List<string>();
        if (attachScreenshot && screenshot != null) parts.Add(L("summary-screenshot"));
        if (includeDetails)
        {
            parts.Add(L("summary-details"));
            parts.Add(readLogs == null
                ? L("summary-logs-reading")
                : L("summary-logs", new ModLogFilter(ReportMods()).Matching(readLogs).Count));
        }
        if (includeFullLogs) parts.Add(L("summary-full-logs"));

        string line = parts.Count == 0 ? L("summary-only-text") : L("summary", string.Join(", ", parts));
        SingleComposer.GetDynamicText("summary").SetNewText(line);
    }

    // "Other mods involved" lists every mod except the one the report is about, keeping what was already picked
    private void UpdateInvolved()
    {
        GuiElementDropDown dropDown = SingleComposer.GetDropDown("involved");
        string[] picked = dropDown.SelectedValues.Where(id => id != selectedModId).ToArray();

        List<Mod> others = mods.Where(mod => mod.Info.ModID != selectedModId).ToList();
        dropDown.SetList(others.Select(mod => mod.Info.ModID).ToArray(), others.Select(ModName).ToArray());
        dropDown.SetSelectedValue(picked);
    }

    // The destinations follow the chosen mod, saving on this computer is always last
    private void UpdateRoutes()
    {
        routes = registry.DestinationsFor(selectedModId);
        string[] values = routes.Select((_, i) => i.ToString()).Append(LocalOnly).ToArray();
        string[] names = routes.Select(route => route.Describe()).Append(L("route-local")).ToArray();

        GuiElementDropDown dropDown = SingleComposer.GetDropDown("route");
        dropDown.SetList(values, names);
        dropDown.SetSelectedIndex(0);
        selectedRoute = values[0];
    }

    private void SelectRating(string code, bool on)
    {
        rating = on ? code : null;
        // Toggle buttons that act like radio buttons
        foreach (string other in Ratings)
        {
            if (other != code) SingleComposer.GetToggleButton("rating-" + other).SetValue(false);
        }
    }

    private DestinationConfig? SelectedRoute()
    {
        return int.TryParse(selectedRoute, out int index) && index < routes.Count ? routes[index] : null;
    }

    private FeedbackReport BuildReport(DestinationConfig? route)
    {
        Mod? mod = mods.FirstOrDefault(m => m.Info.ModID == selectedModId);
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
            Rating = rating,
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
            Context = includeDetails ? context : null,
        };
    }

    private ReportLogs BuildLogs()
    {
        return new ReportLogs(logs, includeDetails ? new ModLogFilter(ReportMods()) : null, includeFullLogs, new Sanitizer(capi));
    }

    // Writes exactly what Send would, into the preview folder, and opens it. The panel stays open.
    private bool Preview()
    {
        try
        {
            ReportWriter.Save(capi, ReportWriter.PreviewFolder, BuildReport(SelectedRoute()), attachScreenshot ? screenshot : null,
                handOverScreenshot: false, BuildLogs(), _ => ReportWriter.OpenFolder(ReportWriter.PreviewFolder));
        }
        catch (Exception e)
        {
            capi.Logger.Error("[{0}] Could not write the preview: {1}", Core.ModId, e);
            capi.ShowChatMessage(L("save-failed"));
        }
        return true;
    }

    private bool Send()
    {
        DestinationConfig? route = SelectedRoute();

        // The first upload to a site is confirmed, the report leaves the computer for someone else's server
        if (route?.Type == DestinationRegistry.Http)
        {
            string host = new Uri(route.Url!).Host;
            if (!config.ConsentedHosts.Contains(host))
            {
                new GuiDialogConfirm(capi, L("consent", host), yes =>
                {
                    if (!yes) return;
                    config.ConsentedHosts.Add(host);
                    capi.StoreModConfig(config, Core.ConfigFile);
                    SaveAndSend(route);
                }).TryOpen();
                return true;
            }
        }

        SaveAndSend(route);
        return true;
    }

    private void SaveAndSend(DestinationConfig? route)
    {
        FeedbackReport report = BuildReport(route);
        string folder = ReportWriter.NewReportFolder(report.ModId);

        BitmapRef? attached = attachScreenshot ? screenshot : null;
        if (attached != null) screenshot = null; // the writer disposes it once saved

        try
        {
            ReportWriter.Save(capi, folder, report, attached, handOverScreenshot: true, BuildLogs(), ok =>
            {
                if (!ok) capi.ShowChatMessage(L("save-partly-failed"));

                if (route != null) ReportSender.Send(capi, route, report, folder);
                else capi.ShowChatMessage(Lang.Get($"{Core.ModId}:saved", folder));
            });
        }
        catch (Exception e)
        {
            capi.Logger.Error("[{0}] Could not save the feedback report: {1}", Core.ModId, e);
            capi.ShowChatMessage(L("save-failed"));
            screenshot ??= attached;
            return; // stay open so the text isn't lost
        }

        TryClose();
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        if (CanPause && config.PauseInSingleplayer) Pause(true);
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
        base.OnGuiClosed();
        screenshot?.Dispose();
        screenshot = null;
    }

    private static string L(string key, params object[] args) => Lang.Get($"{Core.ModId}:{key}", args);
}
