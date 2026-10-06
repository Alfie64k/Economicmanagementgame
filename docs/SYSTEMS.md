# How the systems fit together

Plain-English companion to [MODEL.md](MODEL.md) (equations) and [PLAY.md](PLAY.md) (running it).

## The loop

1. **Pick** a country (28) and either sandbox or one of 11 scenarios.
2. **A turn is one month.** **End turn ▸** (Enter) plays exactly one; Space and 1–4 run turns on a clock. Every country in the world ticks, not just yours.
3. **You plan, then play.** On the Budget, Monetary, Policies, Investment and Trade pages (and the world map) you *add actions to the turn plan*. Each is priced in political capital and validated at once, but nothing is applied until the turn is played: political capital, the news feed and the advisers only move then, so you can change your mind. The **Plan** button in the top bar lists the staged actions, lets you remove them and previews the whole plan against carrying on. Staging the same instrument again replaces the earlier entry, putting a setting back to its live value removes it, and the plan's total cost must fit in the banked political capital. Event decision popups are the one exception: they are forced, and resolve immediately.
4. **Engine order each month:** your staged plan, in order → policy/commands → supply → demand → prices and rates → labour → fiscal and debt → FX and external → sectors → society and environment → world layer (trade, contagion, AI governments) → events and politics → scoring.
5. **Feedback** arrives through the dashboard, news, advisers, events that need a choice, and the Report page (scorecard plus "why did this change?").

## The economy (per country)

* **Accounts:** GDP = C + I + G + X − M. Potential output comes from six sectors (agri, energy, manufacturing, services, finance, public) with capital, labour, human capital, TFP and public-asset quality (infrastructure, education, health, digital, R&D). Actual GDP sits within a band around potential; the gap drives inflation and unemployment.
* **Demand side:** households save more when real rates and unrest rise; firms invest less when loan rates and corporate tax rise; government spending follows your budget lines; exports follow partner demand and the real exchange rate.
* **Prices and rates:** Phillips curve with expectations and central-bank credibility. The central bank follows a Taylor rule unless you override it (moves limited to 0.5pp a month). Monetising deficits wrecks credibility.
* **Fiscal:** taxes have concave revenue responses (Laffer-style), so cuts do not pay for themselves unless rates are very high. Transfers grow with ageing. Debt costs follow the 10-year yield, which is policy rate plus a sovereign spread driven by debt, deficit, inflation, stability, reserves and contagion. Too much stress leads to an IMF programme (with conditionality that blocks tax cuts and spending rises) or default.
* **FX:** floating, managed or pegged. Pegs hold until reserves or competitiveness run out, then break.
* **Society:** three age cohorts, approval (growth, jobs, prices, inequality, taxes, services, corruption), Gini, unrest, political capital. Elections are a logistic function of approval; autocracies face coup risk instead.
* **Environment:** renewables share, emissions, carbon price, climate damage that scales with global temperature (your emissions are a small share of it).

## Decisions

* **Budget:** 10 spending lines, 5 taxes. Current vs capital split matters: capital spending builds assets slowly.
* **Policies:** 40 options (labour, welfare, industrial, trade, environment, regulation, ownership...). Each costs political capital, needs a vote that can fail, takes time, and has multi-channel lagged effects. Some are mutually exclusive.
* **Investment:** 15 multi-year projects with cost overruns, plus sector subsidies. Returns are real but arrive years later.
* **Advisers:** a rule-based cabinet (finance, central bank, trade, social policy, home affairs, chief whip, chief of staff) that deliberately pulls in different directions. They are heuristics, not oracles.
* **Rate preview:** moving the interest-rate slider re-runs the model on a copy for twelve months, with noise and random events switched off, once with the rate pinned at the slider's value and once carrying on. The 3, 6 and 12-month table shows the policy's own effect. Activity and the currency respond first and inflation builds over the year, so the 3-month inflation effect is small by design. A peg overrides the pin and the panel says so.
* **Forecast:** fan charts from re-running the simulation under noise; the preview panels use the same machinery on a copy of the state.

## The world

* **Trade:** gravity model (partner GDP, distance, blocs). Your exports follow partners' demand, so a recession in a major partner hits you.
* **Contagion:** crisis scores spread to neighbours and trade partners. The USD rate and world inflation come from the simulated US and advanced economies.
* **AI governments** use the same model with a policy style (technocrat, populist, export-led, resource-dependent) and periodic reforms.
* **Diplomacy:** trade deals, tariffs (AI retaliates), sanctions, aid, alliances, climate club.

## Events and scoring

* **22 events** in `data/events.json`: conditions on live metrics, cooldowns, chains, resilience scaling and decision popups with trade-offs.
* **Scorecard:** growth, living standards, stability, sustainability, resilience. Scenarios add goals. Difficulty (Sandbox, Easy, Normal, Hard) changes how forgiving the game is.
* **Why did this change?** attributes each headline move to its drivers, so you can learn the model rather than guess.

## Safeguards and honest limits

* Deterministic for a seed (including events); a game can be replayed from seed and command log.
* Regime guards clamp the model to avoid numerical runaways: this makes it stable, not accurate.
* Parameters are **calibrated by hand for plausibility, not estimated** from data. Starting data are 2023/24 approximations (`DATA.md`).
* No banking sector or housing market; sovereign default is stylised; the regional split (nine federations) is illustrative, not statistical.
* Balance harness shows no dominant scripted strategy, but there has been **no human playtest**. Expect rough edges in feel and difficulty.
* Windows/macOS/Linux exports are produced by CI and are unsigned.
