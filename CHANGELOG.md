# Changelog

## Unreleased
* Country select: the chosen country is spotlighted in a card above the list (silhouette, tags, headline stats, difficulty, top strengths and challenges); the right panel shows the full grouped data. A country hidden by the filters stays spotlighted, and Up/Down/Enter work in the list.
* Consistent interaction states across the UI: hover, selected, selected-and-hovered, pressed and keyboard focus are distinct, so the current page, row, chip and tile are always marked and the item under the cursor gets a further cue (fixes highlights that never rendered on flat buttons).
* Map: the hover tooltip now draws above the country polygons and the minimap; hovered countries get an outline and a pointer cursor.
* Turn plan: actions are staged and applied only when the turn is played. New End turn button (Enter), a Plan tray with per-item cost, removal and a combined 5-year preview, and a political-capital bar that shows the pending spend and the level after regeneration. Political capital, the news feed and adviser notes now change only when the turn is played. Replace-by-key staging, no-op detection, cumulative affordability and plan-aware previews are covered by tests.
* Monetary: the rate slider now previews its effect on inflation, unemployment, the output gap and the currency at 3 months, 6 months and 1 year against carrying on, with a chart, time-to-target and the political-capital price. Runs off the main thread with a 250 ms debounce. Also fixes the Monetary page overflowing the window at 1600×900 and the slider range for high-rate countries.
* Budget: a tabbed tax-and-benefit editor. Income tax (personal allowance, taper start and rate, up to eight bands, indexation) with a marginal-rate chart that shows the 60% allowance-taper hump and benefit withdrawal, take-home table and a tenth-by-tenth gain-and-loss chart; payroll contributions; corporation tax (main, small-profits, expensing); VAT (standard, reduced, seven category treatments, who pays); and pensions and seven individual benefits with their own spending, first-year GDP effect and supply-side effects. Real 2024/25 statutory starting codes for 28 countries (approximate, labelled). Benefits are spent differently (unemployment and means-tested support lift demand most, pensions least), tax cuts are weighted by who gains, labour supply, natural unemployment, investment, Gini, poverty and approval respond. Unchanged codes reproduce the previous model bit for bit; old saves are migrated without jumps. Four new balance probes (tax-cutter, welfare-state, fiscal-drag, consumption-shift) find no dominant strategy.
* Keys: Tab / Shift+Tab move keyboard focus; Ctrl+Tab, PageDown and PageUp change page.
* Cabinet page: adviser notes as cards with a stance and a suggested move you can add to the plan, preview or snooze; a "cabinet is divided" box when the finance and social ministers disagree; the autopilot toggle moved here with a chip in the top bar and a count on the nav item.
* Run to ▾ (end of quarter, end of year, next election, a year ahead) and auto-pause: the running clock stops for adviser alerts, recessions, elections, policies and projects taking effect, IMF programmes, grade drops, high inflation or any event, as chosen in Settings, and says why.
* Rankings page: twelve league tables across all 28 countries with year-ago ranks, neighbours above and below, and a rival comparison.
* Journal page and year in review: a timeline of your actions and what happened with twelve-month impacts, and a review of the year just played every January (switch off in Settings); also reachable from the end-of-game screen.
* Charts: the Dashboard, Monetary and Society charts can show the last 2 years, 5 years or everything, mark what you did (▼) and what happened to you (▲) with hover read-outs, and share a crosshair so hovering one chart reads the same month on the others.
* Glossary (F2, also in Help and the menu): about 80 searchable terms in seven categories, each with how this game uses it, related terms and a link to the page where it is changed.
* Accessibility: interface scale (0.8–1.5×, whole interface, not just text), high-contrast theme, reduce-motion option, and a layout that holds together at large scales (top bar on two rows, news panel foldable with a News toggle, wrapping filter chips). The Policies filter chips and Investment project rows no longer overflow narrow windows.
* Year review: GDP per head was shown in thousands of dollars.
* Settings: check boxes now show properly (they were drawn as filled buttons with no visible tick box when unticked); new pause-trigger and year-in-review options.

## 1.0.0-rc1
* Deterministic C# simulation core: 28 countries, six-sector macro model, fiscal, monetary, trade, demography, society and environment.
* Command pattern, 40 policies, 15 projects, advisers, Monte-Carlo fan charts and what-if previews.
* World layer: gravity trade, contagion, AI governments, diplomacy, climate club.
* Events, crises, decision popups, IMF programmes, defaults, elections and coups.
* Scoring, attribution, 11 scenarios, difficulty levels, balance harness.
* Godot 4.3 UI: country select, dashboard, budget, monetary, policies, investment, sectors, trade, society, forecast, report.
* Navigable world: 2D map with overlays, flows, pins, regional drill-down and a 3D globe.
* Accessibility (text scale, colour-blind palette, keyboard shortcuts), synthesised UI audio, atomic autosave, opt-in local diagnostics, export presets and release workflow.
