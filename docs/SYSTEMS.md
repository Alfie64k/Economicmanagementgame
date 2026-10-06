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

* **Budget:** 10 spending lines, 5 taxes. Current vs capital split matters: capital spending builds assets slowly. Your own country also has a **tax-and-benefit code** underneath the five tax rates (below); the one-slider-per-tax view stays for broad-brush changes.
* **Tax and benefit code (the Budget tabs):** income tax (personal allowance, the income at which it tapers away, the taper rate, up to eight rate bands, threshold indexation to earnings, prices or frozen), payroll (employee lower limit, rate, upper limit and second rate; employer threshold and rate), corporation tax (main and small-profits rates, the small-profits limit, capital expensing), VAT (standard and reduced rates and how seven categories of spending are treated: zero-rated, reduced, standard or exempt) and seven benefits (state pension with age and uprating, unemployment benefit with level and duration, child benefit with an income threshold, disability benefit with eligibility strictness, housing benefit, a means-tested award with taper and work allowance, and everything else). Amounts are in your currency and move with earnings and prices; the starting code is real 2024/25 statute for each country, approximate and labelled as such.
  * *How it works.* A fixed lognormal distribution of 400 earners, calibrated to the country's Gini, is run through the schedules cell by cell. That gives average and marginal rates (including the 60% hump in a tapered allowance and the withdrawal of means-tested benefits), what each tenth of earners gains or loses, poverty and inequality. The five engine tax rates are *derived* from the code relative to its starting value, so an unchanged code reproduces the old model exactly (a test checks the state hash is bit-identical for five countries).
  * *Who spends what.* Benefits differ in how much of each extra pound is spent (unemployment ×1.30, means-tested ×1.25, housing ×1.20, disability ×1.12, child ×1.10, pensions ×0.85 of the average household) and tax cuts are weighted by who gains, so a cut that favours low earners lifts first-year demand more than an equal-cost cut for high earners. These are starting priors, not estimates.
  * *Supply side.* Marginal rates and benefit withdrawal move labour supply (extensive and intensive margins), pension age moves the labour force, generous or long unemployment benefits raise the natural rate of unemployment, employer contributions and the corporate wedge (rate after expensing) feed into hiring costs, investment and foreign direct investment. Gini, poverty and the voters directly affected feed approval.
  * *Price and limits.* Every change is priced in political capital from its own size; cuts to benefits and allowances cost half as much again, pensions the most. The Budget footer shows an instant first-order estimate (revenue, benefit spending, first-year demand, long-run output, Gini, poverty, approval, natural unemployment) before you commit and a 5-year preview afterwards.
  * *Not modelled yet:* excise, property and capital-gains taxes, tax credits and deductions beyond the allowance, behavioural response by income level (labour supply uses a single elasticity), a fat top tail of earnings, and household types. Catalogue policies "Raise retirement age" and "Universal basic income" overlap the pension-age and benefit levers; use one or the other.
* **Policies:** 40 options (labour, welfare, industrial, trade, environment, regulation, ownership...). Each costs political capital, needs a vote that can fail, takes time, and has multi-channel lagged effects. Some are mutually exclusive.
* **Investment:** 15 multi-year projects with cost overruns, plus sector subsidies. Returns are real but arrive years later.
* **Advisers:** a rule-based cabinet (finance, central bank, trade, social policy, home affairs, chief whip, chief of staff) that deliberately pulls in different directions. They are heuristics, not oracles.
* **Cabinet page:** each adviser note is a card with a severity, a stance (wants to tighten, wants support) and, where a clear remedy exists, a suggested move (for example a VAT rise when the deficit is wide). *Add to plan* stages the move, *Preview* runs it against carrying on, *Snooze* hides the note for six months. When the finance and social ministers pull opposite ways a "cabinet is divided" box says so. Notes are read from the state of the country, so they change only when a turn is played. The cabinet autopilot toggle (tax, spending and rate dials run by simple rules; policies, projects and trade stay yours) lives here and shows as an AUTOPILOT chip in the top bar.
* **Run to and auto-pause:** *Run to ▾* lets the clock run to the end of the quarter, the end of the year, the next election or twelve months ahead. Whenever the clock is running (play, or Run to) it stops for the reasons ticked in Settings: adviser alerts, a recession starting, an election three months away, a policy taking effect, a project completing, IMF programmes and debt crises, your grade falling, inflation far above target, or any event. The reason appears in the top bar. A manual End turn never stops anywhere. The watcher only reads the log and a few remembered flags, so it is not saved and cannot affect determinism.
* **Rankings:** twelve league tables across all 28 countries (overall score, GDP per head, GDP, growth, inflation, unemployment, debt, deficit, approval, Gini, stability, emissions per head), each with your rank now and a year ago (rebuilt from the recorded history), the next country above and below you, and a rival you pick with a click for a side-by-side comparison.
* **Achievements:** twenty awards (bronze, silver, gold) evaluated once a year and at the end of a run from the history, command log and news log, so they add nothing to the saved world. They count only on Normal and Hard difficulty (scenarios included). Which ones you have earned is kept in a small local profile file, separate from saves.
* **Journal and year in review:** a timeline built from the command log and the news log (no extra state is saved) of what you did and what happened, with the twelve-month change in growth, unemployment and debt next to each action. It shows what moved afterwards, not proof of what caused it. Every January a review of the year just played appears (eight measures, start against end, a verdict per measure, what you did, what happened to you); it can be switched off in Settings and any past year can be reopened from the Journal page.
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
