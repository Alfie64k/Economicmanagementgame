# Economic model

Monthly time step (`dt = 1/12`), real quantities in start-year local currency, prices as an index, debt nominal. Every country runs the same engine; AI countries differ only in their policy agent. Code: `src/Sim.Core/Engine/`.

## Supply (`MacroEngine.Supply`)
* Sector potential output `V*_s = A_s K_s^α_s (L*_s h)^(1-α_s) (1 - D)`; `L*_s = λ_s · LF · (1 - u*)`; `h` human capital; `D` climate damage.
* TFP growth `g_A = g_A0 + 0.012 ln(RnD) + 0.010 ln(Infra) + 0.015 ln(h) + modifiers - unrest drag`; digital capital adds extra to services and finance.
* Public asset indices converge to `budget share / baseline share × (1 - 0.5·corruption)` plus project boosts at asset-specific speeds.
* Calibration chooses `K` from investment and trend growth and `g_A0` from `g - α·g_K - (1-α)·g_L`, so the baseline grows at the data trend.

## Demand (`MacroEngine.Demand`)
* Consumption `C → (1 - s) · Yd`, `s = s0 + 0.35·Δr_real + 0.2·unrest`; `Yd` = household income share × GDP - income and payroll tax + transfers + interest income.
* Private investment `I → I0·Pot·exp(-3Δr_loan)·(1 + 0.5·gap)·(1 - 1.5Δτ_corp)·confidence + ΔFDI`.
* Government `G` from budget lines (current vs capital split), plus project and subsidy flows.
* Exports `X0 · (partner demand / trend) · RER^-0.9 · (Pot/Pot0) · (1 - tariffs)`; imports `M0 · (absorption/base) · RER^0.8 · tariff term`.
* `GDP = C + I + G + X - M`, clamped to [0.75, 1.20] × potential as a regime guard.

## Prices, rates, labour
* Underlying inflation `π → E[π] + κ·gap + cost push (oil, food) + FX pass-through + monetisation + modifiers`; CPI is year-on-year from a price-level ring.
* Expectations adapt to realised inflation with weight `1 - credibility`; credibility is earned by positive real rates and discipline, lost by monetisation.
* Taylor rule `i* = r* + π + 0.5·min(π-π*, 10pp) + 0.2·(next 40pp) + gap`, smoothed; peg and managed regimes modify it; manual override moves ≤ 0.5pp/month.
* Unemployment `u → NAIRU - 0.5·gap`; NAIRU = base (with hysteresis) + minimum wage + payroll tax - skills + policy + long-term-unemployment scar `h` (below).
* The Phillips target also carries the wage-price spiral `w` (below).

## Shadow economy (`ShadowEngine`) and labour market (`LabourMarketEngine`)
Version 3. Everything is relative to the starting state and inside dead-bands (`Soft(x, b) = x - clamp(x, -b, b)`), so it is exactly neutral until something moves; `World.EconomicDepth = false` removes it all. `dt = 1/12`.
* **Shadow share** `S` (start `S0`, from `data/shadow.json`). Target `S* = clamp(S0 + 0.5·Σ_t (τ_t - τ_t0)·B_t + 0.35·Soft(corr - corr0, 0.015) - 0.15·S0·(admin/admin0 - 1) + mod_shadow·S0, lo, hi)` with `B_t` each tax's base as a share of GDP, `lo = min(S0, max(0.02, 0.4·S0))`, `hi = max(S0, min(0.8, S0 + 0.2))`. `S += (S* - S)·dt/3`. Revenue from every tax except tariffs is multiplied by `(1 - S)/(1 - S0)`. Tax burden for approval uses the statutory take. Gini `+0.15·(S - S0)`, approval target `-0.10·(S - S0)`.
* **Bargaining power** `B = coverage·strength`; coverage `= coverage0 + mod_bargaining`, clamped to [0, 0.99].
* **Price shock** `n = Soft(d2 + d3 + d4 + d5 + w, 0.01)` (energy and food, currency, monetisation, carbon and policy drivers of the Phillips curve, plus last month's spiral), times 0.4 when negative (wages do not fall with prices). Credibility `κ` is the one used for expectations (earned credibility, less deficit strain, plus the policy modifier). Claim `c = B·(2·(1 - 0.7κ)·n + 0.3·Soft(NAIRU - u, 0.01))`. Premium `p += (c - p)·min(1, dt/(0.4 + 1.6B))`, clamped to [-4%, 12%]. Spiral `w = 0.5·(1 - 0.4κ)·p`.
* **Real-wage gap** `g += (0.8·n - p - 0.4·g)·dt` in [-10%, 30%]; the market real-wage index is multiplied by `exp(-Δg)`. **Strike risk** `1 - exp(-40·B·Soft(g, 0.01))`; the strike-wave event's probability is multiplied by `1 + 4·risk`.
* **Long-term unemployment** stock `L` (share of the labour force, ≤ 10%): `dL = 0.7·Soft(u - NAIRU, 0.01)⁺ - (0.2 + 1.5·(NAIRU - u)⁺)·L`. Scar `h = min(2%, (0.25 + 0.5B)·L)`, added to NAIRU (not to the stored components).
* **Labour share** trend `→ s0 + 0.15·(B - B0) + 0.15·Soft(gap, 0.02)` at 15% a year; `s = trend·exp(-g)`, in [0.15, 0.85]. Household income `+0.2·(s - s0)·GDP`; Gini `-0.25·(s - s0)`.
* Calibration: hand-set, not estimated. In impulse tests (a six-point cost-push for eighteen months, Britain, bargaining strength 0.6) the wage premium peaks near 1.3% with 90% coverage against 0.2% with 10%, and peak inflation is 0.2 points higher; with low credibility the premium reaches 2% and inflation 0.7 points higher than in the middle case, with high credibility 0.7% and inflation 0.4 points lower. A three-year policy slump (rate held at 20%) leaves a scar of about 1.1 points where unions are strong and 0.65 where they are weak, about a quarter of it still there after twelve years.

## Fiscal and external
* Taxes: concave revenue response `R = base·r0·(r/r0)^ε`; resource revenue scales with oil and GDP; scaled by the shadow multiplier above (not for tariffs).
* Debt accumulates nominal deficits; average cost follows the 10-year yield with 3.5-7 year maturity; yield = policy/neutral blend + sovereign spread; spread = baseline + risk(debt, deficit, inflation, stability, reserves, contagion).
* Real exchange rate targets `CA gap / 0.45 + 2·Δ(real rate differential) - 1.5·Δspread - risk-off`; peg breaks when reserves and competitiveness are exhausted.

## Sectors
Six sectors with a stylised input-output matrix; demand composition (C, I, G, X) is pushed through the Leontief inverse to allocate value added, and capital follows relative returns and subsidies.

## World layer (`WorldEngine`)
Gravity trade weights `GDP_j^0.9 · exp(-dist/5500) · bloc · region`; partner imports drive exports; crisis scores spread to neighbours; USD rate and world inflation come from the simulated US and advanced economies.

## Politics, events, scoring
Approval target from growth, unemployment, inflation, inequality, tax burden, services and corruption; elections use a logistic of approval; autocracies face coup hazard when stability and unrest are adverse. Events are data (`data/events.json`) with conditions, cooldowns, resilience scaling, chains and choices. The scorecard and attribution live in `Scoring/`.

## Validation
Unit tests check the national-accounts identity, steady-state neutrality, impulse responses (rate hikes, stimulus, taxes, education), determinism, save/load, 30-year numerical stability over many seeds, and balance (no dominant scripted strategy). Known simplifications: no explicit banking sector or housing market, sovereign defaults are stylised, the regional split is illustrative.
