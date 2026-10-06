#!/usr/bin/env python3
"""Builds data/scenarios.json: guided tutorials, historical-style scenarios and long-horizon challenges.

goal metrics: infl, unemp, debt, deficit, approval, gdppc (real GDP/head vs start), emissions (vs start), renewables, gini, score,
growth, ca, nonenergy (non-energy share of value added), default (0/1). op: < or >. byMonth: deadline (default: end).
"""
import json, pathlib

def S(id, name, kind, country, years, difficulty, desc, goals, setup=None, events=None, hints=None, unlock=None):
    return dict(id=id, name=name, kind=kind, country=country, years=years, difficulty=difficulty, description=desc, goals=goals,
                setup=setup or [], events=events or [], hints=hints or [], unlock=unlock)

def G(label, metric, op, value, by=0, weight=1.0): return dict(label=label, metric=metric, op=op, value=value, byMonth=by, weight=weight)
def fx(kind, key, value, months=0): return dict(kind=kind, key=key, value=value, months=months, scale="none")
def ev(id, month=0, country=True): return dict(id=id, month=month, country=country)
def H(month, text): return dict(month=month, text=text)

scenarios = [
 S("tut_budget","1. Your First Budget","tutorial","GBR",3,"Easy",
   "You are the new UK Chancellor. Learn the budget screen: bring the deficit down without tipping the economy into recession.",
   [G("Deficit below 4.5% of GDP","deficit","<",0.045,36),G("Unemployment below 5.2%","unemp","<",0.052,36),G("Approval above 30%","approval",">",0.30,36)],
   hints=[H(0,"Open Budget and compare revenue with spending. Taxes and spending lines take effect immediately but cost political capital."),
          H(3,"Check the advisors: the Finance Minister and Social Policy Minister will disagree. You decide."),
          H(12,"Open Forecast to preview the effect of a change before you commit to it.")]),
 S("tut_inflation","2. Taming Inflation","tutorial","GBR",4,"Easy",
   "A cost-of-living shock has pushed inflation toward 9%. Use monetary and fiscal policy to bring it back to target without a deep slump.",
   [G("Inflation below 3.5%","infl","<",0.035,48),G("Unemployment below 7%","unemp","<",0.07,48)],
   setup=[fx("mod","inflation",0.05,30),fx("state","approval",-0.05)],events=[ev("oil_supply_shock",0,False)],
   hints=[H(0,"The central bank follows a rule by default. Pin the policy rate manually in Monetary Policy if you disagree, at a political-capital cost."),
          H(6,"Expectations matter: credibility policies such as central-bank independence slow the spiral.")],unlock="tut_budget"),
 S("tut_invest","3. Investing for Growth","tutorial","IND",15,"Normal",
   "India has a young workforce and a huge infrastructure gap. Allocate public investment and run projects to sustain high growth.",
   [G("Real GDP per head x1.9","gdppc",">",1.9),G("Debt below 100% of GDP","debt","<",1.0),G("Approval above 40%","approval",">",0.40)],
   hints=[H(0,"Open Investment: projects add permanent capital-quality boosts but cost money for years and can overrun."),
          H(12,"Education and infrastructure compound slowly; R&D pays off most when the economy is already productive.")],unlock="tut_inflation"),
 S("hist_gfc","Financial Crisis (UK)","historical","GBR",8,"Normal",
   "A credit boom turns to bust. Stabilise the banks, protect jobs and avoid a sovereign scare.",
   [G("Unemployment below 9% after 5 years","unemp","<",0.09,60),G("Debt below 130% of GDP","debt","<",1.3),G("No sovereign default","default","<",0.5)],
   events=[ev("global_financial_crisis",0,False),ev("banking_crisis",1)],unlock="tut_invest"),
 S("hist_inflation_wave","Post-Pandemic Inflation Wave (USA)","historical","USA",6,"Normal",
   "Supply chains snap, demand surges and energy spikes. Land the economy softly.",
   [G("Inflation below 3.5% by year 4","infl","<",0.035,48),G("Unemployment below 5.5%","unemp","<",0.055,72)],
   events=[ev("pandemic",0,False)],unlock="hist_gfc"),
 S("hist_oil_crash","Oil Crash (Saudi Arabia)","historical","SAU",8,"Normal",
   "Crude collapses. Diversify and keep the peg without exhausting the fiscal buffers.",
   [G("Deficit below 6% of GDP","deficit","<",0.06,60),G("Approval above 50%","approval",">",0.50),G("Non-energy share above 78%","nonenergy",">",0.78)],
   events=[ev("oil_glut",0,False)],unlock="hist_inflation_wave"),
 S("chal_debt","Debt Spiral (Argentina)","challenge","ARG",8,"Hard",
   "Triple-digit inflation, a depreciating peso and a restless population. Stabilise before the next crisis.",
   [G("Inflation below 20%","infl","<",0.20),G("Debt below 100% of GDP","debt","<",1.0),G("Unemployment below 12%","unemp","<",0.12)],unlock="hist_oil_crash"),
 S("chal_green","Great Decarbonisation (Germany)","challenge","DEU",20,"Normal",
   "Halve emissions without hollowing out industry.",
   [G("Emissions below 55% of today","emissions","<",0.55),G("Real GDP per head x1.15","gdppc",">",1.15),G("Approval above 30%","approval",">",0.30)],unlock="chal_debt"),
 S("chal_catchup","Catch-up Growth (Ethiopia)","challenge","ETH",25,"Hard",
   "Escape the low-income trap: build capacity, avoid default, keep society together.",
   [G("Real GDP per head x2.5","gdppc",">",2.5),G("Gini below 0.45","gini","<",0.45),G("No default","default","<",0.5)],unlock="chal_green"),
 S("chal_curse","Resource Curse (Nigeria)","challenge","NGA",15,"Hard",
   "Oil rents, a fragile state and a young population. Diversify before the rents fade.",
   [G("Real GDP per head x1.6","gdppc",">",1.6),G("Inflation below 10%","infl","<",0.10),G("Non-energy share above 96%","nonenergy",">",0.96)],unlock="chal_catchup"),
 S("chal_ageing","Ageing Nation (Japan)","challenge","JPN",20,"Normal",
   "The world's oldest society with the world's largest debt. Keep it solvent and growing.",
   [G("Real GDP per head x1.15","gdppc",">",1.15),G("Debt below 300% of GDP","debt","<",3.0),G("Approval above 30%","approval",">",0.30)],unlock="chal_curse"),
]

if __name__ == "__main__":
    out = pathlib.Path(__file__).resolve().parents[2] / "data" / "scenarios.json"
    out.write_text(json.dumps({"scenarios": scenarios}, indent=1) + "\n")
    print(f"wrote {len(scenarios)} scenarios -> {out}")
