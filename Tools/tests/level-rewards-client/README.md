# Level reward client regressions

This isolated C# executable compiles the **real** `LevelRewardClient.cs` and
`MiniJson.cs` from `Assets/Scripts/Runtime/Net`. Only the Unity/session/network
boundaries are stubbed. Requests queue until each test supplies a JSON-backed
response, allowing deterministic checks of delayed and out-of-order callbacks.
It does not contact a server, launch Unity, or access player data.

Run on Windows with a Unity editor that includes the .NET 8 SDK and runtime:

```powershell
./Tools/tests/level-rewards-client/run.ps1 `
  -UnityEditorData 'C:/Program Files/Unity/Hub/Editor/6000.5.9f1/Editor/Data' `
  -OutputDirectory "$env:TEMP/dot-rpg-level-rewards-client"
```

When `-UnityEditorData` is omitted, the runner selects an installed editor under
`Program Files/Unity/Hub/Editor`. Build products and `results.txt` stay in the
output directory, outside the repository by default. No NuGet restore is needed.
Compilation or assertion failure produces a nonzero exit.

Coverage includes same-account high/low character switches, replaced accounts
with the same character identifier, delayed GET and all three POST operations,
inventory/wallet/autosave side effects, stale reads around a successful claim,
exact level thresholds and endpoint/body checks, claim flags and pass ownership,
failed writes, offline/null state, and invalid character-scoped response data.

This harness checks client business behavior, not Unity rendering or live server
integration. `OnlineSession` lifecycle hooks are deliberately outside its scope;
several tests switch contexts without calling `Reset` to exercise the client's
independent context guard as well.
