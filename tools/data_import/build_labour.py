#!/usr/bin/env python3
"""Builds data/labour.json: approximate collective-bargaining coverage, bargaining strength and labour share of income.

What the numbers mean
  coverage    share of employees whose pay is set or extended by collective agreements (OECD/ICTWSS-style "adjusted bargaining
              coverage"; union membership alone would mislead, since France covers nearly everyone with about 8% density).
  strength    0..1 multiplier turning coverage into effective bargaining power: coordination across employers, legal protection
              of strikes and freedom of association, how often agreements are honoured. Coverage 0.40 with strength 0.20 (China,
              Vietnam: state-run unions) bargains far less than coverage 0.40 with strength 0.90 (Germany). The game's bargaining
              power is coverage x strength.
  labourShare labour compensation as a share of GDP or gross value added (ILO labour income share, national-accounts compensation
              of employees; the two measures differ by up to ten points for countries with large self-employment and
              informality). The engine uses only its deviations from the starting value, so an error in the level does not
              move the economy.

Where the values come from
  Hand-compiled approximations in the range of the OECD/AIAS ICTWSS database, ILO collective-bargaining and labour-share
  statistics and national sources, rounded to two significant figures. Strength is a judgement, not a measurement. They are
  gameplay calibration inputs, not statistics. A live importer was not possible in the build environment.

Structure
  archetypes: fallback template per archetype (for roster countries without an entry, e.g. in tests that build a custom roster).
  countries : id -> {coverage, strength, labourShare}; every roster country must be listed.

Run:  python3 tools/data_import/build_labour.py   (exits non-zero if the data fail validation; writes nothing in that case)
"""
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]

NOTE = ("Approximate collective-bargaining coverage, effective bargaining strength (a judgement) and labour income share, "
        "compiled for gameplay from OECD/ICTWSS and ILO-style figures; not official statistics.")

ARCHETYPES = {
    "advanced": {"coverage": 0.40, "strength": 0.75, "labourShare": 0.58},
    "hub": {"coverage": 0.35, "strength": 0.65, "labourShare": 0.50},
    "emerging": {"coverage": 0.20, "strength": 0.50, "labourShare": 0.46},
    "resource": {"coverage": 0.20, "strength": 0.40, "labourShare": 0.42},
    "developing": {"coverage": 0.08, "strength": 0.30, "labourShare": 0.45},
}

# id: (coverage, strength, labourShare)
LABOUR = {
    # advanced: coverage from the OECD/ICTWSS adjusted series (c. 2019-22)
    "USA": (0.12, 0.80, 0.58), "GBR": (0.26, 0.80, 0.59), "DEU": (0.54, 0.90, 0.61), "FRA": (0.98, 0.55, 0.60),
    "JPN": (0.17, 0.75, 0.55), "KOR": (0.14, 0.70, 0.55), "AUS": (0.60, 0.65, 0.55), "CAN": (0.30, 0.80, 0.57),
    # hubs
    "SGP": (0.20, 0.45, 0.43), "CHE": (0.49, 0.80, 0.65),
    # emerging
    "CHN": (0.40, 0.20, 0.50), "IND": (0.10, 0.60, 0.38), "BRA": (0.55, 0.60, 0.54), "ARG": (0.45, 0.80, 0.49),
    "ZAF": (0.30, 0.90, 0.50), "MEX": (0.10, 0.40, 0.38), "IDN": (0.12, 0.50, 0.40), "TUR": (0.08, 0.50, 0.42),
    "EGY": (0.15, 0.30, 0.35), "VNM": (0.40, 0.20, 0.46), "POL": (0.13, 0.60, 0.52), "CHL": (0.20, 0.60, 0.46),
    # resource
    "RUS": (0.30, 0.30, 0.47), "SAU": (0.02, 0.10, 0.35), "NGA": (0.12, 0.70, 0.40), "ARE": (0.02, 0.10, 0.33),
    "NOR": (0.70, 0.90, 0.52),
    # developing
    "ETH": (0.05, 0.30, 0.45),
}


def roster():
    """(id, archetype) for every country in data/countries.json, the single source of truth for the roster."""
    with open(ROOT / "data" / "countries.json", encoding="utf-8") as f:
        return [(c["id"], c["archetype"]) for c in json.load(f)]


def build():
    return {
        "version": 1,
        "note": NOTE,
        "archetypes": ARCHETYPES,
        "countries": {cid: {"coverage": c, "strength": s, "labourShare": ls} for cid, (c, s, ls) in LABOUR.items()},
    }


def validate(data, ros):
    errors = []
    ids = [i for i, _ in ros]
    for cid, arch in ros:
        if cid not in data["countries"]:
            errors.append(f"{cid}: missing from the labour table (roster country)")
        if arch not in data["archetypes"]:
            errors.append(f"{cid}: archetype '{arch}' has no fallback template")
    for cid in data["countries"]:
        if cid not in ids:
            errors.append(f"{cid}: not in the roster (data/countries.json)")
    entries = list(data["countries"].items()) + [(f"archetype {a}", e) for a, e in data["archetypes"].items()]
    for cid, e in entries:
        if not (0.0 <= e["coverage"] <= 1.0):
            errors.append(f"{cid}: coverage {e['coverage']} outside [0, 1]")
        if not (0.0 <= e["strength"] <= 1.0):
            errors.append(f"{cid}: strength {e['strength']} outside [0, 1]")
        if not (0.25 <= e["labourShare"] <= 0.70):
            errors.append(f"{cid}: labour share {e['labourShare']} outside [0.25, 0.70]")
    # plausibility anchors every source agrees on
    c = data["countries"]
    power = {i: c[i]["coverage"] * c[i]["strength"] for i in c}
    if c.get("FRA", {}).get("coverage", 0) < 0.9:
        errors.append("FRA coverage should be above 0.9 (extension of agreements)")
    if c.get("USA", {}).get("coverage", 1) > 0.2:
        errors.append("USA coverage should be below 0.2")
    if power.get("NOR", 0) <= power.get("USA", 1) or power.get("DEU", 0) <= power.get("GBR", 1):
        errors.append("bargaining power ordering broken: NOR > USA and DEU > GBR expected")
    if power.get("CHN", 1) >= power.get("DEU", 0) or power.get("SAU", 1) >= 0.05:
        errors.append("state-run or banned unions (CHN, SAU) must carry little effective power")
    by = {}
    for cid, arch in ros:
        if cid in power:
            by.setdefault(arch, []).append(power[cid])
    mean = {a: sum(v) / len(v) for a, v in by.items()}
    if mean.get("advanced", 0) <= mean.get("developing", 1):
        errors.append("advanced economies should bargain harder on average than developing ones")
    return errors, power, mean


def main():
    ros = roster()
    data = build()
    errors, power, mean = validate(data, ros)
    print(f"{'id':4} {'archetype':10} {'coverage':>8} {'strength':>8} {'power':>6} {'lab.share':>9}")
    for cid, arch in ros:
        e = data["countries"].get(cid)
        if e:
            print(f"{cid:4} {arch:10} {e['coverage'] * 100:7.0f}% {e['strength']:8.2f} {power[cid]:6.2f} {e['labourShare'] * 100:8.0f}%")
    print("mean bargaining power by archetype: " + ", ".join(f"{a} {m:.2f}" for a, m in sorted(mean.items())))
    if errors:
        print("\nVALIDATION FAILED", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        sys.exit(1)
    out = ROOT / "data" / "labour.json"
    out.write_text(json.dumps(data, indent=1, sort_keys=False, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"\nwrote {len(data['countries'])} countries -> {out}")


if __name__ == "__main__":
    main()
