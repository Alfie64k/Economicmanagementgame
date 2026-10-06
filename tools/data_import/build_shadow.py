#!/usr/bin/env python3
"""Builds data/shadow.json: approximate starting size of the informal ("shadow") economy for the 28 game countries.

What the number means
  "share" is the informal or unreported share of economic activity, as a fraction of official GDP (0.08 = 8%). It is what the
  game calls Shadow0: the share that escapes tax at the start. The engine only ever uses it relative to itself, as the tax-base
  multiplier (1 - Shadow) / (1 - Shadow0), so an error in the level changes how much room a country has to move, not the
  starting revenue.

Where the values come from
  Hand-compiled central values in the range of the MIMIC / currency-demand estimates published by Schneider and co-authors
  and by Medina and Schneider (IMF Working Paper 18/17), cross-checked against the broad ordering in ILO informality statistics
  and World Bank "informal economy" tables. Different methods disagree by a factor of two for some countries (Nigeria, Egypt,
  Russia), so every entry carries a plausible range ("lo" to "hi") and the game uses the central value only. These are
  gameplay calibration inputs, not statistics. A live importer was not possible in the build environment.

Structure
  archetypes: one fallback share per archetype (used for any roster country without an entry, for example in tests that build a
              custom roster).
  countries : id -> {share, lo, hi}.

Run:  python3 tools/data_import/build_shadow.py   (exits non-zero if the data fail validation; writes nothing in that case)
"""
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]

NOTE = ("Approximate informal-economy shares of GDP (central values in the range of Schneider and Medina-Schneider MIMIC estimates), "
        "compiled for gameplay; methods disagree widely, see lo and hi. Not official statistics.")

ARCHETYPES = {"advanced": 0.10, "hub": 0.09, "emerging": 0.25, "resource": 0.28, "developing": 0.38}

# id: (share, lo, hi)
SHADOW = {
    # advanced
    "USA": (0.08, 0.06, 0.10), "GBR": (0.10, 0.08, 0.12), "DEU": (0.10, 0.08, 0.13), "FRA": (0.11, 0.09, 0.14),
    "JPN": (0.09, 0.07, 0.11), "KOR": (0.17, 0.12, 0.21), "AUS": (0.10, 0.08, 0.13), "CAN": (0.10, 0.08, 0.13),
    # hubs
    "SGP": (0.10, 0.07, 0.13), "CHE": (0.07, 0.05, 0.09),
    # emerging
    "CHN": (0.13, 0.10, 0.18), "IND": (0.24, 0.18, 0.30), "BRA": (0.28, 0.22, 0.34), "ARG": (0.26, 0.20, 0.33),
    "ZAF": (0.24, 0.18, 0.30), "MEX": (0.30, 0.23, 0.36), "IDN": (0.22, 0.17, 0.28), "TUR": (0.28, 0.22, 0.34),
    "EGY": (0.33, 0.25, 0.40), "VNM": (0.17, 0.12, 0.22), "POL": (0.19, 0.14, 0.23), "CHL": (0.19, 0.15, 0.24),
    # resource
    "RUS": (0.30, 0.22, 0.37), "SAU": (0.19, 0.14, 0.25), "NGA": (0.45, 0.35, 0.58), "ARE": (0.25, 0.18, 0.31),
    "NOR": (0.13, 0.10, 0.17),
    # developing
    "ETH": (0.38, 0.30, 0.45),
}


def roster():
    """(id, archetype) for every country in data/countries.json, the single source of truth for the roster."""
    with open(ROOT / "data" / "countries.json", encoding="utf-8") as f:
        return [(c["id"], c["archetype"]) for c in json.load(f)]


def build():
    return {
        "version": 1,
        "note": NOTE,
        "archetypes": {a: {"share": s} for a, s in ARCHETYPES.items()},
        "countries": {cid: {"share": s, "lo": lo, "hi": hi} for cid, (s, lo, hi) in SHADOW.items()},
    }


def validate(data, ros):
    errors = []
    ids = [i for i, _ in ros]
    for cid, arch in ros:
        if cid not in data["countries"]:
            errors.append(f"{cid}: missing from the shadow-economy table (roster country)")
        if arch not in data["archetypes"]:
            errors.append(f"{cid}: archetype '{arch}' has no fallback share")
    for cid in data["countries"]:
        if cid not in ids:
            errors.append(f"{cid}: not in the roster (data/countries.json)")
    for cid, e in data["countries"].items():
        s, lo, hi = e["share"], e["lo"], e["hi"]
        if not (0.02 <= s <= 0.70):
            errors.append(f"{cid}: share {s} outside [0.02, 0.70]")
        if not (0.0 < lo <= s <= hi < 1.0):
            errors.append(f"{cid}: need 0 < lo <= share <= hi < 1, got {lo}, {s}, {hi}")
        if hi - lo > 0.4 * 1.0:
            errors.append(f"{cid}: range [{lo}, {hi}] is implausibly wide")
    # plausibility anchors: the broad ordering every method agrees on
    by = {}
    for cid, arch in ros:
        if cid in data["countries"]:
            by.setdefault(arch, []).append(data["countries"][cid]["share"])
    mean = {a: sum(v) / len(v) for a, v in by.items()}
    if mean.get("advanced", 0) >= 0.14:
        errors.append(f"advanced economies average {mean['advanced']:.3f}; expected roughly 8-12%")
    if mean.get("developing", 1) <= 0.25 or mean.get("emerging", 1) <= mean.get("advanced", 0):
        errors.append("archetype ordering broken: developing > emerging > advanced expected")
    for a, s in data["archetypes"].items():
        if not (0.02 <= s["share"] <= 0.70):
            errors.append(f"archetype {a}: share {s['share']} outside [0.02, 0.70]")
    if data["countries"].get("CHE", {}).get("share", 1) > 0.10:
        errors.append("CHE should be among the smallest shadow economies")
    if data["countries"].get("NGA", {}).get("share", 0) < 0.35:
        errors.append("NGA should be among the largest shadow economies")
    return errors, mean


def main():
    ros = roster()
    data = build()
    errors, mean = validate(data, ros)
    print(f"{'id':4} {'archetype':10} {'share':>6} {'range':>12}")
    for cid, arch in ros:
        e = data["countries"].get(cid)
        if e:
            print(f"{cid:4} {arch:10} {e['share'] * 100:5.1f}% {e['lo'] * 100:5.0f}-{e['hi'] * 100:.0f}%")
    print("archetype means: " + ", ".join(f"{a} {m * 100:.1f}%" for a, m in sorted(mean.items())))
    if errors:
        print("\nVALIDATION FAILED", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        sys.exit(1)
    out = ROOT / "data" / "shadow.json"
    out.write_text(json.dumps(data, indent=1, sort_keys=False, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"\nwrote {len(data['countries'])} countries -> {out}")


if __name__ == "__main__":
    main()
