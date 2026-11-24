using Vintagestory.API.Client;

namespace UserFeedback;

/// <summary>
/// What UserFeedback's windows share: they're opened by Core or another window (never by their own hotkey), free the
/// mouse, and are a shaded box with a title bar sized to what's in it.
/// </summary>
public abstract class ModDialog : GuiDialog
{
    protected ModDialog(ICoreClientAPI capi) : base(capi)
    {
    }

    public override string? ToggleKeyCombinationCode => null; // Core handles F8, it has to take the screenshot first
    public override bool PrefersUngrabbedMouse => true;

    /// <summary>The window's box, padded and sized to its children</summary>
    protected static ElementBounds NewBackground(params ElementBounds[] children)
    {
        ElementBounds background = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        background.BothSizing = ElementSizing.FitToChildren;
        return background.WithChildren(children);
    }

    /// <summary>A composer with the box and the title bar, already inside the box: add the content, then EndChildElements</summary>
    protected GuiComposer StartCompo(string name, string title, ElementBounds background)
    {
        return capi.Gui.CreateCompo(Core.ModId + "-" + name, ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(background)
            .AddDialogTitleBar(title, () => TryClose())
            .BeginChildElements(background);
    }

    /// <summary>Builds the window again with compose, for content that changed shape, and frees the old one</summary>
    protected void Recompose(System.Action compose)
    {
        GuiComposer old = SingleComposer;
        compose();
        old.Dispose();
    }
}
