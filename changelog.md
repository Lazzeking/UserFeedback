## Version 0.1.0 (not out yet)

Sooo first version, which means everything is new, here's what's in it

### For players

- F8 takes a screenshot and asks "Something's broken", "Just feedback" or "Works for me", you can change F8 in the controls and in singleplayer the game pauses while the panel is open (theres a "Pause game" button like the handbook's)
- **feedback** is a rating with four faces (great, good, meh, bad), some text and the screenshot, **a bug report** has a description, other mods involved, game details, the log lines about the mod and any files the mod asks for, plus the full logs if you want, a button switches between the two without losing what you typed and you cant send an empty one by accident
- the panel picks the mod from what youre looking at or holding, otherwise from the newest error of the last 5 minutes that names one mod
- it tells you what the report will hold, **Show report** saves exactly what would be sent into a preview folder, and you can open or drop every file a mod asks for before sending
- every report is saved in `VintagestoryData/UserFeedback` no matter what
- **Send** opens a filled in GitHub issue, copies the report for the author's own page, or uploads it to the author's site or the relay, asking once per site
- if a site gives a private follow link it shows in chat and gets saved as `follow-url.txt` with the report's ID, there you can follow the report, read replies or delete it
- "The author may use it on the mod's public page", off by default and only shown when the relay is picked and the mod has a public page
- **Works for me**, one click tells the author through the relay that the mod works for you, once per mod version and game version, theres also a switch per mod (off by default) that tells the author if the mod loads with or without errors, it checks the log a few minutes after you join a world and only sends when something changed, `.uf workscheck` runs it right away
- client only, works on any server

### Privacy and safety

- logs, game details, mod files and mod data get cleaned of personal stuff before anything is saved or sent
- the relay passes reports on without giving authors your IP, its list of mods is fetched at most once a day and if it ever gets huge only the part with the mod you picked is fetched, so it never learns your mod list, `RelayUrl` set to `""` in `ModConfig/userfeedback.json` turns the relay off
- uploads never go to your local network and dont follow redirects, a server's answer shows up as short plain text with the site's name, and "too many reports" tells you how long to wait
- a mod's `userfeedback.json` is only read from that mod's own files so nobody can steal another mod's reports, and files can only be asked for there and never through events
- if other mods patch UserFeedback with Harmony the panel names them and suggests checking the report with Show report (not pointing fingers, just saying)
- reports fit what servers take, screenshots 1920 wide max, `log.txt` its newest 2 MB, text 5000 characters, uploads 10 MB (full logs then mod files get dropped to fit)

### For mod authors

- `config/userfeedback.json` in your mod's assets says where reports go (a GitHub repo, a web page or your own server, each for bugs, feedback or both), it can also add your own files (text files in ModConfig, ModData and Logs), extra log prefixes with `"logMatch"`, and your own wording and faces with `"panel"`, theres a schema at `https://feedback.lazzeking.fyi/schema/userfeedback-v1.json` so your editor can autocomplete it
- events for code mods, no reference to UserFeedback needed
  - `userfeedback:collect` adds your mod's state to reports, 16 KB per mod max
  - `userfeedback:open` opens the panel with your mod picked from a button or hotkey, as bug, feedback or works for me if you want, with text the player can change and `data` for that one report
  - `userfeedback:sent` tells your mod a report about it was saved, opened in the browser, delivered or failed (never whats in it)
- `api/UserFeedbackApi.cs` wraps those events in one file you can copy in
- `.uf destinations` lists every mod's `userfeedback.json` and whats wrong with it, `.uf check <modid>` shows what a bug report about a mod would hold right now, `.uf open` opens the panel and `.uf open bug`, `feedback` or `works` skip the question
- the relay (`feedback.lazzeking.fyi`) takes reports for registered mods and forwards them to the author's Discord or server, and counts "works for me" for mods that turned it on
- `tools/feedback-test-server.py` takes uploads locally and can pretend to be the relay's list and its "works for me" endpoint
