
# UserFeedback

Press **F8** in game to tell a mod's author what you think of their mod, inspired by Subnautica's feedback key.

The mod only runs on your computer: it works in singleplayer and on any multiplayer server, whether the server has it or not.

- F8 takes a screenshot of what you see, then opens a small panel. In singleplayer it pauses the game like the handbook does, with the same "Pause game" button to turn that off. In multiplayer the game can't be paused, so it keeps running.
- The panel guesses the mod from what you look at or hold. You can pick another one.
- Pick a rating, write what happened if you like, and choose whether to attach the screenshot.
- Every report is saved in `VintagestoryData/UserFeedback/<date>_<modid>/`:
  - `report.json`: your rating and text, the mods, and game details (position from spawn, date, season, climate, game mode, language, every loaded mod and its version, your computer's OS, CPU, RAM and graphics card).
  - `screenshot.png`, if you leave it on.
  - `log.txt`: only the log lines about the mods the report is about, with their stack traces. On by default, together with the game details.
  - `logs.zip`: your complete client logs, and in singleplayer the server's too. Off by default. Chat and audit logs are never included.
- **Send** delivers it where the mod's author asked, picked in the "Send to" list:
  - a GitHub issue opens in your browser, filled in, for you to check and submit with your own account
  - the report is copied for you to paste into the author's page, which opens
  - the report is uploaded to the author's site. You're asked to confirm the first time for each site.
  - or it is only saved on your computer
- **Show report** saves exactly what would be sent into `VintagestoryData/UserFeedback/preview` and opens it, without sending anything.
- Before any log is saved, your name and player id, your home folder, the server you connect to, IP addresses, emails and anything that looks like a password or token are replaced with placeholders like `<player>`. The panel says what the report will hold before you save it.

F8 can be changed in the game's controls settings.

## Settings

Players can change these in `ModConfig/userfeedback.json`. The file is written the first time the game starts with the mod.

| Setting | Default | What it does |
|---|---|---|
| `AttachScreenshotByDefault` | `true` | Whether "attach a screenshot" starts switched on |
| `MaxTextLength` | `2000` | The most characters a report's text can have |
| `ConsentedHosts` | `[]` | The sites you agreed to upload reports to |
| `PauseInSingleplayer` | `true` | Whether the panel pauses a singleplayer world, also set by its "Pause game" button |

## Adding support for your own content

Ship `assets/<yourmodid>/config/userfeedback.json` in your mod to tell UserFeedback where reports about it can go. It is plain JSON, so your mod does not depend on UserFeedback: players without it never read the file.

```json
{
	"destinations": [
		{ "type": "github", "repo": "you/yourmod", "labels": ["feedback"] },
		{ "type": "webpage", "url": "https://example.com/yourmod/feedback", "label": "Our feedback form" },
		{ "type": "http", "url": "https://example.com/userfeedback/v1" }
	]
}
```

| Type | Needs | What the player gets |
|---|---|---|
| `github` | `repo` as `owner/repository`, optional `labels` | A prefilled issue opened in their browser, under their own GitHub account |
| `webpage` | `url` | The report copied to the clipboard and your page opened, to paste it in |
| `http` | `url` | The report uploaded to your endpoint |

- The player picks one; "Only save on this computer" is always offered too. `label` replaces the generated name in that list.
- Urls must be `https://`, except `http://localhost` for trying an endpoint on your own computer.
- The `http` upload format and the answers UserFeedback understands are in [docs/http-protocol.md](docs/http-protocol.md). `tools/feedback-test-server.py` receives reports on `http://localhost:8080/userfeedback/v1`, the url the example mod uses, to try it.
- Don't put secrets in the file, like Discord webhooks or API keys: anyone can read them in your mod's zip.
- The file must be in your mod's own asset domain, which is normally its modid.

Type `.uf destinations` in game to see every file UserFeedback found and any mistake in them. They are also written to `client-main.log`.

`examples/userfeedbackexample/` is a content mod with a sample file.

## Testing

Every command is client side, so you run it with a `.` and not a `/`: `.userfeedback`, or `.uf` for short.

- `.uf open` opens the feedback panel, like F8.
- `.uf folder` opens the folder with the saved reports.

## Building

- `dotnet build` builds the mod into `bin/Debug/net10.0`, which the VS Code launch configs load as a mod folder.
- `dotnet build -c Release` packs it as `bin/Release/UserFeedback-v<version>.zip`.
- `dotnet build -c ReleaseToMods` packs it as `UserFeedback-v<version>.zip` straight into the game's `Mods` folder, replacing older versions.
- `dotnet build -c ReleaseToModsWithExample` does the same and adds `UserFeedbackExample-v<version>.zip`.

The version comes from `resources/modinfo.json`, write each one's changes in `changelog.md`. The game and data folders default to the Flatpak install; set `VINTAGE_STORY` and `VINTAGE_STORY_DATA` to use others.

### Checking a problem report

`tools/test-instance.sh <name> [mod ...]` starts the game with its own data folder, holding only this mod, its example and the mods you name, so your normal mods, settings and saves stay out of it. Mods you name come from your `Mods` folder or are downloaded from the Mod DB, dependencies included:

```sh
tools/test-instance.sh insanemonkey overhaullib ardynleveling@1.0.17
tools/test-instance.sh insanemonkey overhaullib --no-mod   # the same without this mod
```

Run it without arguments for every option.
