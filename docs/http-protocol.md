# Receiving UserFeedback reports over HTTP

A mod can let players upload their reports straight to a server by listing an `http` destination in its `assets/<modid>/config/userfeedback.json`:

```json
{ "destinations": [ { "type": "http", "url": "https://example.com/userfeedback/v1" } ] }
```

The url must be `https://`. `http://localhost` also works, for trying a server on your own computer.

The first time a player sends to a site, UserFeedback asks them to confirm that their report goes to it.

## The request

One `POST` to the url, as `multipart/form-data`:

| Field | File name | Type | Always there |
|---|---|---|---|
| `report` | `report.json` | `application/json` | yes |
| `screenshot` | `screenshot.png` | `image/png` | no, the player can leave it out |
| `log` | `log.txt` | `text/plain` | no, the log lines about the reported mods |
| `logs` | `logs.zip` | `application/zip` | no, the player's complete logs, only if they chose to send them |

Headers:

- `User-Agent: UserFeedback/<version>`
- `X-UserFeedback-Mod: <modid>`, the mod the report is about. It is empty when the player wasn't sure which mod it was.

UserFeedback has already replaced the player's name, home folder, server address, IP addresses, emails and anything that looks like a password or token with placeholders in `log.txt`, `logs.zip` and the system details.

### report.json

```json
{
  "FormatVersion": 1,
  "CreatedUtc": "2026-11-11T10:31:44.123Z",
  "ModId": "butchering", "ModName": "Butchering", "ModVersion": "1.2.3",
  "AlsoInvolved": [ { "ModId": "carryon", "Name": "Carry On", "Version": "1.9.0" } ],
  "Rating": "bad",
  "Text": "The skinning knife disappears when ...",
  "GameVersion": "1.22.7",
  "Singleplayer": true,
  "LookingAt": "game:soil-medium-none",
  "Holding": "butchering:skinningknife-flint",
  "HasScreenshot": true,
  "Context": {
    "Position": "120, 115, -340",
    "Date": "12. May, Year 1, 14:30",
    "Season": "Spring",
    "Temperature": 14.5, "Rainfall": 0.6,
    "GameMode": "Survival",
    "Language": "en",
    "Mods": [ "game@1.22.7", "butchering@1.2.3" ],
    "System": { "Operating System": "Windows 11", "Graphics Card Renderer": "..." }
  },
  "LogEntries": 14,
  "HasFullLogs": false,
  "Destination": { "Type": "http", "Target": "https://example.com/userfeedback/v1" }
}
```

- `Rating` is `great`, `good`, `meh`, `bad` or `null`.
- `ModId` and the other mod fields are `null` when the player wasn't sure which mod it was.
- `Context` is `null` when the player chose not to include game details.
- `Position` is relative to the world spawn, the way the game shows coordinates. `Date`, `Season` and the climate are the in-game ones.
- Fields can be added in newer versions, so ignore ones you don't know. `FormatVersion` only changes when existing fields change meaning.

## The answer

| Status | What the player sees |
|---|---|
| `2xx` | "Report sent". If the body is JSON with an `id`, it is shown as the report's reference. |
| `403` or `404` | "This site doesn't accept reports for this mod" |
| `413` | "The report is too large, try without the full logs" |
| `429` | "Too many reports, try again later" |
| anything else | "The site couldn't take the report (error <status>)" |

For any status, a JSON body with a `message` is shown to the player next to it:

```json
{ "id": "A7F3", "message": "Thanks, we read every report." }
```

UserFeedback waits 30 seconds for an answer. Whatever the answer, the report stays in the player's `VintagestoryData/UserFeedback` folder.

## Things a server should do

- **Check the mod.** Accept only the modids you handle (`X-UserFeedback-Mod` and `ModId` in `report.json`), and answer `403` otherwise.
- **Limit by IP.** Limit how many reports a single IP can send, and answer `429` when it is over.
- **Cap the size.** For example 10 MB a request, answering `413` above it.
- **Treat everything as untrusted.** Anyone can post to the url, so don't render the text or logs as HTML.
- **Say what you keep.** Tell players what you store and for how long. A report can still hold things the player wrote themselves.

`tools/feedback-test-server.py` is a server for trying this locally. It prints what it receives and saves it in a folder.
