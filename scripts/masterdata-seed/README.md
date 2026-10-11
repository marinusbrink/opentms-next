# Master Data seed generator

Produces the three embedded seed files of the Master Data module
(`backend/modules/master-data/OpenTms.MasterData.Domain.Shared/Seed/`), per
`docs/designs/41-master-data-first-run.md`, section "Seed":

| File | Content | Rows |
|---|---|---|
| `iso-4217-currencies.json` | every ISO 4217 list-one currency (funds and X codes included), numeric code, minor units, ICU narrow symbol, nl/en names | ~178 |
| `iso-3166-countries.json` | the 249 ISO 3166-1 territories, alpha-3, numeric, nl/en names, current legal tender, primary IANA time zone | 249 |
| `address-types.json` | `Visiting` and `Postal`, nl/en names (hand-written in the script) | 2 |

## Run

```bash
node scripts/masterdata-seed/generate.mjs
```

Node >= 20 with full ICU (the default build); no npm dependencies. The script downloads its sources
on every run into `SEED_SOURCES_DIR` (default: `<os tmpdir>/opentms-masterdata-seed-sources`,
never inside the repository), rebuilds the three files, validates them (counts, every country's
currency exists, every time zone id is known to ICU, nl and en present, no duplicates) and exits
non-zero without writing when a check fails. The output is deterministic for a given set of sources
and ICU version; commit the regenerated JSON and review the diff.

## Sources

- ISO 4217 list one (XML, SIX for the ISO 4217 Maintenance Agency): codes, numeric codes, minor units, English names.
- ISO 3166-1 (JSON, Debian iso-codes): alpha-2, alpha-3, numeric, English names.
- CLDR supplemental `currencyData.json` (Unicode cldr-json): current legal tender per territory.
- tzdata `zone.tab` (eggert/tz, upstream of the IANA time zone database): zones per country.
- ICU through Node's `Intl.DisplayNames` (nl/en region and currency names) and `Intl.NumberFormat` (narrow symbols).

The exact URLs, the derivation of every field and the manual override tables (ambiguous currencies,
primary time zones for multi-zone countries, territories without a time zone) are in the header and
the constants at the top of `generate.mjs`. The run summary lists every override and fallback that
changed a source-derived value.
