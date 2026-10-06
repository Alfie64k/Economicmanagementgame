#!/usr/bin/env python3
"""Builds data/policies.json (policy catalogue + public investment projects).

Effect magnitudes are game-balance parameters informed by the empirical literature (rough orders of magnitude), not forecasts.
Modifier keys are consumed by the engine: tfp (annual TFP growth add), nairu, fdi, export, import, corruption (annual change),
approval, gini, invest, savings, inflation, credibility, risk, fx, participation, fertility, migration (multiplier add),
lifeexp, renewables (annual add), emissions (multiplier add), unrest, polcap (monthly PC add), revenue (share of GDP), stability,
shadow (relative change in the informal share: -0.10 = informality 10% below its starting size, which shrinks evasion and widens the tax base),
bargaining (add to union coverage: +0.10 = ten more points of employees covered by collective agreements).
"""
import json, pathlib

def P(id, name, cat, desc, pc, delay, mods=None, budget=None, subsidy=None, carbon=None, oneoff=0.0, shock=0.0, group="", min_dem=0.0, max_dem=1.0):
    return dict(id=id, name=name, category=cat, desc=desc, pc=pc, delay=delay, mods=mods or {}, budget=budget or {},
                subsidy=subsidy or {}, carbon=carbon, oneOffRevenue=oneoff, approvalShock=shock, group=group,
                minDemocracy=min_dem, maxDemocracy=max_dem)

policies = [
 P("labour_flex","Labour market flexibility","labour","Easier hiring and firing, weaker employment protection. Lowers structural unemployment and lifts productivity but widens inequality.",25,6,{"nairu":-0.012,"tfp":0.0010,"gini":0.012,"approval":-0.03,"bargaining":-0.12},shock=-0.02,group="labour_rules"),
 P("union_rights","Strengthen collective bargaining","labour","Stronger unions and sectoral bargaining. Compresses wages and inequality at some cost to flexibility.",20,6,{"nairu":0.006,"gini":-0.015,"tfp":-0.0005,"approval":0.02,"bargaining":0.15},shock=0.01,group="labour_rules"),
 P("apprenticeships","National apprenticeship scheme","labour","Employer-linked skills programme: lower mismatch unemployment, higher productivity.",10,9,{"tfp":0.0008,"nairu":-0.004},{"Education":0.004}),
 P("active_labour","Active labour market programmes","welfare","Job-search support and retraining.",12,6,{"nairu":-0.005,"approval":0.01},{"Social":0.004}),
 P("fta_network","Free-trade agreement network","trade","Negotiate preferential access to major markets: exports, imports and FDI rise; import-competing sectors protest.",20,12,{"export":0.06,"import":0.04,"fdi":0.05,"approval":-0.01},group="trade_stance"),
 P("import_substitution","Import-substitution strategy","trade","Shelter domestic industry behind barriers. Short-run protection, long-run productivity drag and higher prices.",20,6,{"import":-0.08,"export":-0.03,"tfp":-0.0010,"inflation":0.005,"approval":0.01},group="trade_stance"),
 P("export_credit","Export credit agency","trade","Subsidised export finance and trade missions.",10,6,{"export":0.05},{"Admin":0.003}),
 P("fdi_incentives","FDI incentive package","trade","Tax holidays and fast-track permitting for foreign investors.",15,4,{"fdi":0.25,"approval":-0.01,"gini":0.004},{"Admin":0.002}),
 P("sez","Special economic zones","industrial","Zones with light-touch regulation and infrastructure; attract investment and cluster manufacturing.",18,12,{"fdi":0.15,"tfp":0.0005,"gini":0.005},{"Infrastructure":0.003}),
 P("anti_corruption","Anti-corruption commission","institutions","Independent prosecutors and transparency rules. Slow burn: reduces leakage, raises trust and FDI.",30,12,{"corruption":-0.006,"approval":0.02,"fdi":0.05,"polcap":-0.2},shock=-0.01,min_dem=0.25),
 P("judicial_reform","Judicial independence reform","institutions","Stronger courts and contract enforcement. Boosts credibility and investment.",25,18,{"credibility":0.10,"fdi":0.10,"stability":0.03,"invest":0.02},min_dem=0.2),
 P("cb_independence","Central bank independence","monetary","Legislate operational independence and an inflation mandate. Anchors expectations; removes your ability to lean on policy.",25,6,{"credibility":0.25,"risk":-0.004}),
 P("fiscal_rule","Binding fiscal rule","fiscal","Statutory debt/deficit rule with an independent fiscal council. Lowers risk premia, constrains discretion.",25,12,{"risk":-0.006,"credibility":0.08,"approval":-0.01}),
 P("pension_age","Raise retirement age","welfare","Link retirement age to life expectancy. Raises participation and trims long-run pension costs; politically toxic.",35,12,{"participation":0.02,"approval":-0.05,"unrest":0.02},{"Social":-0.005},shock=-0.04),
 P("ubi","Universal basic income","welfare","Unconditional payment to all adults. Cuts inequality and boosts approval; expensive and mildly raises reservation wages.",30,12,{"gini":-0.04,"nairu":0.005,"approval":0.05,"savings":-0.01},{"Social":0.04},shock=0.03),
 P("childcare","Universal childcare","welfare","Subsidised childcare: higher female participation, slightly higher fertility.",15,12,{"fertility":0.15,"participation":0.015,"approval":0.02},{"Social":0.005}),
 P("open_immigration","Skills-led immigration expansion","immigration","Points-based system and higher quotas. Lifts labour supply and productivity; fiscal gains over time, short-run political friction.",25,6,{"migration":0.8,"tfp":0.0005,"gini":0.003,"approval":-0.03},shock=-0.02,group="immigration"),
 P("restrict_immigration","Tighter immigration controls","immigration","Cut net migration. Popular with some voters; shrinks labour supply growth.",20,6,{"migration":-0.7,"tfp":-0.0004,"approval":0.02},shock=0.01,group="immigration"),
 P("carbon_tax_50","Carbon price (50 per tonne)","environment","Economy-wide carbon price at 50 USD/t. Cuts emissions and raises revenue; raises energy prices.",20,6,{"approval":-0.02,"revenue":0.004},carbon=50,group="carbon"),
 P("carbon_tax_100","Carbon price (100 per tonne)","environment","Aggressive carbon price at 100 USD/t.",30,6,{"approval":-0.04,"revenue":0.008,"unrest":0.01},carbon=100,group="carbon"),
 P("renewables_subsidy","Renewables feed-in tariffs","environment","Guaranteed prices for wind and solar. Accelerates the energy transition.",15,6,{"renewables":0.006},{"Green":0.004}),
 P("nuclear_programme","New nuclear programme","environment","Fleet of reactors: reliable low-carbon baseload with long lead times and cost-overrun risk.",30,24,{"renewables":0.003,"emissions":-0.05},{"Green":0.003}),
 P("fossil_subsidies","Fuel price subsidies","environment","Cap pump prices. Popular and anti-inflationary; fiscally costly and pollutive.",10,1,{"inflation":-0.004,"emissions":0.08,"approval":0.02},{"Admin":0.01},shock=0.02),
 P("deregulation","Broad deregulation","industrial","Cut licensing and red tape. Lifts investment and productivity; weaker consumer and worker protections.",20,9,{"tfp":0.0012,"invest":0.04,"gini":0.005,"stability":-0.01}),
 P("competition_policy","Competition authority","industrial","Strengthen merger control and market opening.",12,12,{"tfp":0.0008,"inflation":-0.002,"approval":0.01}),
 P("privatisation","Privatisation programme","ownership","Sell state enterprises. One-off proceeds and efficiency gains; job losses and unrest risk.",30,9,{"tfp":0.0008,"unrest":0.02,"approval":-0.03,"gini":0.004},oneoff=0.04,shock=-0.03,group="ownership"),
 P("nationalise_strategic","Nationalise strategic industries","ownership","Take utilities and key industries into public hands. Control and jobs, at fiscal and efficiency cost.",30,9,{"tfp":-0.0010,"approval":0.02,"invest":-0.02,"risk":0.004},{"Admin":0.01},oneoff=-0.03,group="ownership"),
 P("industrial_manufacturing","Manufacturing industrial strategy","industrial","Targeted subsidies and procurement for manufacturing clusters.",20,9,{"tfp":0.0003},{"Admin":0.004},subsidy={"Manufacturing":0.04}),
 P("industrial_green","Green industrial strategy","industrial","Subsidies for clean-tech manufacturing and grid build-out.",20,9,{"tfp":0.0003,"renewables":0.002},{"Admin":0.004},subsidy={"Energy":0.03}),
 P("industrial_services","Digital services strategy","industrial","Support for software, fintech and business services exports.",15,9,{"tfp":0.0004,"export":0.02},{"Digital":0.002},subsidy={"Services":0.02}),
 P("agri_support","Agricultural support scheme","industrial","Price floors and input subsidies. Protects rural incomes; distorts markets.",12,6,{"approval":0.01,"tfp":-0.0002},{"Admin":0.005},subsidy={"Agriculture":0.05}),
 P("rnd_tax_credits","R&D tax credits","industrial","Generous credits for private research.",12,6,{"tfp":0.0008},{"RnD":0.003}),
 P("housing_reform","Planning and housing supply reform","welfare","Liberalise planning rules to build more homes.",20,12,{"gini":-0.01,"approval":0.02,"invest":0.02},{"Housing":0.003}),
 P("capital_controls","Capital flow management","monetary","Curb hot money. Reduces crisis risk premia; deters FDI.",25,3,{"risk":-0.010,"fdi":-0.15,"approval":-0.01},group="capital"),
 P("wealth_tax","Annual wealth tax","fiscal","Levy on top fortunes. Reduces inequality, raises modest revenue, risks capital flight.",30,12,{"gini":-0.02,"invest":-0.03,"revenue":0.005,"approval":0.01,"fdi":-0.05},shock=0.01),
 P("tax_compliance","Digital tax compliance drive","fiscal","E-invoicing and data matching to shrink the informal economy and evasion.",15,12,{"revenue":0.002,"shadow":-0.10,"corruption":-0.002},{"Digital":0.001}),
 P("universal_healthcare","Universal healthcare expansion","welfare","Extend free care at point of use. Healthier workforce and happier voters; large permanent cost.",30,18,{"approval":0.04,"lifeexp":2.0},{"Health":0.015},shock=0.03),
 P("student_loans","Graduate tax and student finance","welfare","Shift higher education cost to graduates.",15,9,{"approval":-0.01,"tfp":0.0002},{"Education":-0.003}),
 P("inflation_targeting","Formal inflation-targeting regime","monetary","Publish a target and forecasts, accountability to parliament.",15,6,{"credibility":0.15}),
 P("sovereign_fund","Sovereign wealth fund","fiscal","Save part of resource windfalls abroad. Smooths booms and busts.",20,12,{"risk":-0.004,"revenue":-0.002,"credibility":0.05}),
]

projects = [
 dict(id="hsr",name="High-speed rail network",asset="Infrastructure",cost=0.040,months=84,bonus=0.12,pc=15,maint=0.03,desc="Flagship intercity rail. Long build, big overrun risk, large productivity payoff."),
 dict(id="motorways",name="Motorway and trunk-road programme",asset="Infrastructure",cost=0.020,months=48,bonus=0.07,pc=8,maint=0.04,desc="Road capacity and freight corridors."),
 dict(id="ports",name="Ports and logistics hubs",asset="Infrastructure",cost=0.015,months=48,bonus=0.06,pc=8,maint=0.03,desc="Deep-water ports and inland freight terminals; lifts trade capacity."),
 dict(id="metro",name="Urban metro systems",asset="Infrastructure",cost=0.015,months=60,bonus=0.05,pc=10,maint=0.04,desc="City rail to raise labour-market pooling."),
 dict(id="grid",name="National grid modernisation",asset="Green",cost=0.020,months=48,bonus=0.12,pc=10,maint=0.03,desc="Interconnectors, storage and smart grids that unlock renewables."),
 dict(id="wind_solar",name="Utility-scale wind and solar",asset="Green",cost=0.030,months=48,bonus=0.20,pc=10,maint=0.025,desc="Large renewable build-out."),
 dict(id="nuclear_plants",name="Nuclear power stations",asset="Green",cost=0.050,months=96,bonus=0.15,pc=20,maint=0.03,desc="Baseload low-carbon capacity; notorious overruns."),
 dict(id="broadband",name="National full-fibre broadband",asset="Digital",cost=0.015,months=36,bonus=0.15,pc=8,maint=0.04,desc="Gigabit access for homes and firms."),
 dict(id="datacentres",name="Sovereign cloud and compute",asset="Digital",cost=0.010,months=30,bonus=0.10,pc=8,maint=0.05,desc="Public compute capacity and data infrastructure."),
 dict(id="schools",name="School building programme",asset="Education",cost=0.020,months=48,bonus=0.10,pc=8,maint=0.03,desc="New and refurbished schools."),
 dict(id="universities",name="University and technical colleges",asset="Education",cost=0.015,months=48,bonus=0.08,pc=8,maint=0.03,desc="Capacity for higher and technical education."),
 dict(id="hospitals",name="Hospital building programme",asset="Health",cost=0.020,months=48,bonus=0.10,pc=8,maint=0.04,desc="New hospitals and primary-care centres."),
 dict(id="housing_estates",name="Public housing estates",asset="Housing",cost=0.025,months=48,bonus=0.15,pc=10,maint=0.03,desc="Affordable homes in high-demand areas."),
 dict(id="research_campuses",name="National research campuses",asset="RnD",cost=0.015,months=48,bonus=0.15,pc=10,maint=0.04,desc="Flagship labs and science parks."),
 dict(id="defence_procurement",name="Defence modernisation",asset="Defence",cost=0.030,months=60,bonus=0.15,pc=12,maint=0.05,desc="Capital equipment and bases."),
]

if __name__ == "__main__":
    out = pathlib.Path(__file__).resolve().parents[2] / "data" / "policies.json"
    out.write_text(json.dumps({"policies": policies, "projects": projects}, indent=1) + "\n")
    print(f"wrote {len(policies)} policies, {len(projects)} projects -> {out}")
