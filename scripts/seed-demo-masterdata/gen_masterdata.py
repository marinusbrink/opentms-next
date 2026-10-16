#!/usr/bin/env python3
"""Generates large, realistic, fictional Dutch-logistics master data for one LOCAL development tenant.

Deterministic: the same arguments produce byte-identical output. Every row mirrors the domain rules of
OpenTms.MasterData (normalisation, SearchKey, role rules, combination rules, IBAN mod-97, ...). The output is one
psql script of COPY blocks for the `masterdata` schema, meant to run as a single transaction:

    psql -v ON_ERROR_STOP=1 -1 -f seed.sql

The environment-specific ids (tenant, creator, countries, address types, currency) come from the command line;
scripts/seed-demo-masterdata.sh looks them up and calls this generator. Python 3 standard library only.
"""
import argparse
import datetime as dt
import random
import re
import sys
import unicodedata
import uuid

# Primary IANA time zone per seeded country; must equal masterdata."Countries"."PrimaryTimeZoneId" (verify.sql checks it).
TIME_ZONES = {"NL": "Europe/Amsterdam", "BE": "Europe/Brussels", "DE": "Europe/Berlin"}


def _uuid(value):
    try:
        return str(uuid.UUID(value))
    except ValueError:
        raise argparse.ArgumentTypeError("not a GUID: %r" % value)


def _country(value):
    code, sep, ident = value.partition("=")
    code = code.strip().upper()
    if not sep or code not in TIME_ZONES:
        raise argparse.ArgumentTypeError("expected NL=<id>, BE=<id> or DE=<id>, got %r" % value)
    return code, _uuid(ident.strip())


def _timestamp(value):
    try:
        return dt.datetime.fromisoformat(value).replace(tzinfo=None, microsecond=0)
    except ValueError:
        raise argparse.ArgumentTypeError("not an ISO timestamp (YYYY-MM-DDTHH:MM:SS): %r" % value)


parser = argparse.ArgumentParser(
    description="Generate the demo master data seed (psql COPY script) for one development tenant.")
parser.add_argument("--tenant-id", required=True, type=_uuid, help="AbpTenants.Id of the target tenant")
parser.add_argument("--creator-id", required=True, type=_uuid, help="AbpUsers.Id written as CreatorId (the tenant admin)")
parser.add_argument("--country", required=True, action="append", type=_country, metavar="CC=ID",
                    help="masterdata.Countries.Id per country; give NL=<id>, BE=<id> and DE=<id>")
parser.add_argument("--address-type-visiting", required=True, type=_uuid, help="AddressTypes.Id with Kind = 1 (Visiting)")
parser.add_argument("--address-type-postal", required=True, type=_uuid, help="AddressTypes.Id with Kind = 2 (Postal)")
parser.add_argument("--eur", required=True, type=_uuid, help="Currencies.Id of IsoCode EUR")
parser.add_argument("--seed", type=int, default=20261016, help="random seed (default: %(default)s)")
parser.add_argument("--reference-time", type=_timestamp, default=dt.datetime(2026, 10, 16, 9, 0, 0),
                    help="'now' of the generated data: creation times, qualification validity (default: %(default)s)")
parser.add_argument("--tenant-name", help="tenant name, only used in the header comment")
parser.add_argument("--database", help="if given, the script refuses to run in any other database")
parser.add_argument("--shared-database", action="store_true",
                    help="the tenant lives in the host database: scope the own-company guard to this tenant")
parser.add_argument("--out", help="output file (default: stdout)")
args = parser.parse_args()

countries = dict(args.country)
missing = sorted(set(TIME_ZONES) - set(countries))
if missing:
    parser.error("--country missing for: %s" % ", ".join(missing))

SEED = args.seed
rnd = random.Random(SEED)

TENANT = args.tenant_id
CREATOR = args.creator_id
COUNTRY = {cc: (countries[cc], TIME_ZONES[cc]) for cc in ("NL", "BE", "DE")}
ADDRESS_TYPE_VISITING = args.address_type_visiting
ADDRESS_TYPE_POSTAL = args.address_type_postal
EUR = args.eur
NOW = args.reference_time
TODAY = NOW.date()


# ───────────────────────────── helpers ─────────────────────────────
def new_id():
    return str(uuid.UUID(int=rnd.getrandbits(128), version=4))


def stamp():
    return "%032x" % rnd.getrandbits(128)


def created(days_back_max=3 * 365, days_back_min=0):
    secs = rnd.randint(days_back_min * 86400, days_back_max * 86400)
    t = NOW - dt.timedelta(seconds=secs)
    return t.replace(microsecond=rnd.randint(0, 999999))


def search_key(*parts):
    """Exact port of MasterDataValues.SearchKey."""
    b = []
    for part in parts:
        if part is None or part.strip() == "":
            continue
        for c in unicodedata.normalize("NFD", part):
            if unicodedata.category(c) == "Mn":
                continue
            if c.isalnum():
                b.append(c.lower())
            elif b and b[-1] != " ":
                b.append(" ")
        if b and b[-1] != " ":
            b.append(" ")
    key = "".join(b).strip()
    return key[:320]


def norm_legal(v):
    t = (v or "").replace(" ", "").replace(".", "").replace("-", "").strip().upper()
    return t or None


def norm_postal(v):
    return (v or "").replace(" ", "").strip().upper()


def iban_check(country, bban):
    s = bban + country + "00"
    num = "".join(str(int(c, 36)) for c in s)
    return "%02d" % (98 - int(num) % 97)


def iban_valid(n):
    """Exact port of MasterDataValues.IsValidIban."""
    if not (15 <= len(n) <= 34) or not n.isalnum() or not n[0].isalpha() or not n[1].isalpha():
        return False
    r = n[4:] + n[:4]
    rem = 0
    for c in r:
        v = ord(c) - 48 if c.isdigit() else ord(c) - 55
        rem = (rem * 100 + v) % 97 if v >= 10 else (rem * 10 + v) % 97
    return rem == 1


def ean13(prefix12):
    s = sum(int(d) * (1 if i % 2 == 0 else 3) for i, d in enumerate(prefix12))
    return prefix12 + str((10 - s % 10) % 10)


VIN_CHARS = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789"
VIN_TRANS = dict(zip("ABCDEFGHJKLMNPRSTUVWXYZ", [1, 2, 3, 4, 5, 6, 7, 8, 1, 2, 3, 4, 5, 7, 9, 2, 3, 4, 5, 6, 7, 8, 9]))
VIN_W = [8, 7, 6, 5, 4, 3, 2, 10, 0, 9, 8, 7, 6, 5, 4, 3, 2]
VIN_YEAR = {2014: "E", 2015: "F", 2016: "G", 2017: "H", 2018: "J", 2019: "K", 2020: "L", 2021: "M", 2022: "N", 2023: "P", 2024: "R", 2025: "S", 2026: "T"}


def make_vin(wmi, year):
    vds = "".join(rnd.choice(VIN_CHARS) for _ in range(5))
    plant = rnd.choice("ABCDEGHLMNSWZ")
    serial = "%06d" % rnd.randint(0, 999999)
    v = wmi + vds + "0" + VIN_YEAR[year] + plant + serial
    total = sum((int(c) if c.isdigit() else VIN_TRANS[c]) * VIN_W[i] for i, c in enumerate(v))
    chk = total % 11
    v = v[:8] + ("X" if chk == 10 else str(chk)) + v[9:]
    assert re.fullmatch(r"[A-HJ-NPR-Z0-9]{17}", v), v
    return v


def slug(name):
    t = unicodedata.normalize("NFD", name)
    t = "".join(c for c in t if unicodedata.category(c) != "Mn").lower()
    t = re.sub(r"\b(b\.?v\.?|v\.?o\.?f\.?|n\.?v\.?|bvba|gmbh|co\.?|kg|gebr\.?|zn\.?)\b", " ", t)
    t = t.replace("&", " ").replace("ß", "ss")
    t = re.sub(r"[^a-z0-9]+", "-", t).strip("-")
    return t[:40].strip("-")


# COPY output
out = []


def esc(v):
    if v is None:
        return r"\N"
    if isinstance(v, bool):
        return "t" if v else "f"
    if isinstance(v, float):
        return repr(round(v, 6))
    if isinstance(v, (dt.datetime,)):
        return v.strftime("%Y-%m-%d %H:%M:%S.%f")
    if isinstance(v, dt.date):
        return v.isoformat()
    s = str(v)
    return s.replace("\\", "\\\\").replace("\t", "\\t").replace("\n", "\\n").replace("\r", "\\r")


def copy(table, cols, rows):
    if not rows:
        return
    out.append('COPY masterdata."%s" (%s) FROM stdin;\n' % (table, ", ".join('"%s"' % c for c in cols)))
    for r in rows:
        assert len(r) == len(cols), (table, r)
        out.append("\t".join(esc(v) for v in r) + "\n")
    out.append("\\.\n")


AUDIT = ["ExtraProperties", "ConcurrencyStamp", "CreationTime", "CreatorId", "IsDeleted", "TenantId"]


def audit(ts=None):
    return ["{}", stamp(), ts or created(), CREATOR, False, TENANT]


# ───────────────────────────── reference: transport characteristics ─────────────────────────────
TC_DEF = [
    ("LAADKLEP", "Laadklep", "Hydraulische laadklep aan de achterzijde"),
    ("KOOIAAP", "Kooiaap", "Meeneemheftruck achter op de trailer"),
    ("ADR", "ADR-uitrusting", "Voertuig uitgerust voor gevaarlijke stoffen (oranje borden, blusmiddelen, uitrusting)"),
    ("KOEL", "Koeltransport", "Gekoeld transport 2–8 °C"),
    ("VRIES", "Vriestransport", "Diepgevroren transport tot −25 °C"),
    ("MULTITEMP", "Multi-temperatuur", "Meerdere temperatuurzones in één laadruimte"),
    ("ATP", "ATP-certificaat", "Gecertificeerd voor bederfelijke levensmiddelen (ATP)"),
    ("HUIF", "Huifoplegger", "Huif met schuifdak, zij- en achterbelading"),
    ("TAUTLINER", "Tautliner (schuifzeil)", "Schuifzeilen aan beide zijden"),
    ("MEGA", "Mega-trailer", "3 m binnenhoogte, verlaagde koppelschotel"),
    ("CONTAINER", "Containerchassis", "Geschikt voor 20ft/40ft/45ft containers"),
    ("KIPPER", "Kipper", "Kipbak voor bulkgoederen"),
    ("DIEPLADER", "Dieplader", "Uitzonderlijk transport, verlaagde laadvloer"),
    ("DUBBELDEKS", "Dubbeldeks", "Tweede laadvloer, tot 66 palletplaatsen"),
    ("DOUANE", "Douane-verzegeld", "TIR/douaneverzegelbare laadruimte"),
    ("GDP", "GDP-gecertificeerd", "Good Distribution Practice voor farmaceutische producten"),
    ("KRAAN", "Autolaadkraan", "Laadkraan achter de cabine"),
    ("STUURAS", "Gestuurde naloopas", "Wendbaar in stedelijke distributie"),
    ("WALKINGFLOOR", "Walking floor", "Bewegende laadvloer voor bulk"),
    ("ZIJBELADING", "Zijbelading", "Volledig te openen zijwanden"),
    ("ROLCONT", "Rolcontainerbelading", "Sjorrails en laadklep voor rolcontainers"),
    ("CCKAR", "Bloemen-/plantenkarren", "Ingericht voor CC-containers (Deense karren)"),
    ("ZE", "Zero-emissie", "Elektrisch of waterstof; toegang tot ZE-zones"),
    ("LZV", "LZV-combinatie", "Ecocombi tot 25,25 m / 60 t"),
    ("PALLETWAGEN", "Palletwagen aan boord", "Handpalletwagen mee in de laadruimte"),
    ("SECURE", "TAPA-beveiligd", "Beveiligd transport volgens TAPA TSR"),
]
TC = {}
tc_rows = []
for code, name, desc in TC_DEF:
    i = new_id()
    TC[code] = i
    tc_rows.append([i, code, name, desc] + audit(created(3 * 365, 3 * 365 - 20)))
copy("TransportCharacteristics", ["Id", "Code", "Name", "Description"] + AUDIT, tc_rows)

# ───────────────────────────── temperatures ─────────────────────────────
TEMP_DEF = [
    ("Ambient", None, None, "Geen temperatuureis"),
    ("Conditioned 15–25 °C", 15.0, 25.0, "Geconditioneerd (o.a. chocolade, farma)"),
    ("Koel 2–8 °C", 2.0, 8.0, "Gekoelde levensmiddelen en farma"),
    ("Agri 0–4 °C", 0.0, 4.0, "Groente, fruit en bloemen"),
    ("Vries −18 °C", -25.0, -18.0, "Diepvriesproducten"),
    ("Diepvries −25 °C", -30.0, -25.0, "IJs en diepgevroren vis"),
]
TEMP = {}
rows = []
for name, mn, mx, desc in TEMP_DEF:
    i = new_id()
    TEMP[name] = i
    rows.append([i, name, mn, mx, False, desc] + audit(created(3 * 365, 3 * 365 - 20)))
copy("Temperatures", ["Id", "Name", "MinCelsius", "MaxCelsius", "IsArchived", "Description"] + AUDIT, rows)


def canonical(a, b):
    assert a != b
    return (a, b) if a.replace("-", "") < b.replace("-", "") else (b, a)


temp_inc = set()
for a, b in [("Conditioned 15–25 °C", "Vries −18 °C"), ("Conditioned 15–25 °C", "Diepvries −25 °C"),
             ("Koel 2–8 °C", "Vries −18 °C"), ("Koel 2–8 °C", "Diepvries −25 °C"),
             ("Agri 0–4 °C", "Vries −18 °C"), ("Agri 0–4 °C", "Diepvries −25 °C"), ("Ambient", "Diepvries −25 °C")]:
    temp_inc.add(canonical(TEMP[a], TEMP[b]))
copy("TemperatureIncompatibilities", ["LeftId", "RightId", "TenantId"], [[l, r, TENANT] for l, r in sorted(temp_inc)])

# ───────────────────────────── vehicle types ─────────────────────────────
# name, kind, epq, ldm, L, W, H, GVW, chars, empty weight
VT_DEF = [
    ("Trekker 4x2", 1, None, None, 600, 255, 390, 20000, [], 7400),
    ("Trekker 6x2 (gestuurde naloopas)", 1, None, None, 690, 255, 390, 26000, ["STUURAS"], 8600),
    ("Trekker 6x4 zwaar transport", 1, None, None, 720, 255, 395, 33000, [], 10200),
    ("Oplegger 13,6 m schuifzeil", 2, 33, 13.6, 1360, 255, 400, 39000, ["TAUTLINER"], 6800),
    ("Koeloplegger 13,4 m", 2, 33, 13.4, 1340, 260, 400, 39000, ["KOEL", "VRIES", "ATP"], 8900),
    ("Mega-oplegger", 2, 33, 13.6, 1360, 255, 400, 39000, ["MEGA", "TAUTLINER"], 7100),
    ("Huifoplegger 13,6 m", 2, 33, 13.6, 1360, 255, 400, 39000, ["HUIF"], 6900),
    ("Containerchassis 40ft", 2, None, None, 1240, 255, 150, 39000, ["CONTAINER"], 4900),
    ("Dieplader 3-assig", 2, None, 14.0, 1400, 255, 90, 48000, ["DIEPLADER"], 11500),
    ("Kipperoplegger", 2, None, None, 1000, 255, 360, 39000, ["KIPPER"], 7300),
    ("Dubbeldeks oplegger", 2, 66, 13.6, 1360, 255, 400, 39000, ["DUBBELDEKS", "TAUTLINER"], 8200),
    ("Aanhangwagen tandem 7,2 m", 2, 18, 7.2, 770, 255, 400, 18000, ["TAUTLINER"], 4300),
    ("Bakwagen 7,5 t", 3, 15, 6.0, 750, 245, 340, 7490, ["LAADKLEP"], 4300),
    ("Bakwagen 18 t", 3, 18, 7.3, 1000, 255, 380, 18000, ["LAADKLEP"], 8800),
    ("Koelbakwagen 12 t", 3, 16, 6.5, 850, 255, 370, 12000, ["KOEL", "LAADKLEP", "ATP"], 6900),
    ("Motorwagen 10 m (26 t)", 3, 24, 9.6, 1200, 255, 400, 26000, ["LAADKLEP"], 11200),
    ("Bestelbus 3,5 t", 4, 4, 1.6, 600, 200, 260, 3500, [], 2250),
    ("Bakbus met laadklep", 4, 8, 4.2, 700, 220, 320, 3500, ["LAADKLEP"], 2350),
    ("Elektrische bestelbus", 4, 3, 1.3, 530, 200, 255, 3500, ["ZE"], 2400),
]
VT = {}
rows = []
vtc_rows = []
for name, kind, epq, ldm, L, W, H, gvw, chars, empty in VT_DEF:
    i = new_id()
    VT[name] = dict(id=i, kind=kind, epq=epq, ldm=ldm, L=L, W=W, H=H, gvw=gvw, chars=chars, empty=empty)
    rows.append([i, name, kind, float(epq) if epq else None, ldm, float(L), float(W), float(H), float(gvw), None] + audit(created(3 * 365, 3 * 365 - 30)))
    for c in chars:
        vtc_rows.append([i, TC[c], TENANT])
copy("VehicleTypes", ["Id", "Name", "Kind", "MaxEpq", "MaxLoadingMeters", "DefaultLengthCm", "DefaultWidthCm", "DefaultHeightCm", "DefaultGrossVehicleWeightKg", "Description"] + AUDIT, rows)
copy("VehicleTypeTransportCharacteristics", ["VehicleTypeId", "TransportCharacteristicsId", "TenantId"], vtc_rows)

# ───────────────────────────── transport units ─────────────────────────────
# name, L, W, H, tare, maxload, stackable, maxstack, ldm, epq, exchangeable, desc
TU_DEF = [
    ("Europallet (EPAL)", 120, 80, 14.4, 25, 1500, True, 4, 0.4, 1.0, True, "EUR-/EPAL-pallet, ruilbaar"),
    ("Blokpallet", 120, 100, 15.6, 30, 1500, True, 4, 0.5, 1.5, True, "Industriepallet 120×100"),
    ("Wegwerppallet", 120, 80, 14.4, 12, 1000, True, 3, 0.4, 1.0, False, "Eenmalige pallet, niet ruilbaar"),
    ("Halve pallet (Düsseldorfer)", 80, 60, 14.4, 10, 500, True, 3, 0.2, 0.5, True, "Displaypallet 80×60"),
    ("Kwart pallet", 60, 40, 14.4, 5, 250, True, 3, 0.1, 0.25, False, "Displaypallet 60×40"),
    ("CHEP-pallet (blauw)", 120, 100, 16.2, 32, 1250, True, 4, 0.5, 1.5, False, "Poolpallet, retour via CHEP"),
    ("CHEP halve pallet", 80, 60, 15.0, 10, 500, True, 3, 0.2, 0.5, False, "Poolpallet 80×60"),
    ("Rolcontainer", 80, 72, 170, 35, 500, False, None, 0.25, 0.6, True, "Standaard rolcontainer retail"),
    ("Melkrolcontainer", 72, 66, 160, 30, 400, False, None, 0.25, 0.5, True, "Zuivelrolcontainer"),
    ("CC-container (Deense kar)", 135, 56.5, 190, 32, 400, False, None, 0.25, 0.8, True, "Container Centralen bloemenkar"),
    ("Gitterbox", 124, 83.5, 97, 85, 1000, True, 5, 0.4, 1.0, True, "Stalen gaasbox"),
    ("Colli (doos)", 60, 40, 40, 0.5, 30, True, 8, 0.05, 0.1, False, "Losse doos"),
    ("Pakket", 40, 30, 30, 0.2, 31.5, True, 10, 0.02, 0.05, False, "Pakket tot 31,5 kg"),
    ("IBC (1000 l)", 120, 100, 116, 60, 1000, True, 2, 0.5, 1.5, False, "Intermediate Bulk Container"),
    ("Big bag", 90, 90, 110, 2, 1000, False, None, 0.4, 1.0, False, "FIBC voor bulkgoed"),
    ("Euro-krat E2", 60, 40, 20, 2, 40, True, 10, 0.05, 0.1, True, "Vleeskrat E2, ruilbaar"),
    ("Bierfust 50 l", 40, 40, 60, 12, 62, True, 3, 0.05, 0.1, True, "RVS fust"),
    ("Bloemenfust (emmer 534)", 34, 34, 40, 1, 25, False, None, 0.02, 0.05, True, "Fust voor snijbloemen"),
    ("Plantentray", 60, 40, 8, 0.4, 15, True, 6, 0.03, 0.08, False, "Kunststof tray voor potplanten"),
    ("Palletbox", 120, 100, 76, 40, 600, True, 3, 0.5, 1.5, False, "Kunststof palletbox, inklapbaar"),
    ("Kabelhaspel", 160, 160, 110, 120, 3000, False, None, 0.8, 2.0, False, "Houten haspel"),
    ("Staalcoil", 180, 180, 150, 0, 25000, False, None, 1.5, 3.0, False, "Coil op zadel"),
    ("Container 20ft", 606, 244, 259, 2200, 28200, False, None, 6.1, 11.0, False, "ISO-container 20ft"),
    ("Container 40ft", 1219, 244, 259, 3750, 26700, False, None, 12.2, 24.0, False, "ISO-container 40ft"),
    ("Container 45ft HC", 1372, 244, 290, 4800, 25600, False, None, 13.6, 33.0, False, "High cube 45ft"),
]
TU = {}
rows = []
for name, L, W, H, tare, mx, st, ms, ldm, epq, ex, desc in TU_DEF:
    i = new_id()
    TU[name] = dict(id=i, epq=epq)
    rows.append([i, name, False, float(L), float(W), float(H), float(tare), float(mx), st, ms, ldm, epq, ex, desc] + audit(created(3 * 365, 3 * 365 - 30)))
copy("TransportUnits", ["Id", "Name", "IsArchived", "LengthCm", "WidthCm", "HeightCm", "TareWeightKg", "MaxLoadKg", "IsStackable", "MaxStackCount", "LoadingMeters", "EpqFactor", "IsExchangeable", "Description"] + AUDIT, rows)

lf_rows = []
lf_units = ["Europallet (EPAL)", "Blokpallet", "Wegwerppallet", "Halve pallet (Düsseldorfer)", "CHEP-pallet (blauw)", "Rolcontainer",
            "Melkrolcontainer", "CC-container (Deense kar)", "Gitterbox", "IBC (1000 l)", "Palletbox"]
for tu in lf_units:
    for vtn, vt in VT.items():
        if vt["epq"] is None or vt["kind"] == 1:
            continue
        if tu in ("IBC (1000 l)", "Gitterbox", "Palletbox") and vt["kind"] == 4:
            continue
        epq = TU[tu]["epq"]
        cap = int(vt["epq"] / epq)
        if tu in ("Rolcontainer", "Melkrolcontainer"):
            cap = int(vt["ldm"] / 0.25) * 2 if vt["ldm"] else cap
        if tu == "CC-container (Deense kar)":
            cap = int(vt["ldm"] / 0.57) * 2 if vt["ldm"] else cap
        lf_rows.append([new_id(), TENANT, TU[tu]["id"], vt["id"], epq, float(cap)])
copy("TransportUnitLoadFactors", ["Id", "TenantId", "TransportUnitId", "VehicleTypeId", "LoadingCapacityFactor", "MaxCapacity"], lf_rows)

tu_inc = {canonical(TU[a]["id"], TU[b]["id"]) for a, b in [("IBC (1000 l)", "Euro-krat E2"), ("IBC (1000 l)", "Bloemenfust (emmer 534)"), ("Big bag", "CC-container (Deense kar)")]}
copy("TransportUnitIncompatibilities", ["LeftId", "RightId", "TenantId"], [[l, r, TENANT] for l, r in sorted(tu_inc)])

# ───────────────────────────── products ─────────────────────────────
ADR_DEF = [
    # name, UN, class, PG, description, technical, classcode, labels, HIN, tunnel, TC, env, LQ, waste, flash
    ("Benzine Euro 95 (bulk)", "1203", "3", "II", "BENZINE", None, "F1", "3", "33", "D/E", 2, True, False, False, -40.0),
    ("Benzine E10 in vaten", "1203", "3", "II", "BENZINE", None, "F1", "3", "33", "D/E", 2, True, False, False, -40.0),
    ("Diesel EN590 (bulk)", "1202", "3", "III", "DIESELOLIE", None, "F1", "3", "30", "D/E", 3, True, False, False, 56.0),
    ("Huisbrandolie", "1202", "3", "III", "GASOLIE", None, "F1", "3", "30", "D/E", 3, True, False, False, 60.0),
    ("Kerosine Jet A-1", "1223", "3", "III", "KEROSINE", None, "F1", "3", "30", "D/E", 3, False, False, False, 38.0),
    ("Zwavelzuur 96%", "1830", "8", "II", "ZWAVELZUUR met meer dan 51% zuur", None, "C1", "8", "80", "E", 2, False, False, False, None),
    ("Zoutzuur 30% in IBC", "1789", "8", "II", "ZOUTZUUR", None, "C1", "8", "80", "E", 2, False, False, False, None),
    ("Natronloog 50%", "1824", "8", "II", "NATRIUMHYDROXIDE, OPLOSSING", None, "C5", "8", "80", "E", 2, False, False, False, None),
    ("Natriumhydroxide parels", "1823", "8", "II", "NATRIUMHYDROXIDE, VAST", None, "C6", "8", "80", "E", 2, False, False, False, None),
    ("Salpeterzuur 53%", "2031", "8", "II", "SALPETERZUUR", None, "C1", "8", "80", "E", 2, False, False, False, None),
    ("Bleekwater (natriumhypochloriet)", "1791", "8", "III", "HYPOCHLORIETOPLOSSING", None, "C9", "8", "80", "E", 3, True, False, False, None),
    ("Ammonia 25% oplossing", "2672", "8", "III", "AMMONIAK, OPLOSSING", None, "C5", "8", "80", "E", 3, True, False, False, None),
    ("Bijtende reiniger industrieel", "1760", "8", "II", "BIJTENDE VLOEISTOF, N.E.G.", "kaliumhydroxide", "C9", "8", "80", "E", 2, False, False, False, None),
    ("Ontkalker zuur", "3264", "8", "III", "BIJTENDE ZURE ANORGANISCHE VLOEISTOF, N.E.G.", "fosforzuur", "C1", "8", "80", "E", 3, False, True, False, None),
    ("Oplosmiddel n.e.g.", "1993", "3", "III", "BRANDBARE VLOEISTOF, N.E.G.", "nafta", "F1", "3", "30", "D/E", 3, False, False, False, 42.0),
    ("Verdunner synthetisch", "1993", "3", "II", "BRANDBARE VLOEISTOF, N.E.G.", "xyleen, ethylacetaat", "F1", "3", "33", "D/E", 2, False, False, False, 12.0),
    ("Verf op oplosmiddelbasis", "1263", "3", "III", "VERF", None, "F1", "3", "30", "D/E", 3, False, True, False, 35.0),
    ("Lijm contact", "1133", "3", "II", "LIJM", None, "F1", "3", "33", "D/E", 2, False, True, False, -18.0),
    ("Aceton", "1090", "3", "II", "ACETON", None, "F1", "3", "33", "D/E", 2, False, False, False, -20.0),
    ("Ethanol 96%", "1170", "3", "II", "ETHANOL", None, "F1", "3", "33", "D/E", 2, False, False, False, 13.0),
    ("Methanol", "1230", "3", "II", "METHANOL", None, "FT1", "3+6.1", "336", "D/E", 2, False, False, False, 11.0),
    ("Isopropanol", "1219", "3", "II", "ISOPROPANOL", None, "F1", "3", "33", "D/E", 2, False, False, False, 12.0),
    ("Tolueen", "1294", "3", "II", "TOLUEEN", None, "F1", "3", "33", "D/E", 2, False, False, False, 4.0),
    ("Xyleen", "1307", "3", "III", "XYLENEN", None, "F1", "3", "30", "D/E", 3, False, False, False, 27.0),
    ("Milieugevaarlijke vloeistof n.e.g.", "3082", "9", "III", "MILIEUGEVAARLIJKE STOF, VLOEIBAAR, N.E.G.", "cypermethrine", "M6", "9", "90", None, 3, True, False, False, None),
    ("Afgewerkte olie (afval)", "3082", "9", "III", "AFVAL MILIEUGEVAARLIJKE STOF, VLOEIBAAR, N.E.G.", "minerale olie", "M6", "9", "90", None, 3, True, False, True, None),
    ("Milieugevaarlijke stof vast n.e.g.", "3077", "9", "III", "MILIEUGEVAARLIJKE STOF, VAST, N.E.G.", "zinkoxide", "M7", "9", "90", None, 3, True, False, False, None),
    ("Spuitbussen (aerosolen)", "1950", "2", None, "SPUITBUSSEN", None, "5F", "2.1", None, "D", 2, False, True, False, None),
    ("LPG-flessen", "1965", "2", None, "KOOLWATERSTOFGASMENGSEL, VLOEIBAAR GEMAAKT, N.E.G.", "propaan/butaan", "2F", "2.1", "23", "B/D", 2, False, False, False, None),
    ("Zuurstof samengeperst (cilinders)", "1072", "2", None, "ZUURSTOF, SAMENGEPERST", None, "1O", "2.2+5.1", "25", "E", 3, False, False, False, None),
    ("Stikstof samengeperst (cilinders)", "1066", "2", None, "STIKSTOF, SAMENGEPERST", None, "1A", "2.2", "20", "E", 3, False, False, False, None),
    ("Ammoniak watervrij", "1005", "2", None, "AMMONIAK, WATERVRIJ", None, "2TC", "2.3+8", "268", "C/D", 1, True, False, False, None),
    ("Waterstofperoxide 35%", "2014", "5.1", "II", "WATERSTOFPEROXIDE, WATERIGE OPLOSSING", None, "OC1", "5.1+8", "58", "E", 2, False, False, False, None),
    ("Ammoniumnitraat kunstmest", "2067", "5.1", "III", "KUNSTMEST OP BASIS VAN AMMONIUMNITRAAT", None, "O2", "5.1", "50", "E", 3, False, False, False, None),
    ("Accu's nat (startaccu's)", "2794", "8", None, "ACCUMULATOREN, NAT, GEVULD MET ZUUR", None, "C11", "8", None, "E", 3, False, False, False, None),
    ("Lithium-ion batterijen", "3480", "9", None, "LITHIUMIONBATTERIJEN", None, "M4", "9A", None, "E", 2, False, False, False, None),
    ("Gewasbeschermingsmiddel vloeibaar", "2902", "6.1", "III", "PESTICIDE, VLOEIBAAR, GIFTIG, N.E.G.", "glyfosaat", "T6", "6.1", "60", "E", 2, True, False, False, None),
    ("Chloroform", "1888", "6.1", "III", "CHLOROFORM", None, "T1", "6.1", "60", "E", 2, False, False, False, None),
    ("Bitumen verwarmd", "3257", "9", "III", "VERWARMDE VLOEISTOF, N.E.G.", "bitumen", "M9", "9", "99", "D", 3, False, False, False, None),
    ("Harsoplossing", "1866", "3", "III", "HARSOPLOSSING", None, "F1", "3", "30", "D/E", 3, False, False, False, 30.0),
]

PRODUCT_GROUPS = {
    "food": (["Aardappelen", "Uien", "Wortelen", "Tomaten", "Komkommers", "Paprika's", "Sla", "Champignons", "Appels Elstar", "Peren Conference",
              "Aardbeien", "Bananen", "Sinaasappels", "Kaas Gouda", "Boter", "Melkpoeder", "Yoghurt", "Kipfilet", "Rundvlees", "Varkensvlees",
              "Garnalen", "Haring", "Brood", "Beschuit", "Stroopwafels", "Chocolade", "Hagelslag", "Pindakaas", "Suiker", "Bloem (tarwe)",
              "Rijst", "Pasta", "Frituurolie", "Mayonaise", "Soep in blik", "Diepvriesfriet", "Diepvriespizza", "IJs", "Koffiebonen", "Thee"],
             ["25 kg zak", "palletlading", "kratten", "dozen", "big bag", "gekoeld", "diepvries", "bulk", "retailverpakking", "foodservice"]),
    "beverage": (["Pils", "Speciaalbier", "Frisdrank cola", "Frisdrank sinas", "Mineraalwater", "Spa rood", "Sinaasappelsap", "Appelsap", "Wijn rood",
                  "Wijn wit", "Jenever", "Energiedrank", "Ice tea", "Melk", "Chocomel", "Karnemelk", "Siroop"],
                 ["fust", "krat 24", "tray 6×1,5 l", "blik 24×33 cl", "fles 75 cl", "pak 1 l", "palletlading", "statiegeld retour"]),
    "flowers": (["Rozen", "Tulpen", "Chrysanten", "Gerbera's", "Lelies", "Orchideeën (Phalaenopsis)", "Potplanten", "Kerststerren", "Buxus",
                 "Lavendel", "Hortensia's", "Pioenrozen", "Freesia's", "Tuinplanten", "Kamerplanten", "Bloembollen", "Zaden", "Snijgroen"],
                ["CC-kar", "fust", "doos", "tray", "veilinglading", "export", "bundel"]),
    "building": (["Cement", "Metselzand", "Betonmortel", "Kalkzandsteen", "Bakstenen", "Dakpannen", "Gipsplaten", "Isolatieplaten PIR", "Steenwol",
                  "Betonstaal", "Bouwstaal", "Houten balken", "Multiplex", "OSB-platen", "Tegels", "Klinkers", "Stoeptegels", "Grind", "Tuinaarde",
                  "Kozijnen", "Glas (dubbel)", "PVC-buis", "Dakbedekking bitumen", "Tegellijm", "Voegmiddel", "Stelcon-platen", "Prefab beton"],
                 ["pallet", "big bag", "bundel", "los gestort", "per m³", "per ton", "rol", "kist", "op bok"]),
    "industrial": (["Staalplaat", "Aluminium profielen", "RVS buizen", "Machineonderdelen", "Kunststof granulaat", "Karton", "Papierrollen", "Folie",
                    "Pallets leeg", "Banden", "Auto-onderdelen", "Witgoed", "Meubelen", "Matrassen", "Textiel", "Schoenen", "Elektronica",
                    "Fietsen", "Speelgoed", "Cosmetica", "Huishoudartikelen", "Diervoeding", "Kunstmest", "Veevoer", "Medicijnen", "Medische hulpmiddelen"],
                   ["pallet", "doos", "colli", "rol", "kist", "gitterbox", "pakket", "op maat", "retour"]),
}

product_names = set()
products = []  # dicts


def add_product(name, adr=None, group=None):
    if name in product_names:
        return False
    product_names.add(name)
    products.append(dict(id=new_id(), name=name, adr=adr, group=group))
    return True


for d in ADR_DEF:
    add_product(d[0], adr=d, group="adr")
groups = list(PRODUCT_GROUPS.items())
target_non_adr = 760
while len(products) < 40 + target_non_adr:
    g, (bases, packs) = rnd.choices(groups, weights=[30, 15, 15, 20, 20])[0]
    add_product("%s %s" % (rnd.choice(bases), rnd.choice(packs)), group=g)

prod_rows = []
for p in products:
    g = p["group"]
    epq = rnd.choice([1.0, 1.0, 1.0, 0.5, 1.5, 0.25]) if g != "adr" else rnd.choice([1.0, 1.5])
    L, W = {1.0: (120, 80), 0.5: (80, 60), 1.5: (120, 100), 0.25: (60, 40)}[epq]
    H = float(rnd.choice([100, 120, 140, 160, 180, 200]))
    colli = float(rnd.choice([1, 12, 24, 36, 40, 48, 60, 80, 120]))
    weight = float(rnd.randint(150, 1000)) if g != "building" else float(rnd.randint(600, 1500))
    vol = round(L * W * H / 1e6, 3)
    ldm = {1.0: 0.4, 0.5: 0.2, 1.5: 0.5, 0.25: 0.1}[epq]
    archived = rnd.random() < 0.04
    p["archived"] = archived
    a = p["adr"]
    if a:
        _, un, cls, pg, desc, tech, cc, lab, hin, tun, tcat, env, lq, waste, flash = a
        adr_vals = [True, un, cls, pg, desc, tech, cc, lab, hin, tun, tcat, env, lq, waste, flash, None]
    else:
        adr_vals = [False, None, None, None, None, None, None, None, None, None, None, False, False, False, None, None]
    prod_rows.append([p["id"], p["name"], archived] + adr_vals + [1.0, weight, colli, vol, ldm, epq, float(L), float(W), H, None] + audit(created(3 * 365, 30)))
copy("Products", ["Id", "Name", "IsArchived", "IsAdr", "AdrUnNumber", "AdrClass", "AdrPackingGroup", "AdrDescription", "AdrTechnicalName",
                  "AdrClassificationCode", "AdrHazardLabels", "AdrHazardIdentificationNumber", "AdrTunnelRestrictionCode", "AdrTransportCategory",
                  "AdrIsEnvironmentallyHazardous", "AdrIsLimitedQuantity", "AdrIsWaste", "AdrFlashpointCelsius", "AdrSpecialProvisions",
                  "DefaultQuantity", "DefaultWeightKg", "DefaultColli", "DefaultVolumeM3", "DefaultLoadingMeters", "DefaultEpq",
                  "DefaultLengthCm", "DefaultWidthCm", "DefaultHeightCm", "Description"] + AUDIT, prod_rows)

# sparse incompatibilities: some ADR products vs food/beverage, oxidisers vs flammables
adr_products = [p for p in products if p["adr"]]
food_products = [p for p in products if p["group"] in ("food", "beverage")]
prod_inc = set()
while len(prod_inc) < 45:
    a = rnd.choice(adr_products)
    b = rnd.choice(food_products)
    prod_inc.add(canonical(a["id"], b["id"]))
by_name = {p["name"]: p for p in products}
for a, b in [("Waterstofperoxide 35%", "Benzine Euro 95 (bulk)"), ("Waterstofperoxide 35%", "Aceton"), ("Zuurstof samengeperst (cilinders)", "LPG-flessen"),
             ("Ammoniumnitraat kunstmest", "Diesel EN590 (bulk)"), ("Zwavelzuur 96%", "Natronloog 50%"), ("Salpeterzuur 53%", "Ethanol 96%")]:
    prod_inc.add(canonical(by_name[a]["id"], by_name[b]["id"]))
copy("ProductIncompatibilities", ["LeftId", "RightId", "TenantId"], [[l, r, TENANT] for l, r in sorted(prod_inc)])

# ───────────────────────────── relation categories ─────────────────────────────
CAT_DEF = [("Retail", "Winkelketens en filiaalbedrijven"), ("Groothandel", "Groothandels en distributeurs"), ("Bouw", "Bouwmaterialen en aannemers"),
           ("Agrarisch & Tuinbouw", "Telers, kwekers en agrarische bedrijven"), ("Food & Beverage", "Levensmiddelen en dranken"),
           ("Bloemen & Planten", "Snijbloemen, potplanten, veilingkopers"), ("Chemie", "Chemische industrie, ADR-verladers"), ("Farma", "Farmaceutisch, GDP"),
           ("E-commerce", "Webshops en fulfilment"), ("Industrie & Productie", "Maakindustrie en metaal"), ("Logistiek & Expeditie", "Collega-vervoerders en expediteurs"),
           ("Sleutelklant (A)", "Top-20 klanten op omzet"), ("Leveranciers", "Brandstof, onderhoud, banden en diensten")]
CAT = {}
rows = []
for n, d in CAT_DEF:
    i = new_id()
    CAT[n] = i
    rows.append([i, n, d] + audit(created(3 * 365, 3 * 365 - 30)))
copy("RelationCategories", ["Id", "Name", "Description"] + AUDIT, rows)

# ───────────────────────────── geographical regions ─────────────────────────────
REGION_DEF = [
    # NL provinces
    ("Groningen", "NL", [(9300, 9399), (9500, 9999)]),
    ("Friesland", "NL", [(8400, 9299)]),
    ("Drenthe", "NL", [(7740, 7999), (9400, 9499)]),
    ("Overijssel", "NL", [(7400, 7739), (8000, 8049), (8260, 8299)]),
    ("Flevoland", "NL", [(1300, 1379), (8200, 8259), (8300, 8319)]),
    ("Gelderland", "NL", [(3770, 3799), (3840, 3899), (4000, 4199), (6500, 6999), (7000, 7399), (8050, 8099)]),
    ("Utrecht", "NL", [(3400, 3769), (3800, 3839), (3900, 3999)]),
    ("Noord-Holland", "NL", [(1000, 1299), (1380, 1999), (2000, 2159)]),
    ("Zuid-Holland", "NL", [(2160, 3399), (4200, 4299)]),
    ("Zeeland", "NL", [(4300, 4599)]),
    ("Noord-Brabant", "NL", [(4600, 5799)]),
    ("Limburg (NL)", "NL", [(5800, 6499)]),
    # NL sub-regions
    ("Amsterdam", "NL", [(1000, 1109)]),
    ("Rijnmond", "NL", [(2900, 3299)]),
    ("Haaglanden", "NL", [(2490, 2799)]),
    ("Westland (Greenport)", "NL", [(2670, 2699)]),
    ("Greenport Aalsmeer", "NL", [(1430, 1436)]),
    ("Schiphol / Haarlemmermeer", "NL", [(2130, 2159)]),
    ("Utrecht stad", "NL", [(3500, 3585)]),
    ("Regio Eindhoven", "NL", [(5600, 5699)]),
    ("Greenport Venlo", "NL", [(5900, 5999)]),
    ("Moerdijk / West-Brabant", "NL", [(4600, 4899)]),
    ("Midden-Brabant (Tilburg/Waalwijk)", "NL", [(5000, 5199)]),
    ("Arnhem-Nijmegen", "NL", [(6500, 6599), (6800, 6899)]),
    # BE
    ("Brussel", "BE", [(1000, 1299)]),
    ("Waals-Brabant", "BE", [(1300, 1499)]),
    ("Vlaams-Brabant", "BE", [(1500, 1999), (3000, 3499)]),
    ("Antwerpen (provincie)", "BE", [(2000, 2999)]),
    ("Limburg (BE)", "BE", [(3500, 3999)]),
    ("Luik", "BE", [(4000, 4999)]),
    ("Namen", "BE", [(5000, 5999)]),
    ("Henegouwen", "BE", [(6000, 6599), (7000, 7999)]),
    ("Luxemburg (BE)", "BE", [(6600, 6999)]),
    ("West-Vlaanderen", "BE", [(8000, 8999)]),
    ("Oost-Vlaanderen", "BE", [(9000, 9999)]),
    # DE
    ("Ruhrgebiet / Niederrhein", "DE", [(44000, 47999)]),
    ("Düsseldorf / Mönchengladbach", "DE", [(40000, 42999)]),
    ("Köln / Aachen / Bonn", "DE", [(50000, 53999)]),
    ("Münsterland", "DE", [(48000, 48999)]),
    ("Weser-Ems", "DE", [(26000, 26999), (49000, 49999)]),
    ("Bremen", "DE", [(27000, 28999)]),
    ("Hamburg", "DE", [(20000, 22999)]),
    ("Hannover", "DE", [(30000, 31999)]),
    ("Ostwestfalen-Lippe", "DE", [(32000, 33999)]),
    ("Rhein-Main", "DE", [(60000, 65999)]),
    ("Berlin", "DE", [(10000, 14999)]),
    ("München", "DE", [(80000, 85999)]),
    ("Stuttgart", "DE", [(70000, 71999)]),
]
REGIONS = []
rows, rng_rows = [], []
for name, cc, ranges in REGION_DEF:
    i = new_id()
    REGIONS.append(dict(id=i, cc=cc, ranges=ranges))
    rows.append([i, name, None] + audit(created(3 * 365, 3 * 365 - 30)))
    for f, t in ranges:
        rng_rows.append([new_id(), TENANT, i, COUNTRY[cc][0], f, t, None])
copy("GeographicalRegions", ["Id", "Name", "Description"] + AUDIT, rows)
copy("GeographicalRegionPostalCodeRanges", ["Id", "TenantId", "GeographicalRegionId", "CountryId", "FromNumeric", "TillNumeric", "Prefix"], rng_rows)


def regions_for(cc, postal_key):
    m = re.match(r"\d+", postal_key)
    if not m:
        return []
    n = int(m.group(0))
    return [r["id"] for r in REGIONS if r["cc"] == cc and any(f <= n <= t for f, t in r["ranges"])]


# ───────────────────────────── geography for addresses ─────────────────────────────
# city, postcode from, to, lat, lon, area code
NL_CITIES = [
    ("Amsterdam", 1011, 1109, 52.370, 4.895, "20"), ("Haarlem", 2011, 2037, 52.381, 4.637, "23"), ("Zaandam", 1501, 1509, 52.438, 4.826, "75"),
    ("Alkmaar", 1811, 1827, 52.632, 4.748, "72"), ("Hoorn", 1621, 1628, 52.642, 5.060, "229"), ("Purmerend", 1441, 1448, 52.505, 4.959, "299"),
    ("Hilversum", 1211, 1223, 52.223, 5.176, "35"), ("Aalsmeer", 1431, 1433, 52.264, 4.762, "297"), ("Hoofddorp", 2131, 2136, 52.303, 4.689, "23"),
    ("Den Helder", 1781, 1789, 52.956, 4.760, "223"), ("Almere", 1311, 1363, 52.371, 5.222, "36"), ("Lelystad", 8211, 8245, 52.518, 5.471, "320"),
    ("Emmeloord", 8301, 8316, 52.711, 5.749, "527"), ("Den Haag", 2491, 2597, 52.078, 4.300, "70"), ("Leiden", 2311, 2334, 52.160, 4.497, "71"),
    ("Delft", 2611, 2629, 52.012, 4.359, "15"), ("Zoetermeer", 2711, 2729, 52.061, 4.494, "79"), ("Naaldwijk", 2671, 2671, 51.994, 4.209, "174"),
    ("Honselersdijk", 2675, 2675, 52.005, 4.228, "174"), ("De Lier", 2678, 2678, 51.973, 4.248, "174"), ("Monster", 2681, 2681, 52.026, 4.175, "174"),
    ("Poeldijk", 2685, 2685, 52.025, 4.219, "174"), ("Gouda", 2801, 2809, 52.012, 4.711, "182"), ("Rotterdam", 3011, 3089, 51.922, 4.479, "10"),
    ("Schiedam", 3111, 3125, 51.919, 4.400, "10"), ("Vlaardingen", 3131, 3138, 51.912, 4.342, "10"), ("Spijkenisse", 3201, 3209, 51.845, 4.329, "181"),
    ("Barendrecht", 2991, 2994, 51.856, 4.534, "180"), ("Ridderkerk", 2981, 2987, 51.872, 4.603, "180"), ("Dordrecht", 3311, 3329, 51.813, 4.690, "78"),
    ("Gorinchem", 4201, 4207, 51.834, 4.974, "183"), ("Utrecht", 3511, 3585, 52.091, 5.122, "30"), ("Nieuwegein", 3431, 3439, 52.029, 5.081, "30"),
    ("Amersfoort", 3811, 3829, 52.156, 5.388, "33"), ("Veenendaal", 3901, 3907, 52.028, 5.559, "318"), ("Zeist", 3701, 3709, 52.090, 5.233, "30"),
    ("Middelburg", 4331, 4339, 51.499, 3.611, "118"), ("Vlissingen", 4381, 4389, 51.443, 3.574, "118"), ("Goes", 4461, 4465, 51.504, 3.889, "113"),
    ("Terneuzen", 4531, 4539, 51.336, 3.828, "115"), ("Bergen op Zoom", 4611, 4625, 51.495, 4.287, "164"), ("Roosendaal", 4701, 4709, 51.531, 4.465, "165"),
    ("Breda", 4811, 4839, 51.589, 4.776, "76"), ("Moerdijk", 4782, 4782, 51.701, 4.626, "168"), ("Oosterhout", 4901, 4909, 51.645, 4.860, "162"),
    ("Tilburg", 5011, 5049, 51.556, 5.091, "13"), ("Waalwijk", 5141, 5146, 51.687, 5.070, "416"), ("'s-Hertogenbosch", 5211, 5237, 51.697, 5.304, "73"),
    ("Oss", 5341, 5351, 51.765, 5.518, "412"), ("Veghel", 5461, 5467, 51.617, 5.549, "413"), ("Eindhoven", 5611, 5658, 51.441, 5.478, "40"),
    ("Helmond", 5701, 5709, 51.482, 5.661, "492"), ("Venlo", 5911, 5928, 51.370, 6.172, "77"), ("Venray", 5801, 5809, 51.526, 5.975, "478"),
    ("Roermond", 6041, 6046, 51.194, 5.987, "475"), ("Weert", 6001, 6006, 51.252, 5.706, "495"), ("Sittard", 6131, 6137, 50.998, 5.869, "46"),
    ("Maastricht", 6211, 6229, 50.851, 5.691, "43"), ("Heerlen", 6411, 6419, 50.888, 5.979, "45"), ("Nijmegen", 6511, 6546, 51.842, 5.853, "24"),
    ("Arnhem", 6811, 6846, 51.985, 5.899, "26"), ("Ede", 6711, 6718, 52.040, 5.665, "318"), ("Wageningen", 6701, 6709, 51.969, 5.665, "317"),
    ("Tiel", 4001, 4007, 51.887, 5.429, "344"), ("Culemborg", 4101, 4107, 51.955, 5.228, "345"), ("Doetinchem", 7001, 7009, 51.965, 6.289, "314"),
    ("Zutphen", 7201, 7207, 52.142, 6.196, "575"), ("Apeldoorn", 7311, 7339, 52.211, 5.969, "55"), ("Barneveld", 3771, 3774, 52.140, 5.585, "342"),
    ("Harderwijk", 3841, 3849, 52.342, 5.620, "341"), ("Deventer", 7411, 7429, 52.255, 6.163, "570"), ("Zwolle", 8011, 8048, 52.516, 6.083, "38"),
    ("Kampen", 8261, 8267, 52.555, 5.911, "38"), ("Enschede", 7511, 7548, 52.221, 6.894, "53"), ("Hengelo", 7551, 7559, 52.266, 6.793, "74"),
    ("Almelo", 7601, 7609, 52.357, 6.662, "546"), ("Oldenzaal", 7571, 7577, 52.313, 6.929, "541"), ("Hardenberg", 7771, 7779, 52.576, 6.619, "523"),
    ("Emmen", 7811, 7826, 52.785, 6.897, "591"), ("Hoogeveen", 7901, 7909, 52.722, 6.476, "528"), ("Meppel", 7941, 7948, 52.696, 6.194, "522"),
    ("Assen", 9401, 9409, 52.993, 6.562, "592"), ("Groningen", 9711, 9747, 53.219, 6.567, "50"), ("Veendam", 9641, 9649, 53.106, 6.879, "598"),
    ("Hoogezand", 9601, 9611, 53.161, 6.761, "598"), ("Delfzijl", 9931, 9939, 53.330, 6.919, "596"), ("Leeuwarden", 8911, 8939, 53.201, 5.799, "58"),
    ("Drachten", 9201, 9204, 53.107, 6.099, "512"), ("Heerenveen", 8441, 8448, 52.960, 5.920, "513"), ("Sneek", 8601, 8608, 53.032, 5.659, "515"),
]
BE_CITIES = [
    ("Antwerpen", 2000, 2060, 51.219, 4.402, "3"), ("Gent", 9000, 9052, 51.054, 3.717, "9"), ("Brugge", 8000, 8200, 51.209, 3.225, "50"),
    ("Brussel", 1000, 1210, 50.850, 4.351, "2"), ("Leuven", 3000, 3012, 50.880, 4.700, "16"), ("Hasselt", 3500, 3512, 50.931, 5.338, "11"),
    ("Genk", 3600, 3600, 50.965, 5.500, "89"), ("Mechelen", 2800, 2812, 51.025, 4.477, "15"), ("Kortrijk", 8500, 8511, 50.828, 3.265, "56"),
    ("Zeebrugge", 8380, 8380, 51.330, 3.200, "50"), ("Luik", 4000, 4032, 50.633, 5.567, "4"), ("Charleroi", 6000, 6061, 50.411, 4.444, "71"),
    ("Namen", 5000, 5101, 50.467, 4.872, "81"), ("Turnhout", 2300, 2300, 51.322, 4.945, "14"), ("Aalst", 9300, 9300, 50.938, 4.040, "53"),
    ("Roeselare", 8800, 8800, 50.946, 3.123, "51"), ("Sint-Niklaas", 9100, 9100, 51.165, 4.143, "3"), ("Bergen (Mons)", 7000, 7000, 50.454, 3.952, "65"),
    ("Waver", 1300, 1301, 50.717, 4.612, "10"), ("Aarlen", 6700, 6700, 49.683, 5.817, "63"), ("Geel", 2440, 2440, 51.162, 4.990, "14"),
]
DE_CITIES = [
    ("Duisburg", 47051, 47279, 51.434, 6.762, "203"), ("Düsseldorf", 40210, 40629, 51.227, 6.773, "211"), ("Köln", 50667, 51149, 50.938, 6.960, "221"),
    ("Essen", 45127, 45359, 51.456, 7.012, "201"), ("Dortmund", 44135, 44388, 51.514, 7.466, "231"), ("Bochum", 44787, 44894, 51.482, 7.216, "234"),
    ("Krefeld", 47798, 47839, 51.339, 6.586, "2151"), ("Mönchengladbach", 41061, 41239, 51.185, 6.442, "2161"), ("Neuss", 41460, 41472, 51.198, 6.692, "2131"),
    ("Kleve", 47533, 47533, 51.788, 6.138, "2821"), ("Emmerich am Rhein", 46446, 46446, 51.834, 6.245, "2822"), ("Nettetal", 41334, 41334, 51.312, 6.283, "2153"),
    ("Aachen", 52062, 52080, 50.776, 6.084, "241"), ("Münster", 48143, 48167, 51.961, 7.626, "251"), ("Osnabrück", 49074, 49090, 52.279, 8.047, "541"),
    ("Bremen", 28195, 28779, 53.079, 8.802, "421"), ("Hamburg", 20095, 22769, 53.551, 9.994, "40"), ("Frankfurt am Main", 60306, 60599, 50.110, 8.682, "69"),
    ("Berlin", 10115, 14199, 52.520, 13.405, "30"), ("München", 80331, 81929, 48.137, 11.576, "89"), ("Stuttgart", 70173, 70629, 48.776, 9.183, "711"),
    ("Hannover", 30159, 30669, 52.376, 9.732, "511"), ("Bielefeld", 33602, 33739, 52.022, 8.532, "521"), ("Bocholt", 46395, 46399, 51.839, 6.616, "2871"),
    ("Nordhorn", 48527, 48531, 52.433, 7.068, "5921"), ("Emden", 26721, 26725, 53.367, 7.207, "4921"), ("Oberhausen", 46045, 46149, 51.470, 6.852, "208"),
    ("Wesel", 46483, 46487, 51.658, 6.618, "281"),
]
NL_STREETS = ["Industrieweg", "Transportweg", "Havenweg", "Nijverheidsweg", "Edisonstraat", "Marconistraat", "Energieweg", "Ambachtsweg", "Handelsweg",
              "Stationsweg", "Hoofdstraat", "Kerkstraat", "Dorpsstraat", "Molenweg", "Schoolstraat", "Parallelweg", "Kanaalweg", "Rijksstraatweg",
              "Distributieweg", "Logistiekweg", "Expeditieweg", "Containerweg", "Bedrijvenweg", "Zuidweg", "Noordweg", "Oostweg", "Westweg",
              "Kade", "Veilingweg", "Tuinbouwweg", "Glasblazerstraat", "Smederijstraat", "Ijzerweg", "Koperweg", "Zinkweg", "Platinastraat",
              "Diamantweg", "Robijnstraat", "Saturnusstraat", "Jupiterstraat", "Mercuriusweg", "Columbusweg", "Magellanstraat", "Pascalweg",
              "Newtonstraat", "Keplerweg", "Huygensstraat", "Galileïlaan", "Einsteinstraat", "Fokkerweg", "Van Leeuwenhoekweg", "Philipsweg",
              "Laan van Zuid", "Spoorstraat", "Dijkweg", "Polderweg", "Akkerweg", "Weidestraat", "Beukenlaan", "Eikenlaan", "Lindenlaan",
              "Kastanjelaan", "Wilhelminastraat", "Julianastraat", "Beatrixstraat", "Oranjestraat", "Nassaustraat", "Prins Bernhardweg",
              "Graaf Florisweg", "Willem Barentszstraat", "Abel Tasmanweg", "Kamerlingh Onnesweg", "Bosscheweg", "Utrechtseweg", "Amsterdamseweg",
              "Rotterdamseweg", "Middenweg", "Coldenhovelaan", "Ampèrestraat", "Voltaweg", "Wattstraat", "Ohmweg", "Faradaystraat"]
BE_STREETS = ["Industrielaan", "Kerkstraat", "Stationsstraat", "Nijverheidsstraat", "Havenlaan", "Ambachtenlaan", "Industriepark", "Steenweg op Antwerpen",
              "Kanaaldijk", "Brugsesteenweg", "Gentsesteenweg", "Rue de l'Industrie", "Chaussée de Bruxelles", "Avenue de l'Europe", "Zoning Industriel",
              "Leuvensesteenweg", "Noorderlaan", "Scheldelaan", "Vosseschijnstraat", "Ijzerlaan"]
DE_STREETS = ["Hauptstraße", "Industriestraße", "Gewerbestraße", "Bahnhofstraße", "Hafenstraße", "Am Hafen", "Siemensstraße", "Daimlerstraße",
              "Robert-Bosch-Straße", "Carl-Benz-Straße", "Logistikpark", "Im Gewerbegebiet", "Rheinstraße", "Kanalstraße", "Max-Planck-Straße",
              "Otto-Hahn-Straße", "Speditionstraße", "Werftstraße", "Ruhrorter Straße", "Industriering"]
NL_PC_LETTERS = "ABCDEGHJKLMNPRSTVWXZ"


def nl_postcode(n):
    while True:
        l = rnd.choice(NL_PC_LETTERS) + rnd.choice(NL_PC_LETTERS)
        if l not in ("SA", "SD", "SS"):
            return "%04d %s" % (n, l)


def make_address(cc, city=None, postal=False):
    cities = {"NL": NL_CITIES, "BE": BE_CITIES, "DE": DE_CITIES}[cc]
    c = city or rnd.choice(cities)
    name, f, t, lat, lon, area = c
    n = rnd.randint(f, t)
    if cc == "NL":
        pc = nl_postcode(n)
    elif cc == "BE":
        pc = "%04d" % n
    else:
        pc = "%05d" % n
    if postal:
        street = {"NL": "Postbus", "BE": "Postbus", "DE": "Postfach"}[cc]
        hn = str(rnd.randint(1, 9999))
        suffix = None
    else:
        street = rnd.choice({"NL": NL_STREETS, "BE": BE_STREETS, "DE": DE_STREETS}[cc])
        hn = str(rnd.choice([rnd.randint(1, 40), rnd.randint(1, 250)]))
        suffix = rnd.choice([None] * 12 + ["A", "B", "C", "-2", "a"])
    return dict(cc=cc, street=street, hn=hn, suffix=suffix, pc=pc, city=name, area=area,
                lat=round(lat + rnd.uniform(-0.03, 0.03), 6), lon=round(lon + rnd.uniform(-0.045, 0.045), 6))


def phone(cc, area):
    if cc == "NL":
        rest = 9 - len(area)
        num = "".join(str(rnd.randint(0, 9)) for _ in range(rest))
        num = str(rnd.randint(2, 8)) + num[1:]
        return "+31 %s %s" % (area, num if len(num) != 7 else num[:3] + " " + num[3:])
    if cc == "BE":
        rest = 8 - len(area)
        num = str(rnd.randint(2, 8)) + "".join(str(rnd.randint(0, 9)) for _ in range(rest - 1))
        return "+32 %s %s" % (area, num)
    num = str(rnd.randint(2, 9)) + "".join(str(rnd.randint(0, 9)) for _ in range(rnd.randint(5, 7)))
    return "+49 %s %s" % (area, num)


def mobile(cc):
    if cc == "NL":
        return "+31 6 %08d" % rnd.randint(10000000, 99999999)
    if cc == "BE":
        return "+32 4%02d %02d %02d %02d" % (rnd.choice([70, 75, 77, 78, 79, 84, 85, 86, 88, 89, 90, 92, 93, 94, 95, 96, 97, 98, 99]), rnd.randint(0, 99), rnd.randint(0, 99), rnd.randint(0, 99))
    return "+49 1%d %d" % (rnd.choice([51, 52, 57, 60, 62, 70, 71, 72, 73, 75, 76]), rnd.randint(1000000, 99999999))


# ───────────────────────────── names ─────────────────────────────
NL_FAMILY = ["de Jong", "Jansen", "de Vries", "van den Berg", "van Dijk", "Bakker", "Janssen", "Visser", "Smit", "Meijer", "de Boer", "Mulder", "de Groot",
             "Bos", "Vos", "Peters", "Hendriks", "van Leeuwen", "Dekker", "Brouwer", "de Wit", "Dijkstra", "Smits", "de Graaf", "van der Meer",
             "van der Linden", "Kok", "Jacobs", "de Haan", "Vermeulen", "van den Heuvel", "van der Veen", "van den Broek", "de Bruijn", "de Bruin",
             "van der Heijden", "Schouten", "van Beek", "Willems", "van Vliet", "van de Ven", "Hoekstra", "Maas", "Verhoeven", "Koster", "van Dam",
             "van der Wal", "Prins", "Blom", "Huisman", "Peeters", "de Jonge", "Kuipers", "van Veen", "Post", "Kuiper", "Veenstra", "Kramer",
             "van den Brink", "Scholten", "van Wijk", "Postma", "Martens", "Vink", "de Ruiter", "Timmermans", "Groen", "Gerritsen", "Jonker",
             "van Loon", "Boer", "van der Velde", "Willemsen", "Smeets", "de Lange", "de Vos", "Bosch", "van Dongen", "Schipper", "de Koning",
             "van der Laan", "Koning", "van der Velden", "Driessen", "van Doorn", "Hermans", "Evers", "van den Bosch", "van der Meulen", "Hofman",
             "Bosman", "Wouters", "Sanders", "van der Zee", "Verbeek", "Lammers", "Wolters", "Kempen", "Hoogendoorn", "Molenaar", "Verschoor",
             "Zwart", "Wijnands", "Brink", "Rietveld", "Overbeek", "Haverkamp", "Steenbergen", "Ten Have", "Nijhuis", "Oosterhuis", "Klaassen",
             "Hoekman", "Beekman", "Zijlstra", "Hiemstra", "Bouma", "Wiersma", "Tuinstra", "Kooistra", "Terpstra", "Brandsma", "Westerhof",
             "van Rijn", "van Tol", "van Os", "van Hout", "Heijmans", "Coenen", "Rutten", "Linssen", "Janssens", "Verstappen", "Hagens", "Ebben",
             "Hurkmans", "van Gils", "Hoeks", "Damen", "Pals", "Roelofs", "Arts", "Bongers", "Bruins", "Huiskes", "Lubbers", "Ploeg", "Spijkers"]
BE_FAMILY = ["Peeters", "Janssens", "Maes", "Jacobs", "Mertens", "Willems", "Claes", "Goossens", "Wouters", "De Smet", "Dubois", "Lambert",
             "Dupont", "Martin", "Vermeulen", "Van den Broeck", "Hermans", "Aerts", "Michiels", "Desmet", "Wuyts", "Verstraete", "Coppens", "Van Damme"]
DE_FAMILY = ["Müller", "Schmidt", "Schneider", "Fischer", "Weber", "Meyer", "Wagner", "Becker", "Schulz", "Hoffmann", "Koch", "Richter", "Klein",
             "Wolf", "Schröder", "Neumann", "Schwarz", "Zimmermann", "Braun", "Krüger", "Hofmann", "Hartmann", "Lange", "Schmitt", "Werner", "Krause"]
NL_FIRST_M = ["Jan", "Peter", "Mark", "Kees", "Henk", "Gerrit", "Bas", "Tom", "Dennis", "Sander", "Erik", "Martijn", "Jeroen", "Rob", "Wim", "Hans",
              "Ruud", "Niels", "Thijs", "Daan", "Bram", "Luuk", "Joost", "Arjan", "Wouter", "Stefan", "Patrick", "Ronald", "Marco", "Frank", "Johan",
              "Rick", "Kevin", "Jesse", "Sven", "Koen", "Twan", "Gijs", "Remco", "Edwin"]
NL_FIRST_F = ["Anna", "Maria", "Linda", "Sandra", "Monique", "Ingrid", "Marieke", "Esther", "Sanne", "Lisa", "Eva", "Femke", "Anouk", "Iris", "Kim",
              "Laura", "Nicole", "Petra", "Karin", "Wendy", "Ilse", "Joyce", "Manon", "Inge", "Saskia", "Mirjam", "Annemiek", "Fleur", "Noor", "Lotte"]
EE_FIRST = ["Piotr", "Tomasz", "Marek", "Paweł", "Krzysztof", "Andrzej", "Ion", "Mihai", "Gheorghe", "Vasile", "Dmytro", "Oleksandr", "Jānis", "Tomas"]
EE_LAST = ["Kowalski", "Nowak", "Wiśniewski", "Wójcik", "Kamiński", "Lewandowski", "Popescu", "Ionescu", "Stan", "Dumitru", "Shevchenko", "Bondarenko",
           "Bērziņš", "Kazlauskas"]


def split_family(f):
    parts = f.split(" ")
    i = 0
    while i < len(parts) - 1 and parts[i][0].islower():
        i += 1
    middle = " ".join(parts[:i]) or None
    return middle, " ".join(parts[i:])


def cap_family(f):
    return f[0].upper() + f[1:]


CUST_PATTERNS = [
    ("{F} Groothandel", "Groothandel"), ("{F} Bouwmaterialen", "Bouw"), ("Kwekerij {F}", "Bloemen & Planten"), ("{F} Bloemen", "Bloemen & Planten"),
    ("{F} Agri", "Agrarisch & Tuinbouw"), ("{F} Food", "Food & Beverage"), ("{F} Zuivel", "Food & Beverage"), ("{F} Vleeswaren", "Food & Beverage"),
    ("{F} Dranken", "Food & Beverage"), ("{F} Metaal", "Industrie & Productie"), ("{F} Kunststoffen", "Industrie & Productie"),
    ("{F} Verpakkingen", "Industrie & Productie"), ("{F} Installatietechniek", "Bouw"), ("{F} Elektrotechniek", "Industrie & Productie"),
    ("{F} Meubelen", "Retail"), ("Tuincentrum {F}", "Retail"), ("{F} Hoveniers", "Agrarisch & Tuinbouw"), ("{F} Machinehandel", "Industrie & Productie"),
    ("{F} Staalbouw", "Industrie & Productie"), ("Houthandel {F}", "Bouw"), ("{F} Papier", "Industrie & Productie"), ("{F} Textiel", "Retail"),
    ("{F} Chemie", "Chemie"), ("{F} Coatings", "Chemie"), ("Bakkerij {F}", "Food & Beverage"), ("{F} Groente & Fruit", "Food & Beverage"),
    ("{F} Aardappelhandel", "Agrarisch & Tuinbouw"), ("{F} Veevoeders", "Agrarisch & Tuinbouw"), ("{F} Diervoeding", "Groothandel"),
    ("{F} Sanitair", "Bouw"), ("Tegelhandel {F}", "Bouw"), ("{F} Bestratingen", "Bouw"), ("{F} Grondverzet", "Bouw"), ("{F} Recycling", "Industrie & Productie"),
    ("{F} Webshop", "E-commerce"), ("{F} Fietsen", "Retail"), ("{F} Autoonderdelen", "Groothandel"), ("{F} Witgoed", "Retail"),
    ("Drukkerij {F}", "Industrie & Productie"), ("Glashandel {F}", "Bouw"), ("{F} Isolatie", "Bouw"), ("{F} Dakbedekking", "Bouw"),
    ("{F} & Zn.", "Groothandel"), ("Gebr. {F}", "Groothandel"), ("{F} Holland", "Groothandel"), ("{F} Fresh", "Food & Beverage"),
    ("{F} Farma", "Farma"), ("{F} Medical", "Farma"), ("{F} Pharma Distributie", "Farma"), ("{F} Interieur", "Retail"), ("{F} Supermarkten", "Retail"),
    ("{F} Kaashandel", "Food & Beverage"), ("{F} Vishandel", "Food & Beverage"), ("{F} Plantenkwekerij", "Bloemen & Planten"),
    ("{F} Potplanten", "Bloemen & Planten"), ("{F} Glastuinbouw", "Agrarisch & Tuinbouw"), ("{F} Industrie", "Industrie & Productie"),
    ("{F} Trading", "Groothandel"), ("{F} Import & Export", "Groothandel"), ("{F} Fulfilment", "E-commerce"), ("{C} Fresh Center", "Food & Beverage"),
    ("{C}se Bouwmaterialenhandel", "Bouw"), ("Bloemenexport {C}", "Bloemen & Planten"), ("{C} Distributie", "Groothandel"),
]
DE_CUST = [("{F} Handels", "Groothandel"), ("{F} Baustoffe", "Bouw"), ("{F} Lebensmittel", "Food & Beverage"), ("{F} Metallbau", "Industrie & Productie"),
           ("{F} Kunststofftechnik", "Industrie & Productie"), ("{F} Chemie", "Chemie"), ("{F} Getränke", "Food & Beverage"), ("{F} Gartenbau", "Agrarisch & Tuinbouw"),
           ("{F} Verpackung", "Industrie & Productie"), ("{F} Möbel", "Retail")]
NL_FORMS = [" B.V."] * 7 + [" V.O.F.", "", " Holding B.V."]
BE_FORMS = [" BV", " NV", " BVBA", ""]
DE_FORMS = [" GmbH", " GmbH & Co. KG", " KG", " AG"]


def company_name(cc, kind):
    fam_list = {"NL": NL_FAMILY, "BE": BE_FAMILY, "DE": DE_FAMILY}[cc]
    F = cap_family(rnd.choice(fam_list))
    city = rnd.choice({"NL": NL_CITIES, "BE": BE_CITIES, "DE": DE_CITIES}[cc])[0]
    if kind == "customer":
        if cc == "DE":
            pat, cat = rnd.choice(DE_CUST)
        else:
            pat, cat = rnd.choice(CUST_PATTERNS)
            if cc == "BE" and pat.startswith("{C}se"):
                pat, cat = "{F} Distributie", "Groothandel"
    elif kind == "carrier":
        pats = {"NL": ["{F} Transport", "Transportbedrijf {F}", "{F} Logistics", "{F} Koeltransport", "Gebr. {F} Transport", "{F} Expeditie",
                       "{C} Container Transport", "{F} Internationaal Transport", "{F} Bulktransport", "{F} Tanktransport", "{F} Distributie",
                       "{F} Bloementransport", "{F} Zwaar Transport", "Koeriersdienst {F}"],
                "BE": ["{F} Transport", "Transport {F}", "{F} Logistics", "{F} Koeltransport"],
                "DE": ["Spedition {F}", "{F} Logistik", "{F} Transporte", "{F} Kühltransporte"]}[cc]
        pat, cat = rnd.choice(pats), "Logistiek & Expeditie"
    else:  # creditor
        pats = ["{F} Bandenservice", "Truckservice {C}", "Garagebedrijf {F}", "Tankstation {F}", "{C} Truck Wash", "{F} Brandstoffen",
                "{F} Leasing", "{F} Assurantiën", "{F} Truckparts", "Uitzendbureau {F}", "{F} Schadeherstel", "{F} Opleidingen", "{F} Koeltechniek",
                "{F} Tachograafservice", "{F} Palletservice", "{F} Schoonmaak"]
        pat, cat = rnd.choice(pats), "Leveranciers"
    name = pat.replace("{F}", F).replace("{C}", city)
    form = rnd.choice({"NL": NL_FORMS, "BE": BE_FORMS, "DE": DE_FORMS}[cc])
    if rnd.random() < 0.25 and "{C}" not in pat:
        name = name + " " + city
    return (name + form).strip(), cat, city


# ───────────────────────────── legal numbers ─────────────────────────────
used_kvk, used_vat, used_hrb = set(), set(), set()


def nl_kvk():
    while True:
        k = "%08d" % rnd.randint(10000000, 99999999)
        if k not in used_kvk:
            used_kvk.add(k)
            return k


def nl_vat():
    while True:
        d = [rnd.randint(0, 9) for _ in range(8)]
        d[0] = rnd.randint(0, 9)
        s = sum(x * w for x, w in zip(d, range(9, 1, -1))) % 11
        if s > 9:
            continue
        v = "NL" + "".join(map(str, d)) + str(s) + "B01"
        if v not in used_vat:
            used_vat.add(v)
            return v


def be_enterprise():
    while True:
        base8 = ("0" if rnd.random() < 0.85 else "1") + "%07d" % rnd.randint(2000000, 9999999)
        chk = 97 - int(base8) % 97
        num = base8 + "%02d" % chk
        if len(num) != 10:
            continue
        v = "BE" + num
        if v not in used_vat:
            used_vat.add(v)
            return num, v


def de_vat():
    while True:
        d = [rnd.randint(1, 9)] + [rnd.randint(0, 9) for _ in range(7)]
        product = 10
        for x in d:
            s = (x + product) % 10
            if s == 0:
                s = 10
            product = (2 * s) % 11
        c = 11 - product
        if c == 10:
            c = 0
        v = "DE" + "".join(map(str, d)) + str(c)
        if v not in used_vat:
            used_vat.add(v)
            return v


def de_hrb():
    while True:
        h = "HRB %d" % rnd.randint(1000, 99999)
        if h not in used_hrb:
            used_hrb.add(h)
            return h


# ───────────────────────────── banks ─────────────────────────────
NL_BANKS = [("ABNA", "ABNANL2A", "ABN AMRO"), ("INGB", "INGBNL2A", "ING"), ("RABO", "RABONL2U", "Rabobank"), ("SNSB", "SNSBNL2A", "SNS"),
            ("TRIO", "TRIONL2U", "Triodos Bank"), ("KNAB", "KNABNL2H", "Knab")]
BE_BANKS = [("001", "GEBABEBB", "BNP Paribas Fortis"), ("735", "KREDBEBB", "KBC"), ("363", "BBRUBEBB", "ING België"), ("068", "GKCCBEBB", "Belfius")]
DE_BANKS = [("37040044", "COBADEFFXXX", "Commerzbank"), ("50010517", "INGDDEFFXXX", "ING-DiBa"), ("36050105", "SPESDE3EXXX", "Sparkasse Essen"),
            ("35050000", "DUISDE33XXX", "Sparkasse Duisburg"), ("10010010", "PBNKDEFFXXX", "Postbank")]
used_iban = set()


def make_iban(cc):
    while True:
        if cc == "NL":
            code, bic, bank = rnd.choice(NL_BANKS)
            bban = code + "%010d" % rnd.randint(100000000, 9999999999)
        elif cc == "BE":
            code, bic, bank = rnd.choice(BE_BANKS)
            acct = code + "%07d" % rnd.randint(0, 9999999)
            chk = int(acct) % 97 or 97
            bban = acct + "%02d" % chk
        else:
            code, bic, bank = rnd.choice(DE_BANKS)
            bban = code + "%010d" % rnd.randint(100000, 9999999999)
        iban = cc + iban_check(cc, bban) + bban
        assert iban_valid(iban), iban
        if iban not in used_iban:
            used_iban.add(iban)
            return iban, bic, bank


# ───────────────────────────── relations ─────────────────────────────
relations = []
used_codes, used_names = set(), set()


def rel_code(name):
    words = re.sub(r"[^A-Z0-9 ]", "", unicodedata.normalize("NFD", name).encode("ascii", "ignore").decode().upper()).split()
    stop = {"BV", "VOF", "NV", "BVBA", "GMBH", "CO", "KG", "AG", "GEBR", "ZN", "VAN", "DE", "DEN", "DER", "HET", "TEN", "TER"}
    words = [w for w in words if w not in stop] or ["REL"]
    base = (words[0][:6] + (words[1][:3] if len(words) > 1 else ""))[:9]
    i = 1
    while True:
        c = "%s%02d" % (base, i) if i < 100 else "%s%d" % (base, i)
        if c not in used_codes:
            used_codes.add(c)
            return c
        i += 1


ROLE_C, ROLE_CAR, ROLE_CRED, ROLE_DEPOT, ROLE_OWN = 1, 2, 4, 8, 16
debtor_seq = [100000]
creditor_seq = [700000]
ext_used = set()


def new_relation(name, roles, cc, cat=None, city_name=None, full=None, created_ts=None, legal=True):
    r = dict(id=new_id(), name=name, full=full, roles=roles, cc=cc, cat=cat, created=created_ts or created())
    r["code"] = rel_code(name)
    r["search"] = search_key(name, full, r["code"])
    city_list = {"NL": NL_CITIES, "BE": BE_CITIES, "DE": DE_CITIES}[cc]
    city = next((c for c in city_list if c[0] == city_name), None) if city_name else None
    r["addr_main"] = make_address(cc, city)
    s = slug(name) or "relatie"
    r["domain"] = s + ".example"
    r["email"] = rnd.choice(["info", "info", "planning", "logistiek", "orders", "administratie"]) + "@" + r["domain"]
    r["website"] = "www." + r["domain"] if rnd.random() < 0.8 else None
    r["phone"] = phone(cc, r["addr_main"]["area"])
    r["mobile"] = mobile(cc) if rnd.random() < 0.3 else None
    r["coc"] = r["vat"] = None
    if legal:
        if cc == "NL":
            r["coc"] = nl_kvk() if rnd.random() < 0.94 else None
            r["vat"] = nl_vat() if rnd.random() < 0.9 else None
        elif cc == "BE":
            num, vat = be_enterprise()
            r["coc"] = num if rnd.random() < 0.9 else None
            r["vat"] = vat
        else:
            r["coc"] = norm_legal(de_hrb()) if rnd.random() < 0.7 else None
            r["vat"] = de_vat() if rnd.random() < 0.9 else None
    r["culture"] = "nl" if cc in ("NL", "BE") else rnd.choice(["en", None])
    r["debtor"] = None
    r["creditor"] = None
    if roles & ROLE_C:
        debtor_seq[0] += rnd.randint(1, 3)
        r["debtor"] = str(debtor_seq[0])
    if roles & (ROLE_CAR | ROLE_CRED):
        creditor_seq[0] += rnd.randint(1, 3)
        r["creditor"] = str(creditor_seq[0])
    r["carrier_code"] = None
    if roles & ROLE_CAR and rnd.random() < 0.6:
        r["carrier_code"] = cc + r["code"][:4] + "%02d" % rnd.randint(1, 99)
    r["archived"] = False
    r["blocked"] = False
    r["block_reason"] = 0
    relations.append(r)
    return r


# own company
own = new_relation("Deltalijn Transport & Logistiek B.V.", ROLE_OWN, "NL", city_name="Moerdijk",
                   created_ts=created(3 * 365, 3 * 365 - 1))
own["coc"] = own["coc"] or nl_kvk()
own["vat"] = own["vat"] or nl_vat()
own["website"] = "www.deltalijn.example"
own["email"] = "planning@deltalijn.example"
own["domain"] = "deltalijn.example"
own["addr_main"] = dict(cc="NL", street="Middenweg", hn="18", suffix=None, pc="4782 PM", city="Moerdijk", area="168", lat=51.6935, lon=4.6118)

# depots (own branches and partner warehouses)
DEPOT_DEF = [("Depot Rotterdam Waalhaven", "Rotterdam"), ("Depot Moerdijk", "Moerdijk"), ("Crossdock Venlo", "Venlo"), ("DC Tilburg", "Tilburg"),
             ("Depot Waalwijk", "Waalwijk"), ("Koelhuis Barendrecht", "Barendrecht"), ("Depot Naaldwijk (Westland)", "Naaldwijk"),
             ("Depot Aalsmeer", "Aalsmeer"), ("Crossdock Schiphol", "Hoofddorp"), ("Depot Zwolle", "Zwolle"), ("Depot Groningen", "Groningen"),
             ("Depot Eindhoven", "Eindhoven"), ("Depot Nijmegen", "Nijmegen"), ("Depot Amersfoort", "Amersfoort"), ("Overslag Duisburg", "Duisburg"),
             ("Overslag Antwerpen", "Antwerpen"), ("Depot Vlissingen", "Vlissingen"), ("Depot Heerenveen", "Heerenveen"),
             ("Partnerwarehouse Oss", "Oss"), ("Partnerwarehouse Breda", "Breda")]
depots = []
for i, (n, city) in enumerate(DEPOT_DEF):
    cc = "DE" if city == "Duisburg" else "BE" if city == "Antwerpen" else "NL"
    roles = ROLE_DEPOT | (ROLE_C if n.startswith("Partner") else 0)
    d = new_relation(n, roles, cc, cat="Logistiek & Expeditie", city_name=city, created_ts=created(3 * 365, 2 * 365), legal=n.startswith("Partner"))
    if not n.startswith("Partner"):
        d["email"] = "depot.%s@deltalijn.example" % slug(city)
        d["website"] = None
    depots.append(d)

# carriers
carriers = []
for i in range(300):
    cc = rnd.choices(["NL", "BE", "DE"], [80, 10, 10])[0]
    while True:
        name, cat, city = company_name(cc, "carrier")
        if name not in used_names:
            used_names.add(name)
            break
    x = rnd.random()
    roles = ROLE_CAR | ROLE_CRED if x < 0.8 else (ROLE_CAR | ROLE_C | ROLE_CRED if x < 0.9 else ROLE_CAR)
    carriers.append(new_relation(name, roles, cc, cat=cat, city_name=city))

# creditors
creditors = []
for i in range(150):
    cc = rnd.choices(["NL", "BE", "DE"], [90, 5, 5])[0]
    while True:
        name, cat, city = company_name(cc, "creditor")
        if name not in used_names:
            used_names.add(name)
            break
    creditors.append(new_relation(name, ROLE_CRED, cc, cat=cat, city_name=city))

# customers
customers = []
n_customers = 20000 - len(relations)
for i in range(n_customers):
    cc = rnd.choices(["NL", "BE", "DE"], [90, 6, 4])[0]
    for attempt in range(50):
        name, cat, city = company_name(cc, "customer")
        if name not in used_names:
            used_names.add(name)
            break
    else:
        raise SystemExit("could not find a unique customer name")
    roles = ROLE_C | (ROLE_CRED if rnd.random() < 0.01 else 0)
    full = None
    if rnd.random() < 0.15:
        full = name + " (" + rnd.choice(["vestiging " + city, "hoofdkantoor", "magazijn", "afdeling logistiek"]) + ")"
        if len(full) > 128:
            full = None
    r = new_relation(name, roles, cc, cat=cat, city_name=city, full=full)
    customers.append(r)

# archived / blocked
for r in relations:
    if r is own or r in depots:
        continue
    if rnd.random() < 0.03:
        r["archived"] = True
    if rnd.random() < 0.01:
        r["blocked"] = True
        r["block_reason"] = rnd.choice([1, 1, 1, 2, 2, 3, 4, 5])

rel_rows, addr_rows, contact_rows, bank_rows, catm_rows, regm_rows, ext_rows, rtc_rows = [], [], [], [], [], [], [], []
key_customers = set(rnd.sample(range(len(customers)), 20))
for idx, r in enumerate(relations):
    desc = None
    if r["blocked"]:
        desc = {1: "Geblokkeerd: openstaande facturen > 90 dagen", 2: "Kredietlimiet overschreden", 3: "Faillissement aangevraagd",
                4: "Geschil over schadeclaim", 5: "Geblokkeerd op verzoek van directie"}[r["block_reason"]]
    rel_rows.append([r["id"], r["code"], r["name"], r["full"], r["search"], r["roles"], r["archived"], r["blocked"], r["block_reason"],
                     r["phone"], r["mobile"], r["email"], r["website"], r["coc"], r["vat"], r["culture"], desc,
                     r["carrier_code"], r["creditor"], EUR, r["debtor"]] + audit(r["created"]))
    cc = r["cc"]
    zone = COUNTRY[cc][1]
    a = r["addr_main"]
    x = rnd.random()
    if r is own or x < 0.20:
        # visiting + postal
        addr_rows.append([new_id(), TENANT, r["id"], ADDRESS_TYPE_VISITING, COUNTRY[cc][0], a["street"], a["hn"], a["suffix"], None, a["pc"], norm_postal(a["pc"]), a["city"], a["lat"], a["lon"], zone])
        city = next((c for c in {"NL": NL_CITIES, "BE": BE_CITIES, "DE": DE_CITIES}[cc] if c[0] == a["city"]), None)
        p = make_address(cc, city, postal=True)
        addr_rows.append([new_id(), TENANT, r["id"], ADDRESS_TYPE_POSTAL, COUNTRY[cc][0], p["street"], p["hn"], None, None, p["pc"], norm_postal(p["pc"]), p["city"], None, None, zone])
    else:
        line2 = rnd.choice([None] * 15 + ["Unit 3", "Hal B", "Dock 12", "Kavel 4", "t.a.v. goederenontvangst"])
        addr_rows.append([new_id(), TENANT, r["id"], None, COUNTRY[cc][0], a["street"], a["hn"], a["suffix"], line2, a["pc"], norm_postal(a["pc"]), a["city"], a["lat"], a["lon"], zone])
        if x > 0.97:  # default + separate visiting address
            b = make_address(cc)
            addr_rows.append([new_id(), TENANT, r["id"], ADDRESS_TYPE_VISITING, COUNTRY[cc][0], b["street"], b["hn"], b["suffix"], None, b["pc"], norm_postal(b["pc"]), b["city"], b["lat"], b["lon"], zone])
    # regions of the main address
    if rnd.random() < 0.85 or r["roles"] & (ROLE_DEPOT | ROLE_OWN):
        for g in regions_for(cc, norm_postal(a["pc"])):
            regm_rows.append([r["id"], g, TENANT])
    # categories
    cats = set()
    if r["cat"]:
        cats.add(CAT[r["cat"]])
    if r["roles"] & ROLE_C and rnd.random() < 0.15:
        cats.add(CAT[rnd.choice(["Retail", "Groothandel", "E-commerce", "Food & Beverage"])])
    for c in cats:
        catm_rows.append([r["id"], c, TENANT])
    # contact persons (~2)
    n_contacts = rnd.choices([0, 1, 2, 3, 4], [4, 25, 45, 18, 8])[0]
    roles_pool = [(1, "Planner", "Planning"), (2, "Medewerker crediteurenadministratie", "Financiën"), (2, "Boekhouder", "Financiën"),
                  (3, "Accountmanager", "Verkoop"), (3, "Inkoper", "Inkoop"), (4, "Directeur", "Directie"), (4, "Bedrijfsleider", "Directie"),
                  (1, "Logistiek manager", "Logistiek"), (1, "Expediteur", "Logistiek"), (0, "Magazijnchef", "Magazijn"), (0, "Customer service", "Klantenservice")]
    for _ in range(n_contacts):
        if cc == "DE":
            first = rnd.choice(["Thomas", "Michael", "Andreas", "Stefan", "Sabine", "Katrin", "Julia", "Jürgen", "Uwe", "Anja"])
            middle, last = None, rnd.choice(DE_FAMILY)
        else:
            first = rnd.choice(NL_FIRST_M + NL_FIRST_F)
            middle, last = split_family(rnd.choice(NL_FAMILY if cc == "NL" else BE_FAMILY))
        role, job, dept = rnd.choice(roles_pool)
        local = slug(first)[:1] + "." + slug((middle or "") + last).replace("-", "")
        contact_rows.append([new_id(), TENANT, r["id"], first, middle, last, role, job, dept, local + "@" + r["domain"],
                             phone(cc, r["addr_main"]["area"]) if rnd.random() < 0.6 else None, mobile(cc) if rnd.random() < 0.7 else None])
    # bank accounts: carriers/creditors 1-2, some customers 1
    nb = 0
    if r["roles"] & (ROLE_CAR | ROLE_CRED):
        nb = rnd.choices([1, 2], [75, 25])[0]
    elif r["roles"] & ROLE_C and rnd.random() < 0.10:
        nb = 1
    elif r is own:
        nb = 2
    accts = [make_iban(cc) for _ in range(nb)]
    default_i = rnd.randrange(nb) if nb else -1
    for j, (iban, bic, bank) in enumerate(accts):
        bank_rows.append([new_id(), TENANT, r["id"], iban, bic, bank, r["name"][:128], j == default_i])
    # external identifiers (accounting link)
    if r["roles"] & ROLE_C and rnd.random() < 0.25:
        ext_rows.append([new_id(), TENANT, r["id"], "Exact Online", r["debtor"]])
    if r["roles"] & (ROLE_CAR | ROLE_CRED) and rnd.random() < 0.4:
        ext_rows.append([new_id(), TENANT, r["id"], "Exact Online", "C" + r["creditor"]])
    if r["roles"] & ROLE_C and rnd.random() < 0.05:
        ext_rows.append([new_id(), TENANT, r["id"], "Transporeon", "TP-%07d" % (idx * 7 + 1000000)])
    # transport characteristics demanded/offered
    if r["roles"] & (ROLE_C | ROLE_CAR) and rnd.random() < 0.10:
        for c in rnd.sample(["LAADKLEP", "KOEL", "VRIES", "ADR", "GDP", "KOOIAAP", "CCKAR", "TAUTLINER", "SECURE", "DOUANE"], rnd.randint(1, 3)):
            rtc_rows.append([r["id"], TC[c], TENANT])

# key-account category for 20 customers
for i in key_customers:
    catm_rows.append([customers[i]["id"], CAT["Sleutelklant (A)"], TENANT])
catm_rows = [list(t) for t in {tuple(x) for x in catm_rows}]

copy("Relations", ["Id", "Code", "Name", "FullName", "SearchKey", "Roles", "IsArchived", "IsBlocked", "BlockReason", "Phone", "Mobile", "Email",
                   "Website", "ChamberOfCommerceNumber", "VatNumber", "PreferredCulture", "Description", "CarrierCode", "CreditorNumber",
                   "CurrencyId", "DebtorNumber"] + AUDIT, rel_rows)
copy("RelationAddresses", ["Id", "TenantId", "RelationId", "AddressTypeId", "CountryId", "Street", "HouseNumber", "HouseNumberSuffix", "AddressLine2",
                           "PostalCode", "PostalCodeKey", "City", "Latitude", "Longitude", "TimeZoneId"], addr_rows)
copy("RelationContactPersons", ["Id", "TenantId", "RelationId", "FirstName", "MiddleName", "LastName", "Role", "JobTitle", "Department", "Email",
                                "Phone", "Mobile"], contact_rows)
copy("RelationBankAccounts", ["Id", "TenantId", "RelationId", "Iban", "Bic", "BankName", "AccountHolder", "IsDefault"], bank_rows)
copy("RelationCategoryMemberships", ["RelationId", "RelationCategoryId", "TenantId"], sorted(catm_rows))
copy("RelationGeographicalRegions", ["RelationId", "GeographicalRegionId", "TenantId"], regm_rows)
copy("RelationExternalIdentifiers", ["Id", "TenantId", "RelationId", "Source", "Identifier"], ext_rows)
copy("RelationTransportCharacteristics", ["RelationId", "TransportCharacteristicsId", "TenantId"], rtc_rows)

# ───────────────────────────── drivers ─────────────────────────────
fleet_carriers = [c for c in carriers if not c["archived"]]
rnd.shuffle(fleet_carriers)
fleet_carriers = fleet_carriers[:60]
drivers = []
used_driver_codes = set()
drv_rows, q_rows = [], []
for i in range(500):
    employer = own if i < 350 else fleet_carriers[(i - 350) % len(fleet_carriers)]
    ee = rnd.random() < (0.12 if employer is own else 0.3)
    if ee:
        first, middle, last, culture = rnd.choice(EE_FIRST), None, rnd.choice(EE_LAST), "en"
    else:
        first = rnd.choice(NL_FIRST_M * 6 + NL_FIRST_F)
        middle, last = split_family(rnd.choice(NL_FAMILY if employer["cc"] == "NL" else BE_FAMILY if employer["cc"] == "BE" else DE_FAMILY))
        culture = "nl" if employer["cc"] != "DE" else "en"
    code = "CH%04d" % (1001 + i)
    initials = first[0] + "."
    if rnd.random() < 0.4:
        initials = first[0] + "." + rnd.choice("JHPAMWC") + "."
    archived = rnd.random() < 0.03
    d = dict(id=new_id(), employer=employer, archived=archived)
    drivers.append(d)
    dom = employer["domain"]
    email = (slug(first)[:1] + "." + slug((middle or "") + last).replace("-", "") + "@" + dom) if rnd.random() < 0.7 else None
    drv_rows.append([d["id"], code, first, middle, last, initials, search_key(first, middle, last, code), None, mobile(employer["cc"]), email, culture,
                     employer["id"], None, archived, False, None] + audit(created(3 * 365, 10)))
    # qualifications
    licence_no = "%010d" % rnd.randint(4000000000, 5999999999)

    def validity(years):
        r = rnd.random()
        if r < 0.10:
            until = TODAY + dt.timedelta(days=rnd.randint(1, 89))
        elif r < 0.13:
            until = TODAY - dt.timedelta(days=rnd.randint(1, 120))
        else:
            until = TODAY + dt.timedelta(days=rnd.randint(90, years * 365))
        frm = until - dt.timedelta(days=years * 365)
        return frm, until

    f, u = validity(5)
    q_rows.append([new_id(), TENANT, d["id"], 3, licence_no, f, u, "Rijbewijs C/CE" if employer["cc"] == "NL" else "Führerschein CE" if employer["cc"] == "DE" else "Rijbewijs CE"])
    f, u = validity(5)
    q_rows.append([new_id(), TENANT, d["id"], 4, licence_no, f, u, "Code 95 (vakbekwaamheid)"])
    f, u = validity(5)
    q_rows.append([new_id(), TENANT, d["id"], 6, ("NB" if employer["cc"] == "NL" else employer["cc"]) + "%014d" % rnd.randint(10 ** 12, 10 ** 14 - 1), f, u, "Bestuurderskaart tachograaf"])
    if rnd.random() < 0.3:
        f, u = validity(5)
        q_rows.append([new_id(), TENANT, d["id"], 5, "ADR-%s-%06d" % (employer["cc"], rnd.randint(1, 999999)), f, u, rnd.choice(["ADR basis", "ADR basis + tank", "ADR basis + tank + klasse 1"])])
    if rnd.random() < 0.25:
        q_rows.append([new_id(), TENANT, d["id"], 7, None, TODAY - dt.timedelta(days=rnd.randint(100, 2000)), None, "Heftruckcertificaat"])
copy("Drivers", ["Id", "Code", "FirstName", "MiddleName", "LastName", "Initials", "SearchKey", "Phone", "Mobile", "Email", "PreferredCulture",
                 "EmployerRelationId", "UserId", "IsArchived", "IsAnonymised", "Description"] + AUDIT, drv_rows)
copy("DriverQualifications", ["Id", "TenantId", "DriverId", "Kind", "Number", "ValidFrom", "ValidUntil", "Description"], q_rows)

# ───────────────────────────── vehicles ─────────────────────────────
PLATE_L = "BDFGHJKLMNPRSTVXZ"
used_plates = {"NL": set(), "BE": set(), "DE": set()}
used_vins = set()


def plate(cc, kind):
    while True:
        L = lambda: rnd.choice(PLATE_L)
        D = lambda: str(rnd.randint(0, 9))
        if cc == "NL":
            if kind == 2:  # trailers: O-/W- prefix, sidecode LLDDLL
                p = rnd.choice("OW") + L() + D() + D() + L() + L()
            elif kind == 4:  # vans: V-123-BB (LDDDLL) or VBB-12-X (LLLDDL)
                p = rnd.choice(["V" + D() + D() + D() + L() + L(), "V" + L() + L() + D() + D() + L()])
            else:  # trucks: B prefix, sidecodes LLDDLL / DDLLLD
                p = rnd.choice(["B" + L() + D() + D() + L() + L(), D() + D() + "B" + L() + L() + D()])
        elif cc == "BE":
            p = (rnd.choice("Q") if kind == 2 else str(rnd.randint(1, 2))) + L() + L() + L() + D() + D() + D()
        else:
            pref = rnd.choice(["DU", "KLE", "BOR", "WES", "D", "K", "E", "MG", "NE", "HH", "HB"])
            p = pref + L() + L() + str(rnd.randint(1, 9999))
        if p not in used_plates[cc]:
            used_plates[cc].add(p)
            return p


def fmt_plate_nl(p):
    pat = "".join("D" if c.isdigit() else "L" for c in p)
    dashes = {"LLDDLL": (2, 4), "LDDDLL": (1, 4), "LLLDDL": (3, 5), "DDLLLD": (2, 5)}.get(pat)
    return p if not dashes else p[:dashes[0]] + "-" + p[dashes[0]:dashes[1]] + "-" + p[dashes[1]:]


WMI = {1: ["XLR", "YV2", "YS2", "WMA", "W1T", "ZCF", "VF6"], 3: ["XLR", "WMA", "W1T", "YV2", "ZCF"], 4: ["W1V", "WV1", "WF0", "VF1", "ZFA"],
       2: ["WKE", "WSM", "WK0", "XLB", "YE2", "WDS"]}
vehicles = []
veh_rows, vtc2_rows, vdd_rows, vext_rows = [], [], [], []
used_vnames = set()
own_types = {1: ["Trekker 4x2"] * 6 + ["Trekker 6x2 (gestuurde naloopas)"] * 3 + ["Trekker 6x4 zwaar transport"],
             2: ["Oplegger 13,6 m schuifzeil"] * 5 + ["Koeloplegger 13,4 m"] * 4 + ["Mega-oplegger"] * 2 + ["Huifoplegger 13,6 m"] * 2 +
                ["Containerchassis 40ft"] * 2 + ["Dubbeldeks oplegger", "Dieplader 3-assig", "Kipperoplegger"] + ["Aanhangwagen tandem 7,2 m"] * 2,
             3: ["Bakwagen 7,5 t", "Bakwagen 18 t", "Bakwagen 18 t", "Koelbakwagen 12 t", "Motorwagen 10 m (26 t)", "Motorwagen 10 m (26 t)"],
             4: ["Bestelbus 3,5 t", "Bakbus met laadklep", "Elektrische bestelbus"]}
prefix = {1: "T", 2: "OPL", 3: "BW", 4: "BB"}
fleet_no = {1: 100, 2: 300, 3: 600, 4: 800}
own_drivers = [d for d in drivers if d["employer"] is own and not d["archived"]]
rnd.shuffle(own_drivers)
own_drv_iter = iter(own_drivers * 3)
own_depots = [d for d in depots if not d["name"].startswith("Partner")] + [own]
ext_seq = {"Transics": 100000, "Fleetboard": 500000, "Trimble": 800000}


def add_vehicle(owner, kind, typename, is_own):
    vt = VT[typename]
    cc = owner["cc"] if not is_own else "NL"
    year = rnd.randint(2015, 2026) if kind != 2 else rnd.randint(2014, 2026)
    if is_own:
        fleet_no[kind] += 1
        pre = "KO" if typename.startswith("Koel") and kind == 2 else "AH" if typename.startswith("Aanhangwagen") else prefix[kind]
        name = "%s-%d" % (pre, fleet_no[kind])
    p = plate(cc, kind)
    if not is_own:
        shown = fmt_plate_nl(p) if cc == "NL" else p
        name = "%s %s" % (owner["code"][:6], shown)
    assert name.upper() not in used_vnames
    used_vnames.add(name.upper())
    while True:
        vin = make_vin(rnd.choice(WMI[kind]), year)
        if vin not in used_vins:
            used_vins.add(vin)
            break
    if kind == 2:
        fuel, euro = 12, None
    elif kind == 4:
        if typename == "Elektrische bestelbus":
            fuel, euro = 7, 9
        else:
            fuel = rnd.choices([1, 7, 6], [70, 20, 10])[0]
            euro = 9 if fuel == 7 else (7 if year >= 2017 else 6)
    else:
        fuel = rnd.choices([1, 3, 6, 7, 9], [72, 8, 12, 6, 2])[0]
        euro = 9 if fuel == 7 else (7 if year >= 2014 else 6)
        if fuel in (1, 6) and rnd.random() < 0.05:
            euro = 6
    empty = float(vt["empty"] + rnd.randint(-300, 300))
    gvw = float(vt["gvw"])
    payload = max(0.0, gvw - empty)
    archived = rnd.random() < 0.03
    depot = rnd.choice(own_depots)["id"] if is_own else None
    desc = None
    if rnd.random() < 0.05:
        desc = rnd.choice(["Tweede eigenaar, gekocht in %d" % (year + 2), "Leasecontract tot %d" % (2027 + rnd.randint(0, 3)), "Vaste rit Albert-route", "Reservewagen"])
    v = dict(id=new_id(), name=name, kind=kind, owner=owner, archived=archived, is_own=is_own, typename=typename)
    vehicles.append(v)
    veh_rows.append([v["id"], name, vt["id"], p, COUNTRY[cc][0], vin, owner["id"], depot, float(vt["L"]), float(vt["W"]), float(vt["H"]), gvw, empty,
                     float(vt["epq"]) if vt["epq"] else None, vt["ldm"], payload, fuel, euro, archived, desc, name.upper()] + audit(created(3 * 365, 5)))
    # own characteristics on top of the type
    extra = []
    if kind == 2 and rnd.random() < 0.15:
        extra += rnd.sample(["KOOIAAP", "DOUANE", "PALLETWAGEN", "SECURE", "ADR"], 1)
    if kind in (1, 3) and rnd.random() < 0.2:
        extra.append("ADR")
    if kind == 3 and rnd.random() < 0.2:
        extra.append(rnd.choice(["KRAAN", "ROLCONT", "STUURAS"]))
    if fuel == 7 and kind != 4:
        extra.append("ZE")
    for c in sorted(set(extra) - set(vt["chars"])):
        vtc2_rows.append([v["id"], TC[c], TENANT])
    # default drivers
    if kind != 2 and not archived:
        if is_own:
            n = rnd.choices([1, 2, 0], [70, 22, 8])[0]
            ds = []
            for _ in range(n):
                d = next(own_drv_iter)
                if d["id"] not in ds:
                    ds.append(d["id"])
        else:
            pool = [d["id"] for d in drivers if d["employer"] is owner and not d["archived"]]
            ds = rnd.sample(pool, min(len(pool), rnd.randint(0, 2)))
        for j, did in enumerate(ds[:5]):
            vdd_rows.append([v["id"], did, TENANT, j + 1])
    # telematics ids
    if kind != 2 and is_own and rnd.random() < 0.85:
        src = "Transics" if kind in (1, 3) else "Fleetboard"
        if rnd.random() < 0.2:
            src = "Trimble"
        ext_seq[src] += rnd.randint(1, 9)
        vext_rows.append([new_id(), TENANT, v["id"], src, "%s-%d" % (src[:2].upper(), ext_seq[src])])
    if kind == 2 and is_own and rnd.random() < 0.5:
        ext_seq["Trimble"] += rnd.randint(1, 9)
        vext_rows.append([new_id(), TENANT, v["id"], "Schmitz TrailerConnect", "TC%07d" % ext_seq["Trimble"]])
    return v


for kind, count in [(1, 210), (2, 290), (3, 70), (4, 30)]:
    for _ in range(count):
        add_vehicle(own, kind, rnd.choice(own_types[kind]), True)
carrier_types = {1: own_types[1], 2: own_types[2], 3: own_types[3], 4: own_types[4]}
for kind, count in [(1, 150), (2, 200), (3, 40), (4, 10)]:
    for _ in range(count):
        owner = rnd.choice(fleet_carriers)
        add_vehicle(owner, kind, rnd.choice(carrier_types[kind]), False)

copy("Vehicles", ["Id", "Name", "VehicleTypeId", "LicencePlate", "RegistrationCountryId", "Vin", "OwnerRelationId", "HomeDepotRelationId", "LengthCm",
                  "WidthCm", "HeightCm", "GrossVehicleWeightKg", "EmptyWeightKg", "MaxEpq", "MaxLoadingMeters", "MaxPayloadKg", "FuelType", "EuroClass",
                  "IsArchived", "Description", "NormalizedName"] + AUDIT, veh_rows)
copy("VehicleTransportCharacteristics", ["VehicleId", "TransportCharacteristicsId", "TenantId"], vtc2_rows)
copy("VehicleDefaultDrivers", ["VehicleId", "DriverId", "TenantId", "SortOrder"], vdd_rows)
copy("VehicleExternalIdentifiers", ["Id", "TenantId", "VehicleId", "Source", "Identifier"], vext_rows)

# ───────────────────────────── vehicle combinations ─────────────────────────────
combo_rows, member_rows = [], []
active_taken = set()
used_combo_names = set()


def add_combo(members, archived=False):
    name = " + ".join(v["name"] for v in members)
    if len(name) > 64:
        name = name[:64].rstrip()
    if name in used_combo_names:
        return False
    used_combo_names.add(name)
    cid = new_id()
    desc = None
    if any(v["typename"] == "Aanhangwagen tandem 7,2 m" for v in members[1:]) and len(members) == 3:
        desc = "LZV-combinatie"
    combo_rows.append([cid, name, archived, desc] + audit(created(2 * 365, 1)))
    for pos, v in enumerate(members, start=1):
        member_rows.append([cid, v["id"], TENANT, pos, not archived])
        if not archived:
            assert v["id"] not in active_taken
            active_taken.add(v["id"])
    return True


def free(vs):
    return [v for v in vs if not v["archived"] and v["id"] not in active_taken]


def group(owner_is_own, kind, pred=lambda v: True, owner=None):
    return [v for v in vehicles if v["is_own"] == owner_is_own and v["kind"] == kind and pred(v) and (owner is None or v["owner"] is owner)]


semi = lambda v: v["typename"] != "Aanhangwagen tandem 7,2 m"
drawbar = lambda v: v["typename"] == "Aanhangwagen tandem 7,2 m"
# own fleet: tractor + semi-trailer
own_tractors = free(group(True, 1))
own_semis = free(group(True, 2, semi))
rnd.shuffle(own_tractors)
rnd.shuffle(own_semis)
for t in own_tractors[:175]:
    s = free(own_semis)
    if not s:
        break
    add_combo([t, s[0]])
# LZV: tractor + semi-trailer + drawbar trailer
own_drawbars = free(group(True, 2, drawbar))
for t in free(own_tractors)[:8]:
    s, d = free(own_semis), free(own_drawbars)
    if not s or not d:
        break
    add_combo([t, s[0], d[0]])
# rigid (18 t / motorwagen) + drawbar trailer
for r_ in free(group(True, 3, lambda v: v["typename"] in ("Bakwagen 18 t", "Motorwagen 10 m (26 t)")))[:20]:
    d = free(own_drawbars)
    if not d:
        break
    add_combo([r_, d[0]])
# carriers
for c in fleet_carriers:
    ts = free(group(False, 1, owner=c))
    ss = free(group(False, 2, semi, owner=c))
    for t, s in zip(ts, ss):
        if len(combo_rows) >= 290:
            break
        add_combo([t, s])
# archived history combinations (members inactive; vehicles may be active elsewhere)
hist_t = [v for v in vehicles if v["kind"] == 1 and v["is_own"]]
hist_s = [v for v in vehicles if v["kind"] == 2 and v["is_own"] and semi(v)]
k = 0
while k < 15:
    if add_combo([rnd.choice(hist_t), rnd.choice(hist_s)], archived=True):
        k += 1
copy("VehicleCombinations", ["Id", "Name", "IsArchived", "Description"] + AUDIT, combo_rows)
copy("VehicleCombinationMembers", ["VehicleCombinationId", "VehicleId", "TenantId", "Position", "IsActive"], member_rows)

# ───────────────────────────── product relation codes ─────────────────────────────
used_prc = set()
prc_rows = []
active_customers = [r for r in customers if not r["archived"]]
prc_customers = rnd.sample(active_customers, 2500)
active_products = [p for p in products if not p["archived"]]
tu_ids = [TU[n]["id"] for n in ["Europallet (EPAL)", "Blokpallet", "Wegwerppallet", "Halve pallet (Düsseldorfer)", "Rolcontainer", "CC-container (Deense kar)",
                               "Gitterbox", "Colli (doos)", "IBC (1000 l)", "Euro-krat E2", "Bierfust 50 l", "Bloemenfust (emmer 534)", "CHEP-pallet (blauw)"]]


def prc_code():
    f = rnd.random()
    if f < 0.25:
        return "ART-%05d" % rnd.randint(1, 99999)
    if f < 0.45:
        return ean13("87" + "%010d" % rnd.randint(0, 9999999999))
    if f < 0.55:
        return ean13(rnd.choice(["40", "54", "50", "30"]) + "%010d" % rnd.randint(0, 9999999999))
    if f < 0.70:
        return "SKU-%s%04d" % ("".join(rnd.choice("ABCDEFGHJKLMNPRSTUVWXYZ") for _ in range(2)), rnd.randint(0, 9999))
    if f < 0.80:
        return "%s-%d" % (rnd.choice(["KL", "DEB", "CUST", "AFN"]) + str(rnd.randint(10, 999)), rnd.randint(1000, 999999))
    if f < 0.88:
        return "%d.%03d.%02d" % (rnd.randint(1, 99), rnd.randint(0, 999), rnd.randint(0, 99))
    if f < 0.94:
        return "plu %d" % rnd.randint(3000, 4999)  # lower case on purpose: normalised to upper
    return "%s%d" % (rnd.choice(["Pal", "Rc", "Fust", "Krat", "Doos"]), rnd.randint(1, 99999))


while len(prc_rows) < 50000:
    code = prc_code()
    n = code.strip().upper()
    if n in used_prc:
        continue
    used_prc.add(n)
    t = rnd.random()
    rel = prod = tu = None
    if t < 0.40:
        prod = rnd.choice(active_products)["id"]
    elif t < 0.70:
        rel, prod = rnd.choice(prc_customers)["id"], rnd.choice(active_products)["id"]
    elif t < 0.85:
        rel = rnd.choice(prc_customers)["id"]
    elif t < 0.95:
        tu, prod = rnd.choice(tu_ids), rnd.choice(active_products)["id"]
    else:
        rel, prod, tu = rnd.choice(prc_customers)["id"], rnd.choice(active_products)["id"], rnd.choice(tu_ids)
    archived = rnd.random() < 0.02
    desc = None if rnd.random() < 0.85 else rnd.choice(["Klantcode uit EDI-orders", "Oude artikelcode", "Code op pakbon", "Afleveradres-code DC"])
    prc_rows.append([new_id(), code, n, rel, prod, tu, archived, desc] + audit(created(2 * 365, 0)))
copy("ProductRelationCodes", ["Id", "Code", "NormalizedCode", "RelationId", "ProductId", "TransportUnitId", "IsArchived", "Description"] + AUDIT, prc_rows)

# ───────────────────────────── write ─────────────────────────────
header = "-- Generated by gen_masterdata.py (seed %d) on %s for tenant %s(%s), schema masterdata only.\n" % (
    SEED, NOW.isoformat(), "`%s` " % " ".join(args.tenant_name.split()) if args.tenant_name else "", TENANT)
header += """-- Run with: psql -v ON_ERROR_STOP=1 -1 -f seed_masterdata.sql   (single transaction: on any error nothing stays)
SET client_encoding = 'UTF8';
DO $$
BEGIN
"""
if args.database:
    header += "  IF current_database() <> '%s' THEN RAISE EXCEPTION 'wrong database: %%', current_database(); END IF;\n" % args.database.replace("'", "''")
header += """  IF EXISTS (SELECT 1 FROM masterdata."Relations" WHERE ("Roles" & 16) = 16 AND NOT "IsDeleted"%s) THEN
    RAISE EXCEPTION 'an own company already exists; this seed brings its own';
  END IF;
END $$;
""" % ((' AND "TenantId" = \'%s\'' % TENANT) if args.shared_database else "")

if args.out:
    with open(args.out, "w", encoding="utf-8") as fh:
        fh.write(header)
        fh.writelines(out)
else:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stdout.write(header)
    sys.stdout.writelines(out)

# Summary on stderr, so stdout stays a clean SQL script.
print("relations", len(relations), "customers", sum(1 for r in relations if r["roles"] & 1), "carriers", len(carriers), "creditors(role)",
      sum(1 for r in relations if r["roles"] & 4), "depots", len(depots), file=sys.stderr)
print("addresses", len(addr_rows), "contacts", len(contact_rows), "banks", len(bank_rows), "catm", len(catm_rows), "regm", len(regm_rows),
      "ext", len(ext_rows), file=sys.stderr)
print("products", len(products), "drivers", len(drivers), "quals", len(q_rows), "vehicles", len(vehicles), "combos", len(combo_rows),
      "prc", len(prc_rows), file=sys.stderr)
