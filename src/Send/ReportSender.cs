using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using static UserFeedback.ModLang;

namespace UserFeedback;

/// <summary>
/// Sends a saved report to the destination the player picked. Every outcome ends with a chat line,
/// and the report stays in its folder whatever happens.
/// </summary>
public static class ReportSender
{
    /// <summary>Browsers and GitHub start refusing longer links around here</summary>
    private const int MaxGitHubUrl = 8000;
    /// <summary>Plenty for a page's text box, and the log is in log.txt anyway</summary>
    private const int MaxClipboard = 30000;
    /// <summary>The relay's links are about 80 characters, this leaves room for other servers</summary>
    private const int MaxFollowUrl = 500;

    // The answer is a short JSON
    private static readonly HttpClient Http = ServerHttp.CreateClient(TimeSpan.FromSeconds(30), 64 * 1024);

    /// <param name="onStatus">For http: called on the main thread with the HTTP status of the server's answer</param>
    /// <param name="onOutcome">Called on the main thread once it's known: FeedbackEvents.Opened, Delivered or Failed</param>
    public static void Send(ICoreClientAPI capi, DestinationConfig destination, FeedbackReport report, string folder,
        Action<int>? onStatus = null, Action<string>? onOutcome = null)
    {
        try
        {
            switch (destination.Type)
            {
                case DestinationRegistry.GitHub:
                    SendToGitHub(capi, destination, report, folder);
                    onOutcome?.Invoke(FeedbackEvents.Opened);
                    break;
                case DestinationRegistry.WebPage:
                    SendToWebPage(capi, destination, report, folder);
                    onOutcome?.Invoke(FeedbackEvents.Opened);
                    break;
                case DestinationRegistry.Http:
                    SendToHttp(capi, destination, report, folder, onStatus, onOutcome);
                    break;
            }
        }
        catch (Exception e)
        {
            capi.Logger.Error("[{0}] Could not send the report to {1}: {2}", Core.ModId, destination.Target, e);
            Chat(capi, "send-failed");
            onOutcome?.Invoke(FeedbackEvents.Failed);
        }
    }

    // A prefilled "new issue" page: the player reviews it and sends it under their own GitHub account
    private static void SendToGitHub(ICoreClientAPI capi, DestinationConfig destination, FeedbackReport report, string folder)
    {
        string log = ReadLog(folder);
        string title = ReportText.Title(report);
        string labels = destination.Labels is { Count: > 0 } ? "&labels=" + Uri.EscapeDataString(string.Join(",", destination.Labels)) : "";

        // The body is shrunk until the whole link fits, escaping makes it longer than the text
        string url = "";
        for (int room = 6000; room > 500; room = room * 4 / 5)
        {
            string body = ReportText.Body(report, log, room);
            url = $"https://github.com/{destination.Repo}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}{labels}";
            if (url.Length <= MaxGitHubUrl) break;
        }

        Shell.Open(url);
        if (ReportText.Attachments(report).Count > 0) ReportWriter.OpenFolder(folder);
        Chat(capi, "sent-github", destination.Repo!);
    }

    // The report goes to the clipboard and the author's page opens, the player pastes it there
    private static void SendToWebPage(ICoreClientAPI capi, DestinationConfig destination, FeedbackReport report, string folder)
    {
        capi.Input.ClipboardText = ReportText.Body(report, ReadLog(folder), MaxClipboard);
        Shell.Open(destination.Url!);
        if (ReportText.Attachments(report).Count > 0) ReportWriter.OpenFolder(folder);
        Chat(capi, "sent-webpage", new Uri(destination.Url!).Host);
    }

    // multipart/form-data as described in docs/http-protocol.md
    private static void SendToHttp(ICoreClientAPI capi, DestinationConfig destination, FeedbackReport report, string folder,
        Action<int>? onStatus, Action<string>? onOutcome)
    {
        string host = new Uri(destination.Url!).Host;
        Chat(capi, "sending", host);

        Task.Run(async () =>
        {
            string message;
            int? status = null;
            bool sent = false;
            try
            {
                using var form = new MultipartFormDataContent();
                AddFile(form, folder, ReportFiles.Report, "report", "application/json");
                AddFile(form, folder, ReportFiles.Screenshot, "screenshot", "image/png");
                AddFile(form, folder, ReportFiles.Log, "log", "text/plain");
                AddFile(form, folder, ReportFiles.FullLogs, "logs", "application/zip");
                AddFile(form, folder, ReportFiles.Files, "files", "application/zip");

                using HttpRequestMessage request = ServerHttp.NewRequest(HttpMethod.Post, destination.Url!);
                request.Content = form;
                request.Headers.Add("X-UserFeedback-Mod", report.ModId ?? "");
                // The relay adds the same when it forwards, so a server can route without reading report.json
                request.Headers.Add("X-UserFeedback-Kind", report.Kind);
                using HttpResponseMessage response = await Http.SendAsync(request);
                status = (int)response.StatusCode;
                ServerReply reply = ServerReply.Parse(await response.Content.ReadAsStringAsync());
                sent = response.IsSuccessStatusCode;
                string? followUrl = sent ? FollowUrl(capi, reply, host) : null;
                if (followUrl != null) SaveFollowUrl(capi, folder, followUrl, reply.SafeId);
                message = Describe(response, reply, host, followUrl);
            }
            catch (TaskCanceledException)
            {
                message = L("http-timeout", host);
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[{0}] Uploading the report to {1} failed: {2}", Core.ModId, destination.Url, e.Message);
                message = L("http-unreachable", host);
            }

            capi.Event.EnqueueMainThreadTask(() =>
            {
                // One line when it worked. When it didn't, also where the report waits, so it isn't lost.
                capi.ShowChatMessage(sent ? message : message + " " + L("kept-in"));
                if (status != null) onStatus?.Invoke(status.Value);
                onOutcome?.Invoke(sent ? FeedbackEvents.Delivered : FeedbackEvents.Failed);
            }, Core.ModId + "-sent");
        });
    }

    /// <param name="followUrl">Checked by FollowUrl, safe to put in a link</param>
    private static string Describe(HttpResponseMessage response, ServerReply reply, string host, string? followUrl)
    {
        // Whatever the server wrote is someone else's text in the player's chat: plain, short, and said to be theirs
        string? id = reply.SafeId;
        string? said = reply.SafeMessage;

        string text;
        if (response.IsSuccessStatusCode)
        {
            // With a follow link the reference and the site's own message add nothing: the page has both
            if (followUrl != null) return L("http-sent-follow", host, followUrl);
            text = id != null ? L("http-sent-id", host, id) : L("http-sent", host);
        }
        else
        {
            string key = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => "http-rate-limited",
                HttpStatusCode.Forbidden or HttpStatusCode.NotFound => "http-not-accepted",
                HttpStatusCode.RequestEntityTooLarge => "http-too-large",
                _ => "http-error",
            };
            text = L(key, host, (int)response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && ServerHttp.RetryMinutes(response) is int minutes)
            {
                text = L("http-rate-limited-wait", host, minutes);
            }
        }

        return said != null ? text + "\n" + L("http-says", host, said) : text;
    }

    /// <summary>
    /// The private page where the player can follow the report, read the author's replies or delete it (followUrl in the
    /// answer, see docs/http-protocol.md). Null when there is none or it isn't safe: it must be https on the host the
    /// report went to, and have nothing VTML would read as markup, since chat shows it as a link.
    /// </summary>
    private static string? FollowUrl(ICoreClientAPI capi, ServerReply reply, string host)
    {
        string? text = reply.FollowUrl;
        if (text == null) return null;

        if (text.Length > MaxFollowUrl || !Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) || uri.UserInfo.Length > 0)
        {
            capi.Logger.Warning("[{0}] {1} answered with a followUrl that isn't https on its own host, it is ignored", Core.ModId, host);
            return null;
        }

        string url = uri.AbsoluteUri;
        if (url.IndexOfAny(new[] { '"', '\'', '<', '>', '&', ' ' }) >= 0 || url.Any(char.IsControl))
        {
            capi.Logger.Warning("[{0}] {1} answered with a followUrl that has characters chat can't show safely, it is ignored", Core.ModId, host);
            return null;
        }
        return url;
    }

    /// <summary>
    /// Keeps the follow link as follow-url.txt in the report folder, with the report's id on a second line, which chat
    /// doesn't show then: the player can quote it instead of the private link.
    /// </summary>
    private static void SaveFollowUrl(ICoreClientAPI capi, string folder, string url, string? id)
    {
        // The link stays the first line on its own
        string content = url + Environment.NewLine + (id != null ? $"Report: {id}{Environment.NewLine}" : "");
        try
        {
            File.WriteAllText(Path.Combine(folder, ReportFiles.FollowUrl), content);
        }
        catch (Exception e)
        {
            capi.Logger.Warning("[{0}] Could not save follow-url.txt: {1}", Core.ModId, e.Message);
        }
    }

    private static void AddFile(MultipartFormDataContent form, string folder, string file, string field, string type)
    {
        string path = Path.Combine(folder, file);
        if (!File.Exists(path)) return;

        var content = new ByteArrayContent(File.ReadAllBytes(path));
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(content, field, file);
    }

    private static string ReadLog(string folder)
    {
        string path = Path.Combine(folder, ReportFiles.Log);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

}
