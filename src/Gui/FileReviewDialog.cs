using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using static UserFeedback.ModLang;

namespace UserFeedback;

/// <summary>
/// Lists every file the mods ask to include, who asked and how big it is, with a switch to leave it out and a button to
/// open it. Files whose path doesn't name the mod that asked are flagged, they may be another mod's.
/// Shown before every send that includes files, and from the panel's "Review files" button.
/// </summary>
public class FileReviewDialog : ModDialog
{
    private const double Width = 560;
    private const double RowHeight = 50;
    private const double MaxListHeight = 320;
    private const double ScrollbarWidth = 20;

    private readonly List<RequestedFile> files;
    private readonly HashSet<string> excluded;
    private readonly Func<string, string> modName;
    private readonly Action onChanged;
    private readonly Action? onConfirm;

    private ElementBounds? list;


    /// <param name="excluded">Paths the player switched off, changed in place</param>
    /// <param name="onChanged">Called after each switch, so the panel's summary follows</param>
    /// <param name="onConfirm">When set, the dialog asks before sending and calls this on "Send"</param>
    public FileReviewDialog(ICoreClientAPI capi, List<RequestedFile> files, HashSet<string> excluded, Func<string, string> modName,
        Action onChanged, Action? onConfirm) : base(capi)
    {
        this.files = files;
        this.excluded = excluded;
        this.modName = modName;
        this.onChanged = onChanged;
        this.onConfirm = onConfirm;
        Compose();
    }

    private void Compose()
    {
        double y = GuiStyle.TitleBarHeight + 5;
        ElementBounds intro = ElementBounds.Fixed(0, y, Width, 45);
        y += 50;

        double listHeight = files.Count * RowHeight;
        double visible = Math.Min(listHeight, MaxListHeight);
        bool scrolls = listHeight > visible;
        double rowWidth = Width - (scrolls ? ScrollbarWidth + 10 : 0);

        // The rows sit in one tall list that slides inside a clipped window when it's taller than that
        ElementBounds clip = ElementBounds.Fixed(0, y, rowWidth, visible);
        list = ElementBounds.Fixed(0, 0, rowWidth, listHeight);
        clip.WithChild(list);
        ElementBounds scrollbar = ElementBounds.Fixed(Width - ScrollbarWidth, y, ScrollbarWidth, visible);
        y += visible + 15;

        ElementBounds leftButton = ElementBounds.Fixed(0, y, 0, 0).WithFixedPadding(10, 4);
        ElementBounds rightButton = ElementBounds.Fixed(0, y, 0, 0).WithAlignment(EnumDialogArea.RightFixed).WithFixedPadding(10, 4);

        ElementBounds background = NewBackground(intro, clip, scrollbar, leftButton, rightButton);

        CairoFont warning = CairoFont.WhiteDetailText();
        warning.Color = ColorUtil.Hex2Doubles("#e0a030");

        GuiComposer composer = StartCompo("files", L("review-title"), background)
                .AddStaticText(L(onConfirm != null ? "review-intro-send" : "review-intro"), CairoFont.WhiteSmallText(), intro)
                .BeginClip(clip);

        for (int i = 0; i < files.Count; i++)
        {
            RequestedFile file = files[i];
            double top = i * RowHeight;
            string path = file.Path;

            ElementBounds toggle = ElementBounds.Fixed(0, top + 8, 30, 30);
            ElementBounds name = ElementBounds.Fixed(40, top + 2, rowWidth - 130, 22);
            ElementBounds detail = ElementBounds.Fixed(40, top + 24, rowWidth - 130, 20);
            ElementBounds open = ElementBounds.Fixed(rowWidth - 80, top + 10, 80, 26);
            list.WithChildren(toggle, name, detail, open);

            string details = L("review-detail", Sizes.Format(file.Size), modName(file.ModId));
            if (!file.NamedAfterMod) details += " · " + L("review-not-named", file.ModId);

            composer
                .AddSwitch(on => Toggle(path, on), toggle, "file-" + i)
                .AddStaticText(path, CairoFont.WhiteSmallText(), name)
                .AddStaticText(details, file.NamedAfterMod ? CairoFont.WhiteDetailText() : warning, detail)
                .AddSmallButton(L("review-open"), () => Open(path), open);
        }

        composer
                .EndClip()
                .AddIf(scrolls)
                    .AddVerticalScrollbar(OnScroll, scrollbar, "scroll")
                .EndIf()
                .AddSmallButton(L(onConfirm != null ? "cancel" : "review-done"), () => TryClose(), leftButton)
                .AddIf(onConfirm != null)
                    .AddSmallButton(L("send"), Confirm, rightButton)
                .EndIf()
            .EndChildElements();

        SingleComposer = composer.Compose();

        for (int i = 0; i < files.Count; i++)
        {
            SingleComposer.GetSwitch("file-" + i).SetValue(!excluded.Contains(files[i].Path));
        }
        SingleComposer.GetScrollbar("scroll")?.SetHeights((float)visible, (float)listHeight);
    }

    private void Toggle(string path, bool on)
    {
        if (on) excluded.Remove(path);
        else excluded.Add(path);
        onChanged();
    }

    private void OnScroll(float value)
    {
        if (list == null) return;
        list.fixedY = -value;
        list.CalcWorldBounds();
    }

    private bool Open(string path)
    {
        try
        {
            // The file as it is on disk, in the player's own editor, before it is cleaned for sending
            Shell.Open(ExtraFiles.FullPath(path));
        }
        catch (Exception e)
        {
            capi.ShowChatMessage(L("folder-failed", path, e.Message));
        }
        return true;
    }

    private bool Confirm()
    {
        TryClose();
        onConfirm?.Invoke();
        return true;
    }


}
