using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace UserFeedback;

/// <summary>Picks the mod the player most likely wants to talk about, from what they look at or hold.</summary>
public static class ModGuess
{
    /// <summary>The mods a player can pick, by name. Vanilla's own mods are left out, they have no author to reach here.</summary>
    public static List<Mod> PickableMods(ICoreClientAPI capi)
    {
        return capi.ModLoader.Mods
            .Where(mod => !mod.Info.CoreMod)
            .OrderBy(mod => mod.Info.Name)
            .ToList();
    }

    /// <summary>How a mod is shown in the pickers: "Polymorph (1.2.0)"</summary>
    public static string Label(Mod mod) => $"{mod.Info.Name} ({mod.Info.Version})";

    public static string? LookingAtCode(ICoreClientAPI capi)
    {
        IClientPlayer player = capi.World.Player;
        if (player.CurrentEntitySelection?.Entity is Entity entity) return entity.Code?.ToString();
        if (player.CurrentBlockSelection is BlockSelection selection) return capi.World.BlockAccessor.GetBlock(selection.Position)?.Code?.ToString();
        return null;
    }

    public static string? HoldingCode(ICoreClientAPI capi)
    {
        return capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.ToString();
    }

    /// <summary>The modid whose asset domain matches what the player looks at, else what they hold, else null.</summary>
    public static string? Guess(ICoreClientAPI capi, List<Mod> pickable)
    {
        foreach (string? code in new[] { LookingAtCode(capi), HoldingCode(capi) })
        {
            if (code == null) continue;

            string domain = new AssetLocation(code).Domain;
            Mod? match = pickable.FirstOrDefault(mod => mod.Info.ModID == domain);
            if (match != null) return match.Info.ModID;
        }
        return null;
    }
}
