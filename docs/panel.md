# A mod's own panel

A mod can change the words on its bug report and feedback report and swap the four faces, all from its own `config/userfeedback.json`, nothing set means the normal UserFeedback panel. Underneath its still a normal bug or feedback report, same screenshot, logs, destinations and sending, and a rating is still `great`, `good`, `meh` or `bad` in `report.json` so the relay and the author's tools keep working, the player just sees the mod's labels and drawings

When the picked mod changed anything the bottom right of the window says **via UserFeedback** (gotta get some credit somewhere)

## The file

Anything you leave out stays the normal text, a field that breaks a rule gets dropped too, `.uf destinations` lists it and the normal text is used there

```json
"panel": {
  "title": "Polymorph",
  "titleKey": "polymorph:panel-title",
  "feedback": {
    "heading": "How are the biomes?",
    "prompt": "How is Polymorph treating you?",
    "text": "What should change?",
    "switch": "It's a bug"
  },
  "bug": {
    "heading": "Something broke",
    "text": "What broke, and what did you expect?",
    "switch": "It's just feedback",
    "involved": "Other mods involved?",
    "details": "Include game details and logs",
    "fullLogs": "Include my complete log files",
    "review": "Review files"
  },
  "whichMod": "Which mod is this about?",
  "screenshot": "Attach a screenshot",
  "sendTo": "Send to",
  "publicAllowed": "The author may use it on the mod's public page",
  "cancel": "Cancel",
  "preview": "Show report",
  "send": "Send",
  "ratings": {
    "great": { "label": "Love them", "labelKey": "polymorph:face-great", "icon": "polymorph:textures/icons/great.svg" },
    "good": { "label": "Pretty good" },
    "meh": { "label": "Mixed" },
    "bad": { "label": "Not for me", "icon": "polymorph:textures/icons/bad.svg" }
  }
}
```

- `title` is the window title, `feedback` and `bug` are each kind of report, `prompt` is the line above the faces ("How is it going?"), `text` the line above the text box and `switch` the button that flips to the other kind
- a bug report has no faces and no `prompt`, `involved`, `details`, `fullLogs` and `review` are bug only
- the lines both kinds share (`whichMod`, `screenshot`, `sendTo`, the buttons) go next to `title`
- `ratings` are the four faces and only those four, `great`, `good`, `meh`, `bad`, `label` replaces the name under the face and `icon` replaces the drawing with an svg from your own assets like `yourmodid:textures/icons/great.svg`, if the file's missing the normal face stays (my drawings arent that bad i promise), the name keeps the face's colour
- every text can have a lang key next to it (`titleKey`, `headingKey`, `labelKey`...) in your own domain, used when the player's language has it, `.uf destinations` checks the English text
- lengths, title 40 characters, a heading 48, a button 32, a face label 16, everything else 70, they need to fit on one line
- a file with only `"panel"` and no destinations is fine, reports go wherever the player can send that mod's reports, the relay too if the mod's listed there

The summary under the text box stays UserFeedback's so it always says what the report really holds, and so do the messages after sending
