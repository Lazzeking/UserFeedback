# UserFeedback Example

A reference content mod for receiving UserFeedback reports about your own mod. It has no code and does not depend on UserFeedback.

## What it shows

- `assets/userfeedbackexample/config/userfeedback.json` offers one destination of each type. The repo and urls are placeholders.

## Trying it

From the repository root, `dotnet build -c ReleaseToModsWithExample` packs it as `UserFeedbackExample-v<version>.zip` into the game's `Mods` folder, next to UserFeedback. Type `.uf destinations` in game to check the file was read.
