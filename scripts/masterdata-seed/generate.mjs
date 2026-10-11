#!/usr/bin/env node
/**
 * Master Data seed generator (design doc docs/designs/41-master-data-first-run.md, section "Seed").
 *
 * Run by hand:   node scripts/masterdata-seed/generate.mjs
 * Requires:      Node >= 20 with full ICU (the default build). No npm dependencies.
 *
 * Writes three JSON files into backend/modules/master-data/OpenTms.MasterData.Domain.Shared/Seed/
 * (2-space indentation, sorted by code):
 *
 *   iso-4217-currencies.json   { isoCode, isoNumeric, minorUnits, symbol, names: { nl, en } }
 *   iso-3166-countries.json    { isoAlpha2, isoAlpha3, isoNumeric, names: { nl, en }, defaultCurrency, primaryTimeZoneId }
 *   address-types.json         { kind, names: { nl, en } }   (hand-written below, no source)
 *
 * Sources (downloaded on every run into SEED_SOURCES_DIR, default <os tmpdir>/opentms-masterdata-seed-sources;
 * never into the repository):
 *
 *   [1] ISO 4217 list one (XML), published by SIX on behalf of the ISO 4217 Maintenance Agency
 *       https://www.six-group.com/dam/download/financial-information/data-center/iso-currrency/lists/list-one.xml
 *   [2] ISO 3166-1 (JSON) from the Debian iso-codes project
 *       https://salsa.debian.org/iso-codes-team/iso-codes/-/raw/main/data/iso_3166-1.json
 *   [3] CLDR supplemental currencyData (JSON), Unicode cldr-json
 *       https://raw.githubusercontent.com/unicode-org/cldr-json/main/cldr-json/cldr-core/supplemental/currencyData.json
 *   [4] tzdata zone.tab, Paul Eggert's tz repository (the upstream of the IANA time zone database)
 *       https://raw.githubusercontent.com/eggert/tz/main/zone.tab
 *   [5] ICU, through the Node runtime's Intl.DisplayNames and Intl.NumberFormat
 *
 * Field derivation
 *
 *   Currencies: every <CcyNtry> of [1] that has a <Ccy> element (the "No universal currency" rows for
 *   Antarctica, Palestine and South Georgia have none and are skipped). The list repeats a currency per
 *   country; entries are de-duplicated by code and the script fails when two rows of the same code
 *   disagree on numeric code, minor units or name. Fund codes (IsFund="true") and the X codes (XAU, XDR,
 *   XTS, XXX, ...) are included.
 *     isoCode     <Ccy>
 *     isoNumeric  <CcyNbr> parsed as an integer, null when absent
 *     minorUnits  <CcyMnrUnts> parsed as an integer, null when the list says "N.A."
 *     symbol      [5] Intl.NumberFormat('en', { style: 'currency', currencyDisplay: 'narrowSymbol' })
 *                 formatToParts, the "currency" part, when it differs from the code; otherwise null.
 *                 CURRENCY_SYMBOL_OVERRIDES wins over ICU.
 *     names.nl/en [5] Intl.DisplayNames([lang], { type: 'currency' }). When ICU returns the code itself
 *                 for either language, the English <CcyNm> of [1] is used for both languages.
 *
 *   Countries: every entry of [2] (the 249 officially assigned ISO 3166-1 codes).
 *     isoAlpha2, isoAlpha3, isoNumeric   alpha_2, alpha_3, numeric (integer) from [2]
 *     names.nl/en  [5] Intl.DisplayNames([lang], { type: 'region' }); when ICU returns the code itself
 *                  the English "name" of [2] is used for both languages.
 *     defaultCurrency  [3] supplemental.currencyData.region[alpha2]: the entries that have _from and no
 *                  _to and whose _tender is not "false"; when there are several, the last one. Then
 *                  DEFAULT_CURRENCY_OVERRIDES is applied. The result must be a code of the currencies
 *                  file or null.
 *     primaryTimeZoneId  [4] the first zone listed for the country code in zone.tab, replaced by
 *                  PRIMARY_TIME_ZONE_OVERRIDES where set. Null for the territories in
 *                  TIME_ZONES_NOT_COVERED (not present in zone.tab). Every id is verified with
 *                  new Intl.DateTimeFormat('en', { timeZone: id }), which throws for unknown ids.
 *
 * Manual override tables are the constants directly below this comment. Every override is applied by
 * the script, so re-running it reproduces the committed JSON; the summary at the end prints which
 * overrides changed a source-derived value.
 */

import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

// ---------------------------------------------------------------------------------------------------
// Manual override tables
// ---------------------------------------------------------------------------------------------------

/** ICU narrow symbols that are not usable as a currency symbol. null means "no symbol". */
const CURRENCY_SYMBOL_OVERRIDES = {
  // ICU renders the "no currency" code with the generic currency sign (U+00A4), which is not a symbol
  // of XXX but a placeholder glyph.
  XXX: null,
};

/**
 * Default currency where CLDR [3] lists several current legal tenders, or none, for a territory.
 * Rule applied by the script: the last current entry of CLDR wins; this table corrects that where the
 * last entry is not the territory's own currency.
 */
const DEFAULT_CURRENCY_OVERRIDES = {
  AQ: null, // Antarctica: no universal currency (ISO 4217 says so explicitly)
  BT: 'BTN', // Bhutan: ngultrum is the national currency; CLDR also lists INR as tender
  HT: 'HTG', // Haiti: gourde; CLDR also lists USD as tender
  LS: 'LSL', // Lesotho: loti; CLDR also lists ZAR as tender
  NA: 'NAD', // Namibia: Namibian dollar; CLDR also lists ZAR as tender
  PA: 'PAB', // Panama: balboa; CLDR also lists USD as tender
  PS: 'ILS', // Palestine: no own currency (ISO 4217 "No universal currency"); CLDR lists ILS and JOD, ILS is the circulating one
  ZW: 'ZWG', // Zimbabwe: ZiG (2024) is the current ISO 4217 code; CLDR also lists USD as tender
};

/**
 * Primary IANA time zone per country where zone.tab's first zone is not the capital's or the
 * country's principal zone, plus an explicit setting for every country with more than one zone and
 * for the countries the design names. Values must be zone.tab zones of that country.
 */
const PRIMARY_TIME_ZONE_OVERRIDES = {
  AQ: 'Antarctica/McMurdo',
  AR: 'America/Argentina/Buenos_Aires',
  AU: 'Australia/Sydney', // zone.tab lists Australia/Lord_Howe first
  BR: 'America/Sao_Paulo', // zone.tab lists America/Noronha first
  CA: 'America/Toronto', // zone.tab lists America/St_Johns first
  CD: 'Africa/Kinshasa',
  CL: 'America/Santiago',
  CN: 'Asia/Shanghai',
  CY: 'Asia/Nicosia',
  DE: 'Europe/Berlin',
  DK: 'Europe/Copenhagen',
  EC: 'America/Guayaquil',
  ES: 'Europe/Madrid',
  FM: 'Pacific/Chuuk',
  FR: 'Europe/Paris',
  GB: 'Europe/London',
  GL: 'America/Nuuk',
  ID: 'Asia/Jakarta',
  KI: 'Pacific/Tarawa',
  KZ: 'Asia/Almaty',
  MH: 'Pacific/Majuro',
  MN: 'Asia/Ulaanbaatar',
  MX: 'America/Mexico_City',
  MY: 'Asia/Kuala_Lumpur',
  NL: 'Europe/Amsterdam',
  NO: 'Europe/Oslo',
  NZ: 'Pacific/Auckland',
  PF: 'Pacific/Tahiti',
  PG: 'Pacific/Port_Moresby',
  PS: 'Asia/Gaza',
  PT: 'Europe/Lisbon',
  RU: 'Europe/Moscow', // zone.tab lists Europe/Kaliningrad first
  UA: 'Europe/Kyiv', // zone.tab lists Europe/Simferopol first
  // US Minor Outlying Islands: zone.tab lists Pacific/Midway first, then Pacific/Wake. Neither is a
  // capital; Wake Island carries the larger permanent presence, so Wake is taken. Doubtful; revisit
  // if a tenant ever needs it.
  UM: 'Pacific/Wake',
  US: 'America/New_York',
  UZ: 'Asia/Tashkent', // zone.tab lists Asia/Samarkand first
  VN: 'Asia/Ho_Chi_Minh',
};

/** ISO 3166-1 territories that tzdata does not cover (uninhabited): primaryTimeZoneId is null. */
const TIME_ZONES_NOT_COVERED = ['BV', 'HM'];

/** The two seeded address types (design doc, "Seed"). Hand-written; no external source. */
const ADDRESS_TYPES = [
  { kind: 'Visiting', names: { nl: 'Bezoekadres', en: 'Visiting address' } },
  { kind: 'Postal', names: { nl: 'Postadres', en: 'Postal address' } },
];

// ---------------------------------------------------------------------------------------------------
// Sources
// ---------------------------------------------------------------------------------------------------

const SOURCES = {
  listOne: {
    url: 'https://www.six-group.com/dam/download/financial-information/data-center/iso-currrency/lists/list-one.xml',
    file: 'iso-4217-list-one.xml',
  },
  iso3166: {
    url: 'https://salsa.debian.org/iso-codes-team/iso-codes/-/raw/main/data/iso_3166-1.json',
    file: 'iso_3166-1.json',
  },
  cldrCurrency: {
    url: 'https://raw.githubusercontent.com/unicode-org/cldr-json/main/cldr-json/cldr-core/supplemental/currencyData.json',
    file: 'cldr-currencyData.json',
  },
  zoneTab: {
    url: 'https://raw.githubusercontent.com/eggert/tz/main/zone.tab',
    file: 'zone.tab',
  },
};

const LANGUAGES = ['nl', 'en'];

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '..', '..');
const outputDir = path.join(
  repoRoot,
  'backend',
  'modules',
  'master-data',
  'OpenTms.MasterData.Domain.Shared',
  'Seed',
);
const sourcesDir = path.resolve(
  process.env.SEED_SOURCES_DIR ?? path.join(os.tmpdir(), 'opentms-masterdata-seed-sources'),
);

const violations = [];
const notes = [];

function violate(message) {
  violations.push(message);
}

async function download(source) {
  const target = path.join(sourcesDir, source.file);
  const response = await fetch(source.url);
  if (!response.ok) {
    throw new Error(`Download failed (${response.status} ${response.statusText}): ${source.url}`);
  }
  const text = await response.text();
  await fs.writeFile(target, text, 'utf8');
  console.log(`downloaded ${source.url} -> ${target} (${text.length} chars)`);
  return text;
}

// ---------------------------------------------------------------------------------------------------
// Currencies
// ---------------------------------------------------------------------------------------------------

function decodeXmlEntities(text) {
  return text
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'");
}

function xmlElement(block, tag) {
  const match = block.match(new RegExp(`<${tag}(?:\\s[^>]*)?>([^<]*)</${tag}>`));
  return match ? decodeXmlEntities(match[1].trim()) : null;
}

function parseListOne(xml) {
  const byCode = new Map();
  const blocks = [...xml.matchAll(/<CcyNtry>([\s\S]*?)<\/CcyNtry>/g)].map((m) => m[1]);
  if (blocks.length === 0) throw new Error('ISO 4217 list one: no <CcyNtry> elements found');
  for (const block of blocks) {
    const code = xmlElement(block, 'Ccy');
    if (!code) continue; // "No universal currency" rows
    const entry = {
      isoCode: code,
      isoNumeric: xmlElement(block, 'CcyNbr'),
      minorUnits: xmlElement(block, 'CcyMnrUnts'),
      englishName: xmlElement(block, 'CcyNm'),
    };
    const existing = byCode.get(code);
    if (existing) {
      for (const field of ['isoNumeric', 'minorUnits', 'englishName']) {
        if (existing[field] !== entry[field]) {
          violate(`ISO 4217 rows for ${code} disagree on ${field}: ${existing[field]} vs ${entry[field]}`);
        }
      }
    } else {
      byCode.set(code, entry);
    }
  }
  return byCode;
}

function currencySymbol(code) {
  if (Object.hasOwn(CURRENCY_SYMBOL_OVERRIDES, code)) return CURRENCY_SYMBOL_OVERRIDES[code];
  try {
    const part = new Intl.NumberFormat('en', {
      style: 'currency',
      currency: code,
      currencyDisplay: 'narrowSymbol',
    })
      .formatToParts(1)
      .find((p) => p.type === 'currency');
    if (!part || part.value === code) return null;
    return part.value;
  } catch {
    return null;
  }
}

function displayNames(type, code, fallbackEnglish, what) {
  const names = {};
  let fallback = false;
  for (const lang of LANGUAGES) {
    let name;
    try {
      name = new Intl.DisplayNames([lang], { type, fallback: 'code' }).of(code);
    } catch {
      name = undefined;
    }
    if (!name || name === code) {
      fallback = true;
      break;
    }
    names[lang] = name;
  }
  if (fallback) {
    if (!fallbackEnglish) {
      violate(`${what} ${code}: ICU has no name and the source gives no English name`);
      return { nl: '', en: '' };
    }
    notes.push(`${what} ${code}: ICU has no display name; English source name "${fallbackEnglish}" used for nl and en`);
    return { nl: fallbackEnglish, en: fallbackEnglish };
  }
  return names;
}

function buildCurrencies(listOneXml) {
  const parsed = parseListOne(listOneXml);
  const currencies = [...parsed.values()]
    .sort((a, b) => (a.isoCode < b.isoCode ? -1 : a.isoCode > b.isoCode ? 1 : 0))
    .map((entry) => {
      const isoNumeric = entry.isoNumeric && /^\d+$/.test(entry.isoNumeric) ? Number(entry.isoNumeric) : null;
      let minorUnits = null;
      if (entry.minorUnits && entry.minorUnits !== 'N.A.') {
        if (!/^[0-4]$/.test(entry.minorUnits)) {
          violate(`currency ${entry.isoCode}: unexpected minor units "${entry.minorUnits}"`);
        } else {
          minorUnits = Number(entry.minorUnits);
        }
      }
      const symbol = currencySymbol(entry.isoCode);
      if (symbol === entry.isoCode) violate(`currency ${entry.isoCode}: symbol equals the code`);
      return {
        isoCode: entry.isoCode,
        isoNumeric,
        minorUnits,
        symbol,
        names: displayNames('currency', entry.isoCode, entry.englishName, 'currency'),
      };
    });
  for (const [code, value] of Object.entries(CURRENCY_SYMBOL_OVERRIDES)) {
    if (!parsed.has(code)) violate(`CURRENCY_SYMBOL_OVERRIDES names unknown currency ${code}`);
    else notes.push(`currency ${code}: symbol override -> ${JSON.stringify(value)}`);
  }
  return currencies;
}

// ---------------------------------------------------------------------------------------------------
// Countries
// ---------------------------------------------------------------------------------------------------

function parseZoneTab(text) {
  const zonesByCountry = new Map();
  for (const line of text.split('\n')) {
    if (!line || line.startsWith('#')) continue;
    const [countryCode, , zone] = line.split('\t');
    if (!zonesByCountry.has(countryCode)) zonesByCountry.set(countryCode, []);
    zonesByCountry.get(countryCode).push(zone);
  }
  if (zonesByCountry.size === 0) throw new Error('zone.tab: no entries parsed');
  return zonesByCountry;
}

function cldrCurrentCurrency(regionData, alpha2) {
  const entries = regionData[alpha2];
  if (!entries) return { candidates: [], pick: null };
  const candidates = [];
  for (const entry of entries) {
    for (const [code, period] of Object.entries(entry)) {
      if (period._from && !period._to && period._tender !== 'false') candidates.push(code);
    }
  }
  return { candidates, pick: candidates.length > 0 ? candidates[candidates.length - 1] : null };
}

function timeZoneIsKnown(id) {
  try {
    new Intl.DateTimeFormat('en', { timeZone: id });
    return true;
  } catch {
    return false;
  }
}

function buildCountries(iso3166Json, cldrJson, zoneTabText, currencyCodes) {
  const entries = JSON.parse(iso3166Json)['3166-1'];
  const regionData = JSON.parse(cldrJson).supplemental.currencyData.region;
  const zonesByCountry = parseZoneTab(zoneTabText);
  const knownAlpha2 = new Set(entries.map((e) => e.alpha_2));

  for (const code of Object.keys(DEFAULT_CURRENCY_OVERRIDES)) {
    if (!knownAlpha2.has(code)) violate(`DEFAULT_CURRENCY_OVERRIDES names unknown country ${code}`);
  }
  for (const [code, zone] of Object.entries(PRIMARY_TIME_ZONE_OVERRIDES)) {
    if (!knownAlpha2.has(code)) violate(`PRIMARY_TIME_ZONE_OVERRIDES names unknown country ${code}`);
    else if (!(zonesByCountry.get(code) ?? []).includes(zone)) {
      violate(`PRIMARY_TIME_ZONE_OVERRIDES ${code}: ${zone} is not a zone.tab zone of ${code}`);
    }
  }
  for (const code of TIME_ZONES_NOT_COVERED) {
    if (!knownAlpha2.has(code)) violate(`TIME_ZONES_NOT_COVERED names unknown country ${code}`);
    else if (zonesByCountry.has(code)) violate(`TIME_ZONES_NOT_COVERED lists ${code}, but zone.tab covers it`);
  }

  return entries
    .sort((a, b) => (a.alpha_2 < b.alpha_2 ? -1 : a.alpha_2 > b.alpha_2 ? 1 : 0))
    .map((entry) => {
      const alpha2 = entry.alpha_2;

      // Default currency
      const { candidates, pick } = cldrCurrentCurrency(regionData, alpha2);
      let defaultCurrency = pick;
      if (Object.hasOwn(DEFAULT_CURRENCY_OVERRIDES, alpha2)) {
        defaultCurrency = DEFAULT_CURRENCY_OVERRIDES[alpha2];
        notes.push(
          `country ${alpha2}: defaultCurrency override -> ${JSON.stringify(defaultCurrency)} (CLDR current: ${candidates.join(', ') || 'none'})`,
        );
      } else if (candidates.length !== 1) {
        violate(`country ${alpha2}: CLDR lists ${candidates.length} current currencies (${candidates.join(', ')}) and no override is set`);
      }
      if (defaultCurrency !== null && !currencyCodes.has(defaultCurrency)) {
        violate(`country ${alpha2}: defaultCurrency ${defaultCurrency} is not in the currencies file`);
      }

      // Primary time zone
      const zones = zonesByCountry.get(alpha2) ?? [];
      let primaryTimeZoneId = zones[0] ?? null;
      if (Object.hasOwn(PRIMARY_TIME_ZONE_OVERRIDES, alpha2)) {
        const override = PRIMARY_TIME_ZONE_OVERRIDES[alpha2];
        if (override !== primaryTimeZoneId) {
          notes.push(`country ${alpha2}: primaryTimeZoneId override -> ${override} (zone.tab first: ${primaryTimeZoneId})`);
        }
        primaryTimeZoneId = override;
      } else if (zones.length > 1) {
        violate(`country ${alpha2}: zone.tab lists ${zones.length} zones and no override is set (first: ${zones[0]})`);
      }
      if (primaryTimeZoneId === null && !TIME_ZONES_NOT_COVERED.includes(alpha2)) {
        violate(`country ${alpha2}: not in zone.tab and not listed in TIME_ZONES_NOT_COVERED`);
      }
      if (primaryTimeZoneId !== null && !timeZoneIsKnown(primaryTimeZoneId)) {
        violate(`country ${alpha2}: time zone ${primaryTimeZoneId} is not known to Intl.DateTimeFormat`);
      }

      return {
        isoAlpha2: alpha2,
        isoAlpha3: entry.alpha_3,
        isoNumeric: Number(entry.numeric),
        names: displayNames('region', alpha2, entry.name, 'country'),
        defaultCurrency,
        primaryTimeZoneId,
      };
    });
}

// ---------------------------------------------------------------------------------------------------
// Validation and output
// ---------------------------------------------------------------------------------------------------

function checkNames(rows, keyField, what) {
  for (const row of rows) {
    for (const lang of LANGUAGES) {
      if (typeof row.names[lang] !== 'string' || row.names[lang].trim() === '') {
        violate(`${what} ${row[keyField]}: names.${lang} is empty`);
      }
    }
  }
}

function checkUnique(rows, keyField, what) {
  const seen = new Set();
  for (const row of rows) {
    if (seen.has(row[keyField])) violate(`${what}: duplicate ${keyField} ${row[keyField]}`);
    seen.add(row[keyField]);
  }
}

async function writeJson(fileName, data) {
  const target = path.join(outputDir, fileName);
  await fs.writeFile(target, `${JSON.stringify(data, null, 2)}\n`, 'utf8');
  console.log(`wrote ${target} (${data.length} rows)`);
}

async function main() {
  const relative = path.relative(repoRoot, sourcesDir);
  if (relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative))) {
    throw new Error(`SEED_SOURCES_DIR must lie outside the repository: ${sourcesDir}`);
  }
  await fs.mkdir(sourcesDir, { recursive: true });
  await fs.mkdir(outputDir, { recursive: true });

  const [listOneXml, iso3166Json, cldrJson, zoneTabText] = await Promise.all([
    download(SOURCES.listOne),
    download(SOURCES.iso3166),
    download(SOURCES.cldrCurrency),
    download(SOURCES.zoneTab),
  ]);

  const currencies = buildCurrencies(listOneXml);
  const currencyCodes = new Set(currencies.map((c) => c.isoCode));
  const countries = buildCountries(iso3166Json, cldrJson, zoneTabText, currencyCodes);
  const addressTypes = ADDRESS_TYPES;

  checkUnique(currencies, 'isoCode', 'currencies');
  checkUnique(countries, 'isoAlpha2', 'countries');
  checkUnique(countries, 'isoAlpha3', 'countries');
  checkUnique(countries, 'isoNumeric', 'countries');
  checkUnique(addressTypes, 'kind', 'address types');
  checkNames(currencies, 'isoCode', 'currency');
  checkNames(countries, 'isoAlpha2', 'country');
  checkNames(addressTypes, 'kind', 'address type');
  if (currencies.length < 170 || currencies.length > 185) {
    violate(`currency count ${currencies.length} is outside the expected 170-185`);
  }
  if (countries.length !== 249) violate(`country count ${countries.length}, expected 249`);

  console.log('\nSummary');
  console.log(`  currencies: ${currencies.length} unique codes`);
  console.log(`  countries:  ${countries.length}`);
  console.log(`  countries without currency: ${countries.filter((c) => c.defaultCurrency === null).map((c) => c.isoAlpha2).join(', ') || 'none'}`);
  console.log(`  countries without time zone: ${countries.filter((c) => c.primaryTimeZoneId === null).map((c) => c.isoAlpha2).join(', ') || 'none'}`);
  console.log(`  address types: ${addressTypes.length}`);
  console.log('  notes (overrides and fallbacks applied):');
  for (const note of notes) console.log(`    - ${note}`);

  if (violations.length > 0) {
    console.error(`\n${violations.length} violation(s); nothing written:`);
    for (const v of violations) console.error(`  - ${v}`);
    process.exit(1);
  }

  await writeJson('iso-4217-currencies.json', currencies);
  await writeJson('iso-3166-countries.json', countries);
  await writeJson('address-types.json', addressTypes);
  console.log('\nOK');
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
