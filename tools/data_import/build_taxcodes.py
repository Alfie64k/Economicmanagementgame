#!/usr/bin/env python3
"""Builds data/taxcodes.json: approximate 2024/25 statutory tax and welfare parameters for the 28 game countries.

Structure
  archetypes: one complete template per archetype, amounts in multiples of mean earnings ("me").
  countries : per-country overrides, deep-merged onto the archetype template by the game loader
              (dicts merge recursively; lists such as bands and payroll tiers are REPLACED, not merged).

Units
  * A country with "unit": "lcu" carries EVERY amount field itself (so nothing in "me" is ever inherited) and gives
    "meanEarnings" in local currency per year. Named countries (GBR USA DEU FRA JPN CHN IND BRA) and several others
    whose thresholds are reliably known are written this way.
  * A country with "unit": "me" overrides only dimensionless fields (rates, ages, months, shares, categories) and, where
    useful, bands/allowances expressed as multiples of mean earnings (estimated; flagged in the label).

Conventions (see docs/DATA.md, "Tax and benefit codes")
  * Single person, wage income, 2024/25 statutory values rounded to ~2 significant figures; sub-national taxes ignored
    unless noted (CAN federal PIT only; CHE uses a federal+average-cantonal stand-in; USA federal only).
  * income.bands apply to TAXABLE income, i.e. income less the allowance. Where a statute is quoted on gross income with a
    zero-rate band or a credit equal to the lowest rate times the allowance (UK, AUS, CAN, ZAF, POL, FRA, NOR, CHL,
    BRA, ...) the allowance is the zero band and every "from" is the statutory threshold minus the allowance, which
    reproduces the statutory tax exactly above the allowance.
  * Where the statute has more than seven brackets or a smooth tariff (DEU, MEX, KOR, SGP) adjacent brackets are merged
    by chord rates so that tax at each retained breakpoint is exact.
  * Payroll rates are statutory contributions seen as a wedge on labour. They include compulsory funded schemes
    (AUS superannuation guarantee, SGP CPF, CHL AFP, KOR/JPN/CHN social insurance) even where the money is not
    government revenue. Employer contributions have one tier (no ceiling), so capped systems are expressed as an
    effective flat rate.
  * Where a country has no reduced VAT rate the "reduced" field is set to about half the standard rate so that a policy
    switch to "reduced" is meaningful (USA is 0.0 as specified; categories there treat "reduced" as untaxed).
  * Reduced corporate rates that are turnover-tested (AUS, POL, IND, CHL) are converted to a profit-equivalent limit at
    a ~10 percent margin.
"""
import copy
import json
import math
import pathlib
import sys
from statistics import NormalDist

NOTE = ("Approximate 2024/25 statutory parameters compiled for gameplay; not tax advice. "
        "Archetype templates stand in where a country is not modelled in detail.")

ROSTER = [
    ("USA", "advanced"), ("GBR", "advanced"), ("DEU", "advanced"), ("FRA", "advanced"),
    ("JPN", "advanced"), ("KOR", "advanced"), ("AUS", "advanced"), ("CAN", "advanced"),
    ("CHN", "emerging"), ("IND", "emerging"), ("BRA", "emerging"), ("ARG", "emerging"),
    ("ZAF", "emerging"), ("MEX", "emerging"), ("IDN", "emerging"), ("TUR", "emerging"),
    ("EGY", "emerging"), ("VNM", "emerging"), ("POL", "emerging"), ("CHL", "emerging"),
    ("RUS", "resource"), ("SAU", "resource"), ("NGA", "resource"), ("ARE", "resource"),
    ("NOR", "resource"), ("SGP", "hub"), ("CHE", "hub"), ("ETH", "developing"),
]
ARCHETYPE_NAMES = ["advanced", "hub", "emerging", "resource", "developing"]
NAMED_DETAILED = ["GBR", "USA", "DEU", "FRA", "JPN", "CHN", "IND", "BRA"]

CATEGORIES = ["food", "energy", "housing", "transport", "services", "goods", "health_edu"]
TREATMENTS = {"zero", "reduced", "standard", "exempt"}
PENSION_INDEX = {"neutral", "cpi", "triplelock", "earnings", "frozen"}
INCOME_INDEX = {"earnings", "cpi", "frozen"}
SHARE_KEYS = ["pension", "unemployment", "child", "disability", "housing", "meanstest", "other"]


# ----------------------------------------------------------------------------------------------------------------------
# small constructors (keep the data below readable)
# ----------------------------------------------------------------------------------------------------------------------
def bands(*pairs):
    return [{"from": a, "rate": r} for a, r in pairs]


def tiers(*pairs):
    return [{"from": a, "rate": r} for a, r in pairs]


def vat(standard, reduced, food, energy, housing, transport, services, goods, health_edu):
    return {"standard": standard, "reduced": reduced,
            "categories": {"food": food, "energy": energy, "housing": housing, "transport": transport,
                           "services": services, "goods": goods, "health_edu": health_edu}}


def shares(pension, unemployment, child, disability, housing, meanstest, other):
    return {"pension": pension, "unemployment": unemployment, "child": child, "disability": disability,
            "housing": housing, "meanstest": meanstest, "other": other}


def benefits(sh, pension, age, pindex, unemp, months, child, cthr, disab, housing, mt, taper, wa):
    return {"shares": sh,
            "pension": {"level": pension, "age": age, "index": pindex},
            "unemployment": {"level": unemp, "months": months},
            "child": {"level": child, "threshold": cthr},
            "disability": {"level": disab},
            "housing": {"level": housing},
            "meanstest": {"level": mt, "taper": taper, "workAllowance": wa}}


def income(allowance, bnds, index, taper_start=0, taper_rate=0):
    return {"allowance": allowance, "taperStart": taper_start, "taperRate": taper_rate, "bands": bnds, "index": index}


def corp(main, small, small_limit, expensing):
    return {"main": main, "small": small, "smallLimit": small_limit, "expensing": expensing}


# ----------------------------------------------------------------------------------------------------------------------
# ARCHETYPE TEMPLATES (unit "me": every amount is a multiple of mean annual earnings)
# ----------------------------------------------------------------------------------------------------------------------
ARCHETYPES = {
    # Generic OECD-style economy: modest allowance, four bands to 40 percent, payroll 8/15, corporate 25, VAT 19/7.
    "advanced": {
        "label": "Advanced-economy template (approximate)", "unit": "me",
        "income": income(0.30, bands((0, 0.10), (0.40, 0.20), (0.90, 0.30), (3.0, 0.40)), "cpi"),
        "payroll": {"employee": tiers((0.10, 0.08), (2.5, 0.02)), "employer": {"from": 0.05, "rate": 0.15}},
        "corp": corp(0.25, 0.25, 0, 0.3),
        "vat": vat(0.19, 0.07, "reduced", "standard", "exempt", "standard", "standard", "standard", "exempt"),
        "benefits": benefits(shares(0.42, 0.04, 0.06, 0.10, 0.04, 0.10, 0.24),
                             0.30, 65, "cpi", 0.30, 12, 0.04, 0, 0.22, 0.12, 0.20, 0.50, 0.10),
    },
    # Singapore / Switzerland-like: low rates, light payroll, 15 percent corporate, single-digit VAT, lean state.
    "hub": {
        "label": "Hub-economy template (approximate)", "unit": "me",
        "income": income(0.20, bands((0, 0.03), (0.6, 0.08), (1.5, 0.15), (4.0, 0.22)), "cpi"),
        "payroll": {"employee": tiers((0.0, 0.08)), "employer": {"from": 0.0, "rate": 0.08}},
        "corp": corp(0.15, 0.15, 0, 0.4),
        "vat": vat(0.08, 0.03, "reduced", "standard", "exempt", "standard", "standard", "standard", "exempt"),
        "benefits": benefits(shares(0.40, 0.04, 0.06, 0.10, 0.03, 0.08, 0.29),
                             0.25, 65, "earnings", 0.35, 12, 0.03, 0, 0.20, 0.08, 0.20, 0.60, 0.10),
    },
    # Middle-income economy: 0.4 ME allowance, bands to 33 percent, lean contributions and benefits.
    "emerging": {
        "label": "Emerging-economy template (approximate)", "unit": "me",
        "income": income(0.40, bands((0, 0.05), (0.8, 0.15), (2.0, 0.25), (5.0, 0.33)), "cpi"),
        "payroll": {"employee": tiers((0.1, 0.07), (3.0, 0.02)), "employer": {"from": 0.1, "rate": 0.12}},
        "corp": corp(0.25, 0.25, 0, 0.3),
        "vat": vat(0.16, 0.08, "reduced", "standard", "exempt", "standard", "standard", "standard", "exempt"),
        "benefits": benefits(shares(0.45, 0.02, 0.03, 0.04, 0.01, 0.14, 0.31),
                             0.22, 62, "cpi", 0.20, 6, 0.02, 0, 0.12, 0.05, 0.12, 0.50, 0.10),
    },
    # Commodity exporter: low income tax, lighter VAT, generous but narrow benefits (shares skewed to "other"/pension).
    "resource": {
        "label": "Resource-economy template (approximate)", "unit": "me",
        "income": income(0.50, bands((0, 0.05), (1.5, 0.12), (4.0, 0.20)), "cpi"),
        "payroll": {"employee": tiers((0.0, 0.06)), "employer": {"from": 0.0, "rate": 0.10}},
        "corp": corp(0.20, 0.20, 0, 0.3),
        "vat": vat(0.10, 0.05, "reduced", "reduced", "exempt", "standard", "standard", "standard", "exempt"),
        "benefits": benefits(shares(0.30, 0.01, 0.04, 0.04, 0.04, 0.07, 0.50),
                             0.30, 60, "cpi", 0.20, 6, 0.03, 0, 0.15, 0.10, 0.15, 0.50, 0.10),
    },
    # Very small formal tax base and tiny benefits (Ethiopia-like).
    "developing": {
        "label": "Developing-economy template (approximate)", "unit": "me",
        "income": income(0.80, bands((0, 0.10), (2.0, 0.20), (5.0, 0.30)), "frozen"),
        "payroll": {"employee": tiers((0.0, 0.05)), "employer": {"from": 0.0, "rate": 0.08}},
        "corp": corp(0.30, 0.30, 0, 0.2),
        "vat": vat(0.15, 0.07, "zero", "standard", "exempt", "standard", "standard", "standard", "exempt"),
        "benefits": benefits(shares(0.30, 0.00, 0.02, 0.02, 0.00, 0.30, 0.36),
                             0.10, 60, "neutral", 0.0, 3, 0.0, 0, 0.05, 0.0, 0.10, 0.50, 0.0),
    },
}

# ----------------------------------------------------------------------------------------------------------------------
# COUNTRIES
# ----------------------------------------------------------------------------------------------------------------------
COUNTRIES = {}

# --- United Kingdom: exemplar, exactly as specified --------------------------------------------------------------------
COUNTRIES["GBR"] = {
    "label": "United Kingdom 2024/25 (approximate)", "unit": "lcu", "meanEarnings": 35000,
    "income": {"allowance": 12570, "taperStart": 100000, "taperRate": 0.5,
               "bands": bands((0, 0.20), (37700, 0.40), (125140, 0.45)), "index": "frozen"},
    "payroll": {"employee": tiers((12570, 0.08), (50270, 0.02)), "employer": {"from": 9100, "rate": 0.138}},
    "corp": {"main": 0.25, "small": 0.19, "smallLimit": 50000, "expensing": 0.6},
    "vat": vat(0.20, 0.05, "zero", "reduced", "zero", "standard", "standard", "standard", "exempt"),
    "benefits": {"shares": shares(0.42, 0.03, 0.05, 0.18, 0.07, 0.13, 0.12),
                 "pension": {"level": 11500, "age": 66, "index": "triplelock"},
                 "unemployment": {"level": 4700, "months": 36}, "child": {"level": 1330, "threshold": 60000},
                 "disability": {"level": 7000}, "housing": {"level": 5500},
                 "meanstest": {"level": 7000, "taper": 0.55, "workAllowance": 5000}},
}

# --- United States: federal income tax, single filer; "VAT" is the average state and local sales tax --------------------
COUNTRIES["USA"] = {
    "label": "United States 2024 (federal income tax, single filer; sales tax as VAT; approximate)",
    "unit": "lcu", "meanEarnings": 66000,
    "income": income(14600, bands((0, 0.10), (11600, 0.12), (47150, 0.22), (100525, 0.24), (191950, 0.32),
                                  (243725, 0.35), (609350, 0.37)), "cpi"),
    # FICA: OASDI 6.2% to the 168,600 wage base plus Medicare 1.45% uncapped, each side
    "payroll": {"employee": tiers((0, 0.0765), (168600, 0.0145)), "employer": {"from": 0, "rate": 0.0765}},
    # federal 21% flat; 2024 bonus depreciation 60% of qualifying investment
    "corp": corp(0.21, 0.21, 0, 0.6),
    # average combined state+local sales tax about 6.5%; groceries, housing and health/education are untaxed; most services and
    # utilities fall outside the base ("reduced" is 0.0 here, so a reduced-rate switch is how a player would tax them)
    "vat": vat(0.065, 0.0, "zero", "reduced", "zero", "standard", "reduced", "standard", "zero"),
    "benefits": benefits(shares(0.50, 0.02, 0.04, 0.08, 0.03, 0.15, 0.18),
                         23000, 67, "cpi",          # average Social Security retirement benefit
                         23000, 6,                  # average weekly UI benefit (~$450) annualised; typically 26 weeks
                         2000, 200000,              # child tax credit; phase-out starts at $200k (single)
                         18500, 9000,               # SSDI average; HUD assistance per household
                         9200, 0.30, 4000),         # SNAP maximum for a 3-person household; 30% benefit reduction
}

# --- Germany: Einkommensteuertarif 2024 fitted by chord rates (exact at 5,401 / 20,401 / 40,401 / 55,156 / 266,221) ---
COUNTRIES["DEU"] = {
    "label": "Germany 2024 (approximate; Tarif 2024 fitted by chord rates, solidarity surcharge ignored)",
    "unit": "lcu", "meanEarnings": 50000,
    # from values are taxable income above the Grundfreibetrag (11,604): thresholds 17,005 / 32,005 / 52,005 / 66,760 / 277,825
    "income": income(11604, bands((0, 0.19), (5401, 0.267), (20401, 0.33), (40401, 0.393), (55156, 0.42),
                                  (266221, 0.45)), "cpi"),
    # health+care and pension+unemployment, ~20.5% to the 62,100 health ceiling; ~3% on the 62,100-90,600 band only partly
    "payroll": {"employee": tiers((0, 0.205), (62100, 0.03)), "employer": {"from": 0, "rate": 0.19}},
    "corp": corp(0.30, 0.30, 0, 0.3),               # 15% + soli + trade tax
    "vat": vat(0.19, 0.07, "reduced", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.52, 0.05, 0.08, 0.06, 0.02, 0.09, 0.18),
                         20000, 67, "earnings",     # Eckrente roughly 1,700 a month gross
                         15000, 12,                 # Arbeitslosengeld I
                         3000, 0,                   # Kindergeld 250 a month, universal
                         11000, 4600,               # Erwerbsminderungsrente; Wohngeld
                         7000, 0.80, 1200),         # Buergergeld; very high effective withdrawal above a 100 a month disregard
}

# --- France: barème 2024 (income of 2023), allowance = zero band; contributions effective after general reductions ----
COUNTRIES["FRA"] = {
    "label": "France 2024 (approximate; single, no quotient familial, 10% employment deduction ignored)",
    "unit": "lcu", "meanEarnings": 43000,
    # thresholds 28,797 / 82,341 / 177,106 less the 11,294 zero band
    "income": income(11294, bands((0, 0.11), (17503, 0.30), (71047, 0.41), (165812, 0.45)), "cpi"),
    # employee: CSG/CRDS + pension + Agirc-Arrco ~21%. Employer: ~42% statutory, ~33% effective after the general reductions at low pay
    "payroll": {"employee": tiers((0, 0.21)), "employer": {"from": 0, "rate": 0.33}},
    "corp": corp(0.25, 0.15, 42500, 0.2),             # reduced 15% rate on the first 42,500 for SMEs
    "vat": vat(0.20, 0.055, "reduced", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.54, 0.06, 0.07, 0.03, 0.03, 0.05, 0.22),
                         19500, 64, "cpi",          # average retirement pension ~1,600 a month; age phasing to 64
                         15000, 18,                 # ARE
                         1700, 75000,               # allocations familiales averaged over children; modulated above ~75k
                         12200, 2800,               # AAH; APL
                         7600, 0.40, 1000),         # RSA (~636 a month) with prime d'activite softening withdrawal
}

# --- Japan: national income tax; basic deduction 480k + minimum employment deduction 550k --------------------------------
COUNTRIES["JPN"] = {
    "label": "Japan 2024 (approximate; national income tax only, resident tax and surtax ignored)",
    "unit": "lcu", "meanEarnings": 4600000,
    "income": income(1030000, bands((0, 0.05), (1950000, 0.10), (3300000, 0.20), (6950000, 0.23), (9000000, 0.33),
                                    (18000000, 0.40), (40000000, 0.45)), "frozen"),
    # health + care + pension + employment insurance ~15% each side; pension ceiling 650k a month
    "payroll": {"employee": tiers((0, 0.15), (7800000, 0.06)), "employer": {"from": 0, "rate": 0.15}},
    "corp": corp(0.30, 0.21, 8000000, 0.3),            # effective national+local; SME rate on first 8m
    "vat": vat(0.10, 0.08, "reduced", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.62, 0.02, 0.08, 0.04, 0.01, 0.05, 0.18),
                         1500000, 65, "neutral",    # basic + employees' pension average; macro-economic slide
                         1900000, 11,               # basic allowance ~6,000 a day; up to 330 days
                         120000, 0,                 # child allowance 10,000 a month, universal from Oct 2024
                         1000000, 300000,
                         1200000, 0.80, 180000),    # livelihood protection
}

# --- China: IIT comprehensive income schedule, basic deduction 60,000 ------------------------------------------------------
COUNTRIES["CHN"] = {
    "label": "China 2024 (approximate; IIT comprehensive income, special additional deductions ignored)",
    "unit": "lcu", "meanEarnings": 90000,
    "income": income(60000, bands((0, 0.03), (36000, 0.10), (144000, 0.20), (300000, 0.25), (420000, 0.30),
                                  (660000, 0.35), (960000, 0.45)), "frozen"),
    # pension 8 + medical 2 + unemployment 0.5; contribution base capped at ~3x local average wage
    "payroll": {"employee": tiers((0, 0.105), (270000, 0.0)), "employer": {"from": 0, "rate": 0.28}},
    "corp": corp(0.25, 0.05, 3000000, 0.4),            # small low-profit enterprises effective 5% to 3m
    "vat": vat(0.13, 0.09, "reduced", "reduced", "exempt", "standard", "reduced", "standard", "exempt"),
    "benefits": benefits(shares(0.70, 0.03, 0.00, 0.01, 0.04, 0.05, 0.17),
                         30000, 60, "neutral",      # urban enterprise pension ~3,500 a month blended with rural basic pension
                         20000, 24,
                         0, 0,                      # no national child benefit in 2024
                         3000, 2500,
                         7000, 0.80, 3000),         # dibao
}

# --- India: new tax regime, FY2024-25 as amended by the July 2024 Budget ----------------------------------------------------
COUNTRIES["IND"] = {
    "label": "India FY2024-25 (approximate; new regime after July 2024 Budget, cess/surcharge/rebate ignored)",
    "unit": "lcu", "meanEarnings": 300000,
    # 300k exemption + 75k standard deduction; slabs 3-7L 5%, 7-10L 10%, 10-12L 15%, 12-15L 20%, >15L 30% (thresholds less 375k)
    "income": income(375000, bands((0, 0.05), (400000, 0.10), (700000, 0.15), (900000, 0.20), (1200000, 0.30)), "frozen"),
    # EPF 12% each side on a basic wage capped at 15,000 a month (180,000); employer expressed as an effective flat rate
    "payroll": {"employee": tiers((0, 0.12), (180000, 0.0)), "employer": {"from": 0, "rate": 0.07}},
    "corp": corp(0.2517, 0.25, 400000000, 0.2),        # section 115BAA 22% + surcharge + cess; 25% regime for turnover <= 400 crore
    "vat": vat(0.18, 0.05, "reduced", "reduced", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.50, 0.00, 0.00, 0.01, 0.04, 0.30, 0.15),
                         24000, 60, "neutral",      # EPS and NSAP blended; very low
                         24000, 3,                  # ESIC Atal Bimit Vyakti: 50% of wages for up to 90 days
                         0, 0,
                         3600, 4000,
                         25000, 0.50, 0),           # MGNREGA / food subsidy bundle for a poor household
}

# --- Brazil: IRPF monthly table (Feb 2024) annualised, INSS 2024 ------------------------------------------------------------
COUNTRIES["BRA"] = {
    "label": "Brazil 2024 (approximate; IRPF table from Feb 2024, INSS employee ~11% to the 93k ceiling, VAT as an average of ICMS/PIS/COFINS/IPI/ISS)",
    "unit": "lcu", "meanEarnings": 45000,
    # exempt to 2,259.20 a month; 7.5% to 2,826.65; 15% to 3,751.05; 22.5% to 4,664.68; 27.5% above (all x12, less the allowance)
    "income": income(27110, bands((0, 0.075), (6810, 0.15), (17900, 0.225), (28870, 0.275)), "frozen"),
    # INSS 7.5/9/12/14% progressive to 7,786.02 a month (93,432 a year); employer 20% (excludes FGTS, RAT, system S)
    "payroll": {"employee": tiers((0, 0.11), (93432, 0.0)), "employer": {"from": 0, "rate": 0.20}},
    "corp": corp(0.34, 0.24, 240000, 0.2),             # IRPJ 15% + 10% surcharge + CSLL 9%; no surcharge below 240k
    "vat": vat(0.17, 0.07, "reduced", "standard", "exempt", "reduced", "reduced", "standard", "exempt"),
    "benefits": benefits(shares(0.68, 0.04, 0.01, 0.04, 0.01, 0.06, 0.16),
                         20000, 65, "cpi",          # INSS floor = minimum wage (1,412 x 13)
                         21000, 5,                  # seguro-desemprego
                         1800, 22000,               # Bolsa Familia early-childhood benefit / salario-familia
                         18400, 4000,               # BPC; Minha Casa Minha Vida subsidies
                         8000, 0.50, 2000),         # Bolsa Familia
}

# --- Korea -----------------------------------------------------------------------------------------------------------------
COUNTRIES["KOR"] = {
    "label": "South Korea 2024 (approximate; national income tax, 42% and 40% brackets merged, local surtax ignored)",
    "unit": "lcu", "meanEarnings": 48000000,
    # basic deduction 1.5m + typical earned-income deduction ~12m; brackets 6/15/24/35/38/40/42/45 at 14m/50m/88m/150m/300m/500m/1bn
    "income": income(13500000, bands((0, 0.06), (14000000, 0.15), (50000000, 0.24), (88000000, 0.35),
                                     (150000000, 0.38), (300000000, 0.41), (1000000000, 0.45)), "frozen"),
    # employee: pension 4.5 + health 3.5 + LTC 0.5 + employment 0.9; pension ceiling ~74m, then only the others (~4.9%)
    "payroll": {"employee": tiers((0, 0.094), (74000000, 0.049)), "employer": {"from": 0, "rate": 0.105}},
    "corp": corp(0.24, 0.09, 200000000, 0.3),          # top statutory 24% (9% on the first 200m)
    "vat": vat(0.10, 0.05, "exempt", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.52, 0.04, 0.05, 0.02, 0.02, 0.08, 0.27),
                         8000000, 63, "cpi",        # basic pension 4.0m + NPS average; NPS age phasing to 65
                         23000000, 9,               # 60% of wage with a floor at 80% of minimum wage; 120-270 days
                         1200000, 0,                # child allowance 100,000 a month, universal
                         5000000, 4000000,
                         8500000, 0.70, 1000000),   # basic livelihood security
}

# --- Australia: Stage 3 rates from 1 July 2024 ---------------------------------------------------------------------------------
COUNTRIES["AUS"] = {
    "label": "Australia 2024/25 (approximate; Stage 3 rates; employer = superannuation guarantee 11.5%, a funded private contribution)",
    "unit": "lcu", "meanEarnings": 90000,
    # 18,200 tax-free; 16% to 45,000; 30% to 135,000; 37% to 190,000; 45% above
    "income": income(18200, bands((0, 0.16), (26800, 0.30), (116800, 0.37), (171800, 0.45)), "frozen"),
    # employee side is the 2% Medicare levy (above ~26k); state payroll tax ignored
    "payroll": {"employee": tiers((26000, 0.02)), "employer": {"from": 0, "rate": 0.115}},
    "corp": corp(0.30, 0.25, 5000000, 0.3),            # base rate entity 25% for turnover < 50m (~5m profit)
    "vat": vat(0.10, 0.05, "zero", "standard", "exempt", "standard", "standard", "standard", "zero"),
    "benefits": benefits(shares(0.30, 0.07, 0.12, 0.25, 0.03, 0.07, 0.16),
                         29700, 67, "earnings",     # Age Pension single ~1,144 a fortnight
                         19500, 36,                 # JobSeeker single ~749 a fortnight; no time limit
                         5700, 62000,               # Family Tax Benefit A per child; income test from ~62k
                         29700, 4800,               # Disability Support Pension; Commonwealth Rent Assistance
                         25000, 0.40, 5700),        # Parenting Payment
}

# --- Canada: federal personal income tax only --------------------------------------------------------------------------------
COUNTRIES["CAN"] = {
    "label": "Canada 2024 (approximate; federal income tax only, provincial taxes ignored; CPP/EI; combined GST/HST/PST average)",
    "unit": "lcu", "meanEarnings": 64000,
    # basic personal amount 15,705; brackets 15/20.5/26/29/33% at 55,867/111,733/173,205/246,752 less the allowance
    "income": income(15705, bands((0, 0.15), (40162, 0.205), (96028, 0.26), (157500, 0.29), (231047, 0.33)), "cpi"),
    # CPP 5.95 + EI 1.66 from 3,500 to ~70k (CPP2 4% above the 68,500 YMPE ignored); employer ~ CPP 5.95 + EI 2.32 effective
    "payroll": {"employee": tiers((3500, 0.0761), (70000, 0.0)), "employer": {"from": 3500, "rate": 0.075}},
    "corp": corp(0.265, 0.12, 500000, 0.4),            # federal + average provincial; small business rate to 500k
    "vat": vat(0.12, 0.05, "zero", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.55, 0.07, 0.10, 0.04, 0.03, 0.08, 0.13),
                         17500, 65, "cpi",          # OAS ~8,700 + average CPP
                         29000, 10,                 # EI regular ~570 a week; up to 45 weeks
                         8000, 36500,               # Canada Child Benefit per child under 6; income-tested from 36.5k
                         12000, 3500,
                         12500, 0.50, 5000),        # GIS / social assistance
}

# --- South Africa: 2024/25 --------------------------------------------------------------------------------------------------
COUNTRIES["ZAF"] = {
    "label": "South Africa 2024/25 (approximate)", "unit": "lcu", "meanEarnings": 300000,
    # R237,100 / 370,500 / 512,800 / 673,000 / 857,900 / 1,817,000 less the 95,750 tax threshold (primary rebate 17,235 / 18%)
    "income": income(95750, bands((0, 0.18), (141350, 0.26), (274750, 0.31), (417050, 0.36), (577250, 0.39),
                                  (762150, 0.41), (1721250, 0.45)), "frozen"),
    # UIF 1% + 1% to R17,712 a month; SDL 1% -> ~1.5% effective employer
    "payroll": {"employee": tiers((0, 0.01), (212544, 0.0)), "employer": {"from": 0, "rate": 0.015}},
    "corp": corp(0.27, 0.07, 365000, 0.3),
    "vat": vat(0.15, 0.075, "zero", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.34, 0.05, 0.28, 0.12, 0.01, 0.12, 0.08),
                         25100, 60, "cpi",          # Older Person's grant R2,090 a month
                         50000, 8,                  # UIF ~38-60% of earnings; 238 days
                         6100, 60000,               # Child Support Grant R510 a month; means-tested
                         25100, 6000,
                         4400, 0.80, 0),            # SRD grant R370 a month
}

# --- Norway: 22% general income + bracket tax (trinnskatt) 2024 ---------------------------------------------------------------
COUNTRIES["NOR"] = {
    "label": "Norway 2024 (approximate; 22% general income tax plus trinnskatt, merged on one base)",
    "unit": "lcu", "meanEarnings": 700000,
    # personal allowance 88,250 + minstefradrag cap 104,450; trinnskatt 1.7/4.0/13.6/16.6/17.6% from 208k/293k/670k/938k/1.35m
    "income": income(192700, bands((0, 0.22), (15350, 0.237), (100150, 0.26), (477300, 0.356), (745200, 0.386),
                                   (1157300, 0.396)), "cpi"),
    "payroll": {"employee": tiers((69650, 0.078)), "employer": {"from": 0, "rate": 0.141}},
    "corp": corp(0.22, 0.22, 0, 0.3),
    "vat": vat(0.25, 0.15, "reduced", "standard", "exempt", "reduced", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.45, 0.03, 0.05, 0.22, 0.01, 0.03, 0.21),
                         250000, 67, "earnings",    # folketrygd old-age pension average
                         300000, 24,                # dagpenger 62.4% of earnings up to 6G
                         21200, 0,                  # barnetrygd 1,766 a month, universal
                         280000, 35000,
                         100000, 0.80, 10000),      # social assistance
}

# --- Singapore ------------------------------------------------------------------------------------------------------------------
COUNTRIES["SGP"] = {
    "label": "Singapore YA2024 (approximate; resident rates, CPF 20%/17% to the S$6,800 monthly ceiling, GST 9%)",
    "unit": "lcu", "meanEarnings": 80000,
    # chargeable income less the first 20,000 at 0%; 2% and 3.5% merged; 18-20% merged; 22-23% merged; 24% above 1m
    "income": income(20000, bands((0, 0.0275), (20000, 0.07), (60000, 0.115), (100000, 0.15), (140000, 0.19),
                                  (300000, 0.225), (980000, 0.24)), "frozen"),
    # CPF 20% employee / 17% employer to S$81,600; employer expressed as an effective flat rate
    "payroll": {"employee": tiers((0, 0.20), (81600, 0.0)), "employer": {"from": 0, "rate": 0.15}},
    "corp": corp(0.17, 0.083, 200000, 0.4),            # partial exemption on the first 200k
    "vat": vat(0.09, 0.045, "standard", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.10, 0.01, 0.12, 0.03, 0.20, 0.10, 0.44),
                         6000, 65, "cpi",           # Silver Support and related top-ups; main system is funded (CPF)
                         12000, 6,                  # SkillsFuture Jobseeker Support S$1,000 a month for 6 months
                         2000, 0,
                         4800, 3500,
                         9600, 0.70, 0),            # ComCare
}

# --- Switzerland ------------------------------------------------------------------------------------------------------------------
COUNTRIES["CHE"] = {
    "label": "Switzerland 2024 (approximate; federal plus average cantonal/communal stand-in, AHV/IV/EO/ALV only)",
    "unit": "lcu", "meanEarnings": 90000,
    # stylised combined schedule calibrated to ~10% average rate at the average wage; BVG occupational pension excluded
    "income": income(14500, bands((0, 0.08), (30000, 0.15), (70000, 0.22), (140000, 0.30), (300000, 0.36),
                                  (700000, 0.40)), "cpi"),
    "payroll": {"employee": tiers((0, 0.064)), "employer": {"from": 0, "rate": 0.07}},
    "corp": corp(0.15, 0.15, 0, 0.3),                  # federal 8.5% + average cantonal and communal
    "vat": vat(0.081, 0.026, "reduced", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.55, 0.07, 0.07, 0.13, 0.01, 0.07, 0.10),
                         29000, 65, "earnings",     # AHV maximum 2,450 a month
                         63000, 19,                 # ALV 70-80% of insured earnings; 400 daily allowances
                         2800, 0,
                         22000, 6000,
                         20000, 0.80, 6000),        # social assistance
}

# --- Poland ------------------------------------------------------------------------------------------------------------------------
COUNTRIES["POL"] = {
    "label": "Poland 2024 (approximate; 30,000 tax-free amount, 12%/32%, ZUS plus 9% health)",
    "unit": "lcu", "meanEarnings": 96000,
    # tax-free amount 30,000 (credit 3,600); 12% to 120,000 (x = 90,000); 32% above
    "income": income(30000, bands((0, 0.12), (90000, 0.32)), "frozen"),
    # employee ZUS 13.71% + health 9% of net (~7.75% of gross) to the 234,720 pension cap; health only above
    "payroll": {"employee": tiers((0, 0.215), (234720, 0.0775)), "employer": {"from": 0, "rate": 0.205}},
    "corp": corp(0.19, 0.09, 900000, 0.3),             # 9% for small taxpayers (turnover < EUR 2m)
    "vat": vat(0.23, 0.08, "reduced", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.60, 0.01, 0.13, 0.09, 0.01, 0.03, 0.13),
                         44000, 65, "cpi",          # average old-age pension incl. 13th and 14th pensions
                         17000, 6,
                         9600, 0,                   # 800+ per child per month, universal
                         29000, 3600,
                         8600, 0.80, 0),
}

# --- Mexico: annual ISR table 2024 merged to seven bands ----------------------------------------------------------------------------
COUNTRIES["MEX"] = {
    "label": "Mexico 2024 (approximate; annual ISR table merged to 7 bands by chord rates, employment subsidy ignored)",
    "unit": "lcu", "meanEarnings": 230000,
    # exact at 75,985 / 185,853 / 374,838 / 590,796 / 1,127,927 / 4,511,707
    "income": income(0, bands((0, 0.059), (75985, 0.1385), (185853, 0.2136), (374838, 0.2352), (590796, 0.30),
                              (1127927, 0.338), (4511707, 0.35)), "cpi"),
    # IMSS + SAR employee ~3%; employer ~18% including INFONAVIT 5% and state payroll taxes
    "payroll": {"employee": tiers((0, 0.03)), "employer": {"from": 0, "rate": 0.18}},
    "corp": corp(0.30, 0.30, 0, 0.4),
    "vat": vat(0.16, 0.08, "zero", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.65, 0.00, 0.02, 0.02, 0.02, 0.08, 0.21),
                         40000, 65, "cpi",          # Bienestar universal pension 6,000 bimonthly blended with IMSS pensions
                         0, 3,                      # no federal unemployment insurance
                         9600, 0,
                         18600, 6000,
                         9000, 0.50, 0),
}

# --- Turkey: 2024 wage-income tariff with the minimum-wage exemption folded into the allowance ----------------------------------------
COUNTRIES["TUR"] = {
    "label": "Turkey 2024 (approximate; wage tariff above the minimum-wage exemption, stamp duty ignored)",
    "unit": "lcu", "meanEarnings": 450000,
    # tax on wages up to the gross minimum wage (20,002.5 x 12) is credited away; 27% to 580,000; 35% to 3,000,000; 40% above
    "income": income(240000, bands((0, 0.27), (340000, 0.35), (2760000, 0.40)), "frozen"),
    # SGK employee 14% + unemployment 1% to the 7.5x minimum-wage ceiling (150,019 a month); employer ~20% after incentives
    "payroll": {"employee": tiers((0, 0.15), (1800000, 0.0)), "employer": {"from": 0, "rate": 0.20}},
    "corp": corp(0.25, 0.25, 0, 0.3),
    "vat": vat(0.20, 0.10, "reduced", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.70, 0.02, 0.01, 0.04, 0.01, 0.06, 0.16),
                         140000, 60, "cpi",         # minimum pension 10,000 a month (Jul 2024) and above
                         96000, 10,                 # 40% of gross wage, capped at 80% of minimum wage; up to 300 days
                         3000, 120000,
                         60000, 6000,
                         30000, 0.70, 0),
}

# --- Indonesia ----------------------------------------------------------------------------------------------------------------------------
COUNTRIES["IDN"] = {
    "label": "Indonesia 2024 (approximate; PTKP single, BPJS; 35% band above IDR 5bn omitted)",
    "unit": "lcu", "meanEarnings": 40000000,
    # PTKP 54m; 5% to 60m; 15% to 250m; 25% to 500m; 30% above. The 35% band above 5bn (~125 ME) is dropped: beyond the 40 ME band cap
    "income": income(54000000, bands((0, 0.05), (60000000, 0.15), (250000000, 0.25), (500000000, 0.30)), "frozen"),
    "payroll": {"employee": tiers((0, 0.04)), "employer": {"from": 0, "rate": 0.105}},
    "corp": corp(0.22, 0.22, 0, 0.2),
    "vat": vat(0.11, 0.055, "exempt", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.30, 0.01, 0.04, 0.01, 0.02, 0.25, 0.37),
                         12000000, 58, "cpi",
                         18000000, 6,               # JKP 45% x3 months then 25% x3 months
                         1500000, 40000000,         # PKH child component, targeted at poor households
                         3600000, 1500000,
                         3000000, 0.50, 0),
}

# --- Chile: impuesto global complementario 2024 in UTA (UTA ~790,000 pesos) -----------------------------------------------------------------
COUNTRIES["CHL"] = {
    "label": "Chile 2024 (approximate; global complementary tax with UTA ~ 790,000 pesos, AFP contributions)",
    "unit": "lcu", "meanEarnings": 12000000,
    # exempt to 13.5 UTA; then 4% to 30; 8% to 50; 13.5% to 70; 23% to 90; 30.4% to 120; 35% to 310; 40% above (UTA, less the allowance)
    "income": income(10670000, bands((0, 0.04), (13000000, 0.08), (28800000, 0.135), (44600000, 0.23),
                                     (60400000, 0.304), (84100000, 0.35), (234200000, 0.40)), "cpi"),
    # AFP 10% + commission + health 7% + unemployment insurance ~18.5% to ~84 UF a month (~38m a year); employer SIS/unemployment/accident ~5%
    "payroll": {"employee": tiers((0, 0.185), (38000000, 0.0)), "employer": {"from": 0, "rate": 0.05}},
    "corp": corp(0.27, 0.25, 300000000, 0.3),          # general 27%; Pro-Pyme 25%
    "vat": vat(0.19, 0.095, "standard", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.55, 0.03, 0.06, 0.05, 0.04, 0.09, 0.18),
                         2600000, 65, "cpi",        # Pension Garantizada Universal ~214,000 a month
                         5400000, 5,
                         180000, 9000000,
                         2500000, 1800000,
                         1200000, 0.50, 0),
}

# --- Russia -------------------------------------------------------------------------------------------------------------------------------------
COUNTRIES["RUS"] = {
    "label": "Russia 2024 (approximate; 13%/15% personal income tax, unified insurance contributions paid by employers)",
    "unit": "lcu", "meanEarnings": 1050000,
    "income": income(0, bands((0, 0.13), (5000000, 0.15)), "frozen"),
    "payroll": {"employee": tiers((0, 0.0)), "employer": {"from": 0, "rate": 0.30}},
    "corp": corp(0.20, 0.20, 0, 0.2),
    "vat": vat(0.20, 0.10, "reduced", "standard", "exempt", "standard", "standard", "standard", "exempt"),
    "benefits": benefits(shares(0.72, 0.01, 0.07, 0.04, 0.01, 0.04, 0.11),
                         265000, 63, "cpi",         # average old-age pension ~22,000 a month; men's age phasing to 65
                         80000, 12,                 # maximum 8,000 a month
                         80000, 540000,
                         190000, 25000,
                         150000, 0.50, 0),
}

# --- Resource and developing countries kept in "me" units: dimensionless overrides plus ME-relative bands -------------------------------
COUNTRIES["ARG"] = {
    "label": "Argentina 2024 (approximate; amounts are template multiples of mean earnings, rates are statutory)",
    "unit": "me",
    "income": {"bands": bands((0, 0.05), (0.4, 0.12), (0.9, 0.19), (1.5, 0.27), (2.5, 0.31), (4.0, 0.35)),
               "index": "earnings"},
    "payroll": {"employee": tiers((0.0, 0.17), (2.0, 0.0)), "employer": {"from": 0.0, "rate": 0.22}},
    "corp": {"main": 0.35, "small": 0.35},
    "vat": {"standard": 0.21, "reduced": 0.105},
    "benefits": {"shares": shares(0.65, 0.01, 0.10, 0.05, 0.00, 0.07, 0.12),
                 "pension": {"age": 65, "index": "cpi"}},
}
COUNTRIES["EGY"] = {
    "label": "Egypt 2024 (approximate; amounts are multiples of an assumed ~100,000 pound mean wage, rates statutory)",
    "unit": "me",
    # 40,000 exempt then 10/15/20/22.5/25/27.5% at 55k/70k/200k/400k/1.2m (x = income less 0.4 ME)
    "income": {"bands": bands((0, 0.10), (0.15, 0.15), (0.30, 0.20), (1.6, 0.225), (3.6, 0.25), (11.6, 0.275)),
               "index": "frozen"},
    "payroll": {"employee": tiers((0.0, 0.11)), "employer": {"from": 0.0, "rate": 0.19}},
    "corp": {"main": 0.225, "small": 0.225},
    "vat": {"standard": 0.14, "reduced": 0.05, "categories": {"food": "exempt"}},
    "benefits": {"shares": shares(0.50, 0.00, 0.01, 0.02, 0.00, 0.15, 0.32),
                 "pension": {"age": 60, "index": "cpi"}},
}
COUNTRIES["VNM"] = {
    "label": "Vietnam 2024 (approximate; amounts are multiples of an assumed ~8m dong a month mean wage, rates statutory)",
    "unit": "me",
    # personal deduction 11m a month (1.4 ME); brackets 5/10/18/32/52/80m a month of taxable income
    "income": {"allowance": 1.4,
               "bands": bands((0, 0.05), (0.625, 0.10), (1.25, 0.15), (2.25, 0.20), (4.0, 0.25), (6.5, 0.30), (10.0, 0.35)),
               "index": "frozen"},
    "payroll": {"employee": tiers((0.0, 0.105), (4.5, 0.0)), "employer": {"from": 0.0, "rate": 0.215}},
    "corp": {"main": 0.20, "small": 0.20},
    "vat": {"standard": 0.10, "reduced": 0.05},
    "benefits": {"shares": shares(0.62, 0.03, 0.01, 0.02, 0.00, 0.06, 0.26),
                 "pension": {"age": 60, "index": "cpi"}},
}
COUNTRIES["SAU"] = {
    "label": "Saudi Arabia 2024 (approximate; no personal income tax, GOSI rates for nationals)",
    "unit": "me",
    "income": {"allowance": 0.0, "bands": bands((0, 0.0)), "index": "frozen"},
    "payroll": {"employee": tiers((0.0, 0.0975)), "employer": {"from": 0.0, "rate": 0.1175}},
    "corp": {"main": 0.20, "small": 0.20},
    "vat": {"standard": 0.15, "reduced": 0.075,
            "categories": {"food": "standard", "energy": "standard", "health_edu": "zero"}},
    "benefits": {"shares": shares(0.30, 0.02, 0.02, 0.05, 0.06, 0.12, 0.43),
                 "pension": {"age": 60, "index": "cpi"}},
}
COUNTRIES["NGA"] = {
    "label": "Nigeria 2024 (approximate; amounts are multiples of an assumed ~1.3m naira mean wage, PAYE rates statutory)",
    "unit": "me",
    # PAYE 7/11/15/19/21/24% at 0/300k/600k/1.1m/1.6m/3.2m of taxable pay (after consolidated relief ~0.3 ME)
    "income": {"allowance": 0.30,
               "bands": bands((0, 0.07), (0.23, 0.11), (0.46, 0.15), (0.85, 0.19), (1.23, 0.21), (2.46, 0.24)),
               "index": "frozen"},
    "payroll": {"employee": tiers((0.0, 0.105)), "employer": {"from": 0.0, "rate": 0.12}},
    "corp": {"main": 0.30, "small": 0.30},
    "vat": {"standard": 0.075, "reduced": 0.04, "categories": {"food": "exempt"}},
    "benefits": {"shares": shares(0.55, 0.00, 0.03, 0.01, 0.00, 0.15, 0.26),
                 "pension": {"level": 0.15, "age": 60, "index": "neutral"},
                 "meanstest": {"level": 0.08}},
}
COUNTRIES["ARE"] = {
    "label": "United Arab Emirates 2024 (approximate; no personal income tax, 9% corporate tax above AED 375,000)",
    "unit": "me",
    "income": {"allowance": 0.0, "bands": bands((0, 0.0)), "index": "frozen"},
    "payroll": {"employee": tiers((0.0, 0.05)), "employer": {"from": 0.0, "rate": 0.125}},
    "corp": {"main": 0.09, "small": 0.0, "smallLimit": 3.0, "expensing": 0.2},
    "vat": {"standard": 0.05, "reduced": 0.025,
            "categories": {"food": "standard", "energy": "standard", "health_edu": "zero"}},
    "benefits": {"shares": shares(0.35, 0.01, 0.02, 0.02, 0.10, 0.10, 0.40),
                 "pension": {"level": 0.50, "age": 60, "index": "cpi"},
                 "unemployment": {"level": 0.35, "months": 3}},
}
COUNTRIES["ETH"] = {
    "label": "Ethiopia 2024 (approximate; amounts are multiples of an assumed ~7,000 birr a month mean wage, rates statutory)",
    "unit": "me",
    # 2,000 birr a month exempt, then 15/20/25/30/35% at 2k/5k/8k/12k of taxable pay
    "income": {"allowance": 0.29, "bands": bands((0, 0.15), (0.29, 0.20), (0.71, 0.25), (1.14, 0.30), (1.71, 0.35)),
               "index": "frozen"},
    "payroll": {"employee": tiers((0.0, 0.07)), "employer": {"from": 0.0, "rate": 0.11}},
    "corp": {"main": 0.30, "small": 0.30},
    "vat": {"standard": 0.15, "reduced": 0.075, "categories": {"food": "exempt"}},
    "benefits": {"shares": shares(0.25, 0.00, 0.00, 0.01, 0.00, 0.60, 0.14),
                 "pension": {"level": 0.08, "age": 60, "index": "neutral"},
                 "meanstest": {"level": 0.10}},
}


# ----------------------------------------------------------------------------------------------------------------------
# merge and unit helpers (the loader in the game performs the same deep merge)
# ----------------------------------------------------------------------------------------------------------------------
def deep_merge(base, over):
    out = copy.deepcopy(base)
    for k, v in over.items():
        if isinstance(v, dict) and isinstance(out.get(k), dict):
            out[k] = deep_merge(out[k], v)
        else:
            out[k] = copy.deepcopy(v)
    return out


def to_me(code):
    """Return a copy with every amount converted to multiples of mean earnings."""
    c = copy.deepcopy(code)
    if c["unit"] != "lcu":
        return c
    m = float(c["meanEarnings"])
    inc, pay, cp, ben = c["income"], c["payroll"], c["corp"], c["benefits"]
    inc["allowance"] /= m
    inc["taperStart"] /= m
    for b in inc["bands"]:
        b["from"] /= m
    for t in pay["employee"]:
        t["from"] /= m
    pay["employer"]["from"] /= m
    cp["smallLimit"] /= m
    ben["pension"]["level"] /= m
    ben["unemployment"]["level"] /= m
    ben["child"]["level"] /= m
    ben["child"]["threshold"] /= m
    ben["disability"]["level"] /= m
    ben["housing"]["level"] /= m
    ben["meanstest"]["level"] /= m
    ben["meanstest"]["workAllowance"] /= m
    c["unit"] = "me"
    return c


def tax_on(taxable, bnds):
    tax = 0.0
    for i, b in enumerate(bnds):
        hi = bnds[i + 1]["from"] if i + 1 < len(bnds) else math.inf
        if taxable > b["from"]:
            tax += (min(taxable, hi) - b["from"]) * b["rate"]
    return tax


def income_tax(e, inc):
    allowance = inc["allowance"]
    if inc["taperRate"] > 0 and e > inc["taperStart"]:
        allowance = max(0.0, allowance - inc["taperRate"] * (e - inc["taperStart"]))
    return tax_on(max(0.0, e - allowance), inc["bands"])


def employee_payroll(e, tier_list):
    total = 0.0
    for i, t in enumerate(tier_list):
        hi = tier_list[i + 1]["from"] if i + 1 < len(tier_list) else math.inf
        if e > t["from"]:
            total += (min(e, hi) - t["from"]) * t["rate"]
    return total


def earnings_grid(n=100, sigma=0.7):
    nd = NormalDist()
    mu = -0.5 * sigma * sigma  # mean of the lognormal is exactly 1 ME
    return [math.exp(mu + sigma * nd.inv_cdf((i + 0.5) / n)) for i in range(n)]


GRID = earnings_grid()


def stats(me_code):
    tot_e = sum(GRID)
    inc = sum(income_tax(e, me_code["income"]) for e in GRID) / tot_e
    emp = sum(employee_payroll(e, me_code["payroll"]["employee"]) for e in GRID) / tot_e
    er = me_code["payroll"]["employer"]
    empr = sum(max(0.0, e - er["from"]) * er["rate"] for e in GRID) / tot_e
    return inc, emp + empr


# ----------------------------------------------------------------------------------------------------------------------
# validation
# ----------------------------------------------------------------------------------------------------------------------
def get_path(d, path):
    cur = d
    for p in path.split("."):
        if not isinstance(cur, dict) or p not in cur:
            return None, False
        cur = cur[p]
    return cur, True


AMOUNT_PATHS = [
    "income.allowance", "income.taperStart", "income.taperRate", "income.bands", "income.index",
    "payroll.employee", "payroll.employer.from", "payroll.employer.rate",
    "corp.main", "corp.small", "corp.smallLimit", "corp.expensing",
    "vat.standard", "vat.reduced",
] + ["vat.categories." + c for c in CATEGORIES] + [
    "benefits.shares." + k for k in SHARE_KEYS
] + [
    "benefits.pension.level", "benefits.pension.age", "benefits.pension.index",
    "benefits.unemployment.level", "benefits.unemployment.months",
    "benefits.child.level", "benefits.child.threshold",
    "benefits.disability.level", "benefits.housing.level",
    "benefits.meanstest.level", "benefits.meanstest.taper", "benefits.meanstest.workAllowance",
]


def check_code(cid, code, errors):
    """Structural and range checks on a fully merged code (any unit)."""

    def err(msg):
        errors.append(f"{cid}: {msg}")

    for p in AMOUNT_PATHS:
        _, ok = get_path(code, p)
        if not ok:
            err(f"missing field {p}")
            return False
    inc = code["income"]
    bnds = inc["bands"]
    if not (1 <= len(bnds) <= 7):
        err(f"{len(bnds)} income bands (need 1-7)")
    if bnds[0]["from"] != 0:
        err("first income band must start at 0")
    for i, b in enumerate(bnds):
        if not (0.0 <= b["rate"] <= 0.75):
            err(f"band rate {b['rate']} outside [0, 0.75]")
        if i and b["from"] <= bnds[i - 1]["from"]:
            err("income bands not strictly increasing")
    if inc["index"] not in INCOME_INDEX:
        err(f"bad income index {inc['index']}")
    if not (0.0 <= inc["taperRate"] <= 1.0):
        err("taperRate outside [0,1]")
    if (inc["taperRate"] == 0) != (inc["taperStart"] == 0):
        err("taperStart and taperRate must both be 0 or both positive")
    if inc["allowance"] < 0:
        err("negative allowance")
    emp = code["payroll"]["employee"]
    if not (1 <= len(emp) <= 2):
        err("employee payroll needs 1-2 tiers")
    for i, t in enumerate(emp):
        if not (0.0 <= t["rate"] <= 0.5):
            err(f"employee payroll rate {t['rate']} outside [0, 0.5]")
        if i and t["from"] <= emp[i - 1]["from"]:
            err("employee tiers not increasing")
    if not (0.0 <= code["payroll"]["employer"]["rate"] <= 0.5):
        err("employer payroll rate outside [0, 0.5]")
    c = code["corp"]
    if not (0.0 <= c["main"] <= 0.5) or not (0.0 <= c["small"] <= c["main"] + 1e-9):
        err("corporate rates out of range or small > main")
    if not (0.0 <= c["expensing"] <= 1.0):
        err("expensing outside [0,1]")
    if c["small"] == c["main"] and c["smallLimit"] != 0:
        err("smallLimit should be 0 when there is no small rate")
    v = code["vat"]
    for key in ("standard", "reduced"):
        if not (0.0 <= v[key] <= 0.30):
            err(f"VAT {key} {v[key]} outside [0, 0.30]")
    if v["reduced"] > v["standard"]:
        err("reduced VAT above standard")
    for cat in CATEGORIES:
        if v["categories"][cat] not in TREATMENTS:
            err(f"bad VAT treatment for {cat}")
    ben = code["benefits"]
    total = sum(ben["shares"][k] for k in SHARE_KEYS)
    if abs(total - 1.0) > 0.005:
        err(f"benefit shares sum to {total:.4f}")
    if any(ben["shares"][k] < 0 for k in SHARE_KEYS):
        err("negative benefit share")
    if ben["pension"]["index"] not in PENSION_INDEX:
        err(f"bad pension index {ben['pension']['index']}")
    if not (55 <= ben["pension"]["age"] <= 70):
        err("pension age outside 55-70")
    if not (0 <= ben["unemployment"]["months"] <= 36):
        err("unemployment months outside 0-36")
    if not (0.0 <= ben["meanstest"]["taper"] <= 1.0):
        err("means-test taper outside [0,1]")
    for path in ("pension.level", "unemployment.level", "child.level", "child.threshold", "disability.level",
                 "housing.level", "meanstest.level", "meanstest.workAllowance"):
        val, _ = get_path(ben, path)
        if val < 0:
            err(f"negative benefit amount {path}")
    return True


# Bounds the game engine applies when it loads a code (mirrors FiscalParams): amounts are multiples of mean earnings.
ENGINE_BOUNDS = {
    "income.allowance": (0, 3), "income.taperStart": (0, 40), "payroll.employer.from": (0, 3), "corp.smallLimit": (0, 2000),
    "benefits.pension.level": (0, 1.5), "benefits.unemployment.level": (0, 1), "benefits.child.level": (0, 0.5),
    "benefits.child.threshold": (0, 15), "benefits.disability.level": (0, 1), "benefits.housing.level": (0, 0.8),
    "benefits.meanstest.level": (0, 1), "benefits.meanstest.taper": (0, 0.9), "benefits.meanstest.workAllowance": (0, 1.5),
    "payroll.employer.rate": (0, 0.5), "corp.main": (0, 0.45), "corp.small": (0, 0.45), "vat.standard": (0, 0.30),
    "vat.reduced": (0, 0.30), "benefits.pension.age": (55, 75), "benefits.unemployment.months": (1, 36),
}


def check_engine_bounds(cid, me, errors):
    for path, (lo, hi) in ENGINE_BOUNDS.items():
        val, ok = get_path(me, path)
        if ok and not (lo - 1e-9 <= val <= hi + 1e-9):
            errors.append(f"{cid}: {path} = {val:.4g} (in ME) outside the engine range [{lo}, {hi}]")
    prev = 0.0
    for i, b in enumerate(me["income"]["bands"]):
        if i and not (0.02 <= b["from"] <= 40 and b["from"] >= prev + 0.01):
            errors.append(f"{cid}: band {i + 1} starts at {b['from']:.4g} ME (engine range 0.02-40, spacing 0.01)")
        prev = b["from"]
    emp = me["payroll"]["employee"]
    if not (0 <= emp[0]["from"] <= 3):
        errors.append(f"{cid}: employee contributions start at {emp[0]['from']:.4g} ME (engine range 0-3)")
    if len(emp) > 1 and not (emp[0]["from"] + 0.05 <= emp[1]["from"] <= 40):
        errors.append(f"{cid}: employee upper limit {emp[1]['from']:.4g} ME must be 0.05-40 ME above the lower threshold")
    for t in emp:
        if t["rate"] > 0.4:
            errors.append(f"{cid}: employee rate {t['rate']} above the engine cap 0.4")


def build():
    archetypes = {name: ARCHETYPES[name] for name in ARCHETYPE_NAMES}
    countries = {cid: COUNTRIES[cid] for cid, _ in ROSTER if cid in COUNTRIES}
    return {"version": 1, "note": NOTE, "archetypes": archetypes, "countries": countries}


def validate(data):
    errors = []
    if data.get("version") != 1:
        errors.append("version must be 1")
    arch = data["archetypes"]
    for name in ARCHETYPE_NAMES:
        if name not in arch:
            errors.append(f"archetype {name} missing")
            continue
        if arch[name]["unit"] != "me":
            errors.append(f"archetype {name} must use unit 'me'")
        if check_code(f"[{name}]", arch[name], errors):
            check_engine_bounds(f"[{name}]", arch[name], errors)
    countries = data["countries"]
    for cid, a in ROSTER:
        if cid not in countries:
            errors.append(f"{cid}: not present")
    for cid in countries:
        if cid not in dict(ROSTER):
            errors.append(f"{cid}: not in roster")
    for cid in NAMED_DETAILED:
        if cid in countries and countries[cid].get("unit") != "lcu":
            errors.append(f"{cid}: named country must use unit 'lcu'")

    rows = []
    for cid, a in ROSTER:
        if cid not in countries or a not in arch:
            continue
        raw = countries[cid]
        unit = raw.get("unit")
        if unit not in ("lcu", "me"):
            errors.append(f"{cid}: unit must be 'lcu' or 'me'")
            continue
        if unit == "lcu":
            if not isinstance(raw.get("meanEarnings"), (int, float)) or raw["meanEarnings"] <= 0:
                errors.append(f"{cid}: unit 'lcu' needs meanEarnings")
                continue
            # an lcu entry must not inherit any amount from a template that is in ME
            for p in AMOUNT_PATHS:
                _, ok = get_path(raw, p)
                if not ok:
                    errors.append(f"{cid}: lcu entry lacks {p} (would inherit an ME value)")
        else:
            if "meanEarnings" in raw:
                errors.append(f"{cid}: unit 'me' entries must omit meanEarnings")
        merged = deep_merge(arch[a], raw)
        merged["unit"] = unit
        if not check_code(cid, merged, errors):
            continue
        me = to_me(merged)
        check_engine_bounds(cid, me, errors)
        inc_rate, pay_yield = stats(me)
        if not (0.0 <= inc_rate <= 0.40):
            errors.append(f"{cid}: average income-tax rate {inc_rate:.3f} outside [0, 0.40]")
        if not (0.0 <= pay_yield <= 0.55):
            errors.append(f"{cid}: payroll yield {pay_yield:.3f} outside [0, 0.55]")
        top = merged["income"]["bands"][-1]["rate"]
        rows.append((cid, a, unit, inc_rate, top, pay_yield, merged["vat"]["standard"], merged["corp"]["main"],
                     merged["benefits"]["pension"]["age"]))

    print(f"{'id':4} {'archetype':10} {'unit':4} {'avg inc':>8} {'top':>6} {'payroll':>8} {'VAT':>6} {'corp':>6} {'pens age':>8}")
    for cid, a, unit, ir, top, py, vs, cm, age in rows:
        print(f"{cid:4} {a:10} {unit:4} {ir*100:7.1f}% {top*100:5.1f}% {py*100:7.1f}% {vs*100:5.1f}% {cm*100:5.1f}% {age:8d}")
    return errors


def main():
    data = build()
    errors = validate(data)
    if errors:
        print("\nVALIDATION FAILED", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        sys.exit(1)
    out = pathlib.Path(__file__).resolve().parents[2] / "data" / "taxcodes.json"
    out.write_text(json.dumps(data, indent=1, sort_keys=False, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"\nwrote {len(data['archetypes'])} archetypes, {len(data['countries'])} countries -> {out}")


if __name__ == "__main__":
    main()
