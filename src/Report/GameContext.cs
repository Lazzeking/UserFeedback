using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace UserFeedback;

/// <summary>Where and when the player was, what they run it on and with which mods. Collected on the main thread when F8 is pressed.</summary>
public class GameContext
{
    /// <summary>Like the game shows coordinates: x and z from the world spawn, y as it is</summary>
    public string? Position;
    public string? Date;
    public string? Season;
    public float? Temperature;
    public float? Rainfall;
    public string? GameMode;
    public string Language = "";

    /// <summary>Every loaded mod as modid@version, vanilla's own included</summary>
    public List<string> Mods = new();

    /// <summary>Game version, operating system, CPU, RAM and graphics card, as the game logged them at start</summary>
    public Dictionary<string, string> System = new();

    public static GameContext Collect(ICoreClientAPI capi)
    {
        var context = new GameContext
        {
            Language = Lang.CurrentLocale,
            Mods = capi.ModLoader.Mods.Select(mod => $"{mod.Info.ModID}@{mod.Info.Version}").ToList(),
        };

        IClientPlayer? player = capi.World?.Player;
        if (player?.Entity == null) return context;

        try
        {
            BlockPos pos = player.Entity.Pos.AsBlockPos;
            EntityPos spawn = capi.World!.DefaultSpawnPosition;
            context.Position = $"{pos.X - (int)spawn.X}, {pos.Y}, {pos.Z - (int)spawn.Z}";
            context.GameMode = player.WorldData.CurrentGameMode.ToString();
            context.Date = capi.World.Calendar.PrettyDate();
            context.Season = capi.World.Calendar.GetSeason(pos).ToString();

            ClimateCondition? climate = capi.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.NowValues);
            if (climate != null)
            {
                context.Temperature = (float)Math.Round(climate.Temperature, 1);
                context.Rainfall = (float)Math.Round(climate.WorldgenRainfall, 2);
            }
        }
        catch (Exception e)
        {
            // Context is a bonus, a report without it is still worth sending
            capi.Logger.Warning("[{0}] Could not collect the game context: {1}", Core.ModId, e.Message);
        }

        return context;
    }
}
