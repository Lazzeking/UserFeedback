using System;
using Vintagestory.API.Client;
using static UserFeedback.ModLang;

namespace UserFeedback;

/// <summary>
/// The first step after F8: "something's broken" or "just feedback", so the panel only shows what that kind needs, and
/// "works for me" (<see cref="WorksDialog.Choice"/>) while the relay is on. Closing it without a choice (Esc, the X, F8
/// again) calls onCancel.
/// </summary>
public class KindSelectorDialog : ModDialog
{
    private const double Width = 360;
    private const double ButtonHeight = 40;

    private readonly Action<string> onPick;
    private readonly Action onCancel;
    private readonly bool offerWorks;
    private bool answered;


    /// <param name="offerWorks">Whether to show "works for me", which only goes through the relay</param>
    public KindSelectorDialog(ICoreClientAPI capi, Action<string> onPick, Action onCancel, bool offerWorks) : base(capi)
    {
        this.onPick = onPick;
        this.onCancel = onCancel;
        this.offerWorks = offerWorks;
        Compose();
    }

    private void Compose()
    {
        CairoFont buttonFont = CairoFont.ButtonText();
        CairoFont hintFont = CairoFont.WhiteDetailText();
        double y = GuiStyle.TitleBarHeight + 10;

        ElementBounds bugButton = ElementBounds.Fixed(0, y, Width, ButtonHeight);
        ElementBounds bugHint = ElementBounds.Fixed(0, y += ButtonHeight + 5, Width, 20);
        ElementBounds feedbackButton = ElementBounds.Fixed(0, y += 35, Width, ButtonHeight);
        ElementBounds feedbackHint = ElementBounds.Fixed(0, y += ButtonHeight + 5, Width, 20);
        ElementBounds worksButton = ElementBounds.Fixed(0, y += 35, Width, ButtonHeight);
        ElementBounds worksHint = ElementBounds.Fixed(0, y += ButtonHeight + 5, Width, 20);

        ElementBounds background = NewBackground(bugButton, bugHint, feedbackButton, feedbackHint);
        if (offerWorks) background.WithChildren(worksButton, worksHint);

        SingleComposer = StartCompo("kind", L("dialog-title"), background)
                .AddButton(L("kind-bug"), () => Pick(FeedbackReport.Bug), bugButton, buttonFont, EnumButtonStyle.Normal)
                .AddStaticText(L("kind-bug-hint"), hintFont, EnumTextOrientation.Center, bugHint)
                .AddButton(L("kind-feedback"), () => Pick(FeedbackReport.Feedback), feedbackButton, buttonFont, EnumButtonStyle.Normal)
                .AddStaticText(L("kind-feedback-hint"), hintFont, EnumTextOrientation.Center, feedbackHint)
                .AddIf(offerWorks)
                    .AddButton(L("kind-works"), () => Pick(WorksDialog.Choice), worksButton, buttonFont, EnumButtonStyle.Normal)
                    .AddStaticText(L("kind-works-hint"), hintFont, EnumTextOrientation.Center, worksHint)
                .EndIf()
            .EndChildElements()
            .Compose();
    }

    private bool Pick(string kind)
    {
        answered = true;
        TryClose();
        onPick(kind);
        return true;
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        if (answered) return;
        answered = true;
        onCancel();
    }

}
