# Economic Country Management Game — End-to-End Plan

## Context
Greenfield repo (`alfie64k/economicmanagementgame`, no commits). Goal: a desktop game where the player picks a country, then runs its economy: fiscal/monetary/trade/industrial policy, investment allocation, politics, shocks — ending in a navigable world (2D map first, 3D globe later).

Decisions made: **Desktop engine (Godot)**, **deep DSGE/CGE-style model**, **2D map first then globe**.

## Key architecture decisions
- **Engine: Godot 4 (.NET/C#)** over Unity: free/open source, text-based scenes (clean git diffs), strong 2D/UI, light CI. C# gives access to MathNet.Numerics for solvers.
- **Simulation core is a pure C# class library (`Sim.Core`) with zero Godot dependency.** Headless, deterministic (seeded RNG, fixed-step), unit-testable with `dotnet test`, runnable in batch for balancing. Godot is a thin view/controller over it.
- **Model design (deep, but tractable):**
  - Monthly tick, quarterly reporting. Multi-country: every country simulated; AI countries use the same model with simpler policy agents.
  - Real economy: multi-sector CGE-lite (agri, energy, manufacturing, services, finance, public) with input-output matrix, Cobb-Douglas/CES production, capital accumulation, labour by skill, TFP growth driven by R&D/education/institutions.
  - Demand/macro: DSGE-inspired IS curve, Phillips curve, Taylor-rule central bank (player can override), fiscal multipliers, debt dynamics (r−g), sovereign spreads from debt/inflation/political risk.
  - External: bilateral trade (gravity model), exchange rates (UIP + current-account pressure), FDI, capital flows, commodity prices, sanctions/tariffs.
  - Society/politics: demographics (cohort model), inequality (Gini), approval, coalition/party support, elections or regime-stability by government type, corruption, unrest.
  - Environment: emissions, energy mix, climate damage, transition cost.
  - Solver: run discrete-time simulation with explicit lagged equations plus a per-tick market-clearing iteration (tâtonnement) for sectors/trade; avoid full rational-expectations solve (unfriendly to real-time play). Document each equation in `docs/model/`.
- **Data-driven:** countries, sectors, policies, events, techs in JSON/TOML under `data/`; seeded from World Bank / IMF / OECD / UN / Penn World Table (build a one-off `tools/data_import` script; commit processed snapshots, not raw downloads, with source/licence notes).
- **Save/load:** versioned JSON/MessagePack snapshot of sim state; replay via seed + command log (enables debugging and balance regression).
- **Command pattern:** all player actions are serialisable `PolicyCommand`s applied at tick boundaries.

## Repo layout
```
/docs            design, model equations, data sources, ADRs
/data            countries/, sectors/, policies/, events/ (JSON)
/src/Sim.Core    deterministic simulation library
/src/Sim.Tests   unit + property + regression tests
/src/Sim.Cli     headless runner / balancing harness
/game            Godot project (scenes, UI, map, autoloads)
/tools           data import, map-prep scripts
/.github         CI (build, test, export)
```

## Stages

### Stage 0 — Foundations (week 1–2)
- Game design doc (pillars, player fantasy, win/lose conditions, difficulty modes: sandbox, scenario, campaign).
- Repo scaffold, Godot 4 .NET project, `Sim.Core`/`Tests` solution, CI (build + tests + Godot export on tag), code style, ADRs.
- Define core data schemas: `Country`, `Sector`, `Policy`, `Event`, `StateSnapshot`.
- **Exit:** CI green on empty vertical slice; GDD + schema docs merged.

### Stage 1 — Framework + UI shell (week 2–5)
- Game state manager, tick scheduler (pause/1×/2×/4×), command bus, event bus, save/load skeleton.
- UI framework: theme, design tokens, window layout (top bar: date/speed/treasury; left nav; main panel; right news/advisor feed), tooltip system, number formatting (currency = country's own, GBP default for neutral displays).
- Screens as stubs wired to mock data: Main Menu, **Country Select** (list + filters + key stats + difficulty), Dashboard, Budget, Policies, Sectors, Trade, Society, Settings.
- Reusable widgets: time-series chart, stacked area, sankey/flow, sliders with projected-impact preview, table with sort/filter.
- **Exit:** can launch, pick a country from a list, land on a dashboard with fake numbers, save/load a stub.

### Stage 2 — Data pipeline + country roster (week 4–7, overlaps)
- Import scripts for GDP (sectoral), population/age, debt, rates, trade matrix, inflation, unemployment, Gini, emissions, energy mix.
- Initial roster ~20 countries spanning archetypes (US, UK, China, Germany, India, Brazil, Nigeria, Japan, Russia, Saudi Arabia, Singapore, Argentina, etc.), scaled to ~60 then ~190 later.
- Calibration sheets and start-date scenarios (e.g. 2024 baseline; historical later).
- **Exit:** every roster country loads into a valid `StateSnapshot` that passes sanity invariants (accounts balance, shares sum to 1).

### Stage 3 — Core economic engine v1 (week 5–10)
- National accounts identity (Y = C + I + G + NX), sector production + labour/capital, inflation, unemployment, central bank, government budget and debt, simple FX.
- Single-country closed→open economy; policy levers: tax rates (income/corp/VAT/tariff), spending by category, policy rate, min wage.
- Tests: accounting identities, steady-state convergence, impulse-response checks (e.g. +1pp rate → lower inflation after lag), property tests for non-negativity.
- Headless CLI to run 50-year sims and plot outputs for calibration.
- **Exit:** a plausible 30-year run for 5 test countries (no blow-ups, stylised facts hold). UI dashboard now shows real numbers.

### Stage 4 — Player decisions: investment & policy (week 9–14)
- **Investment allocator:** public capex across infrastructure, education, health, defence, R&D, energy, housing, digital, with returns, lags, depreciation and project queues (multi-year builds, cost overruns).
- **Policy tree:** fiscal, monetary, trade, labour, industrial, welfare/pensions, immigration, environment, regulation, privatisation/nationalisation. Each policy: cost, time to implement, political capital cost, lagged multi-channel effects, tooltips showing mechanism.
- Advisor system (finance minister, central banker, trade, social) giving conflicting recommendations; forecast panel with fan charts.
- Political capital + approval constraints; legislature/coalition friction.
- **Exit:** a full playable single-country loop with decisions that visibly matter; first internal playtest.

### Stage 5 — Multi-country world sim (week 13–18)
- Simulate all roster countries concurrently (LOD: full model for player + neighbours/major partners, reduced model for the rest, upgraded on demand).
- Bilateral trade (gravity), FX, capital flows, commodity markets, contagion (sovereign debt, banking crises), global growth/oil/interest-rate cycles.
- AI policy agents by archetype (technocrat, populist, export-led, resource-dependent).
- Diplomacy lite: trade deals, tariffs/retaliation, sanctions, aid, IMF programmes, alliances.
- Performance budget: full world tick < 50 ms; multithreaded per-country where independent.
- **Exit:** player's choices ripple into partners and back.

### Stage 6 — Events, risk & society depth (week 16–21)
- Event engine (data-driven, conditional, chained): pandemics, wars, commodity shocks, financial crises, natural disasters, tech breakthroughs, scandals, strikes, coups.
- Society layer: demographics, inequality, unrest, elections / regime stability, corruption, brain drain.
- Climate/energy transition layer, with carbon pricing and stranded assets.
- Crisis-management UX (decision popups with trade-offs, not a single right answer).
- **Exit:** 20-year runs feel varied; shocks create meaningful dilemmas.

### Stage 7 — 2D navigable world map (week 18–23)
- Natural Earth shapefiles → simplified polygons (build step in `tools/`), country + sub-national regions for the largest economies.
- Pan/zoom, hover/click, choropleth overlays (GDP growth, inflation, debt/GDP, unemployment, unrest, trade intensity), trade-flow arrows, event pins, region drill-down to a regional economy panel, search + bookmarks, minimap.
- Map as the primary navigation hub: click any country → read-only briefing → diplomacy actions.
- **Exit:** the map replaces list-only navigation; 60 fps at full-world zoom.

### Stage 8 — Balance, scoring, progression (week 22–27)
- Scoring: composite index (growth, living standards, stability, sustainability, resilience) + scenario-specific goals.
- Difficulty levels, tutorial campaign (3 guided scenarios), historical scenarios (e.g. 1990 transitions, 2008 GFC, post-COVID).
- Automated balance harness: run thousands of seeded games with scripted strategies (austerity, stimulus, laissez-faire, industrial); flag dominant strategies and runaway states.
- Explainability: "why did this change?" drill-down for every headline metric (attribution of drivers).
- **Exit:** no dominant strategy; every starting archetype winnable and distinct.

### Stage 9 — 3D globe view (week 26–30)
- Sphere mesh + country-ID texture lookup shader, same overlay data as 2D, atmosphere, trade-route arcs, smooth camera, toggle between 2D/globe.
- Fallbacks for low-end GPUs; keep 2D fully featured.
- **Exit:** feature parity of overlays/navigation with 2D.

### Stage 10 — Polish, audio, QA, release (week 29–36)
- Art pass, animation, UI audio, ambient music, news ticker, accessibility (colour-blind palettes, scalable text, keyboard nav), localisation scaffolding (British English default).
- Performance profiling, memory, autosave robustness, crash reporting (opt-in).
- Closed beta; telemetry-free feedback form; economists' sanity review of model behaviour.
- Export to Windows/macOS/Linux; Steam page/itch.io build; patch pipeline.
- **Exit:** 1.0 release candidate.

### Post-1.0 backlog
Multiplayer/hot-seat, modding API (data packs + scripted events), historical campaign pack, corporate/market layer (listed firms, equity indices, bond markets), custom country creator, Steam Workshop.

## Cross-cutting workstreams
- **Testing:** unit + property tests (Sim.Tests), golden-run regression (seed → hash of key outputs), balance harness in CI nightly.
- **Docs:** every equation and parameter source documented; changelog; model validation notes.
- **Risk register:** model instability (mitigate: bounds, damping, regime guards), data licensing (use CC-BY / open sources, attribute), scope creep (stage gates, vertical slice first), performance of 190-country sim (LOD), "not fun" risk (early playtests at Stage 4, not Stage 8).
- **Legal/content:** real-world politics sensitivity — neutral tone, disputed borders handled via configurable border sets; no real leaders' likenesses.

## Milestones
| Milestone | After stage | Deliverable |
|---|---|---|
| M1 Vertical slice | 4 | One country, real model, real decisions, UI polished enough to judge fun |
| M2 World alpha | 6 | Multi-country, events, society |
| M3 Navigable world beta | 8 | 2D map, scoring, campaign |
| M4 1.0 | 10 | Globe, polish, release |

## First implementation steps (when approved)
1. Commit `docs/GDD.md`, this plan as `docs/ROADMAP.md`, `.gitignore`, `README.md`.
2. Scaffold `src/Sim.Core`, `src/Sim.Tests` (xUnit), `src/Sim.Cli`; `game/` Godot 4 .NET project referencing Sim.Core.
3. Implement `StateSnapshot` + tick loop + command bus + one trivial economy (GDP, inflation, debt) end to end.
4. Build Country Select + Dashboard screens reading from the live sim.
5. GitHub Actions: `dotnet build/test` on every push.

## Verification
- `dotnet test` green (identity, convergence, impulse-response, determinism: same seed ⇒ identical hash).
- `Sim.Cli` 50-year runs for roster countries: no NaN/negative stocks, key ratios within bounds.
- Godot: launch, select country, run 10 game-years at 4×, save/load round-trip equals state hash.
- Stage gates: playtest sessions at M1/M2/M3 with written feedback before proceeding.

## Open items to confirm during Stage 0
- Godot vs. Unity final lock (plan assumes Godot 4 .NET).
- Target start year(s) and initial roster list.
- Whether player has a head-of-state personality/role layer or is a faceless "government".
