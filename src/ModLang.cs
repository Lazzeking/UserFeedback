using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace UserFeedback;

/// <summary>
/// This mod's own texts from lang/en.json. Classes use it with <c>using static UserFeedback.ModLang;</c>, so a text is
/// <c>L("send")</c> and a chat line <c>Chat(capi, "saved")</c>.
/// </summary>
public static class ModLang
{
    /// <summary>The text of "userfeedback:key", with {0}, {1}… filled from args</summary>
    public static string L(string key, params object?[] args) => Lang.Get($"{Core.ModId}:{key}", args!);

    /// <summary>Shows the text of "userfeedback:key" in the player's chat</summary>
    public static void Chat(ICoreClientAPI capi, string key, params object?[] args) => capi.ShowChatMessage(L(key, args));
}
