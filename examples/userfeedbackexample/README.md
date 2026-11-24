# UserFeedback Example

A small mod to show how getting UserFeedback reports about your own mod works, it doesnt depend on UserFeedback, everything goes through events by name, so without UserFeedback installed the commands just say so and thats it

## What it shows

- `.example` opens the feedback panel with this mod picked so you see its own wording, bottom right says "via UserFeedback"
- `.example bug` is what a "Report this error" button would do, it logs a warning starting with `ExampleProbe` and then opens a bug report with some text the player can change and a `data` tree (a recipe code, the exception, a count) just for that report, in **Show report** the data is in `report.json` under `ModData` and the warning is in `log.txt`
- the code goes through `UserFeedbackApi.cs`, the helper file from `api/` that modders copy into their mod (here its linked so it stays up to date), it also adds `timesOpened` to its reports with `OnCollect`
- after a report about it is handled it says thanks in chat from the `userfeedback:sent` event, the message depends on the outcome, `saved`, `opened`, `delivered` or `failed`
- `assets/<modid>/config/userfeedback.json` has one destination of each type and the custom wording, the repo and urls are placeholders so please dont send me bug reports for a mod that doesnt exist
- its `"logMatch"` has `ExampleProbe` so lines with that prefix end up in its log extract even though they dont name the mod
- its `"files"` adds `ModConfig/userfeedback.json` to reports just to show extra files, a real mod would list its own config

## Trying it

From the repo root `dotnet build -c ReleaseToModsWithExample` packs it as `UserFeedbackExample-v<version>.zip` into the game's `Mods` folder next to UserFeedback, then `.uf destinations` or `.uf check userfeedbackexample` in game to see the file got read
