# UserFeedback for mod authors

So, with UserFeedback installed players can press F8 and send you a report about your mod, they pick what it is first

- **feedback**, a rating, some text, a screenshot
- **bug report**, what happened, a screenshot, game details, the log lines about your mod and any files you ask for

`Kind` in `report.json` tells them apart, and titles start with `[Bug]` or `[Feedback (Rating)]`, this page is about where those reports go and what extra stuff you can put in them

All of it is optional and none of it makes your mod depend on UserFeedback, players without it never see any of this

- [The relay](#the-userfeedback-relay)
- [Where reports go](#where-reports-go)
- [Extra files](#extra-files)
- [More log lines](#more-log-lines)
- [Your own wording](#your-own-wording)
- [From code](#from-code)
- [Checking your setup](#checking-your-setup)

## The UserFeedback relay

The lazy way (my favourite kind of way), just getting reports through the relay doesnt need any file in your mod, verifying your modid is a token you put on your ModDB or GitHub page, you only need a `userfeedback.json` once you want more, like your own destinations, [extra files](#extra-files), [more log lines](#more-log-lines) or [your own wording](#your-own-wording)

1. log in at [feedback.lazzeking.fyi](https://feedback.lazzeking.fyi) with Discord and verify your modid
2. add a destination there, a Discord channel or your own server, your server gets the same upload as in [http-protocol.md](http-protocol.md) but signed by the relay, that page shows how to check the signature

Then "UserFeedback relay" shows up first in "Send to" for your mod, before anything from your own `userfeedback.json`, reports go to the relay, it checks them and passes them on, you never see the player's IP and the relay deals with rate limits and broken uploads so you dont have to

Some things to know

- the mod downloads the relay's list of mods at most once a day so a freshly verified mod can take up to a day to show up for players, if the list ever gets huge its split in buckets and only the picked mod's bucket is downloaded, so the relay still doesnt learn what mods a player has
- reports where the player picked "Not sure / other" cant go through the relay, it wouldnt know whose they are
- players can turn the relay off with `"RelayUrl": ""` in `ModConfig/userfeedback.json`
- `.uf destinations` shows if the relay lists your mod
- `"files"` (see [Extra files](#extra-files)) still needs your own `userfeedback.json`, works fine together with the relay

## Where reports go

Ship `assets/<yourmodid>/config/userfeedback.json` in your mod

```json
{
	"$schema": "https://feedback.lazzeking.fyi/schema/userfeedback-v1.json",
	"destinations": [
		{ "type": "github", "repo": "you/yourmod", "labels": ["bug"], "for": ["bug"] },
		{ "type": "webpage", "url": "https://example.com/yourmod/feedback", "label": "Our feedback form", "for": ["feedback"] },
		{ "type": "http", "url": "https://example.com/userfeedback/v1" }
	]
}
```

So here bugs go to GitHub issues, feedback to your form, and your own server takes both

| Type | Needs | What the player gets |
|---|---|---|
| `github` | `repo` as `owner/repository`, `labels` if you want | a prefilled issue in their browser, they check it and submit it with their own GitHub account |
| `webpage` | `url` | the report copied to the clipboard and your page opened, they paste it in |
| `http` | `url` | the report uploaded to your server, see [http-protocol.md](http-protocol.md) |

- the player picks one in "Send to", "Only save on this computer" is always there too
- `label` replaces the name shown in that list
- `for` limits a destination to `["bug"]` or `["feedback"]`, without it it takes both, players only see destinations for the kind theyre writing, want different GitHub labels for bugs and feedback? add the repo twice each with its own `labels` and `for`
- relay destinations have the same choice in the relay's dashboard and the panel only offers the relay for kinds at least one of them takes
- urls have to be `https://`, except `http://localhost` for trying your server on your own pc
- the first time a player uploads to a site they get asked to confirm
- **no secrets in the file please**, like Discord webhooks or API keys, anyone can open your zip and read them (yes people do that)
- the file has to be in your mod's own files under your modid, a copy some other mod ships for your modid is ignored so nobody can steal your reports
- addresses in the player's local network (`192.168.x.x`, `*.local` and so on) arent allowed

No file and no relay? players can still report about your mod, it just stays on their computer

## Extra files

Add `"files"` to include your own files, relative to `VintagestoryData`

```json
{ "files": ["ModConfig/yourmod.json", "ModData/yourmod/", "Logs/yourmod-*.log"] }
```

A folder means all its files, and `*` and `?` work in file names

To keep players safe only these get in

- text files (`.json`, `.txt`, `.log`, `.cfg`, `.ini`, `.yaml`, `.xml`, `.csv`, `.md` etc)
- inside `ModConfig`, `ModData` or `Logs`
- never chat or audit logs
- 50 files max, 2 MB each, 8 MB total

What happens with them

- before sending the player sees every file, which mod asked for it and how big it is, they can open each one and leave any of them out
- **name your files after your mod**, a file without your modid in the path gets flagged to the player as maybe someone elses
- personal stuff gets cleaned out the same way as the logs, then it all goes in `files.zip`

A `userfeedback.json` with only `"files"` and no destinations is fine too, the files then go wherever the player sends the report

## More log lines

A bug report has the log lines that mention your modid, your mod's name, its zip or your code's namespace, if your mod logs under some other name like its own logger prefix or a Harmony patch class in another namespace put those in `"logMatch"`

```json
{ "logMatch": ["PolyBiome", "[poly]"] }
```

- a line counts when it has the prefix, same case, and not in the middle of a longer word, `PolyBiome` matches `PolyBiomeGen.Patch` but not `MyPolyBiome`
- up to 8 prefixes, 4 to 40 characters each with at least 4 letters or digits
- prefixes that would grab everyone's lines get refused, like `Error`, `Exception`, `Server`, or the game's and .NET's namespaces like `Vintagestory` or `System.`, `.uf destinations` lists the refused ones
- the lines go in the normal log extract which the player sees before sending

The same matching is used to guess the mod when the player presses F8 right after an error, if theyre not looking at or holding something from a mod the panel picks the mod named in the newest error of the last 5 minutes (errors naming more than one mod are skipped), errors logged under your `logMatch` prefixes count too so listing your logger's name helps reports end up on your mod and not on mine, trust me i dont need more

## Your own wording

`"panel"` changes the words on the bug report and feedback panels for your mod and can swap the four faces too, leave it out and players get the normal panel, the report itself doesnt change, a rating is still `great`, `good`, `meh` or `bad`, when you change anything the bottom right of the window says "via UserFeedback"

```json
"panel": {
  "title": "Polymorph",
  "feedback": {
    "heading": "How are the biomes?",
    "prompt": "How is Polymorph treating you?",
    "text": "What should change?"
  },
  "bug": {
    "heading": "Something broke",
    "text": "What broke, and what did you expect?"
  },
  "ratings": {
    "great": { "label": "Love them", "icon": "polymorph:textures/icons/great.svg" },
    "good": { "label": "Pretty good" },
    "meh": { "label": "Mixed" },
    "bad": { "label": "Not for me" }
  }
}
```

Whatever you leave out stays the normal text, a face's `icon` is an svg in your own assets and if the file's missing the normal face stays, every text can have a lang key next to it (`titleKey`, `headingKey`, `promptKey`, `textKey`, `labelKey`...) in your own domain which is used when the player has that translation, full list and length limits are in [panel.md](panel.md) and `.uf destinations` shows your panel and anything it had to drop

## From code

There are events on the client's event bus for code mods, `userfeedback:collect`, `userfeedback:open` and `userfeedback:sent`, they work by name so your mod doesnt reference `UserFeedback.dll`, and if UserFeedback isnt installed nothing happens

### The helper file

Easiest is to copy [`api/UserFeedbackApi.cs`](../api/UserFeedbackApi.cs) into your mod, one file, only uses the game's API, copy it freely no credit needed, it wraps everything below so you cant typo an event name (i did, more than once)

```csharp
using UserFeedbackInterop;

UserFeedbackApi.OnCollect(capi, "yourmod", mine => mine.SetString("questStage", currentStage));
UserFeedbackApi.OnSent(capi, "yourmod", report => { if (report.Delivered) capi.ShowChatMessage("Thanks!"); });

// from your own button or hotkey, returns false when UserFeedback isnt installed
UserFeedbackApi.OpenFeedback(capi, "yourmod");
UserFeedbackApi.OpenBugReport(capi, "yourmod", "The recipe failed to load", data);
UserFeedbackApi.OpenWorksForMe(capi, "yourmod");
```

The example mod uses it, below are the raw events if youd rather not add the file

### Add your mod's state to reports

When the player presses F8 UserFeedback pushes `userfeedback:collect` with an empty tree, add a subtree named after your modid and it ends up in `report.json` under `ModData` (and in the GitHub or web page text), cleaned like the logs

```csharp
capi.Event.RegisterEventBusListener((string name, ref EnumHandling handling, IAttribute data) =>
{
    ITreeAttribute mine = ((ITreeAttribute)data).GetOrAddTreeAttribute("yourmod");
    mine.SetString("questStage", currentStage);
    mine.SetInt("activeMachines", machines.Count);
}, filterByEventName: "userfeedback:collect");
```

A report only keeps the data of the mods its about, the picked one and the ones marked as involved

Keep it small, **16 KB per mod** max and 64 KB per report with the picked mod first, anything over that is left out whole, logged in `client-main.log` and listed in `LeftOut` as `ModData.yourmod`, any mod on the event bus could write under your modid so reports mark this data as "unverified"

Files can only be asked for in `userfeedback.json` and not through the event, anyone on the bus could pretend to be you but `userfeedback.json` is read from your mod's own files so players know for sure who asked for what

### Open the panel from your mod

You can open the panel with your mod already picked, like from a "Report a problem" button in your GUI or your own hotkey, the example mod does it with `.example`

```csharp
if (capi.ModLoader.IsModEnabled("userfeedback"))
{
    capi.Event.PushEvent("userfeedback:open", new StringAttribute("yourmod"));
}
```

To skip the "bug or feedback?" question or put some text in for the player send a tree

```csharp
var request = new TreeAttribute();
request.SetString("modid", "yourmod");
request.SetString("kind", "bug");                          // or "feedback", e.g. for a "Rate this mod" button
request.SetString("text", $"Error while loading the recipe: {e.Message}"); // optional, the player can change it
capi.Event.PushEvent("userfeedback:open", request);
```

- **only open it when the player asks**, with a button or a hotkey, UserFeedback never pops up by itself because nobody likes popups, it ignores the event for 10 seconds after a mod last opened the panel and logs the ignored ones in `client-main.log`
- the screenshot is taken when the event arrives so it shows your GUI if its still open
- a `kind` that isnt `bug` or `feedback` is ignored and the player just gets the question
- the text is cut to the player's max length (2000 by default), dont put secrets or personal stuff in it

To attach data to just that one report, like the exception or the state of whatever failed, add a `data` tree

```csharp
var data = new TreeAttribute();
data.SetString("recipe", recipe.Code.ToString());
data.SetString("exception", e.ToString());
request["data"] = data;
```

- its added to your mod's data in that report only, next to what you add with `userfeedback:collect`, same key in both? `data` wins
- same limits as `collect`, 16 KB for your mod and 64 KB per report, over 16 KB `data` is left out, and if the two together are over 16 KB only `data` is kept
- its cleaned of personal stuff like the rest and the player sees it in **Show report** before sending
- like collected data it only goes in bug reports with game details on, and if the player picks another mod its dropped
- no files in `data`, those go in `"files"` in your `userfeedback.json`

### Know when a report about your mod was sent

After a bug or feedback report about your mod is saved or sent UserFeedback pushes `userfeedback:sent`, handy to say thanks or hide your "Report a problem" button

```csharp
capi.Event.RegisterEventBusListener((string eventName, ref EnumHandling handling, IAttribute data) =>
{
    if (data is not ITreeAttribute tree || tree.GetString("modid") != "yourmod") return;
    string kind = tree.GetString("kind");       // "bug" or "feedback"
    string outcome = tree.GetString("outcome"); // see below
}, filterByEventName: "userfeedback:sent");
```

| `outcome` | Means |
| --- | --- |
| `saved` | only kept on the player's computer |
| `opened` | the player's browser opened your GitHub issue form or page with the report in it, if they actually submit it no idea |
| `delivered` | uploaded, and your server or the relay took it |
| `failed` | couldnt be saved or the upload wasnt accepted, if it got saved its still on the player's computer |

- it doesnt carry anything from the report, no text, rating, data or files
- once per report, for the mod the report is about and not the other involved mods, for http uploads it comes when the server answers
- any mod can push an event with this name so use it for small stuff like a thank you, nothing important

### Ask for a "works for me" from your mod

If you count "works for me" on the relay (the switch on your mod's Settings page there) you can open the "Works for me" panel with your mod picked, say from a "Works for me? Tell the author" button in your settings

```csharp
var request = new TreeAttribute();
request.SetString("modid", "yourmod");
request.SetString("kind", "works");
capi.Event.PushEvent("userfeedback:open", request);
```

- this doesnt send anything, the player sees your mod's name and version and clicks "Works for me" themselves (after the relay question the first time), your mod cant send a result and cant turn on the automatic check for them, that switch is in the same panel for the player to use
- once per mod version and game version per player, after that the panel says "Counted for this version"
- if the relay doesnt list your mod as counting yet (or the player turned the relay off) the panel still opens and says why, `.uf open works` opens it by hand to try your setup
- same 10 second limit as opening the panel

### Dont bundle UserFeedback

Please dont copy UserFeedback into your mod, if all your players should have it put it under `dependencies` in your `modinfo.json`, that way theres always exactly one copy and it gets updates, one of me is plenty

## Checking your setup

- **`.uf check <yourmodid>`** in game shows what a bug report about your mod would hold right now without sending anything, destinations, if the relay takes your reports or counts "works for me", what each `files` pattern finds, how many log lines are about your mod (and how many only because of `logMatch`), and how big your `collect` data is, it also lists mistakes in your `userfeedback.json`
- **[`schemas/userfeedback.schema.json`](../schemas/userfeedback.schema.json)** is a schema for your editor, with the `"$schema"` line at the top of your file VS Code and Rider autocomplete the fields and flag typos, wrong types and texts that are too long, UserFeedback ignores the line, it cant check everything (lang keys and icons in your own domain, local network urls) so `.uf check` has the last word
- **`.uf destinations`** lists every `userfeedback.json` it found with its mistakes (also in `client-main.log`), and any mod that patches UserFeedback with Harmony
- **Show report** in the F8 panel saves exactly what would be sent into `VintagestoryData/UserFeedback/preview` without sending
- **`tools/feedback-test-server.py`** in this repo takes uploads on `http://localhost:8080/userfeedback/v1` so you can try an `http` destination, with `RELAY_MODS` set it also pretends to be the relay's list, see the top of the file
- **[`examples/userfeedbackexample/`](../examples/userfeedbackexample/)** is a small code mod with a sample `userfeedback.json` using the helper file
