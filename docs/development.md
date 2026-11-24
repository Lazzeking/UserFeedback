# Working on UserFeedback

## Building

- `dotnet build` builds the mod into `bin/Debug/net10.0`, the VS Code launch configs load that folder as a mod
- `dotnet build -c Release` packs it as `bin/Release/UserFeedback-v<version>.zip`
- `dotnet build -c ReleaseToMods` packs it straight into the game's `Mods` folder replacing older versions
- `dotnet build -c ReleaseToModsWithExample` same thing plus `UserFeedbackExample-v<version>.zip`

The version comes from `resources/modinfo.json` and each version's changes go in `changelog.md`

Game and data folders default to the Flatpak install, set `VINTAGE_STORY` and `VINTAGE_STORY_DATA` if yours are somewhere else

## Tests

`dotnet test tests/UserFeedback.Tests` runs the unit tests, the stuff that doesnt need a running game (reading the relay's list, the log matching, the panel rules and so on), first run downloads xunit from NuGet, the test project isnt part of the mod's build and never gets packed

## Trying it in a clean game

`tools/test-instance.sh <name> [mod ...]` starts the game with its own data folder with only this mod, the example and whatever mods you name, so your normal mods, settings and saves stay out of it (learned that one the hard way), named mods come from your `Mods` folder or get downloaded from the Mod DB with their dependencies

```sh
tools/test-instance.sh insanemonkey overhaullib ardynleveling@1.0.17
tools/test-instance.sh insanemonkey overhaullib --no-mod   # same without this mod
```

Run it with no arguments to see all options, the test instance's logs are in `~/.var/app/at.vintagestory.VintageStory/test-instances/<name>/Logs`

## Trying uploads

`tools/feedback-test-server.py` takes reports on `http://localhost:8080/userfeedback/v1`, the url the example mod uses, and saves each one in `received-reports/` (git ignores it)

It can also pretend to be the relay, its mod list with `:works` mods for "works for me", and `POST /works` checked like the relay does, `WORKS_ANSWER=429` (or any other status) to see how the mod handles getting refused, the comment at the top of the script has the details

## Where things are

| Where | What |
|---|---|
| `src/Core.cs`, `src/Commands.cs`, `src/SetupCheck.cs` | start-up and F8, the `.uf` commands, `.uf check` |
| `src/Gui` | the F8 panel, the "works for me" panel, file review, the consent question, rating icons, `ModDialog` is the base they share |
| `src/Panel` | a mod's own wording and faces and the rules theyre checked against |
| `src/Report` | reading logs and matching them to mods (`LogPatterns` has the shared rules), game details, removing personal stuff, writing a report folder |
| `src/Send` | the report as text and sending it, `ServerHttp` is what every request shares (no redirects, small answers, server text made safe), `UploadLimits` has every size limit the relay checks |
| `src/Destinations` | reading each mod's `userfeedback.json` and the relay's list (`RelayRegistry.Format.cs` is its JSON) |
| `src/Works` | "works for me", the result, the automatic log check, sending, what was already sent |
| `src/Api` | the event bus API for other mods |
| `src/ModLang.cs`, `src/Helpers.cs` | `L(...)` and `Chat(...)` for this mod's texts, and small helpers (opening files, sizes, hashes, modids, safe file writes) |
| `src/PatchAudit.cs` | finds other mods' Harmony patches on UserFeedback |
| `src/Capture` | the screenshot taken before the panel opens |
| `api/UserFeedbackApi.cs` | the file modders copy into their mod, not compiled into UserFeedback, the example mod and the tests link it |
| `schemas/userfeedback.schema.json` | the schema for `userfeedback.json`, also at `https://feedback.lazzeking.fyi/schema/userfeedback-v1.json` |
| `examples/userfeedbackexample` | a code mod using the helper file with a sample `userfeedback.json` |
| `examples/userfeedbackpatchtest` | a mod that misbehaves on purpose to check the warnings, own project, never packed, see its README |
| `tests/UserFeedback.Tests` | unit tests, own project |
| `docs` | the modders' guide, the upload protocol, the panel and this file |

## Gotchas

- write `System.Func<...>` and not `Func<...>`, the VS API has its own `Func` and the compiler gets confused (it got me twice in this mod alone)
- relay limits live in `UploadLimits`, if you change one there the relay has to change too or it starts refusing reports
- `api/UserFeedbackApi.cs` gets copied into other people's mods so it can only use the game's API, nothing from UserFeedback

## The relay

The relay (the web service between players and mod authors, forwarding reports, the authors' inbox, follow links, the public page and the "works for me" counter) is built and run separately, what the mod sends it is in [http-protocol.md](http-protocol.md), and [panel.md](panel.md) is how a mod gives its panels its own wording and faces
