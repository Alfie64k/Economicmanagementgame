# Playing the game locally

The game is a Godot 4.3 **.NET** project on top of a pure C# simulation. You need two things installed; nothing else.

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 8.0 | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| Godot | 4.3 **.NET / Mono** edition | <https://godotengine.org/download> — pick the ".NET" download, not the standard one (the standard build cannot run C#) |

## Option A — pre-built download

The `Release` GitHub Actions workflow (run manually from the Actions tab, or by pushing a `v*` tag) builds Windows, Linux and macOS exports; it completes successfully, but the exported binaries have not been run on real hardware. Open the repository's **Actions → Release → latest run → Artifacts → `builds`**, download, **unzip the whole archive** (do not run from inside the zip viewer) and run `EconomicGame.exe` (Windows), `EconomicGame.x86_64` (Linux) or the `.app` inside the zip (macOS). Keep the `.exe`/`.x86_64`, its `.pck` and the `data_EconomicGame_*` folder together in one directory; the .NET runtime is bundled, so no separate install is needed. These builds are unsigned: Windows SmartScreen and macOS Gatekeeper will warn (macOS: right-click → Open).

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
dotnet test EconGame.sln                                # 149 tests
dotnet run --project src/Sim.Cli -- smoke --years 30    # 28 countries, 30 years each, no NaNs/runaways
dotnet run --project src/Sim.Cli -- run --country GBR --years 20
```

## Troubleshooting

* **Window opens then closes with no message** — run `EconomicGame.console.exe` (Windows) or the binary from a terminal to see the error, and check `%APPDATA%\Godot\app_userdata\Economic Management Game\logs\godot.log` (Linux: `~/.local/share/godot/app_userdata/Economic Management Game/logs/`, macOS: `~/Library/Application Support/Godot/app_userdata/Economic Management Game/logs/`). A startup trace is also written to `startup.log` in the same folder, and a startup exception is shown on screen.

* **"Could not find .NET" / blank C# build** — you downloaded the standard Godot build. Get the .NET edition.
* **Build errors on first open** — run `dotnet build game/EconomicGame.csproj` once from a terminal and read the error; .NET 8 SDK must be on `PATH`.
* **Black or garbled window** — the project uses the Compatibility renderer; update GPU drivers. Run `godot --path game --rendering-driver opengl3`.
* **Globe is slow or blank** — toggle back to the 2D map (World map page); the 2D map is fully featured.
* **Window too big for the screen, or text too small** — Settings changes text size, interface scale (0.8–1.5×; the top bar wraps and the news panel folds away at large scales), a high-contrast theme, the colour-blind palette and fullscreen; the default window is 1600×900.

## Controls

| Key / action | Effect |
|---|---|
| Enter | End turn (play one month) |
| Run to ▾ (top bar) | Run the clock to the end of the quarter or year, the next election, or a year ahead; stops early for the reasons set in Settings |
| Space | Pause / resume |
| 1 – 4 | Speed (1 = slowest) |
| Ctrl+Tab or PageDown / PageUp | Next / previous page |
| Tab / Shift+Tab | Move keyboard focus (Enter or Space activates) |
| Esc | Menu (save, load, quit) |
| F1 | Help |
| F5 / F9 | Quick save / quick load (asks first) |
| F2 | Glossary (search about 80 terms; each links to the page where it is changed) |
| Map: drag / wheel / click | Pan / zoom / select country |
| Globe: drag | Rotate |

## A ten-minute test script

0. The first time you start a sandbox game a short tutorial coach walks you through one turn. Close it any time; **Help (F1) → Replay the tutorial** brings it back.
1. Main menu → **New game (sandbox)** → pick Germany (stable) or Brazil (more drama) → **Start as this country**. (For guided play use **Scenarios & campaign**, starting with "1. Your First Budget".)
2. Dashboard: note GDP growth, inflation, unemployment, debt/GDP, approval. Press **Space**, speed 3, run two years.
3. **Monetary**: drag the target-rate slider and watch the panel below it: expected inflation, unemployment, output gap and currency in 3 months, 6 months and a year, against carrying on. Add a pin to the plan, end the turn, and watch the real economy follow the same lag.
4. **Budget**: drag a tax slider on the Overview, or open a tab (Income tax, Payroll, Corporation tax, VAT, Pensions & welfare, Departments) to change the allowance, where it tapers, individual rate bands, VAT treatments or a single benefit. The footer shows an instant estimate of revenue, GDP, inequality and approval, and the preview panel shows the projected path before you commit. **Add** one cut and one spending rise **to the plan**, then open **Plan** in the top bar to review it. Nothing has happened yet: political capital, the news feed and adviser notes stay put.
5. **Policies**: add one to the plan; it needs political capital and a legislative vote, both resolved when you end the turn.
6. Press **End turn ▸** (Enter): the plan is applied first, then the economy moves a month, and the news feed and adviser notes update. Running the clock with Space or 1–4 applies whatever is staged on its first month.
7. **Cabinet**: read the advisers' cards. If one carries a suggested move, try **Preview** and then **Add to plan**; snooze a note you disagree with. **Rankings**: pick a rival by clicking its bar. **Journal**: see what you did and how the measures moved afterwards. Use **Run to ▾ → End of year**: the clock stops on the date (or earlier, with the reason in the top bar) and a year in review appears.
8. **Forecast**: fan chart of the next ten years; compare with and without your changes.
9. **World map**: change the overlay, click a partner, try a trade deal.
10. Let a crisis event pop up; choose between options. Open **Report** for the scorecard and the "why did this change?" attribution.
11. F5 quick-saves. Esc → **Saves…** to save under a name, then Quit to main menu → **Continue** (the newest save) or **Saves…** to pick any: three autosaves roll over each January, plus your named and quick saves.

Please note anything that feels broken, unreadable or boring; this build has had automated testing only, no human playtest.
