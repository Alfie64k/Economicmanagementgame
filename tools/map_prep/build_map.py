#!/usr/bin/env python3
"""Builds game/data/world_map.json and data/regions.json from Natural Earth (public domain).

Countries: ne_110m_admin_0_countries. Sub-national regions (9 large federations): ne_50m_admin_1_states_provinces.
Polygons are simplified (Douglas-Peucker) and coordinates rounded to keep the file small. Downloads are cached in tools/map_prep/.cache.
Run: python3 tools/map_prep/build_map.py
"""
import json, math, pathlib, urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = pathlib.Path(__file__).parent / ".cache"
BASE = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/"
FILES = {"countries": "ne_110m_admin_0_countries.geojson", "admin1": "ne_50m_admin_1_states_provinces.geojson"}
ROSTER_REGIONS = {"USA", "CHN", "IND", "BRA", "RUS", "CAN", "AUS", "IDN", "ZAF"}

def fetch(name):
    CACHE.mkdir(exist_ok=True)
    p = CACHE / name
    if not p.exists():
        print("downloading", name)
        urllib.request.urlretrieve(BASE + name, p)
    return json.load(open(p, encoding="utf-8"))

def dp(points, eps):
    if len(points) < 4: return points
    def dist(p, a, b):
        (x, y), (x1, y1), (x2, y2) = p, a, b
        dx, dy = x2 - x1, y2 - y1
        if dx == dy == 0: return math.hypot(x - x1, y - y1)
        t = max(0, min(1, ((x - x1) * dx + (y - y1) * dy) / (dx * dx + dy * dy)))
        return math.hypot(x - (x1 + t * dx), y - (y1 + t * dy))
    keep = [False] * len(points); keep[0] = keep[-1] = True
    stack = [(0, len(points) - 1)]
    while stack:
        i, j = stack.pop(); md, mi = 0, None
        for k in range(i + 1, j):
            d = dist(points[k], points[i], points[j])
            if d > md: md, mi = d, k
        if mi is not None and md > eps: keep[mi] = True; stack += [(i, mi), (mi, j)]
    return [p for p, k in zip(points, keep) if k]

def rings(geom, eps, min_area):
    polys = geom["coordinates"] if geom["type"] == "MultiPolygon" else [geom["coordinates"]]
    out = []
    for poly in polys:
        ring = poly[0][:-1]
        s = dp([(round(x, 2), round(y, 2)) for x, y in ring], eps)
        if len(s) < 3: continue
        a = abs(sum(s[i][0] * s[(i + 1) % len(s)][1] - s[(i + 1) % len(s)][0] * s[i][1] for i in range(len(s)))) / 2
        if a >= min_area: out.append([[x, y] for x, y in s])
    return out

def centroid(polys):
    best = max(polys, key=lambda r: len(r))
    # area-weighted centroid of the largest ring
    a = cx = cy = 0
    for i in range(len(best)):
        x0, y0 = best[i]; x1, y1 = best[(i + 1) % len(best)]
        cr = x0 * y1 - x1 * y0; a += cr; cx += (x0 + x1) * cr; cy += (y0 + y1) * cr
    if abs(a) < 1e-9: return best[0]
    return [round(cx / (3 * a), 2), round(cy / (3 * a), 2)]

def main():
    c = fetch(FILES["countries"]); a1 = fetch(FILES["admin1"])
    countries = []
    for f in c["features"]:
        p = f["properties"]; iso = p.get("ADM0_A3") or p.get("ISO_A3")
        polys = rings(f["geometry"], 0.12, 0.02)
        if not polys: continue
        countries.append({"id": iso, "name": p.get("NAME") or p.get("ADMIN"), "polys": polys, "c": centroid(polys)})
    # city-state missing from 110m
    if not any(x["id"] == "SGP" for x in countries):
        r = [[103.62 + 0.28 * math.cos(t * math.pi / 4), 1.35 + 0.18 * math.sin(t * math.pi / 4)] for t in range(8)]
        countries.append({"id": "SGP", "name": "Singapore", "polys": [[[round(x, 2), round(y, 2)] for x, y in r]], "c": [103.82, 1.35]})
    regions, region_defs = {}, []
    for f in a1["features"]:
        p = f["properties"]; iso = p["adm0_a3"]
        if iso not in ROSTER_REGIONS: continue
        polys = rings(f["geometry"], 0.1, 0.01)
        if not polys: continue
        rid = p.get("adm1_code") or p["name"]
        cen = centroid(polys)
        regions.setdefault(iso, []).append({"id": rid, "name": p["name"], "polys": polys, "c": cen})
        region_defs.append({"id": rid, "name": p["name"], "country": iso, "area": round(p.get("area_sqkm") or 1000), "lat": cen[1], "lon": cen[0]})
    out = ROOT / "game" / "data"; out.mkdir(exist_ok=True)
    (out / "world_map.json").write_text(json.dumps({"countries": countries, "regions": regions}, separators=(",", ":")))
    (ROOT / "data" / "regions.json").write_text(json.dumps(region_defs, separators=(",", ":")) + "\n")
    size = (out / "world_map.json").stat().st_size
    print(f"{len(countries)} countries, {sum(len(v) for v in regions.values())} regions, {size/1024:.0f} KB")
    roster = {r["id"] for r in json.load(open(ROOT / "data" / "countries.json"))}
    missing = roster - {x["id"] for x in countries}
    print("roster missing from map:", sorted(missing))

main()
