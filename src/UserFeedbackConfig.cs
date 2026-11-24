using System.Collections.Generic;

namespace UserFeedback;

// ModConfig/userfeedback.json, read by the client. The mod does nothing on the server.
public class UserFeedbackConfig
{
    public bool AttachScreenshotByDefault = true;
    /// <summary>
    /// Whether "include game details and logs" starts switched on in a bug report. Feedback never has them, it stays
    /// a rating, text and the screenshot, like Subnautica.
    /// </summary>
    public bool IncludeDetailsByDefault = true;
    public int MaxTextLength = 2000;
    public bool PauseInSingleplayer = true;

    /// <summary>
    /// The UserFeedback relay, which passes reports on to mods' authors without giving them the player's IP address.
    /// An empty value switches it off: no registry fetch and no relay in "Send to".
    /// </summary>
    public string RelayUrl = "https://feedback.lazzeking.fyi/v1";

    /// <summary>Sites the player agreed to upload reports to, asked the first time for each one</summary>
    public List<string> ConsentedHosts = new();

    /// <summary>
    /// Mods the player switched "tell the author when this mod loads with or without errors" on for, in the "works for me"
    /// panel. A few minutes after joining a world their result goes to the relay (see WorksCheck). Empty by default.
    /// </summary>
    public List<string> WorksOptIn = new();
}
