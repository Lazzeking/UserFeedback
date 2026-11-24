# Receiving UserFeedback reports over HTTP

A mod can have players upload reports straight to a server with an `http` destination in its `assets/<modid>/config/userfeedback.json`

```json
{ "destinations": [ { "type": "http", "url": "https://example.com/userfeedback/v1" } ] }
```

The url has to be `https://`, `http://localhost` also works for trying a server on your own pc, addresses in the player's own network (`192.168.x.x`, `10.x.x.x`, `*.local` etc) are refused so no mod can make the game post stuff to someone's router

The first time a player sends to a site they get asked if thats ok

## The request

One `POST` to the url, `multipart/form-data`

| Field | File name | Type | Always there |
|---|---|---|---|
| `report` | `report.json` | `application/json` | yes |
| `screenshot` | `screenshot.png` | `image/png` | no, the player can leave it out |
| `log` | `log.txt` | `text/plain` | no, the log lines about the reported mods |
| `logs` | `logs.zip` | `application/zip` | no, the player's full logs, only if they chose to |
| `files` | `files.zip` | `application/zip` | no, files the reported mods asked for, by their path in `VintagestoryData` |

Headers

- `User-Agent: UserFeedback/<version>`
- `X-UserFeedback-Mod: <modid>`, the mod the report is about, empty when the player wasnt sure
- `X-UserFeedback-Kind: bug` or `feedback`, same as `Kind` in `report.json` so you can route without opening the report, reports from the relay have it too, its not covered by the relay's signature though, `Kind` inside the signed `report.json` is

The player's name, home folder, server address, IP addresses, emails and anything that looks like a password or token are already swapped for placeholders in `log.txt`, `logs.zip`, `files.zip`, `ModData` and the system details

### Sizes

UserFeedback keeps every upload inside these so your server can just refuse anything bigger

| Part | Max |
|---|---|
| everything together | 10 MB, `logs.zip` then `files.zip` get left out to fit and listed in `LeftOut` |
| `screenshot.png` | 1920 pixels wide |
| `log.txt` | 2 MB, the newest entries |
| `Text` | 5000 characters |
| `AlsoInvolved` | 20 mods |
| `Context.Mods` | 1000 entries of up to 100 characters, a longer list ends with "… and N more" (modpack people, i see you) |
| `ModVersion`, `GameVersion`, involved mods' versions | 32 characters |
| `LeftOut` | 20 entries |
| `ModData` | 16 KB per mod and 64 KB total, counted the way PHP's `json_encode` writes it by default (`\/`, and `\uXXXX` for each non-ASCII character) after personal stuff is removed, a mod's data over that is left out whole and listed in `LeftOut` as `ModData.<modid>` |

### report.json

```json
{
  "FormatVersion": 1,
  "CreatedUtc": "2026-11-11T10:31:44.123Z",
  "ModId": "butchering", "ModName": "Butchering", "ModVersion": "1.2.3",
  "AlsoInvolved": [ { "ModId": "carryon", "Name": "Carry On", "Version": "1.9.0" } ],
  "Kind": "bug",
  "Rating": null,
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
  "ModData": { "butchering": { "skinningProgress": 0.4 } },
  "ExtraFiles": [ "ModConfig/butchering.json" ],
  "LeftOut": [],
  "PublicAllowed": false,
  "Destination": { "Type": "http", "Target": "https://example.com/userfeedback/v1" }
}
```

- `Kind` is `bug` or `feedback`, if its missing or something else treat it as `feedback`
- `Rating` is `great`, `good`, `meh`, `bad` or `null`, always `null` for a bug and always set for feedback, a mod can show its own labels and drawings for the four faces but the value here is always one of these four
- a bug always has `Text`, feedback never has `Context`, logs, `ModData`, `ExtraFiles` or `AlsoInvolved`
- `ModId` and the other mod fields are `null` when the player wasnt sure which mod it was
- `Context` is `null` when the player left game details out, and then `ModData` and `ExtraFiles` are empty too
- `PublicAllowed` is `true` when the player let the author use the report on the mod's public page on the relay, its only offered when the relay is picked and the mod has a public page there, so on your own server its always `false`
- `ModData` is what the reported mods added with the `userfeedback:collect` event, plus the `data` a mod sent with `userfeedback:open` for that one report (merged into its entry), by modid, any mod can write under any modid so take it as a hint and not proof
- `LeftOut` lists what was too big to send, `logs.zip`, `files.zip` or `ModData.<modid>`
- `Position` is relative to world spawn like the game shows it, `Date`, `Season` and the climate are the in-game ones
- new fields can show up in later versions so ignore the ones you dont know, `FormatVersion` only changes if existing fields change meaning

## The answer

| Status | What the player sees |
|---|---|
| `2xx` | "Report sent", if the body is JSON with an `id` its shown as the report's reference |
| `3xx` | an error, redirects arent followed |
| `403` or `404` | "This site doesn't accept reports for this mod" |
| `413` | "The report is too large, try without the full logs" |
| `429` | "Too many reports, try again later" |
| anything else | "The site couldn't take the report (error <status>)" |

With any status a JSON body with a `message` gets shown to the player next to it

```json
{ "id": "A7F3", "message": "Thanks, we read every report." }
```

A `2xx` answer can also have a `followUrl`, a private page where the player can follow the report, read your replies or delete it, the relay sends one when the author keeps reports in the relay's inbox, UserFeedback shows it in chat as a link and saves it as `follow-url.txt` in the report folder, with `Report: <id>` on a second line if theres an `id`, to keep chat short the link replaces `id` and `message` there so put whatever the player should know on that page

It has to be `https://` on the same host the report went to, 500 characters max, no `"`, `'`, `<`, `>`, `&` or spaces, otherwise its ignored, anyone with the link can see the report so make it long and random

```json
{ "id": "A7F3", "message": "Thanks!", "followUrl": "https://example.com/r/1422a006aa66…" }
```

UserFeedback waits 30 seconds for an answer, whatever happens the report stays in the player's `VintagestoryData/UserFeedback` folder

## What UserFeedback does to keep players safe

- uploads only after the player said yes to the site once, the question names the site and says itll see their IP
- doesnt follow redirects, a `3xx` is an error, the player said yes to this site and not wherever it points
- reads 64 KB of the answer at most
- a `followUrl` is only used if its `https://` on the site's own host so an answer cant send the player somewhere else
- `id` and `message` are shown as plain text, markup and line breaks removed, cut to 40 and 200 characters, with the site's name in front ("example.com says: …")
- only the `userfeedback.json` inside a mod's own files counts, another mod shipping one for your modid is ignored (and logged) so it cant steal your reports

## Things your server should do

- **check the mod**, only take the modids you handle (`X-UserFeedback-Mod` and `ModId` in `report.json`) and answer `403` otherwise
- **limit by IP**, cap how many reports one IP can send and answer `429` past that
- **cap the size**, like 10 MB a request and `413` above it
- **trust nothing**, anyone can post to the url so never render the text or logs as HTML
- **say what you keep** and for how long, a report can still have stuff the player wrote themselves

`tools/feedback-test-server.py` is a little server to try all this locally, it prints what it gets and saves it in a folder, please dont put it on the internet it checks nothing

## Getting reports through the UserFeedback relay

Instead of putting your own server in `userfeedback.json` you can let players send to the relay (`feedback.lazzeking.fyi`) and add your server there as a destination, the relay checks every upload and then posts it to you, your server never sees the player's IP and you dont have to deal with rate limits or broken uploads

Add your server on your mod's page on the relay ("Your own server"), it needs `https://` with a public hostname (not an IP) on port 443 or above 1023, the relay then shows you a **signing secret** once that starts with `ufs_`, keep it on your server and nowhere else

### The request

Exactly the same request as above so a server written for direct uploads works as is, the parts are the files after the relay checked them, the screenshot is re-encoded (only the picture is left), the log has no terminal colour codes, and the zips were looked through, the `User-Agent` is `UserFeedbackRelay (+https://feedback.lazzeking.fyi)`

It adds three headers

```
X-UserFeedback-Relay: 1
X-UserFeedback-Report: K7Q2MX9DPA
X-UserFeedback-Signature: t=1762857104,v1=5f2b…
```

- `X-UserFeedback-Report` is the relay's ID for the report, its the same when the relay sends a report again so use it to skip duplicates
- `X-UserFeedback-Signature` proves it came from the relay and nobody changed it

### Checking the signature

1. split the header into `t` (Unix time in seconds) and `v1` (64 hex characters)
2. refuse it if `t` is more than 5 minutes off your server's clock so an old request cant be replayed
3. take the sha256 of each part as lowercase hex in this order, `report`, `screenshot`, `log`, `files`, `logs`, empty string for a part that isnt there, and join them with `.`
4. compute `hmac_sha256(secret, t + "." + that joined string)` as lowercase hex, `secret` being the whole `ufs_…` text
5. compare with `v1` in constant time and refuse if they differ

PHP

```php
$secret = 'ufs_…';
parse_str(str_replace(',', '&', $_SERVER['HTTP_X_USERFEEDBACK_SIGNATURE'] ?? ''), $sig);
if (!isset($sig['t'], $sig['v1']) || abs(time() - (int) $sig['t']) > 300) { http_response_code(401); exit; }

$digests = [];
foreach (['report', 'screenshot', 'log', 'files', 'logs'] as $field) {
    $digests[] = isset($_FILES[$field]) ? hash_file('sha256', $_FILES[$field]['tmp_name']) : '';
}
$expected = hash_hmac('sha256', $sig['t'] . '.' . implode('.', $digests), $secret);
if (!hash_equals($expected, (string) $sig['v1'])) { http_response_code(401); exit; }
```

Python (Flask)

```python
import hashlib, hmac, time
from flask import request, abort

SECRET = b"ufs_..."

def check_relay_signature():
    sig = dict(p.split("=", 1) for p in request.headers.get("X-UserFeedback-Signature", "").split(",") if "=" in p)
    if "t" not in sig or "v1" not in sig or abs(time.time() - int(sig["t"])) > 300:
        abort(401)
    digests = []
    for field in ["report", "screenshot", "log", "files", "logs"]:
        f = request.files.get(field)
        digests.append(hashlib.sha256(f.read()).hexdigest() if f else "")
        if f:
            f.seek(0)
    expected = hmac.new(SECRET, (sig["t"] + "." + ".".join(digests)).encode(), hashlib.sha256).hexdigest()
    if not hmac.compare_digest(expected, sig["v1"]):
        abort(401)
```

### Your answer

| Status | What the relay does |
|---|---|
| `2xx` | delivered, the body is ignored |
| anything else, or nothing within 30 seconds | tries again after 1 minute, 5 minutes, 30 minutes, 2 hours and 12 hours, then gives up, you see each failure on your mod's page on the relay |

The relay doesnt follow redirects (a `3xx` is a failure) and reads 64 KB of your answer at most, once every destination has the report the relay deletes it so your server is where it lives from then on

### Test reports

"Send test" on the relay posts a test report, just `report.json` with `"Test": true`, a text saying its a test, and an `X-UserFeedback-Report` starting with `TEST`, its signed like any real one so it tests your signature check too, answer `2xx` and dont keep it

### Whats different with the relay

- **dont limit by IP**, every request comes from the relay, it already limits players by IP and each mod by a daily number
- **check the signature instead of the mod**, the relay only sends you your own mods' reports but checking `X-UserFeedback-Mod` doesnt hurt
- **keep the secret secret**, if it leaks remove the destination on the relay and add it again to get a new one
