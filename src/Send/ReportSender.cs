using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

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

    private static readonly HttpClient Http = CreateClient();

    public static void Send(ICoreClientAPI capi, DestinationConfig destination, FeedbackReport report, string folder)
    {
        try
        {
            switch (destination.Type)
            {
                case DestinationRegistry.GitHub:
                    SendToGitHub(capi, destination, report, folder);
                    break;
                case DestinationRegistry.WebPage:
                    SendToWebPage(capi, destination, report, folder);
                    break;
                case DestinationRegistry.Http:
                    SendToHttp(capi, destination, report, folder);
                    break;
            }
        }
        catch (Exception e)
        {
            capi.Logger.Error("[{0}] Could not send the report to {1}: {2}", Core.ModId, destination.Target, e);
            Chat(capi, "send-failed", folder);
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
            string body = ReportText.Body(report, log, room, attachmentsNote: true);
            url = $"https://github.com/{destination.Repo}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}{labels}";
            if (url.Length <= MaxGitHubUrl) break;
        }

        Open(url);
        if (report.HasScreenshot || report.HasFullLogs || report.LogEntries > 0) ReportWriter.OpenFolder(folder);
        Chat(capi, "sent-github", destination.Repo!);
    }

    // The report goes to the clipboard and the author's page opens, the player pastes it there
    private static void SendToWebPage(ICoreClientAPI capi, DestinationConfig destination, FeedbackReport report, string folder)
    {
        capi.Input.ClipboardText = ReportText.Body(report, ReadLog(folder), MaxClipboard, attachmentsNote: true);
        Open(destination.Url!);
        if (report.HasScreenshot || report.HasFullLogs) ReportWriter.OpenFolder(folder);
        Chat(capi, "sent-webpage", new Uri(destination.Url!).Host);
    }

    // multipart/form-data as described in docs/http-protocol.md
    private static void SendToHttp(ICoreClientAPI capi, DestinationConfig destination, FeedbackReport report, string folder)
    {
        string host = new Uri(destination.Url!).Host;
        Chat(capi, "sending", host);

        Task.Run(async () =>
        {
            string message;
            try
            {
                using var form = new MultipartFormDataContent();
                AddFile(form, folder, "report.json", "report", "application/json");
                AddFile(form, folder, "screenshot.png", "screenshot", "image/png");
                AddFile(form, folder, "log.txt", "log", "text/plain");
                AddFile(form, folder, "logs.zip", "logs", "application/zip");

                using var request = new HttpRequestMessage(HttpMethod.Post, destination.Url) { Content = form };
                // Names the software, nothing about the player
                request.Headers.UserAgent.ParseAdd($"UserFeedback/{Core.Version}");
                request.Headers.Add("X-UserFeedback-Mod", report.ModId ?? "");
                using HttpResponseMessage response = await Http.SendAsync(request);
                message = await Describe(response, host);
            }
            catch (TaskCanceledException)
            {
                message = Lang.Get($"{Core.ModId}:http-timeout", host);
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[{0}] Uploading the report to {1} failed: {2}", Core.ModId, destination.Url, e.Message);
                message = Lang.Get($"{Core.ModId}:http-unreachable", host);
            }

            capi.Event.EnqueueMainThreadTask(() =>
            {
                capi.ShowChatMessage(message);
                capi.ShowChatMessage(Lang.Get($"{Core.ModId}:kept-in", folder));
            }, Core.ModId + "-sent");
        });
    }

    private static async Task<string> Describe(HttpResponseMessage response, string host)
    {
        string body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            string? id = TryJson(body, "id");
            return id != null ? Lang.Get($"{Core.ModId}:http-sent-id", host, id) : Lang.Get($"{Core.ModId}:http-sent", host);
        }

        string? reason = TryJson(body, "message");
        string key = response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => "http-rate-limited",
            HttpStatusCode.Forbidden or HttpStatusCode.NotFound => "http-not-accepted",
            HttpStatusCode.RequestEntityTooLarge => "http-too-large",
            _ => "http-error",
        };
        string text = Lang.Get($"{Core.ModId}:{key}", host, (int)response.StatusCode);
        return reason != null ? $"{text} ({reason})" : text;
    }

    private static string? TryJson(string body, string field)
    {
        try
        {
            return JObject.Parse(body)[field]?.ToString();
        }
        catch (Exception)
        {
            return null;
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
        string path = Path.Combine(folder, "log.txt");
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    private static void Open(string url)
    {
        // UseShellExecute opens it in the default browser
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static void Chat(ICoreClientAPI capi, string key, params object[] args)
    {
        capi.ShowChatMessage(Lang.Get($"{Core.ModId}:{key}", args));
    }

    private static HttpClient CreateClient() => new() { Timeout = TimeSpan.FromSeconds(30) };
}
