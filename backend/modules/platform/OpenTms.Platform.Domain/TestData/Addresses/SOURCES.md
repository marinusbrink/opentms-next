# Sources of `addresses.csv.gz`

Real, existing addresses from official open-data registers, used as test data: the test datasets pair
them with fictitious company names. Built by `scripts/testdata/build-address-extract.py`
(re-run it to refresh; the source URLs and the sampling method are documented at the top of that script).
Extraction date: **2026-10-30**. No OpenStreetMap (ODbL) data is used.

Format: gzip'd UTF-8 CSV, RFC 4180 quoting, header
`country,street,house_number,house_number_suffix,postal_code,city,latitude,longitude`; coordinates are
WGS84 (ETRS89) decimal degrees with 6 decimals. Addresses are unique per country on
(postal code, street, house number, suffix).

| Country | Rows |
|---|---|
| NL | 200,000 |
| BE | 60,000 |
| DE | 60,000 |
| **Total** | 320,000 |

## Netherlands (NL) — 200,000 rows

- **Source:** Basisregistratie Adressen en Gebouwen (BAG), collection `verblijfsobject`
- **Publisher:** Kadaster, served by PDOK
- **URL:** <https://api.pdok.nl/kadaster/bag/ogc/v2/collections/verblijfsobject/items> (OGC API Features)
- **Licence:** CC0 1.0 (the API declares Public Domain Mark 1.0). No attribution required; suggested
  credit: "Bron: Kadaster, BAG (via PDOK)".
- **Selection:** only objects with status *Verblijfsobject in gebruik* and main address status
  *Naamgeving uitgegeven*. Sampled with bbox queries over a randomly shuffled grid covering the whole
  country, capped per grid cell so cities do not dominate. 80,000 (40 %) addresses come from a pass filtered on
  `gebruiksdoel` = industriefunctie, kantoorfunctie or winkelfunctie (non-residential); the rest from a
  pass over all uses (mostly residential). Postal codes are written `1234 AB`; `house_number_suffix` is the
  BAG huisletter, followed by a space and the huisnummertoevoeging when present.

## Belgium (BE) — 60,000 rows

- **Source:** BeST Address (Belgian Streets and Addresses), "openaddress" CSV per region
- **Publisher:** FOD BOSA (Federal Public Service Policy and Support), DG Digital Transformation, compiled
  from the regional registers: Digitaal Vlaanderen (Adressenregister), Service public de Wallonie (ICAR)
  and paradigm.brussels (UrbIS)
- **URLs:** <https://opendata.bosa.be/download/best/openaddress-bevlg.zip>,
  <https://opendata.bosa.be/download/best/openaddress-bewal.zip>,
  <https://opendata.bosa.be/download/best/openaddress-bebru.zip> (files dated 2026-10-28)
- **Licence:** Creative Commons Attribution 4.0 International (CC BY 4.0),
  <https://creativecommons.org/licenses/by/4.0/>. Required attribution:
  "Contains BeST Address data, © FOD BOSA / Digitaal Vlaanderen, SPW, paradigm.brussels, CC BY 4.0
  (modified: sampled and reformatted)".
- **Selection:** status `current` only; box numbers (units) are dropped and collapsed onto one building
  address. Random sample per region: Flanders 32,000, Wallonia 20,000, Brussels 8,000. Street and
  city in the region's language (Flanders Dutch; Wallonia French, German in the German-speaking
  municipalities; Brussels French or Dutch, chosen per address). Coordinates computed from the Lambert 2008
  (EPSG:3812) columns.

## Germany (DE) — 60,000 rows

Only states that publish house-level addresses *with postal codes* under a permissive licence, and that can
be downloaded without an order process, are included.

### North Rhine-Westphalia — 35,000 rows

- **Source:** Gebäudereferenzen NRW (GEBREF, ASCII, EPSG:25832) for street, number, municipality and
  coordinates; the postal code from the INSPIRE Addresses WFS that is built on the same GEBREF data
  (joined on the address id)
- **Publisher:** Bezirksregierung Köln, Geobasis NRW
- **URLs:** <https://www.opengeodata.nrw.de/produkte/geobasis/lk/akt/gebref_txt/gebref_EPSG25832_ASCII.zip>
  (stand 2026-07-01), <https://www.wfs.nrw.de/geobasis/wfs_nw_inspire-adressen_gebref>
- **Licence:** Datenlizenz Deutschland – Zero – Version 2.0 (dl-de/zero-2-0),
  <https://www.govdata.de/dl-de/zero-2-0>. No attribution required; suggested credit:
  "Geobasis NRW, dl-de/zero-2-0".
- **Selection:** 900 random anchor addresses across the state, up to 45 addresses from a 500 m window
  around each.

### Berlin — 15,000 rows

- **Source:** Adressen Regionales Bezugssystem (RBS) Berlin
- **Publisher:** Amt für Statistik Berlin-Brandenburg; served by Senatsverwaltung für Stadtentwicklung,
  Bauen und Wohnen Berlin
- **URL:** <https://gdi.berlin.de/services/wfs/adressen_rbs>
- **Licence:** Datenlizenz Deutschland – Zero – Version 2.0 (dl-de/zero-2-0). No attribution required;
  suggested credit: "Amt für Statistik Berlin-Brandenburg, dl-de/zero-2-0".
- **Selection:** street addresses only (`str_typ` = STRASSE), random sample from the full register.

### Hamburg — 10,000 rows

- **Source:** INSPIRE Adressen Hamburg (ALKIS-based), WFS
- **Publisher:** Freie und Hansestadt Hamburg, Landesbetrieb Geoinformation und Vermessung (LGV)
- **URL:** <https://geodienste.hamburg.de/HH_WFS_INSPIRE_Adressen>
- **Licence:** Datenlizenz Deutschland – Namensnennung – Version 2.0 (dl-de/by-2-0),
  <https://www.govdata.de/dl-de/by-2-0>. **Required attribution:** "Freie und Hansestadt Hamburg,
  Landesbetrieb Geoinformation und Vermessung (LGV), dl-de/by-2-0 (Daten geändert)".
- **Selection:** status `current`; the service rejects bbox filters, so the sample is 70 random pages of
  200 addresses (the service orders by district, so each page is one neighbourhood).

### Not included

- **Brandenburg** (georeferenzierte Adressen, dl-de/by-2-0): only obtainable through an order in the LGB
  Geobroker, not as a scripted download.
- **Saxony, Thuringia** and the other states: not evaluated in this run; three states were enough for
  the target. Note that the common open "Hauskoordinaten" products carry no postal code, and open
  postal-code areas to join them with are hard to find (Hamburg's postcode WFS is restricted; nationwide
  PLZ areas are either licensed or OpenStreetMap-derived).
