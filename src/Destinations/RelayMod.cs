using System.Collections.Generic;

namespace UserFeedback;

/// <summary>What the relay's registry says about one mod</summary>
public class RelayMod
{
    /// <summary>Listed in "mods": takes reports through the relay. False for a mod that only counts "works for me".</summary>
    public bool Reports;

    /// <summary>Listed in "works": counts "works for me" results</summary>
    public bool Works;

    /// <summary>The author has a public page, so the panel offers "may be shown publicly"</summary>
    public bool Public;

    /// <summary>The kinds of report the relay takes for this mod, "bug" and/or "feedback". Null means both.</summary>
    public List<string>? Kinds;

    public bool Takes(string kind) => Kinds == null || Kinds.Contains(kind);
}
