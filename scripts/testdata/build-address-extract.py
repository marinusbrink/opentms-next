#!/usr/bin/env python3
"""Build the test-data address extract (real addresses, NL/BE/DE) from official open data.

Output: a gzip'd UTF-8 CSV (RFC 4180 quoting) with the header
    country,street,house_number,house_number_suffix,postal_code,city,latitude,longitude
country = ISO 3166-1 alpha-2; latitude/longitude = WGS84 (ETRS89, identical at this precision),
6 decimals. The addresses are real; the system pairs them with fictitious company names.

Sources and licences (see also SOURCES.md next to the output file)
-------------------------------------------------------------------
NL  Kadaster, Basisregistratie Adressen en Gebouwen (BAG), via PDOK OGC API Features
    https://api.pdok.nl/kadaster/bag/ogc/v2/collections/verblijfsobject/items
    Licence: CC0 1.0 / Public Domain Mark 1.0 (no attribution required).
    Sampled with bbox queries over a randomised grid that covers the whole country:
    one pass over all uses, one pass filtered on gebruiksdoel industrie-/kantoor-/winkelfunctie.
BE  FOD BOSA, BeST Address (Belgian Streets and Addresses), "openaddress" CSV per region
    https://opendata.bosa.be/download/best/openaddress-bevlg.zip   (Flanders, from Informatie Vlaanderen)
    https://opendata.bosa.be/download/best/openaddress-bewal.zip   (Wallonia, from SPW)
    https://opendata.bosa.be/download/best/openaddress-bebru.zip   (Brussels, from paradigm.brussels)
    Licence: CC BY 4.0. Coordinates computed from the Lambert 2008 (EPSG:3812) columns.
DE  North Rhine-Westphalia: Geobasis NRW, Gebaeudereferenzen (GEBREF), ASCII, EPSG:25832
    https://www.opengeodata.nrw.de/produkte/geobasis/lk/akt/gebref_txt/gebref_EPSG25832_ASCII.zip
    plus the postcode from the INSPIRE Addresses WFS built on the same GEBREF data
    https://www.wfs.nrw.de/geobasis/wfs_nw_inspire-adressen_gebref
    Licence: Datenlizenz Deutschland - Zero - Version 2.0 (dl-de/zero-2-0).
    Berlin: Amt fuer Statistik Berlin-Brandenburg, Adressen Regionales Bezugssystem (RBS), WFS
    https://gdi.berlin.de/services/wfs/adressen_rbs
    Licence: dl-de/zero-2-0.
    Hamburg: Landesbetrieb Geoinformation und Vermessung (LGV), INSPIRE Adressen WFS
    https://geodienste.hamburg.de/HH_WFS_INSPIRE_Adressen
    Licence: Datenlizenz Deutschland - Namensnennung - Version 2.0 (dl-de/by-2-0);
    attribution: "Freie und Hansestadt Hamburg, Landesbetrieb Geoinformation und Vermessung".
No OpenStreetMap/ODbL data is used.

Usage
-----
    python3 -I scripts/testdata/build-address-extract.py --raw <download dir> --out <addresses.csv.gz>

Downloads land in <raw>/<source>/ and are reused on a re-run (delete them to refresh). Sampling uses a
fixed seed, so a re-run on the same downloads gives the same file. Stdlib only (downloads go through curl when present, else urllib); the UTM and Lambert
conformal conic inverse projections are implemented below (GRS80 ellipsoid).
"""

from __future__ import annotations

import argparse
import concurrent.futures as cf
import csv
import gzip
import hashlib
import io
import json
import math
import os
import random
import re
import shutil
import subprocess
import sys
import time
import unicodedata
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

SEED = 20261030
USER_AGENT = "opentms-next test-data builder (stdlib urllib)"

TARGET_NL_ANY = 120_000
TARGET_NL_BUSINESS = 80_000  # industrie-, kantoor-, winkelfunctie
TARGET_BE = {"BE-VLG": 32_000, "BE-WAL": 20_000, "BE-BRU": 8_000}
TARGET_DE = {"NW": 35_000, "BE": 15_000, "HH": 10_000}

# ---------------------------------------------------------------------------------------------------
# Projections (GRS80; ETRS89 is treated as WGS84, the difference is far below 6 decimals' meaning here)
# ---------------------------------------------------------------------------------------------------
A = 6378137.0
F = 1 / 298.257222101
E2 = F * (2 - F)
E = math.sqrt(E2)


def utm_to_wgs84(easting: float, northing: float, zone: int) -> tuple[float, float]:
    """Inverse transverse Mercator (Snyder), northern hemisphere, k0 = 0.9996."""
    k0 = 0.9996
    x = easting - 500000.0
    y = northing
    ep2 = E2 / (1 - E2)
    e1 = (1 - math.sqrt(1 - E2)) / (1 + math.sqrt(1 - E2))
    m = y / k0
    mu = m / (A * (1 - E2 / 4 - 3 * E2**2 / 64 - 5 * E2**3 / 256))
    phi1 = (
        mu
        + (3 * e1 / 2 - 27 * e1**3 / 32) * math.sin(2 * mu)
        + (21 * e1**2 / 16 - 55 * e1**4 / 32) * math.sin(4 * mu)
        + (151 * e1**3 / 96) * math.sin(6 * mu)
        + (1097 * e1**4 / 512) * math.sin(8 * mu)
    )
    sin1, cos1, tan1 = math.sin(phi1), math.cos(phi1), math.tan(phi1)
    c1 = ep2 * cos1**2
    t1 = tan1**2
    n1 = A / math.sqrt(1 - E2 * sin1**2)
    r1 = A * (1 - E2) / (1 - E2 * sin1**2) ** 1.5
    d = x / (n1 * k0)
    lat = phi1 - (n1 * tan1 / r1) * (
        d**2 / 2
        - (5 + 3 * t1 + 10 * c1 - 4 * c1**2 - 9 * ep2) * d**4 / 24
        + (61 + 90 * t1 + 298 * c1 + 45 * t1**2 - 252 * ep2 - 3 * c1**2) * d**6 / 720
    )
    lon0 = math.radians(zone * 6 - 183)
    lon = lon0 + (
        d
        - (1 + 2 * t1 + c1) * d**3 / 6
        + (5 - 2 * c1 + 28 * t1 - 3 * c1**2 + 8 * ep2 + 24 * t1**2) * d**5 / 120
    ) / cos1
    return math.degrees(lat), math.degrees(lon)


def _lcc_m(phi: float) -> float:
    return math.cos(phi) / math.sqrt(1 - E2 * math.sin(phi) ** 2)


def _lcc_t(phi: float) -> float:
    s = math.sin(phi)
    return math.tan(math.pi / 4 - phi / 2) / ((1 - E * s) / (1 + E * s)) ** (E / 2)


def lambert2008_to_wgs84(x: float, y: float) -> tuple[float, float]:
    """Inverse Lambert conformal conic 2SP for EPSG:3812 (Belgian Lambert 2008, ETRS89/GRS80)."""
    lat1, lat2 = math.radians(49 + 50 / 60), math.radians(51 + 10 / 60)
    lat0 = math.radians(50 + 47 / 60 + 52.134 / 3600)
    lon0 = math.radians(4 + 21 / 60 + 33.177 / 3600)
    fe, fn = 649328.0, 665262.0
    m1, m2 = _lcc_m(lat1), _lcc_m(lat2)
    t1, t2, t0 = _lcc_t(lat1), _lcc_t(lat2), _lcc_t(lat0)
    n = (math.log(m1) - math.log(m2)) / (math.log(t1) - math.log(t2))
    f_ = m1 / (n * t1**n)
    r0 = A * f_ * t0**n
    dx, dy = x - fe, r0 - (y - fn)
    r = math.copysign(math.hypot(dx, dy), n)
    t = (r / (A * f_)) ** (1 / n)
    theta = math.atan2(dx, dy)
    lon = theta / n + lon0
    phi = math.pi / 2 - 2 * math.atan(t)
    for _ in range(10):
        s = math.sin(phi)
        phi = math.pi / 2 - 2 * math.atan(t * ((1 - E * s) / (1 + E * s)) ** (E / 2))
    return math.degrees(phi), math.degrees(lon)


# ---------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------
_CTRL = re.compile(r"[\x00-\x1f\x7f]")
_WS = re.compile(r"\s+")


def clean(value) -> str:
    if value is None:
        return ""
    s = unicodedata.normalize("NFC", str(value))
    s = _CTRL.sub(" ", s)
    return _WS.sub(" ", s).strip()


CURL = shutil.which("curl")


def _fetch(url: str, timeout: int, dest: str | None = None) -> bytes:
    # curl uses the operating system's trust store; Python's bundled one is often incomplete on macOS.
    if CURL:
        cmd = [CURL, "-sSfL", "--max-time", str(timeout), "-A", USER_AGENT, url]
        if dest:
            cmd[1:1] = ["-o", dest]
        return subprocess.run(cmd, check=True, capture_output=True).stdout
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        if not dest:
            return resp.read()
        with open(dest, "wb") as out:
            while chunk := resp.read(1 << 20):
                out.write(chunk)
    return b""


def http_get(url: str, attempts: int = 5, timeout: int = 300) -> bytes:
    last = None
    for i in range(attempts):
        try:
            return _fetch(url, timeout)
        except Exception as exc:  # network hiccups: back off and retry
            last = getattr(exc, "stderr", None) or exc
            time.sleep(3 * (i + 1))
    raise RuntimeError(f"GET failed after {attempts} attempts: {url}: {last}")


def download(url: str, path: str) -> str:
    if not os.path.exists(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        print(f"  downloading {url}", file=sys.stderr)
        tmp = path + ".part"
        _fetch(url, 3600, tmp)
        os.replace(tmp, path)
    return path


def cached_get(url: str, cache_dir: str) -> bytes:
    """GET with an on-disk gzip cache keyed by the URL."""
    os.makedirs(cache_dir, exist_ok=True)
    key = hashlib.sha1(url.encode()).hexdigest()
    path = os.path.join(cache_dir, key + ".gz")
    if os.path.exists(path):
        with gzip.open(path, "rb") as fh:
            return fh.read()
    data = http_get(url)
    with gzip.open(path + ".part", "wb") as fh:
        fh.write(data)
    os.replace(path + ".part", path)
    return data


def split_number(raw: str) -> tuple[str, str] | None:
    m = re.match(r"^\s*0*(\d+)\s*(.*)$", raw or "")
    if not m or m.group(1) == "":
        return None
    num = str(int(m.group(1)))
    if num == "0":
        return None
    return num, clean(m.group(2)).lstrip("-/ ").strip()


def row(country, street, number, suffix, postal, city, lat, lon) -> dict:
    return {
        "country": country,
        "street": clean(street),
        "house_number": number,
        "house_number_suffix": clean(suffix),
        "postal_code": postal,
        "city": clean(city),
        "latitude": f"{lat:.6f}",
        "longitude": f"{lon:.6f}",
    }


BOUNDS = {"NL": (50.7, 53.7, 3.2, 7.3), "BE": (49.4, 51.6, 2.4, 6.5), "DE": (47.2, 55.1, 5.8, 15.1)}


def in_bounds(country: str, lat: float, lon: float) -> bool:
    b = BOUNDS[country]
    return b[0] <= lat <= b[1] and b[2] <= lon <= b[3]


def dedupe_key(r: dict) -> tuple:
    return (r["country"], r["postal_code"], r["street"].casefold(), r["house_number"], r["house_number_suffix"].casefold())


# ---------------------------------------------------------------------------------------------------
# NL: BAG via the PDOK OGC API
# ---------------------------------------------------------------------------------------------------
BAG_URL = "https://api.pdok.nl/kadaster/bag/ogc/v2/collections/verblijfsobject/items"
NL_BBOX = (3.30, 50.74, 7.25, 53.56)
_NL_PC = re.compile(r"^(\d{4})\s?([A-Z]{2})$")


def _bag_cell(args) -> list[dict]:
    bbox, doel, limit, cache_dir = args
    params = {
        "f": "json",
        "limit": str(limit),
        "bbox": ",".join(f"{v:.5f}" for v in bbox),
        "status": "Verblijfsobject in gebruik",
    }
    if doel:
        params["gebruiksdoel"] = doel
    url = BAG_URL + "?" + urllib.parse.urlencode(params)
    try:
        data = json.loads(cached_get(url, cache_dir))
    except Exception as exc:  # a throttled or failing cell is skipped (not cached); others fill the target
        print(f"  NL cell skipped: {exc}"[:300], file=sys.stderr)
        return []
    out = []
    for feat in data.get("features", []):
        p = feat.get("properties") or {}
        geom = feat.get("geometry") or {}
        if p.get("hoofdadres_status") != "Naamgeving uitgegeven" or geom.get("type") != "Point":
            continue
        pc = _NL_PC.match((p.get("postcode") or "").strip().upper())
        if not pc or not p.get("huisnummer") or not p.get("openbare_ruimte_naam"):
            continue
        letter = clean(p.get("huisletter"))
        toev = clean(p.get("toevoeging"))
        suffix = (letter + " " + toev).strip() if toev else letter
        lon, lat = geom["coordinates"][:2]
        out.append(
            row("NL", p["openbare_ruimte_naam"], str(int(p["huisnummer"])), suffix,
                f"{pc.group(1)} {pc.group(2)}", p.get("woonplaats_naam"), lat, lon)
        )
    return out


def build_nl(raw: str, rng: random.Random) -> list[dict]:
    cache = os.path.join(raw, "nl", "cache")
    seen: set = set()
    result: list[dict] = []

    def run_pass(cells, doelen, limit, cap, target, label):
        added = 0
        tasks = [(c, d, limit, cache) for c in cells for d in doelen]
        with cf.ThreadPoolExecutor(max_workers=8) as pool:
            for start in range(0, len(tasks), 120):
                if added >= target:
                    break
                for rows in pool.map(_bag_cell, tasks[start:start + 120]):
                    rng.shuffle(rows)
                    taken = 0
                    for r in rows:
                        if taken >= cap or added >= target:
                            break
                        k = dedupe_key(r)
                        if k in seen:
                            continue
                        seen.add(k)
                        result.append(r)
                        taken += 1
                        added += 1
                print(f"  NL {label}: {added}/{target} after {min(start + 120, len(tasks))} requests", file=sys.stderr)
        return added

    def grid(dx, dy):
        x0, y0, x1, y1 = NL_BBOX
        cells = []
        x = x0
        while x < x1:
            y = y0
            while y < y1:
                cells.append((x, y, x + dx, y + dy))
                y += dy
            x += dx
        rng.shuffle(cells)
        return cells

    # Business addresses first (so the 40 % share is met), on a coarser grid; then any use.
    # The API is slow (seconds per request), so few large pages: ~8 x 8 km cells for the business
    # pass, ~5 x 5 km cells for the all-uses pass, each capped so dense cities do not dominate.
    run_pass(grid(0.12, 0.075), ["industriefunctie", "kantoorfunctie", "winkelfunctie"], 1000, 200,
             TARGET_NL_BUSINESS, "industrie/kantoor/winkel")
    run_pass(grid(0.07, 0.045), [None], 500, 75, TARGET_NL_ANY, "all uses")
    return result


# ---------------------------------------------------------------------------------------------------
# BE: BeST Address openaddress CSV
# ---------------------------------------------------------------------------------------------------
BE_FILES = {
    "BE-VLG": "https://opendata.bosa.be/download/best/openaddress-bevlg.zip",
    "BE-WAL": "https://opendata.bosa.be/download/best/openaddress-bewal.zip",
    "BE-BRU": "https://opendata.bosa.be/download/best/openaddress-bebru.zip",
}


def build_be(raw: str, rng: random.Random) -> list[dict]:
    result = []
    for region, url in BE_FILES.items():
        path = download(url, os.path.join(raw, "be", os.path.basename(url)))
        seen: set = set()
        pool: list[dict] = []
        with zipfile.ZipFile(path) as zf:
            name = next(n for n in zf.namelist() if n.endswith(".csv"))
            with zf.open(name) as fh:
                reader = csv.DictReader(io.TextIOWrapper(fh, encoding="utf-8", newline=""))
                for rec in reader:
                    if rec.get("status") != "current":
                        continue
                    num = split_number(rec.get("house_number", ""))
                    pc = (rec.get("postcode") or "").strip()
                    if not num or not re.fullmatch(r"\d{4}", pc):
                        continue
                    if region == "BE-VLG":
                        langs = ["nl"]
                    elif region == "BE-WAL":
                        langs = ["fr", "de"]
                    else:  # Brussels is bilingual: pick one language per address, street and city alike
                        langs = ["fr", "nl"] if rng.random() < 0.5 else ["nl", "fr"]
                    street = city = ""
                    for lang in langs:
                        street = clean(rec.get(f"streetname_{lang}"))
                        city = clean(rec.get(f"municipality_name_{lang}"))
                        if street and city:
                            break
                    if not street or not city:
                        continue
                    try:
                        lat, lon = lambert2008_to_wgs84(float(rec["EPSG:3812_x"]), float(rec["EPSG:3812_y"]))
                    except (KeyError, ValueError):
                        continue
                    if not in_bounds("BE", lat, lon):  # rows without a real position carry 0/0
                        continue
                    r = row("BE", street, num[0], num[1], pc, city, lat, lon)
                    k = dedupe_key(r)  # box numbers (units) collapse onto one building address
                    if k in seen:
                        continue
                    seen.add(k)
                    pool.append(r)
        take = min(TARGET_BE[region], len(pool))
        print(f"  BE {region}: {len(pool)} unique addresses, sampling {take}", file=sys.stderr)
        result.extend(rng.sample(pool, take))
    return result


# ---------------------------------------------------------------------------------------------------
# DE: shared INSPIRE Addresses (GML) parsing
# ---------------------------------------------------------------------------------------------------
def _local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


XLINK = "{http://www.w3.org/1999/xlink}href"


def parse_inspire_addresses(data: bytes) -> list[dict]:
    """Extract the fields we need from an INSPIRE ad:Address WFS response."""
    out = []
    root = ET.fromstring(data)
    for el in root.iter():
        if _local(el.tag) != "Address":
            continue
        rec = {"id": None, "pos": None, "status": None, "designators": [], "components": []}
        for sub in el.iter():
            name = _local(sub.tag)
            if name == "localId" and rec["id"] is None:
                rec["id"] = (sub.text or "").strip()
            elif name == "pos" and rec["pos"] is None:
                rec["pos"] = (sub.text or "").split()
            elif name == "status" and sub.get(XLINK):
                rec["status"] = sub.get(XLINK).rsplit("/", 1)[-1]
            elif name == "LocatorDesignator":
                text, typ = "", ""
                for d in sub:
                    if _local(d.tag) == "designator":
                        text = (d.text or "").strip()
                    elif _local(d.tag) == "type":
                        typ = (d.get(XLINK) or "").rsplit("/", 1)[-1]
                rec["designators"].append((typ, text))
            elif name == "component" and sub.get(XLINK):
                rec["components"].append(sub.get(XLINK).rsplit("#", 1)[-1].rsplit("/", 1)[-1].rsplit("=", 1)[-1])
        out.append(rec)
    return out


def component(rec: dict, prefix: str) -> str | None:
    for c in rec["components"]:
        if c.startswith(prefix):
            return c[len(prefix):]
    return None


# --- NRW -------------------------------------------------------------------------------------------
GEBREF_URL = "https://www.opengeodata.nrw.de/produkte/geobasis/lk/akt/gebref_txt/gebref_EPSG25832_ASCII.zip"
NW_WFS = "https://www.wfs.nrw.de/geobasis/wfs_nw_inspire-adressen_gebref"


def _gebref_rows(path: str):
    with zipfile.ZipFile(path) as zf, zf.open("gebref.txt") as fh:
        for line in io.TextIOWrapper(fh, encoding="utf-8", errors="replace", newline=""):
            f = line.rstrip("\r\n").split(";")
            if len(f) >= 20 and f[0] == "N":
                yield f


def _nw_key(oid: str) -> str:
    # GEBREF ids (DENW..HK..) and INSPIRE ids (DENW..AL..) differ only in characters 7-8.
    return oid[:6] + oid[8:]


def _nw_wfs(args):
    e, n, cache = args
    h = 250
    params = {
        "SERVICE": "WFS", "VERSION": "2.0.0", "REQUEST": "GetFeature", "TYPENAMES": "ad:Address",
        "COUNT": "1000", "BBOX": f"{e - h:.0f},{n - h:.0f},{e + h:.0f},{n + h:.0f},urn:ogc:def:crs:EPSG::25832",
    }
    data = cached_get(NW_WFS + "?" + urllib.parse.urlencode(params), cache)
    out = {}
    for rec in parse_inspire_addresses(data):
        plz = component(rec, "PostalDescriptor_")
        if rec["id"] and plz and re.fullmatch(r"\d{5}", plz):
            out[_nw_key(rec["id"].replace("Address_", ""))] = plz
    return out


def build_de_nw(raw: str, rng: random.Random) -> list[dict]:
    path = download(GEBREF_URL, os.path.join(raw, "de-nw", os.path.basename(GEBREF_URL)))
    cache = os.path.join(raw, "de-nw", "wfs-cache")
    anchors_n = 900
    anchors: list = []
    for i, f in enumerate(_gebref_rows(path)):  # reservoir sample of anchor points
        if len(anchors) < anchors_n:
            anchors.append(f)
        else:
            j = rng.randint(0, i)
            if j < anchors_n:
                anchors[j] = f
    wanted: dict[str, str] = {}
    cap = 45
    with cf.ThreadPoolExecutor(max_workers=6) as pool:
        tasks = [(float(f[18]), float(f[19]), cache) for f in anchors]
        for found in pool.map(_nw_wfs, tasks):
            keys = sorted(found)
            rng.shuffle(keys)
            for k in keys[:cap]:
                wanted[k] = found[k]
    print(f"  DE NW: {len(wanted)} addresses with postcode from {anchors_n} WFS windows", file=sys.stderr)
    seen: set = set()
    pool_rows = []
    for f in _gebref_rows(path):
        plz = wanted.get(_nw_key(f[1]))
        if not plz:
            continue
        num = split_number(f[15])
        if not num or not f[14]:
            continue
        lat, lon = utm_to_wgs84(float(f[18]), float(f[19]), 32)
        r = row("DE", f[14], num[0], (num[1] + " " + f[16]).strip(), plz, f[10], lat, lon)
        k = dedupe_key(r)
        if k not in seen:
            seen.add(k)
            pool_rows.append(r)
    take = min(TARGET_DE["NW"], len(pool_rows))
    print(f"  DE NW: {len(pool_rows)} joined, sampling {take}", file=sys.stderr)
    return rng.sample(pool_rows, take)


# --- Berlin ----------------------------------------------------------------------------------------
BERLIN_WFS = "https://gdi.berlin.de/services/wfs/adressen_rbs"


def build_de_berlin(raw: str, rng: random.Random) -> list[dict]:
    cache = os.path.join(raw, "de-be", "wfs-cache")
    page, start = 50_000, 0
    seen: set = set()
    pool_rows = []
    while True:
        params = {
            "SERVICE": "WFS", "VERSION": "2.0.0", "REQUEST": "GetFeature", "TYPENAMES": "adressen_rbs",
            "OUTPUTFORMAT": "application/json", "COUNT": str(page), "STARTINDEX": str(start),
            "SORTBY": "adr_ident",
            "PROPERTYNAME": "strnam,str_typ,hausnr,hausnrz,postleit,geom",
        }
        data = json.loads(cached_get(BERLIN_WFS + "?" + urllib.parse.urlencode(params), cache))
        feats = data.get("features", [])
        for feat in feats:
            p = feat.get("properties") or {}
            g = feat.get("geometry") or {}
            if p.get("str_typ") != "STRASSE" or g.get("type") != "Point":
                continue
            num = split_number(p.get("hausnr") or "")
            plz = (p.get("postleit") or "").strip()
            if not num or not p.get("strnam") or not re.fullmatch(r"\d{5}", plz):
                continue
            e, n = g["coordinates"][:2]
            lat, lon = utm_to_wgs84(e, n, 33)
            r = row("DE", p["strnam"], num[0], (num[1] + " " + clean(p.get("hausnrz"))).strip().lower(),
                    plz, "Berlin", lat, lon)
            k = dedupe_key(r)
            if k not in seen:
                seen.add(k)
                pool_rows.append(r)
        print(f"  DE Berlin: {start + len(feats)} features read", file=sys.stderr)
        if not feats:  # the server may cap the page size, so page by what came back
            break
        start += len(feats)
    take = min(TARGET_DE["BE"], len(pool_rows))
    print(f"  DE Berlin: {len(pool_rows)} unique, sampling {take}", file=sys.stderr)
    return rng.sample(pool_rows, take)


# --- Hamburg ---------------------------------------------------------------------------------------
HAMBURG_WFS = "https://geodienste.hamburg.de/HH_WFS_INSPIRE_Adressen"


def build_de_hamburg(raw: str, rng: random.Random) -> list[dict]:
    cache = os.path.join(raw, "de-hh", "wfs-cache")
    base = {"SERVICE": "WFS", "VERSION": "2.0.0", "REQUEST": "GetFeature"}
    # Street names (all ~8k ThoroughfareName features).
    streets: dict[str, str] = {}
    start = 0
    while True:
        q = dict(base, TYPENAMES="ad:ThoroughfareName", COUNT="5000", STARTINDEX=str(start))
        root = ET.fromstring(cached_get(HAMBURG_WFS + "?" + urllib.parse.urlencode(q), cache))
        n = 0
        for el in root.iter():
            if _local(el.tag) != "ThoroughfareName":
                continue
            n += 1
            lid = next((s.text for s in el.iter() if _local(s.tag) == "localId"), None)
            txt = next((s.text for s in el.iter() if _local(s.tag) == "text"), None)
            if lid and txt:
                streets[lid.replace("ThoroughfareName_", "")] = clean(txt)
        if n == 0:  # the server caps the page size, so page by what came back
            break
        start += n
    hits = ET.fromstring(cached_get(HAMBURG_WFS + "?" + urllib.parse.urlencode(
        dict(base, TYPENAMES="ad:Address", RESULTTYPE="hits")), cache))
    total = int(hits.get("numberMatched"))
    # The service rejects BBOX filters, so sample random pages; ids are ordered by district,
    # so many small pages spread the sample over the city.
    pages, size = 70, 200
    starts = sorted(rng.sample(range(0, total - size, size), pages))
    seen: set = set()
    pool_rows = []
    for s in starts:
        q = dict(base, TYPENAMES="ad:Address", COUNT=str(size), STARTINDEX=str(s))
        for rec in parse_inspire_addresses(cached_get(HAMBURG_WFS + "?" + urllib.parse.urlencode(q), cache)):
            if rec["status"] not in (None, "current") or not rec["pos"]:
                continue
            plz = component(rec, "PostalDescriptor_")
            street = streets.get(component(rec, "ThoroughfareName_") or "")
            d = dict(rec["designators"])
            num = split_number(d.get("addressNumber", ""))
            if not plz or not re.fullmatch(r"\d{5}", plz) or not street or not num:
                continue
            suffix = (num[1] + " " + d.get("addressNumberExtension", "")).strip()
            lat, lon = utm_to_wgs84(float(rec["pos"][0]), float(rec["pos"][1]), 32)
            r = row("DE", street, num[0], suffix, plz, "Hamburg", lat, lon)
            k = dedupe_key(r)
            if k not in seen:
                seen.add(k)
                pool_rows.append(r)
    take = min(TARGET_DE["HH"], len(pool_rows))
    print(f"  DE Hamburg: {len(pool_rows)} unique from {pages} pages, sampling {take}", file=sys.stderr)
    return rng.sample(pool_rows, take)


# ---------------------------------------------------------------------------------------------------
FIELDS = ["country", "street", "house_number", "house_number_suffix", "postal_code", "city", "latitude", "longitude"]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--raw", required=True, help="download/cache directory")
    ap.add_argument("--out", required=True, help="output .csv.gz")
    ap.add_argument("--only", nargs="*", default=["NL", "BE", "DE-NW", "DE-BE", "DE-HH"])
    args = ap.parse_args()

    builders = {"NL": build_nl, "BE": build_be, "DE-NW": build_de_nw, "DE-BE": build_de_berlin, "DE-HH": build_de_hamburg}
    rows: list[dict] = []
    for name in args.only:
        print(f"[{name}]", file=sys.stderr)
        rows.extend(builders[name](args.raw, random.Random(f"{SEED}-{name}")))

    final, seen, counts = [], set(), {}
    for r in rows:
        if not in_bounds(r["country"], float(r["latitude"]), float(r["longitude"])):
            continue
        k = dedupe_key(r)
        if k in seen:
            continue
        seen.add(k)
        final.append(r)
        counts[r["country"]] = counts.get(r["country"], 0) + 1
    # Sorted by country and postcode: deterministic and compresses well.
    final.sort(key=lambda r: (r["country"], r["postal_code"], r["street"], int(r["house_number"]), r["house_number_suffix"]))

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=FIELDS, quoting=csv.QUOTE_MINIMAL, lineterminator="\r\n")
    w.writeheader()
    w.writerows(final)
    with open(args.out, "wb") as raw_out:
        with gzip.GzipFile(filename="addresses.csv", mode="wb", fileobj=raw_out, compresslevel=9, mtime=0) as gz:
            gz.write(buf.getvalue().encode("utf-8"))
    print(json.dumps({"rows": counts, "bytes": os.path.getsize(args.out)}), file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
