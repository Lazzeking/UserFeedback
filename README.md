# UserFeedback

Press **F8** in game and tell a mod's author what you think of their mod, idea shamelessly stolen from Subnautica's feedback key (thanks Subnautica)

It only runs on your side so it works in singleplayer and on any server, doesnt matter if the server has it or not

## How it works

1. F8 takes a screenshot and asks what it is, **Something's broken**, **Just feedback** or **Works for me** (more on that [below](#works-for-me)), in singleplayer the game pauses while the panel is open like the handbook does
2. the panel guesses which mod you mean from what youre looking at or holding, or from an error that just happened, you can always pick another one and if you picked the wrong kind theres a button at the top to switch
3. **feedback** is a rating plus some text if you want and the screenshot, thats it
   **bug report** is what happened, other mods involved if any, and what to include, game details and the log lines about the mod are on by default, the full logs are off
4. **Send** delivers it wherever the author wants it
   - the UserFeedback relay, which passes it on without giving them your IP (asks you once first)
   - a GitHub issue, filled in and opened in your browser so you just hit submit
   - the author's web page, with the report copied so you can paste it
   - the author's own site (asks you once first too)
   - or just your computer

   **Show report** shows you exactly what would be sent without sending anything (i mostly use it to double check my own mess)

You can change F8 in the game's controls settings

## Whats in a report

Every report gets saved in `VintagestoryData/UserFeedback/` no matter what happens after

| File | Whats in it | When |
|---|---|---|
| `report.json` | your rating and text, the mods and their versions, with game details also position from spawn, date, season, climate, game mode, language, the loaded mods, and your OS, CPU, RAM and graphics card | always (game details are bug reports only) |
| `screenshot.png` | what you saw when you pressed F8, scaled down to 1920 wide if its bigger | on by default |
| `log.txt` | only the log lines about the mods in the report | with game details |
| `files.zip` | files the mod asks for, you go through them one by one before sending | with game details |
| `logs.zip` | your full logs | off by default |

Chat and audit logs never go in, your conversations are safe

If the site gives you a private link to follow your report it shows up in chat and gets saved next to the report as `follow-url.txt` with the report's ID, there you can read the author's replies, answer, or delete the report, if you write to the author about it quote the ID and not the link since anyone with the link can do what you can

If the mod has a public page on the relay the panel also asks if the author can show your report there, its off unless you turn it on and you can change your mind later from the follow page

**Privacy**, before anything gets saved this stuff gets swapped for placeholders like `<player>`

- your name and player id
- your home folder
- the server youre on
- IP addresses and emails
- anything that looks like a password or token

And the panel always tells you what the report will hold before you send it

## Settings

`ModConfig/userfeedback.json` gets created the first time you play with the mod

| Setting | Default | What it does |
|---|---|---|
| `AttachScreenshotByDefault` | `true` | if "attach a screenshot" starts on |
| `IncludeDetailsByDefault` | `true` | if "include game details and logs" starts on in a bug report (feedback never has them) |
| `MaxTextLength` | `2000` | max characters in the text, up to 5000, nobody reads past that anyway |
| `PauseInSingleplayer` | `true` | if the panel pauses singleplayer, the "Pause game" button in the panel changes it too |
| `RelayUrl` | `https://feedback.lazzeking.fyi/v1` | the relay, set it to `""` and the relay is off completely, the mod never talks to it |
| `ConsentedHosts` | `[]` | sites you already said yes to |
| `WorksOptIn` | `[]` | mods you turned "tell the author when this mod loads with or without errors" on for |

### The relay

Mod authors can register their mod at [feedback.lazzeking.fyi](https://feedback.lazzeking.fyi) and for those mods "UserFeedback relay" shows up first in "Send to", the relay checks the report and passes it on to the author (their Discord or their own server), it sees your IP like any website does but never hands it to the author

To know which mods are registered the mod downloads the relay's list at most once a day and keeps it in `VintagestoryData/UserFeedback/relay-registry-v2.json`, it gets the whole list so the relay doesnt learn what mods you have, if the list ever gets huge (i wish) its split in buckets and only the bucket of the mod you picked gets fetched, so the relay only learns "one of the hundred or so mods in there", nothing else is sent until you actually send something

### Works for me

Some authors count on the relay how many people their mod works for, for those mods **Works for me** after F8 gives you

- **the Works for me button**, one click and the author knows it works for you, once per mod version and game version
- **"Tell the author when this mod loads with or without errors"**, a switch per mod thats off until you turn it on, a few minutes after you join a world it checks the log for that mod's errors and tells the relay yes or no, only when that changed so playing every day still counts once

Both only send the mod, its version, the game version, if it worked, and if youre in singleplayer, not your mod list and nothing from the log, the relay just keeps counts, what was already sent is in `VintagestoryData/UserFeedback/works-sent.json` on your computer only

Authors love this one, its the only kind of report thats never a bug

## Commands

They run on your side so they start with a dot, `.userfeedback` or `.uf` for short

- `.uf open` opens the panel like F8, `.uf open bug`, `.uf open feedback` or `.uf open works` skip the question
- `.uf folder` opens the folder with your saved reports
- `.uf destinations` lists where each mod's reports can go and which of your mods the relay takes (mostly for mod authors)
- `.uf check <modid>` shows what a bug report about that mod would hold right now (also for mod authors)
- `.uf workscheck` runs the "works for me" check now instead of waiting 3 minutes, for the mods you turned on, stuff already counted isnt sent again and what it found goes in the client log

## More

- **mod authors**, [docs/for-modders.md](docs/for-modders.md) is how to get reports about your mod, add your own files and your mod's state from code
- **running your own server for uploads**, [docs/http-protocol.md](docs/http-protocol.md)
- **building and testing this mod**, [docs/development.md](docs/development.md)
