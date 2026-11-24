using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;

namespace UserFeedback;

/// <summary>The relay's answer to one result. Status null when it didn't answer at all (timeout, no connection).</summary>
/// <param name="Message">The relay's own message for players, cleaned for chat. Null on a 204.</param>
/// <param name="RetryMinutes">From Retry-After on a 429</param>
public record WorksAnswer(int? Status, string? Message, int? RetryMinutes)
{
    public bool Counted => Status == 204;
}

/// <summary>
/// POST {RelayUrl}/works, one mod per request, so no request shows more than one of the player's mods. Sends in the background and calls back
/// on the main thread. Nothing here talks to the player: the button and the automatic check decide that.
/// </summary>
public static class WorksSender
{
    /// <summary>The relay refuses larger bodies with 413</summary>
    public const int MaxBody = 2048;

    private static readonly HttpClient Http = ServerHttp.CreateClient(TimeSpan.FromSeconds(15), 64 * 1024);

    public static void Send(ICoreClientAPI capi, string url, WorksResult result, Action<WorksAnswer> onAnswer)
    {
        Send(capi, url, result.ModId, result.ToJson(), onAnswer);
    }

    /// <summary>A body made earlier by <see cref="WorksResult.ToJson"/>, kept in works-sent.json until the relay answers</summary>
    public static void Send(ICoreClientAPI capi, string url, string modId, JObject json, Action<WorksAnswer> onAnswer)
    {
        string body = json.ToString(Newtonsoft.Json.Formatting.None);
        if (Encoding.UTF8.GetByteCount(body) > MaxBody)
        {
            // Can't happen with the version checks, kept so a later field can't make the relay count refusals against the player
            onAnswer(new WorksAnswer(413, null, null));
            return;
        }

        Task.Run(async () =>
        {
            WorksAnswer answer;
            try
            {
                using HttpRequestMessage request = ServerHttp.NewRequest(HttpMethod.Post, url);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.Add("X-UserFeedback-Mod", modId);
                using HttpResponseMessage response = await Http.SendAsync(request);
                int status = (int)response.StatusCode;
                string text = status == 204 ? "" : await response.Content.ReadAsStringAsync();
                answer = new WorksAnswer(status, ServerReply.Parse(text).SafeMessage, status == 429 ? ServerHttp.RetryMinutes(response) : null);
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[{0}] Sending the \"works for me\" result for {1} to {2} failed: {3}", Core.ModId, modId, url, e.Message);
                answer = new WorksAnswer(null, null, null);
            }

            capi.Event.EnqueueMainThreadTask(() => onAnswer(answer), Core.ModId + "-works");
        });
    }
}
