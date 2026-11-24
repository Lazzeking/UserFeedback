using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace UserFeedback;

/// <summary>
/// What every request to a server has in common: report uploads, "works for me" results and the relay registry.
/// The safeguards live here once: no redirects, small answers, and server text made safe before the player sees it.
/// </summary>
public static class ServerHttp
{
    /// <param name="maxAnswerBytes">A larger answer is refused instead of filling memory. With decompress, it counts the unpacked size.</param>
    public static HttpClient CreateClient(TimeSpan timeout, int maxAnswerBytes, bool decompress = false)
    {
        // No redirects: the player agreed to send to this site, not to wherever it points next
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            MaxResponseHeadersLength = 16,
            AutomaticDecompression = decompress ? DecompressionMethods.All : DecompressionMethods.None,
        };
        return new HttpClient(handler) { Timeout = timeout, MaxResponseContentBufferSize = maxAnswerBytes };
    }

    /// <summary>A request with UserFeedback's User-Agent, which names the software and nothing about the player</summary>
    public static HttpRequestMessage NewRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.ParseAdd($"UserFeedback/{Core.Version}");
        return request;
    }

    /// <summary>Retry-After in whole minutes, at least 1, null without a usable one</summary>
    public static int? RetryMinutes(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retry = response.Headers.RetryAfter;
        TimeSpan? wait = retry?.Delta ?? (retry?.Date is DateTimeOffset date ? date - DateTimeOffset.UtcNow : null);
        if (wait == null || wait.Value.TotalDays > 2) return null;
        return Math.Max(1, (int)Math.Ceiling(wait.Value.TotalMinutes));
    }

    /// <summary>
    /// Text from a server, made safe for chat: no markup (chat understands links and fonts), no control characters,
    /// one line, at most max characters. Null when nothing is left.
    /// </summary>
    public static string? Untrusted(string? text, int max)
    {
        if (text == null) return null;

        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, "<[^>]*>", "");
        text = new string(text.Select(c => char.IsControl(c) ? ' ' : c).Where(c => c != '<' && c != '>' && c != '&').ToArray());
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length > max) text = text[..max] + "…";
        return text.Length > 0 ? text : null;
    }
}

/// <summary>
/// The JSON a server may answer with (docs/http-protocol.md), all optional. The raw values are someone else's text:
/// use <see cref="SafeId"/> and <see cref="SafeMessage"/> to show them, and check FollowUrl before linking it.
/// </summary>
public record ServerReply(string? Id, string? Message, string? FollowUrl)
{
    public string? SafeId => ServerHttp.Untrusted(Id, 40);
    public string? SafeMessage => ServerHttp.Untrusted(Message, 200);

    /// <summary>The fields of a JSON answer; all null for an empty or non-JSON one</summary>
    public static ServerReply Parse(string body)
    {
        try
        {
            if (JToken.Parse(body) is JObject json)
            {
                return new ServerReply(json["id"]?.ToString(), json["message"]?.ToString(), json["followUrl"]?.ToString());
            }
        }
        catch (Exception)
        {
            // Not JSON: nothing to read
        }
        return new ServerReply(null, null, null);
    }
}
