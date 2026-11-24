using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using static UserFeedback.ModLang;

namespace UserFeedback;

/// <summary>
/// The "first time you send to this site" question with a clickable privacy link, which the game's GuiDialogConfirm
/// can't show because it only draws plain text. Closing it without answering counts as no.
/// </summary>
public class ConsentDialog : ModDialog
{
    private const double Width = 500;

    /// <summary>
    /// Runs onYes right away when the player already agreed to send to host. Otherwise asks question first; a yes is
    /// remembered in ConsentedHosts (and saved), so it's asked once per site. A no or closing runs onNo.
    /// </summary>
    /// <param name="privacyUrl">The relay's privacy page, checked by RelayRegistry.PrivacyUrl; null for other sites</param>
    public static void AskOnce(ICoreClientAPI capi, UserFeedbackConfig config, string host, string question, string? privacyUrl,
        Action onYes, Action? onNo = null)
    {
        if (config.ConsentedHosts.Contains(host))
        {
            onYes();
            return;
        }

        new ConsentDialog(capi, question, privacyUrl, yes =>
        {
            if (!yes)
            {
                onNo?.Invoke();
                return;
            }
            config.ConsentedHosts.Add(host);
            capi.StoreModConfig(config, Core.ConfigFile);
            onYes();
        }).TryOpen();
    }

    /// <summary>"Privacy: url" as plain text, to measure how much room the link takes; empty without one</summary>
    public static string PrivacyPlain(string? privacyUrl) => privacyUrl == null ? "" : $"{L("consent-privacy")} {privacyUrl}";

    /// <summary>"Privacy: url" with the url as a link, for rich text; empty without one</summary>
    public static string PrivacyVtml(string? privacyUrl) =>
        privacyUrl == null ? "" : $"{L("consent-privacy")} <a href=\"{privacyUrl}\">{privacyUrl}</a>";

    private readonly string text;
    private readonly string? privacyUrl;
    private readonly Action<bool> onAnswer;
    private bool answered;

    public override double DrawOrder => 2.0;

    /// <param name="privacyUrl">Checked by RelayRegistry.PrivacyUrl: https on the relay's host, nothing VTML would read as markup</param>
    public ConsentDialog(ICoreClientAPI capi, string text, string? privacyUrl, Action<bool> onAnswer) : base(capi)
    {
        this.text = text;
        this.privacyUrl = privacyUrl;
        this.onAnswer = onAnswer;
        Compose();
    }

    private void Compose()
    {
        CairoFont font = CairoFont.WhiteSmallText();
        // The text is from the lang file with a hostname in it, so it holds no markup. Strip it anyway, VTML would read it.
        string plain = text.Replace("<", "").Replace(">", "");
        string link = PrivacyPlain(privacyUrl);
        string vtml = privacyUrl == null ? plain : $"{plain}<br><br>{PrivacyVtml(privacyUrl)}";

        // Rich text measures itself only once composed, the buttons go below the plain text's height
        var measure = new TextDrawUtil();
        double height = measure.GetMultilineTextHeight(font, plain, Width);
        if (link.Length > 0) height += 2 * measure.GetMultilineTextHeight(font, link, Width);

        ElementBounds textBounds = ElementBounds.Fixed(0, GuiStyle.TitleBarHeight + 5, Width, height);
        ElementBounds noButton = ElementBounds.Fixed(0, textBounds.fixedY + height + 20, 0, 0).WithFixedPadding(10, 4);
        ElementBounds yesButton = ElementBounds.Fixed(0, noButton.fixedY, 0, 0).WithAlignment(EnumDialogArea.RightFixed).WithFixedPadding(10, 4);

        ElementBounds background = NewBackground(textBounds, noButton, yesButton);

        SingleComposer = StartCompo("consent", Lang.Get("Please Confirm"), background)
                .AddRichtext(vtml, font, textBounds)
                .AddSmallButton(Lang.Get("Cancel"), () => Answer(false), noButton)
                .AddSmallButton(Lang.Get("Confirm"), () => Answer(true), yesButton)
            .EndChildElements()
            .Compose();
    }

    private bool Answer(bool yes)
    {
        answered = true;
        TryClose();
        onAnswer(yes);
        return true;
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        if (!answered)
        {
            answered = true;
            onAnswer(false);
        }
    }
}
