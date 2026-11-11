using System.Collections.Generic;

namespace UserFeedback;

// ModConfig/userfeedback.json, read by the client. The mod does nothing on the server.
public class UserFeedbackConfig
{
    public bool AttachScreenshotByDefault = true;
    public int MaxTextLength = 2000;
    public bool PauseInSingleplayer = true;

    /// <summary>Sites the player agreed to upload reports to, asked the first time for each one</summary>
    public List<string> ConsentedHosts = new();
}
