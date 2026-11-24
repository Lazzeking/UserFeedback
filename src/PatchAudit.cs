using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace UserFeedback;

/// <summary>
/// Finds other mods' Harmony patches on UserFeedback's own code, so players and authors can see who changed it.
/// It can't stop a mod that wants to read or change reports: every code mod runs in the same process with full
/// access, and one could also hide from this check or change code without Harmony. It makes the usual cases visible
/// and shows which mod to ask about.
/// </summary>
public static class PatchAudit
{
    /// <summary>Each patching mod's name (or Harmony id when no loaded mod has it), with the methods it patches</summary>
    public static Dictionary<string, List<string>> ForeignPatches(ICoreAPI api)
    {
        var found = new Dictionary<string, List<string>>();
        Assembly ours = typeof(Core).Assembly;

        try
        {
            foreach (MethodBase method in Harmony.GetAllPatchedMethods())
            {
                if (method.DeclaringType?.Assembly != ours) continue;
                Patches? info = Harmony.GetPatchInfo(method);
                if (info == null) continue;

                foreach (string owner in info.Owners.Where(owner => owner != Core.ModId))
                {
                    string who = Describe(api, owner);
                    if (!found.TryGetValue(who, out List<string>? methods)) found[who] = methods = new();
                    methods.Add($"{method.DeclaringType!.Name}.{method.Name}");
                }
            }
        }
        catch (Exception e)
        {
            api.Logger.Warning("[{0}] Could not check for patches on UserFeedback: {1}", Core.ModId, e.Message);
        }

        return found;
    }

    /// <summary>Writes what ForeignPatches found to the log, returning the names of the patching mods</summary>
    public static List<string> LogForeignPatches(ICoreAPI api)
    {
        Dictionary<string, List<string>> found = ForeignPatches(api);
        foreach ((string who, List<string> methods) in found)
        {
            api.Logger.Warning("[{0}] {1} changes UserFeedback's code with Harmony: {2}", Core.ModId, who, string.Join(", ", methods.Distinct()));
        }
        return found.Keys.ToList();
    }

    // Harmony ids are free text, many mods use their modid
    private static string Describe(ICoreAPI api, string owner)
    {
        Mod? mod = api.ModLoader.GetMod(owner);
        return mod != null ? $"{mod.Info.Name} ({owner})" : owner;
    }
}
