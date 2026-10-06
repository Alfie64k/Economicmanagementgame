#!/usr/bin/env python3
"""Builds data/events.json: stochastic events, crises and decision popups.

Conditions are lists of "metric op value" strings (all must hold). prob is per year.
Effect kinds: level (one-off relative change), state (additive; the key "wagecatchup" instead makes good that share of the real-wage shortfall),
mod (temporary modifier for `months`), global (world variable), imf (loan + conditionality), default (debt haircut), election (early election).
Metrics for conditions and probMods are listed in EventEngine.Metric (including "strike", the labour model's strike risk, and "shadow", the informal share).
scale: none | vuln (poorer = worse) | health | infra | digital | defence (resilience: strong asset index softens the blow)
"""
import json, pathlib

def E(id, name, text, scope="country", prob=0.0, cond=None, probmods=None, effects=None, choices=None, default=0, deadline=3,
      nxt=None, cooldown=36, cat="shock", all_countries=False):
    return dict(id=id, name=name, text=text, scope=scope, prob=prob, cond=cond or [], probMods=probmods or [], effects=effects or [],
                choices=choices or [], defaultChoice=default, deadline=deadline, next=nxt or [], cooldown=cooldown, category=cat, allCountries=all_countries)

def fx(kind, key, value, months=0, scale="none"): return dict(kind=kind, key=key, value=value, months=months, scale=scale)

events = [
 # ---------------- global ----------------
 E("pandemic","Novel virus outbreak","A fast-spreading respiratory virus triggers lockdowns and travel restrictions worldwide.",scope="global",prob=0.012,all_countries=True,cat="global",cooldown=240,
   effects=[fx("level","cons",-0.07,0,"health"),fx("level","inv",-0.08,0,"health"),fx("mod","export",-0.12,12,"health"),fx("mod","tfp",-0.004,12,"none"),
            fx("state","approval",-0.03),fx("level","pop",-0.001,0,"health"),fx("global","risk",-0.25),fx("global","oil",0.7)],
   nxt=[dict(id="post_pandemic_rebound",prob=1.0,delay=18)]),
 E("post_pandemic_rebound","Post-pandemic rebound","Pent-up demand and stimulus unwind: spending surges, supply chains strain.",scope="global",all_countries=True,cat="global",
   effects=[fx("mod","inflation",0.02,12),fx("level","cons",0.03),fx("global","oil",1.3),fx("global","risk",0.2)]),
 E("oil_supply_shock","Oil supply shock","Conflict and OPEC cuts send crude soaring.",scope="global",prob=0.04,cat="global",cooldown=60,
   effects=[fx("global","oil",1.7),fx("global","food",1.1)],nxt=[dict(id="oil_glut",prob=0.4,delay=30)]),
 E("oil_glut","Oil glut","Shale output and weak demand crash oil prices.",scope="global",prob=0.025,cat="global",cooldown=60,effects=[fx("global","oil",0.6)]),
 E("global_financial_crisis","Global financial crisis","A leveraged-finance meltdown freezes credit across advanced economies.",scope="global",prob=0.015,all_countries=True,cat="global",cooldown=180,
   effects=[fx("global","risk",-0.35),fx("level","inv",-0.10,0,"none"),fx("level","cons",-0.03),fx("mod","export",-0.08,18),fx("state","approval",-0.04),fx("mod","risk",0.01,24)],
   nxt=[dict(id="austerity_pressure",prob=0.6,delay=12)]),
 E("austerity_pressure","Austerity pressure","Markets and lenders demand deficit reduction after the crisis.",scope="global",cat="global",all_countries=True,
   effects=[fx("mod","risk",0.005,24)]),
 E("tech_boom","Global technology boom","A productivity wave (AI-driven software and automation) lifts world demand.",scope="global",prob=0.02,all_countries=True,cat="global",cooldown=120,
   effects=[fx("mod","tfp",0.003,60,"digital"),fx("global","risk",0.2),fx("mod","export",0.03,36)]),
 E("food_price_spike","Food price spike","Harvest failures and export bans lift global food prices.",scope="global",prob=0.03,cat="global",cooldown=48,
   effects=[fx("global","food",1.5)]),
 # ---------------- country shocks ----------------
 E("earthquake_flood","Major natural disaster","A catastrophic earthquake or flood strikes a populated region.",prob=0.03,
   probmods=[dict(metric="temp",ref=1.2,slope=0.8)],
   effects=[fx("level","capital",-0.012,0,"infra"),fx("state","approval",-0.02),fx("level","cons",-0.015,0,"infra")],
   choices=[dict(label="Full national relief and rebuild package (1.2% GDP)",cost=0.012,effects=[fx("state","approval",0.04),fx("mod","tfp",0.0006,24)]),
            dict(label="Targeted relief (0.5% GDP)",cost=0.005,effects=[fx("state","approval",0.01)]),
            dict(label="Appeal for international aid only",cost=0.0,effects=[fx("state","approval",-0.04),fx("state","unrest",0.05)])],default=1),
 E("drought","Severe drought","Rains fail across the agricultural belt.",prob=0.04,probmods=[dict(metric="temp",ref=1.2,slope=1.0)],cond=["agri>0.04"],
   effects=[fx("mod","inflation",0.015,12),fx("level","cons",-0.01,0,"vuln"),fx("state","unrest",0.05,0,"vuln"),fx("mod","export",-0.03,12)],
   choices=[dict(label="Emergency food subsidies (0.6% GDP)",cost=0.006,effects=[fx("state","approval",0.02),fx("mod","inflation",-0.01,12)]),
            dict(label="Let markets adjust",cost=0.0,effects=[fx("state","unrest",0.06)])],default=1),
 # "strike" is the labour-market model's StrikeRisk (0 in a quiet economy, so the base probability is unchanged there): it rises when real wages have
 # fallen behind and bargaining power is high. "wagecatchup" makes good that share of the real-wage shortfall (a no-op when workers are not behind).
 E("strike_wave","Strike wave","Unions walk out over pay and conditions.",prob=0.05,cond=["unemp<0.07","infl>0.03"],probmods=[dict(metric="strike",ref=0.0,slope=4.0)],
   effects=[fx("mod","tfp",-0.002,6),fx("state","approval",-0.03),fx("mod","inflation",0.008,12)],
   choices=[dict(label="Concede with a pay settlement",cost=0.003,effects=[fx("mod","inflation",0.006,12),fx("state","approval",0.04),fx("state","wagecatchup",0.6)]),
            dict(label="Hold the line",cost=0.0,effects=[fx("state","unrest",0.08),fx("state","approval",-0.02)])],default=0),
 E("corruption_scandal","Corruption scandal","A ministerial scandal dominates the headlines.",prob=0.06,cond=["corruption>0.25"],
   effects=[fx("state","approval",-0.07),fx("state","polcap",-12),fx("state","corruption",0.01),fx("mod","fdi",-0.05,12)],
   choices=[dict(label="Sack the minister and open an inquiry",cost=0.0,effects=[fx("state","approval",0.03),fx("state","polcap",-5)]),
            dict(label="Defend the government",cost=0.0,effects=[fx("state","approval",-0.03),fx("state","stability",-0.03)])],default=0),
 E("tech_breakthrough","Domestic technology breakthrough","Your universities and firms lead a new field.",prob=0.05,probmods=[dict(metric="rnd",ref=1.0,slope=1.5)],
   effects=[fx("mod","tfp",0.004,36),fx("mod","fdi",0.1,24),fx("state","approval",0.02)]),
 E("resource_discovery","Major resource discovery","Prospectors confirm a large offshore field.",prob=0.012,cooldown=240,cond=["dev<0.8"],
   effects=[fx("mod","revenue",0.012,120),fx("mod","fdi",0.1,24),fx("mod","fx",0.04,60),fx("state","approval",0.03)]),
 E("cyberattack","Major cyberattack","State-linked hackers hit hospitals, banks and ministries.",prob=0.04,
   effects=[fx("mod","tfp",-0.0025,6,"digital"),fx("state","approval",-0.02),fx("state","stability",-0.01)],
   choices=[dict(label="Emergency cyber-defence programme (0.4% GDP)",cost=0.004,effects=[fx("mod","tfp",0.0006,24)]),
            dict(label="Business as usual",cost=0.0,effects=[fx("mod","fdi",-0.05,12)])],default=0),
 E("social_movement","Mass protest movement","Cost-of-living protests fill city squares.",prob=0.06,cond=["unrest>0.3"],
   effects=[fx("state","unrest",0.1),fx("state","stability",-0.04)],
   choices=[dict(label="Concede: cost-of-living support (0.8% GDP)",cost=0.008,effects=[fx("state","unrest",-0.15),fx("state","approval",0.04)]),
            dict(label="Repress",cost=0.0,effects=[fx("state","unrest",-0.05),fx("state","stability",-0.05),fx("state","approval",-0.06),fx("mod","fdi",-0.08,18)])],default=0),
 E("refugee_inflow","Refugee inflow","Conflict next door sends hundreds of thousands across your border.",prob=0.02,cond=["dev<0.9"],
   effects=[fx("mod","migration",1.5,12),fx("state","approval",-0.03),fx("level","cons",0.004)],
   choices=[dict(label="Open borders with a reception programme (0.5% GDP)",cost=0.005,effects=[fx("mod","tfp",0.0003,60),fx("state","unrest",-0.02)]),
            dict(label="Close the border",cost=0.0,effects=[fx("state","approval",0.01),fx("mod","fdi",-0.03,12)])],default=0),
 # ---------------- financial / sovereign crises ----------------
 E("banking_crisis","Banking crisis","A credit boom turns to bust; lenders fail and credit dries up.",prob=0.05,cond=["gap>0.03","realloan<0.0"],cooldown=120,cat="crisis",
   effects=[fx("level","inv",-0.14),fx("level","cons",-0.02),fx("state","approval",-0.05),fx("mod","risk",0.012,24)],
   choices=[dict(label="Bank bailout (4% GDP)",cost=0.04,effects=[fx("mod","risk",-0.006,24),fx("level","inv",0.05),fx("state","approval",-0.03)]),
            dict(label="Let banks fail (bail-in)",cost=0.0,effects=[fx("level","inv",-0.05),fx("level","cons",-0.02),fx("state","unrest",0.05)])],default=0),
 E("currency_crisis","Currency crisis","Capital flees; the currency collapses under pressure.",prob=0.25,cond=["ca<-0.045","reserves<3.5"],cooldown=60,cat="crisis",
   effects=[fx("level","fx",0.30),fx("mod","inflation",0.04,12),fx("state","approval",-0.07),fx("level","reserves",-1.0)],
   nxt=[dict(id="sovereign_debt_crisis",prob=0.3,delay=6)]),
 E("sovereign_debt_crisis","Sovereign debt crisis","Bond markets shut. The treasury cannot roll over its debt.",prob=0.0,cond=["yield>0.16","debt>0.8"],cooldown=60,cat="crisis",
   effects=[fx("state","approval",-0.05),fx("mod","fdi",-0.15,12)],
   choices=[dict(label="IMF programme (loan 6% GDP, 3 years of conditionality)",cost=0.0,effects=[fx("imf","loan",0.06,36),fx("mod","risk",-0.04,36),fx("state","approval",-0.05)]),
            dict(label="Unilateral restructuring (40% haircut)",cost=0.0,effects=[fx("default","haircut",0.40),fx("level","cons",-0.04),fx("level","inv",-0.08),fx("mod","risk",0.03,24)]),
            dict(label="Muddle through with emergency taxes",cost=0.0,effects=[fx("state","unrest",0.1),fx("state","approval",-0.08)])],default=0),
 E("coalition_collapse","Coalition collapse","Your governing partners walk out; an early election is called.",prob=0.08,cond=["approval<0.32","democracy>0.5"],cooldown=48,cat="politics",
   effects=[fx("election","early",1)]),
 E("coup_attempt","Coup attempt","Officers move against the government.",prob=0.10,cond=["stability<0.30","democracy<0.55","unrest>0.35"],cooldown=60,cat="politics",
   effects=[fx("state","stability",-0.05)],
   choices=[dict(label="Buy off the army (1% GDP defence bonus)",cost=0.01,effects=[fx("state","stability",0.12)]),
            dict(label="Rely on loyal units",cost=0.0,effects=[fx("gameover","coup",0.5)])],default=0),
]

if __name__ == "__main__":
    out = pathlib.Path(__file__).resolve().parents[2] / "data" / "events.json"
    out.write_text(json.dumps({"events": events}, indent=1) + "\n")
    print(f"wrote {len(events)} events -> {out}")
