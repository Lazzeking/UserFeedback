## Version 0.1.0 (unreleased)

- F8 opens a feedback panel, after taking a screenshot of the game
- The panel guesses the mod from what you look at or hold
- The panel pauses singleplayer games, with a "Pause game" button like the handbook's
- Reports are saved in `VintagestoryData/UserFeedback`
- Reports include game details and the log lines about the chosen mods, optionally the complete logs, cleaned of personal details
- The panel sums up what a report will hold
- Send opens a prefilled GitHub issue, copies the report for the author's page, or uploads it to the author's site after asking once
- Show report saves what would be sent into a preview folder
- Client only, works on servers without the mod
- Mods choose where their reports go with `config/userfeedback.json`
- Reports can name other mods involved
- Rating buttons show coloured face icons
- `.uf destinations` lists the files found and their mistakes
