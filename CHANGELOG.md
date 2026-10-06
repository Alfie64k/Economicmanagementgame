# Changelog

## Unreleased
* Country select: the chosen country is spotlighted in a card above the list (silhouette, tags, headline stats, difficulty, top strengths and challenges); the right panel shows the full grouped data. A country hidden by the filters stays spotlighted, and Up/Down/Enter work in the list.
* Consistent interaction states across the UI: hover, selected, selected-and-hovered, pressed and keyboard focus are distinct, so the current page, row, chip and tile are always marked and the item under the cursor gets a further cue (fixes highlights that never rendered on flat buttons).
* Map: the hover tooltip now draws above the country polygons and the minimap; hovered countries get an outline and a pointer cursor.
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
