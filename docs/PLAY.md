# Playing the game locally

The game is a Godot 4.3 **.NET** project on top of a pure C# simulation. You need two things installed; nothing else.

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 8.0 | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| Godot | 4.3 **.NET / Mono** edition | <https://godotengine.org/download> — pick the ".NET" download, not the standard one (the standard build cannot run C#) |

## Option A — pre-built download (if available)

The `Release` GitHub Actions workflow builds Windows, Linux and macOS exports. Open the repository's **Actions → Release → latest run → Artifacts → `builds`**, download, unzip and run `EconomicGame.exe` (Windows), `EconomicGame.x86_64` (Linux) or the `.app` inside the zip (macOS). These builds are unsigned: Windows SmartScreen and macOS Gatekeeper will warn (macOS: right-click → Open).

## Option B — run from source

```bash
git clone https://github.com/Alfie64k/Economicmanagementgame
cd Economicmanagementgame
git checkout claude/magical-curie-1f4240
```

Then either:

* **Helper script** — `tools/run.sh` (macOS/Linux) or `tools/run.ps1` (Windows PowerShell). It checks both tools are installed, runs the tests, builds and launches. Set `GODOT=/path/to/Godot` if `godot` is not on your `PATH`.
* **Godot editor** — open `game/project.godot`, let it import, click the hammer (Build) once, then press **F5**.
* **Command line** — `dotnet build game/EconomicGame.csproj && godot --path game`.

## Sanity checks without the engine

```bash
dotnet test EconGame.sln                                # 68 tests
dotnet run --project src/Sim.Cli -- smoke --years 30    # 28 countries, 30 years each, no NaNs/runaways
dotnet run --project src/Sim.Cli -- run --country GBR --years 20
```

## Troubleshooting

* **"Could not find .NET" / blank C# build** — you downloaded the standard Godot build. Get the .NET edition.
* **Build errors on first open** — run `dotnet build game/EconomicGame.csproj` once from a terminal and read the error; .NET 8 SDK must be on `PATH`.
* **Black or garbled window** — the project uses the Compatibility renderer; update GPU drivers. Run `godot --path game --rendering-driver opengl3`.
* **Globe is slow or blank** — toggle back to the 2D map (World map page); the 2D map is fully featured.
* **Window too big for the screen** — Settings changes text scale, fullscreen and the colour-blind palette; the default window is 1600×900.

## Controls

| Key / action | Effect |
|---|---|
| Space | Pause / resume |
| 1 – 4 | Speed (1 = slowest) |
| Tab | Next page |
| Esc | Menu (save, load, quit) |
| F1 | Help |
| Map: drag / wheel / click | Pan / zoom / select country |
| Globe: drag | Rotate |

## A ten-minute test script

1. Main menu → **New game (sandbox)** → pick Germany (stable) or Brazil (more drama) → **Start as this country**. (For guided play use **Scenarios & campaign**, starting with "1. Your First Budget".)
2. Dashboard: note GDP growth, inflation, unemployment, debt/GDP, approval. Press **Space**, speed 3, run two years.
3. **Monetary**: switch from rule to manual, raise the policy rate 2pp; watch inflation, FX and the output gap respond with a lag.
4. **Budget**: drag a tax slider; the preview panel shows the projected effect before you commit. Commit one cut and one spending rise.
5. **Policies**: propose one; it needs political capital and a legislative vote.
6. **Forecast**: fan chart of the next ten years; compare with and without your changes.
7. **World map**: change the overlay, click a partner, try a trade deal.
8. Let a crisis event pop up; choose between options. Open **Report** for the scorecard and the "why did this change?" attribution.
9. Esc → **Save to slot 1** → Quit to main menu → **Load slot 1** (or **Continue autosave**).

Please note anything that feels broken, unreadable or boring; this build has had automated testing only, no human playtest.
