# Game Design Document

## Pillars
1. **Mechanism over magic** – every number can be explained ("why did this change?").
2. **Trade-offs, no free lunch** – each lever costs money, political capital, or time, and works with lags.
3. **A living world** – every country runs the same model; your choices ripple through trade, FX and capital flows.
4. **Stable by design** – bounded, damped dynamics; no runaway states without a narrative cause.

## Player fantasy
Chancellor/finance-minister-level control of a national economy over decades: set budgets, tax, interest-rate stance (or let the central bank run), trade policy, industrial strategy and long-horizon public investment, while managing approval, coalitions, shocks and rivals.

## Core loop
Monthly tick -> review dashboard/news -> enact policy (applied at tick boundary with implementation lag) -> sim advances -> advisors, events and crises respond -> quarterly/annual reports -> score.

## Modes
- **Sandbox** – any country, no end.
- **Scenario** – fixed horizon with goals (e.g. 20 years: lift real GDP/head by 30% with debt < 90% GDP).
- **Campaign** – guided tutorials then historical scenarios.

## Win/lose
Score = weighted composite of growth, living standards, stability, sustainability, resilience. Lose conditions: sovereign default with no bailout, government collapse (approval/stability), hyperinflation.

## Units
Money in LCU (billions) of the country; neutral comparisons in GBP (user default) via the FX table. Annual rates unless stated.
