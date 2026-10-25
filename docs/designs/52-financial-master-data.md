# PBI: Financial master data: VAT percentages, additional services, discounts and surcharges, invoice types

<!-- Written on the PO's instruction of 2026-10-22 ("bouw stamgegevens die een order en trip nodig hebben; hou het
     Engels: OutboundInvoiceType i.p.v. factuurtypes"). Assessment docs/input/masterdata-rebuild-assessment.md §2 row 2,
     §2.1 (Financial row, PO names), §2.2 (Financial seeds VAT percentages and a default outbound invoice type), §3.4,
     §3.5, §3.7, §3.8, §3.9, §3.10, §5 "Reference lists", "Financial configuration". -->

## Story

As an invoicing clerk and a planner I want the VAT categories with their percentages over time, the additional
services an order line can carry, the discounts and surcharges (diesel surcharge, weekend surcharge, regional
surcharges) with their values over time, and the outbound and inbound invoice types, because the revenue and cost of
an order and a trip are calculated and invoiced with them, and a percentage that changes must never change what was
already calculated (§3.4).

## Affected area

Financial module, schema `financial`, first entities of the module: `VatPercentage` (+ rates), `AdditionalService`,
`DiscountOrSurcharge` (+ values, regions), `OutboundInvoiceType`, `InboundInvoiceType`; services under
`/api/financial/...`; seed; five lists in the Master data app under a new domain group **Financial** with the groups
**Invoicing** and **Pricing**. Uses Master Data's `IMasterDataReferenceLookup` (design 51) for countries, currencies
and regions.

## Tenant dimension

Configurable per tenant. Features: `Apps.MasterData.Financial` (on), `….Financial.Invoicing` (on),
`….Financial.Pricing` (on); lists `….Invoicing.VatPercentages`, `….Invoicing.OutboundInvoiceTypes`,
`….Invoicing.InboundInvoiceTypes`, `….Pricing.AdditionalServices`, `….Pricing.DiscountsOrSurcharges` (off).

## Expected volumes

Per tenant: up to 50 VAT categories (≤ 50 rates each), 500 additional services, 500 discounts and surcharges (≤ 200
value rows each: a monthly diesel surcharge over 15 years; ≤ 100 regions each), 20 outbound and 50 inbound invoice
types.

## Known edge cases

- A VAT rate change announced in advance (a future row); a change to a past rate (refused: history is fixed).
- A diesel surcharge updated every month; a value row on the same date twice.
- A fixed surcharge without a currency; a percentage with a currency.
- No day of the week selected.
- Deleting a VAT category used by a service or surcharge; deleting the default VAT category or invoice type.
- A country, currency or region deleted or deactivated in Master Data later.

## Privacy note

None.

---

# Design: Financial master data

**PBI:** above (this file)
**Status:** approved (gate 1, PO instruction 2026-10-22; Assumptions are the open details)
**Date:** 2026-10-22
**Depends on:** design 50; design 51's `IMasterDataReferenceLookup`.

## Domain impact

**Module:** Financial, schema `financial`; migration `FinancialMasterData`. Submodule folders (Financial README):
VAT and invoice types under `Invoicing/`, additional services and discounts/surcharges under `Tariffs/`.
**Decisions relied on:** AD-02, AD-06, AD-08 (money lives in rate lines; these entities hold the *inputs* a rate line
is calculated from, never a calculated amount), AD-18. **§3.4 versioning rule:** value-bearing fields (VAT percentage,
discount/surcharge value) are validity rows: a change is a new row with `ValidFrom`; a row's validity ends where the
next row starts. Rows dated today or earlier are **fixed** (neither changed nor removed); only future rows can be
edited. A rate line later stores the amount and the row it used, so money history is safe twice over.
**§3.5:** money and percentages are `decimal` (`numeric`) — the assessment's recommendation, recorded here as the
deliberate choice for money columns; measures elsewhere stay `double`.

**Cross-module (rule 3):** `IMasterDataReferenceLookup.GetCountriesAsync(isoAlpha2[])`, `GetCurrenciesAsync(isoCodes[])`
(with `isActive`), `GetGeographicalRegionsAsync(ids)`. Countries and currencies are referenced by **ISO code**, not id
(§3.7: integrations match on ISO code; the seed needs no Master Data row to exist first).

### VatPercentage (a tax category with validity rows, §5)

| Field | Type | Rule |
|---|---|---|
| `Code` | string(16) | unique per tenant |
| `Name` | `LocalizedName` | |
| `Kind` | enum `VatKind { Standard = 1, Reduced, Zero, Exempt, ReverseCharge, IntraCommunity, Export }` | accounting connectors map by kind, never by comparing percentages (§5) |
| `CountryCode` | string(2), ISO alpha-2 | must exist in Master Data |
| `SalesLedgerAccount`, `PurchaseLedgerAccount` | string(32)? | the ledger accounts per category (§5) |
| `IsDefault` | bool | one per tenant (§3.8) |
| `IsArchived` | bool | |
| child **VatPercentageRate** | `ValidFrom` DateOnly, `Percentage` decimal(5,2) 0–100 | at least one; unique per category and date; ≤ 50; past rows fixed |

`RateOn(DateOnly)` → the row valid on that date (null before the first). The row DTO shows the percentage valid today.

### AdditionalService

| Field | Type | Rule |
|---|---|---|
| `Code` | string(16) | unique |
| `Name` | string(128) | |
| `Unit` | enum `ServiceUnit { Piece = 1, Hour, Kilometre, Kilogram, Stop, Day }` | what the quantity on an order line counts |
| `VatPercentageId` | Guid? FK → VatPercentages (restrict) | null = the tenant's default VAT |
| `RevenueLedgerAccount`, `CostLedgerAccount` | string(32)? | |
| `IsArchived` | bool | |

No price: a price is a rate line or a price-list row (AD-08; the price-list run).

### DiscountOrSurcharge

| Field | Type | Rule |
|---|---|---|
| `Code` | string(16) | unique; **the stable key invoicing matches on**, never the name (§5) |
| `Name` | string(128) | |
| `Direction` | enum `{ Surcharge = 1, Discount = 2 }` | |
| `ValueKind` | enum `{ FixedAmount = 1, Percentage = 2, PerKilometre = 3 }` | replaces OpenTMS's two booleans (§5) |
| `Side` | enum `{ Revenue = 1, Cost = 2, Both = 3 }` | applies to what the customer pays, what the carrier is paid, or both |
| `DaysOfWeek` | `[Flags] { Monday = 1 … Sunday = 64 }`, not 0 | replaces seven columns (§5) |
| `VatPercentageId` | Guid? FK (restrict) | |
| `LedgerAccount` | string(32)? | |
| `IsArchived` | bool | |
| child **DiscountOrSurchargeValue** | `ValidFrom` DateOnly, `Value` decimal(18,4) ≥ 0 (≤ 1,000 for a percentage), `CurrencyCode` string(3)? | currency **required** for FixedAmount and PerKilometre, **absent** for Percentage (§5: a currency on fixed amounts); at least one; unique per date; ≤ 200; past rows fixed |
| child **DiscountOrSurchargeRegion** | `GeographicalRegionId` (Master Data id), `AppliesAt` enum `{ Loading = 1, Unloading = 2, Either = 3 }` | none = everywhere; unique per region; ≤ 100 (OpenTMS's region links were never saved on create, §6 — the service writes them in the same unit of work as the parent) |

### OutboundInvoiceType

| Field | Type | Rule |
|---|---|---|
| `Code`, `Name` (`LocalizedName`) | | |
| `Kind` | enum `{ Invoice = 1, CreditNote = 2 }` | |
| `IsDefault` | bool | one default **per kind** per tenant |
| `Numbering` | `NumberSequenceFormat` | key `Financial.OutboundInvoiceType:<id>`; gapless (design 50) |
| `IsArchived` | bool | |

The line-creation strategy (OpenTMS: class names compared by `.Name`, §3.10) is an enum the invoicing design adds.

### InboundInvoiceType

`Code`, `Name` (string), `Kind` enum `{ CarrierInvoice = 1, Other = 2 }` (a carrier invoice is matched against
trips: *chartermatch*), `CostLedgerAccount` string(32)?, `IsArchived`. No numbering: an inbound invoice keeps the
supplier's number (Assumption 3). `RelationId` left out (assessment note 2).

**Delete rules:** a VAT category used by a service or surcharge is refused (`InUse`); defaults cannot be deleted or
archived (`DefaultRequired`).

### Out of scope, deliberately

`OutboundInvoiceReminderProfile` (no dunning is built, §5), `CurrencyDayRate` (no FX conversion, §5),
`PurchaseOrderType` and `RelationFinancialSettings` (the purchasing and relation runs), price lists (no assessment
exists yet; the price-list run).

## API contract

Route root `financial`; the design-41/50 shape per service (list with `includeArchived`, detail, lookup, create,
update, delete, bulk delete).

| Service | Route | Extra | Row DTO |
|---|---|---|---|
| `VatPercentageAppService` | `/api/financial/vat-percentage` | `POST /{id}/set-default` | `id, code, name, kind, countryCode, currentPercentage, isDefault, isArchived` |
| `AdditionalServiceAppService` | `/api/financial/additional-service` | — | `id, code, name, unit, vatPercentageName, isArchived` |
| `DiscountOrSurchargeAppService` | `/api/financial/discount-or-surcharge` | — | `id, code, name, direction, valueKind, side, currentValue, currentCurrencyCode, regionCount, isArchived` |
| `OutboundInvoiceTypeAppService` | `/api/financial/outbound-invoice-type` | `POST /{id}/set-default` | `id, code, name, kind, numberingExample, isDefault, isArchived` |
| `InboundInvoiceTypeAppService` | `/api/financial/inbound-invoice-type` | — | `id, code, name, kind, isArchived` |

Details carry all fields, children (`rates[] { validFrom, percentage, isFixed }`, `values[] { validFrom, value,
currencyCode, isFixed }`, `regions[] { geographicalRegionId, name, appliesAt }`), audit and stamp; the invoice type
also `numbering`, `nextNumber`, `nextNumberPreview`. Child updates are replace-semantics, but a fixed row must come
back unchanged (`Financial:FixedValidityRowChanged` otherwise). Errors also `Financial:ValidityRowRequired`,
`Financial:DuplicateValidFrom`, `Financial:CurrencyRequired`, `Financial:CurrencyNotAllowed`,
`Financial:DaysOfWeekRequired`, `Financial:PercentageOutOfRange`, plus the Platform codes. Permissions
(`FinancialPermissions`): `Financial.VatPercentages`, `Financial.AdditionalServices`, `Financial.DiscountsOrSurcharges`,
`Financial.OutboundInvoiceTypes`, `Financial.InboundInvoiceTypes`, each with `.Create`, `.Update`, `.Delete`.

## Migration strategy

Expand only: eight tables in `financial`; per-tenant unique codes, the one-default indexes (VAT per tenant, invoice
type per kind), unique `(ParentId, ValidFrom)` on both validity tables, unique region per surcharge, FK indexes for
the in-use checks; `numeric(5,2)` and `numeric(18,4)`. Seed (idempotent on `Code`; rows of an existing category are
never touched):

| Entity | Rows |
|---|---|
| VatPercentage (NL) | `HIGH` Hoog/High, Standard, default: 19 % from 2001-01-01, 21 % from 2012-10-01; `LOW` Laag/Low, Reduced: 6 % from 2001-01-01, 9 % from 2019-01-01; `ZERO` Nul/Zero, Zero: 0 %; `EXEMPT` Vrijgesteld/Exempt, Exempt: 0 %; `REVERSE` Verlegd/Reverse charge, ReverseCharge: 0 %; `ICP` Intracommunautair/Intra-community, IntraCommunity: 0 %; `EXPORT` Export buiten EU/Export outside the EU, Export: 0 % (0 % rows from 2001-01-01) |
| OutboundInvoiceType | `INV` Factuur/Invoice, Invoice, default, numbering `F`, year, `-`, 5 digits; `CRN` Creditnota/Credit note, CreditNote, default, numbering `C`, year, `-`, 5 digits |
| InboundInvoiceType | `CARRIER` Inkoopfactuur charter/Carrier invoice, CarrierInvoice; `OTHER` Overige inkoop/Other purchase, Other |

## UI design

Nav: domain group **Financial** (`/master-data/financial`) → **Invoicing** (`/master-data/invoicing`): VAT
percentages, Outbound invoice types, Inbound invoice types; **Pricing** (`/master-data/pricing`): Additional services,
Discounts and surcharges. Routes `/master-data/vat-percentages`, `/master-data/outbound-invoice-types`,
`/master-data/inbound-invoice-types`, `/master-data/additional-services`, `/master-data/discounts-or-surcharges`.

- **VAT percentages:** list Code, Name, Kind, Country, Current %, Default badge; editor (`md`): *General* (Code, Name,
  Kind, Country `ReferenceCombobox` over countries by ISO code, Archived), *Rates* (`LinesGrid`: Valid from, Percentage;
  fixed rows read-only with a lock icon and tooltip "A rate that has taken effect cannot be changed"), *Ledger*
  (Sales and purchase account), *Remarks*, Record identification.
- **Additional services:** list Code, Name, Unit, VAT; editor (`sm`): Code, Name, Unit, VAT `ReferenceCombobox`,
  Revenue and cost account, Archived, Remarks.
- **Discounts and surcharges:** list Code, Name, Direction, Kind, Side, Current value (formatted by kind: `12.5 %`,
  `€ 25.00`, `€ 0.12/km`), Regions; editor (`lg`): *General* (Code, Name, Direction, Kind, Side, Days of week as seven
  toggles, VAT, Ledger account, Archived), *Values* (`LinesGrid`: Valid from, Value, Currency — the currency column
  only for fixed amounts and per km; fixed rows read-only), *Regions* (`LinesGrid`: region `ReferenceCombobox`,
  Applies at), *Remarks*, Record identification.
- **Outbound invoice types:** list Code, Name, Kind, Number example, Default badge; editor (`md`): *General*,
  *Numbering* (`NumberingFields`), *Remarks*, Record identification.
- **Inbound invoice types:** list Code, Name, Kind; editor (`sm`).

Money and percentages through `useFormat()` (UI-FMT). `DEVIATION(UI-LOOK-001)`: VAT categories, countries,
currencies and regions use `ReferenceCombobox` over their lookups (all under 500 per tenant), as design 48.

## Test risk analysis

| Part | Class | Coverage |
|---|---|---|
| Tenant isolation of every entity, child and lookup | **Critical** | PostgreSQL negative tests |
| Permissions and features | **Critical** | definition, flag-off, architecture tests |
| Validity rows: past rows fixed, future rows editable, at least one, unique date, `RateOn`/`ValueOn` per date | **Critical** | unit + SQLite service tests (money history, §3.4) |
| Currency rules per value kind; days of week not empty; percentage ranges; decimal round trip (no binary rounding) | **High** | SQLite + PostgreSQL tests |
| Unique codes, defaults (VAT per tenant, invoice type per kind), unique region and date | **Critical** | PostgreSQL index tests |
| Regions saved on create in the same unit of work (OpenTMS bug, §6) | **High** | SQLite test |
| Cross-module codes and ids refused when unknown or (currency) inactive | **High** | SQLite tests with the lookup |
| In-use and default refusals | **High** | SQLite tests |
| Seed idempotent; migration | **Critical** | PostgreSQL tests |
| Frontend lists, editors, fixed rows read-only, value formatting | **Medium** | vitest |
| Localization parity | **Low** | key test |

## Flag & rollout plan

List features default off; the seed fills VAT and invoice types for every tenant. Rollback: features off.

## Cost & SLO impact

Negligible storage (a few thousand rows per tenant). List and lookup p95 < 300 ms; mutations < 500 ms. Current
percentage and value in the list are one grouped subquery per page, not per row.

## Assumptions

1. VAT is seeded for the Netherlands only; other countries' categories are added by the tenant.
2. Ledger accounts are free strings of at most 32 characters until a chart of accounts exists (accounting links,
   Integrations).
3. Inbound invoice types are not numbered; an internal booking number is the purchasing design's choice.
4. A discount or surcharge without regions applies everywhere; with regions it applies when the loading or unloading
   address (as configured) lies in one of them.
5. One default outbound invoice type per kind (one for invoices, one for credit notes).
6. Price lists are not built in this run (no assessment of OpenTMS's price lists exists yet).

## Security quickscan

Twenty permissions plus the feature chain; enums, ranges, ISO codes and children bounded (50 rates, 200 values, 100
regions); history rows immutable once in effect; cross-module codes checked through the published lookup;
tenant-scoped; no raw SQL; no personal data; logs ids and counts.
