# UserFeedback Patch Test

A mod that misbehaves on purpose to check UserFeedback's defences against other mods, **never ship it and never install it in a real game**, its the evil twin

## What it does

- **patches UserFeedback**, a Harmony postfix on `ReportText.Title` that adds `[patched by test mod]` to GitHub issue titles
- **adds oversized data under another mod's modid**, 20 KB under `userfeedbackexample` through `userfeedback:collect`, the limit is 16 KB per mod
- **adds small data under its own modid** which should be kept as usual

It finds UserFeedback's code by name at runtime like a real foreign mod would, so it doesnt reference `UserFeedback.dll`

## Trying it

```sh
dotnet build examples/userfeedbackpatchtest
tools/test-instance.sh patchtest examples/userfeedbackpatchtest/bin/Debug/net10.0
```

The first line builds a mod folder in `bin/Debug/net10.0`, the second starts a separate test game with UserFeedback, the example mod and this one without touching your normal mods

In game check that

1. **the panel warns**, press F8 and pick either kind, the top of the panel says "Other mods change how UserFeedback works: UserFeedback Patch Test (userfeedbackpatchtest)"
2. **the patch is listed**, `.uf destinations` ends with `! UserFeedback Patch Test (userfeedbackpatchtest) changes UserFeedback's code with Harmony: ReportText.Title` and `client-main.log` says the same every time the panel opens
3. **oversized data is left out**
   1. press F8, pick **Something's broken** and choose **UserFeedback Example** as the mod
   2. mark **UserFeedback Patch Test** as involved
   3. the summary lists "data from userfeedbackpatchtest" but not userfeedbackexample
   4. click **Show report**, in `report.json` `ModData` only has `userfeedbackpatchtest` and `LeftOut` has `ModData.userfeedbackexample`, `client-main.log` has "The data added for userfeedbackexample is over 16 KB"
4. **the title is patched**, with a GitHub destination the issue title starts with `[patched by test mod]`
