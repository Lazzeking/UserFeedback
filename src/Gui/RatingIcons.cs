using System.Collections.Generic;
using Cairo;
using Vintagestory.API.MathTools;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace UserFeedback;

/// <summary>
/// Optional icons for the rating buttons: assets/userfeedback/textures/icons/rating-&lt;code&gt;.svg, drawn in their own colors.
/// Missing ones fall back to the rating's name on the button.
/// </summary>
public static class RatingIcons
{
    /// <summary>The square button an icon sits in, in GUI units. The icon itself is drawn 8 smaller.</summary>
    public const double ButtonSize = 48;

    // The accent color of each face (its eyes and mouth), so the name under a button matches it.
    // Fixed for now: change these together with the svgs.
    private static readonly Dictionary<string, string> LabelColors = new()
    {
        ["great"] = "#35ff00",
        ["good"] = "#9cda07",
        ["meh"] = "#daa707",
        ["bad"] = "#c02817",
    };

    /// <summary>The font for the name under a rating's icon, in the color of its face</summary>
    public static CairoFont LabelFont(string rating)
    {
        CairoFont font = CairoFont.WhiteDetailText();
        if (LabelColors.TryGetValue(rating, out string? hex)) font.Color = ColorUtil.Hex2Doubles(hex);
        return font;
    }

    /// <summary>The icon name to give a button, or null when the svg isn't there</summary>
    public static string? Get(ICoreClientAPI capi, string rating)
    {
        string name = $"{Core.ModId}-rating-{rating}";
        if (capi.Gui.Icons.CustomIcons.ContainsKey(name)) return name;

        IAsset? asset = capi.Assets.TryGet(new AssetLocation(Core.ModId, $"textures/icons/rating-{rating}.svg"));
        if (asset == null) return null;

        // The vanilla SvgIconSource paints the icon in the button's text color. Passing no color keeps the svg's own.
        capi.Gui.Icons.CustomIcons[name] = (ctx, x, y, w, h, rgba) =>
        {
            if (ctx.GetTarget() is ImageSurface surface) capi.Gui.DrawSvg(asset, surface, x, y, (int)w, (int)h, null);
        };
        return name;
    }
}
