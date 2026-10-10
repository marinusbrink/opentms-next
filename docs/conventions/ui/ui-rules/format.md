# Formatting (UI-FMT)

**Load for:** showing or entering dates, times, numbers, amounts, percentages, units, durations.

**Library:** `lib/format/index.ts` exports `useFormat()`. It is bound to the user's culture (`useL().culture`) and timezone.

```ts
const f = useFormat();
f.date(value)          // 09-10-2026      (nl)  · 09/10/2026 (en)
f.dateTime(value)      // 09-10-2026 14:30
f.time(value)          // 14:30
f.timestamp(value)     // 2026-10-09 14:30:15   (culture-independent)
f.dateMedium(value)    // 9 okt. 2026     (read-only summaries only)
f.number(n, { decimals?: number; maxDecimals?: number })  // 1.234,5
f.amount(n)            // 1.234,50  (always 2 decimals, no symbol)
f.unitPrice(n)         // 2 to 5 decimals: 0,12345
f.percent(n)           // 12,5 %    (n = 12.5, not 0.125)
f.duration(minutes)    // 1d 2u 5m  (unit letters localized)
```

Grid cells (`DateCell`, `NumberCell`, `AmountCell` …) call the same functions.

---

### UI-FMT-001 · MUST · All formatted output goes through `useFormat()`
Feature code never calls these directly:
- `toLocaleString`, `toLocaleDateString`
- `Intl.*`
- `toFixed`
- `date-fns` / `dayjs` `format`

It also never hard-codes a locale (`"nl-NL"`) or `undefined` as the locale.
- **Check:** grep the diff for `toLocale`, `Intl.`, `toFixed(`, `format(` from date libraries, and `"nl-NL"` / `"en-US"` outside `lib/format`.
- **Evidence:** inventory §8.3 (4 competing number approaches, hard-coded nl-NL, browser locale), §13 (`Intl.DateTimeFormat(undefined, …)` in the new app).

### UI-FMT-002 · MUST · Culture mapping and the 24-hour clock
`lib/format` maps the UI language to a formatting locale:

| UI language | Formatting locale |
|---|---|
| nl | nl-NL |
| **en** | **en-GB** |
| de | de-DE |
| fr | fr-FR |
| es | es-ES |
| it | it-IT |
| pt | pt-PT |
| ru | ru-RU |

The week starts on Monday. Times are always 24-hour (`hourCycle: "h23"`).
- **Check:** the unit test in `lib/format` asserts en → `09/10/2026 14:30` (not `10/9/2026, 2:30 PM`).
- **Evidence:** inventory §8.1 (en was intended as en-GB, `AppConsts.localeMappings`; 24h `HH:mm` everywhere except the buggy en picker); D-23.

### UI-FMT-003 · MUST · Three date formats only: date, date-time, technical timestamp

| Format | Use | Example (nl) |
|---|---|---|
| `f.date` | dates in grids, forms, documents | `09-10-2026` |
| `f.dateTime` | date-times in grids and forms (ETA, ATA, loading windows) | `09-10-2026 14:30` |
| `f.timestamp` | logs, audit trails, API messages, technical events | `2026-10-09 14:30:15` |

`f.dateMedium` ("9 okt. 2026") is allowed only in read-only summary headers and the planboard (bespoke). Never in grid columns or inputs.
- **Check:** grid date columns use `DateCell` / `DateTimeCell`; log screens use `f.timestamp`.
- **Evidence:** inventory §8.2 (`dd-MM-yyyy HH:mm` in operational cells and pickers; `YYYY-MM-DD HH:mm:ss` for logs, deliberate; `ll` in grid columns); D-23.

### UI-FMT-004 · MUST · Date and time inputs use the same format as display
Date inputs show and accept `f.date` format; date-time inputs `f.dateTime`; time inputs `HH:mm`. Typing is allowed and validated on blur; the calendar is a convenience. Date-only values are sent as `YYYY-MM-DD` without a time or timezone.
- **Check:** no `<input type="date">` / `"time"`; date-only DTO fields are strings, not shifted `Date` objects.
- **Evidence:** inventory §8.2 (owl pickers `DD-MM-YYYY HH:mm`, 93 uses; `owlDisableTimeZone` for date-only values); component-map GAP-03.

### UI-FMT-005 · MUST · Timezone conversion happens only inside `lib/format`
All conversion from API UTC to displayed time happens in `useFormat()`. Today that means the browser timezone; D-24 decides whether the user's timezone setting replaces it. Components never call `.getHours()`, `.toLocaleTimeString()` or shift dates by hand.
- **Check:** no timezone arithmetic outside `lib/format`.
- **Evidence:** inventory §8.1 (browser-local display; a user timezone setting exists but is unused); D-24 (open).

### UI-FMT-006 · MUST · Numbers have culture separators and fixed decimals per kind

| Kind | Formatter | Decimals |
|---|---|---|
| amount | `f.amount` | 2 |
| unit price | `f.unitPrice` | 2 to 5 |
| quantity, weight, volume, distance | `f.number` | as specified in the design; default max 2, trailing zeros dropped |
| percentage | `f.percent` | max 2 |
| count | `f.number` | 0 |

Thousands grouping is on, except for identifiers (order numbers, postcodes, years), which are plain text.
- **Check:** numeric fields declare their kind in the design; grid columns use the matching cell.
- **Evidence:** inventory §8.3 (`number:'1.2-2'` for amounts; `PRICE_MIN/MAX_DECIMALS = 2/5`; `formatPrice` with grouping); D-11.

### UI-FMT-007 · MUST · No currency symbol per line; the currency code appears once
- Amounts in lines, grids and fields show no symbol.
- A document or list that holds amounts shows its currency code (ISO 4217, e.g. `EUR`) once: in the totals block, or in the column header (`Amount (EUR)`) when all rows share one currency.
- A multi-currency list has a separate Currency column.
- Never hard-code `€`.
- **Check:** grep for `€` and `currency:` styles in `Intl` options.
- **Evidence:** inventory §8.4 (no symbol per line, by design in `price-format.ts`; `currency:' '`; `€` only in bespoke LOS / map); D-25.

### UI-FMT-008 · MUST · Units go in the label or header, never in the value
- The unit appears once, in brackets after the field label or column header: `Weight (kg)`, `Distance (km)`, `Volume (m³)`, `Loading metres (ldm)`.
- Values and inputs contain only the number.
- Unit symbols come from localization keys (`t("Unit:Kg")`).
- **Check:** no unit text in cell renderers or input values; numeric labels whose design names a unit include it.
- **Evidence:** inventory §8.5 (units absent from freight inputs = gap; "(%)" in a label is the one existing pattern); D-25.

### UI-FMT-009 · SHOULD · Durations are compact with localized unit letters
Durations display as `f.duration` (`1d 2u 5m` in nl, `1d 2h 5m` in en). Zero parts are omitted; under one minute shows `0m`.
- **Check:** no hand-built "hours" / "minutes" strings.
- **Evidence:** inventory §8.5 (`formatRouteDuration` with localized HourUnit / MinuteUnit, deliberate; one hard-coded English variant = legacy).
