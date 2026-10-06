# Changelog

## Unreleased
* Country select: the chosen country is spotlighted in a card above the list (silhouette, tags, headline stats, difficulty, top strengths and challenges); the right panel shows the full grouped data. A country hidden by the filters stays spotlighted, and Up/Down/Enter work in the list.
* Consistent interaction states across the UI: hover, selected, selected-and-hovered, pressed and keyboard focus are distinct, so the current page, row, chip and tile are always marked and the item under the cursor gets a further cue (fixes highlights that never rendered on flat buttons).
* Map: the hover tooltip now draws above the country polygons and the minimap; hovered countries get an outline and a pointer cursor.
* Turn plan: actions are staged and applied only when the turn is played. New End turn button (Enter), a Plan tray with per-item cost, removal and a combined 5-year preview, and a political-capital bar that shows the pending spend and the level after regeneration. Political capital, the news feed and adviser notes now change only when the turn is played. Replace-by-key staging, no-op detection, cumulative affordability and plan-aware previews are covered by tests.
* Monetary: the rate slider now previews its effect on inflation, unemployment, the output gap and the currency at 3 months, 6 months and 1 year against carrying on, with a chart, time-to-target and the political-capital price. Runs off the main thread with a 250 ms debounce. Also fixes the Monetary page overflowing the window at 1600×900 and the slider range for high-rate countries.
* Budget: a tabbed tax-and-benefit editor. Income tax (personal allowance, taper start and rate, up to eight bands, indexation) with a marginal-rate chart that shows the 60% allowance-taper hump and benefit withdrawal, take-home table and a tenth-by-tenth gain-and-loss chart; payroll contributions; corporation tax (main, small-profits, expensing); VAT (standard, reduced, seven category treatments, who pays); and pensions and seven individual benefits with their own spending, first-year GDP effect and supply-side effects. Real 2024/25 statutory starting codes for 28 countries (approximate, labelled). Benefits are spent differently (unemployment and means-tested support lift demand most, pensions least), tax cuts are weighted by who gains, labour supply, natural unemployment, investment, Gini, poverty and approval respond. Unchanged codes reproduce the previous model bit for bit; old saves are migrated without jumps. Four new balance probes (tax-cutter, welfare-state, fiscal-drag, consumption-shift) find no dominant strategy.
* Keys: Tab / Shift+Tab move keyboard focus; Ctrl+Tab, PageDown and PageUp change page.

## 1.0.0-rc1
* Deterministic C# simulation core: 28 countries, six-sector macro model, fiscal, monetary, trade, demography, society and environment.
* Command pattern, 40 policies, 15 projects, advisers, Monte-Carlo fan charts and what-if previews.
* World layer: gravity trade, contagion, AI governments, diplomacy, climate club.
* Events, crises, decision popups, IMF programmes, defaults, elections and coups.
* Scoring, attribution, 11 scenarios, difficulty levels, balance harness.
* Godot 4.3 UI: country select, dashboard, budget, monetary, policies, investment, sectors, trade, society, forecast, report.
* Navigable world: 2D map with overlays, flows, pins, regional drill-down and a 3D globe.
* Accessibility (text scale, colour-blind palette, keyboard shortcuts), synthesised UI audio, atomic autosave, opt-in local diagnostics, export presets and release workflow.
