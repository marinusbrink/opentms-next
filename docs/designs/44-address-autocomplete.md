# PBI: Address autocomplete ("auto complete")

<!-- Written on the PO's instruction of 2026-10-13 ("make the design for auto complete based on the
     solution architecture documents"). No GitHub issue was opened; "44" is the next free number after
     PR #43. The reading of "auto complete" is assumption 1 of the design below: the free-text address
     autocomplete that the component map still lists as *build* (`mat-autocomplete` → `EntityLookup`
     `allowFreeText`), now that the searched entity picker (`EntityLookup`, GAP-01) exists. -->

## Story

As an order clerk, a planner or an administrator entering an address (today in the relation editor,
later on an order stop) I want to type a few characters of a postal code or street and pick the full
address from a list, because typing street, house number, postal code, city and coordinates by hand is
slow and produces addresses the geocoder and the matcher cannot find later. The list must offer the
addresses this tenant already knows (its own relations' addresses) first, and the national address
register of the country after that, so a depot I have entered once is one keystroke away and a new
customer's address arrives correct the first time.

## Affected area

The Master Data domain: an address-suggestion application service under `/api/master-data/...`, a
published provider interface in its `Application.Contracts`, and the address block of the relation
editor in the Master data app. The Integrations domain: the first external address provider as a
vendor adapter (the anti-corruption layer of the domain map). The UI library: `EntityLookup` gains the
free-text mode the component map names. Builds on design 43 (Relation, `RelationAddress`, the
`AddressFields` block, `EntityLookup`).

## Tenant dimension

Configurable per tenant, twice: the feature flag `MasterData.AddressAutocomplete` (default off) switches
the control on; the setting `MasterData.AddressProvider` (default `None`) selects whether, and which,
external register is asked. With the flag on and the provider `None`, the list shows the tenant's own
addresses only and nothing leaves the system. Own-address suggestions are tenant data and tenant-scoped;
provider answers are public reference data.

## Expected volumes

Per tenant, steady state (the test engineer multiplies by five):

| What | Volume | Note |
|---|---|---|
| Own addresses searched | 30,000 `RelationAddress` rows (design 43) | contains search on a search key |
| Addresses entered per day | 20 to 200 | relations today; order stops later multiply this by 10 to 50 |
| Suggest calls per address entered | 3 to 6 | one per debounce window while typing; one resolve call per pick |
| Provider calls per day | up to 1,200 now; up to 60,000 later with order entry | PDOK is unmetered; a paid provider is priced in the cost section |

## Known edge cases

- The row's country is not served by the provider (PDOK: anything but NL): own suggestions only.
- The row has no country yet: the provider is asked without a filter; the pick sets the country.
- The provider is slow or down: own suggestions appear, provider ones silently do not; no error toast.
- The same address exists at several of the tenant's relations: one row per relation, the relation's
  name on the second line, so the user sees which record it is.
- A pick arrives after the user kept typing (stale response): the later term wins; a pick always fills
  from the resolved record, never from the suggestion row.
- A picked address of which the tenant has no `Country` row (code known, country not seeded): the pick
  fills everything but the country and the usual required-field validation shows it.
- Postal code typed with or without a space, lower-case, with a house number appended ("1234ab 12").
- An address with a house letter or an addition ("12a", "12-II"): letter and addition go to the suffix.
- The user ignores the list and keeps typing: the text stays; nothing is forced.

## Privacy note

Partly. The typed term leaves the tenant's database to the external provider when a provider is
configured. An address alone is not personal data; a term can contain a person's name when the user
types one. The design states what leaves, to whom, and what is logged. No new entity holds personal
data; the GDPR art. 15/17 answers of design 43 are unchanged.

---

# Design: Address autocomplete

**PBI:** above (this file)
**Status:** draft
**Date:** 2026-10-13
**Depends on:** design 43 (PR #43) merged: `Relation`, `RelationAddress`, `AddressFields`,
`EntityLookup`, the trigram extension `pg_trgm`.

> Architecture fit (`docs/architecture/`): this is a **read** that helps a person compose a command
> (`01-overview.md`: people send the same commands from the screens; the core takes no operational
> decision, AD-01: a suggestion is a proposal the user picks, never a fact the system writes). It
> extends none of the three flows of `02-flows.md`; it is not ingest (AD-15: nothing is recorded, nothing
> arrives unasked) and not an agent (`06`). The external register is a new external service that the
> table in `05-runtime-and-deployment.md` does not list; it is added under *Assumptions* and the vendor
> adapter lives in Integrations as the domain map's anti-corruption layer (AP-16 says the same for
> ingest adapters; this design applies the same placement to a read adapter, assumption 4). The same
> application service is what the intake boundary agent (flow 1, step 2) will call to resolve an
> address from an order PDF: an agent reaches the core through the same application services as a
> person (`01`, `06`).

## Domain impact

**Modules:** Master Data (owner of the behavior and of the provider contract) and Integrations (the
first provider adapter). The UI library (`frontend/src/components/ui`). No Platform change.

**Decisions relied on:** AD-01, AD-02, AD-03, AD-16 (untouched: no event), AD-18 (the suggestion
service is an application service exposed as a conventional controller; no separate pipeline).
AD-12 noted: geocoding is not routing; when the routing engine service exists it may implement the
provider interface below, nothing here presumes it. **AO-12** not depended on: no positions, no devices.

**Events:** none. Nothing reacts to a suggestion; a pick ends in the relation editor's save, which is
design 43's command.

### Master Data

1. **`RelationAddress.SearchKey`** (new column, string(320), generated by the entity like
   `Relation.SearchKey`: `MasterDataValues.SearchKey(Street, HouseNumber, HouseNumberSuffix, PostalCode,
   City)`; maintained on every create and update of the address; never shown). Index: trigram GIN on
   `SearchKey` (contains search), the same mechanism as the relation lookup of design 43.
2. **`IAddressSuggestionProvider`** in `OpenTms.MasterData.Application.Contracts/Addresses/`: the
   published interface an adapter implements (module rule 2: Integrations references Master Data's
   `Application.Contracts`; Master Data references nothing of Integrations). Resolved as
   `IEnumerable<IAddressSuggestionProvider>`; the one whose `Name` equals the tenant's setting is used.

   ```csharp
   public interface IAddressSuggestionProvider
   {
       /// The value of the MasterData.AddressProvider setting that selects this provider ("Pdok").
       string Name { get; }
       /// ISO 3166-1 alpha-2 codes the provider serves; empty means every country.
       IReadOnlyCollection<string> CountryCodes { get; }
       Task<IReadOnlyList<AddressSuggestionDto>> SuggestAsync(AddressSuggestionQuery query, CancellationToken ct);
       /// The full record behind a suggestion; null when the reference is unknown or expired.
       Task<AddressSuggestionDto?> ResolveAsync(string reference, CancellationToken ct);
   }
   ```
3. **`AddressSuggestionAppService`** (`IAddressSuggestionAppService`, `/api/master-data/address-suggestion`):
   - `SuggestAsync(term, countryId?, take)`: own suggestions first (at most 5: non-archived relations'
     addresses whose `SearchKey` contains the normalised term, optionally in the country, shortest key
     first, relation name on the row), then the provider's (at most 10) when a provider is configured
     and serves the row's country (or no country is given). Own and provider are queried concurrently;
     a provider failure or timeout yields zero provider rows and a `Warning` log line without the term.
   - `ResolveAsync(source, reference)`: `Own` returns the address row's full DTO (tenant-filtered, so
     a foreign id is "not found"); `Provider` calls `ResolveAsync` on the configured provider and maps
     the country code to the tenant's `Country` row (`countryId` null when the tenant has none).
   - Normalisation of the term: `MasterDataValues.SearchKey(term)` for the own search; trimmed and
     collapsed whitespace for the provider.
   - Provider answers are cached per (provider, country code, normalised term) for 10 minutes in
     ABP's distributed cache (`IDistributedCache<AddressSuggestionCacheItem>`, tenant-prefixed by ABP's
     default key normaliser; the data is public, the prefix is harmless). Own suggestions are not cached.
4. **Setting `MasterData.AddressProvider`** (`MasterDataSettingDefinitionProvider` in
   `OpenTms.MasterData.Domain/Settings`, per tenant, string, default `None`, values `None` | a provider
   `Name`; an unknown value behaves as `None` and logs once at `Warning`). No screen in this run: the
   host sets it through ABP setting management (`ISettingManager`, which `OpenTms.Application`
   references); a tenant settings screen is a later Administration-app design (assumption 6).
5. **Feature `MasterData.AddressAutocomplete`** (group `MasterData`, default `false`, per tenant) on the
   service (`[RequiresFeature]`) and on the control (the Master data app's `useIsFeatureEnabled`).
6. **Permission `MasterData.AddressSuggestions`** (view only, no children): a read anybody who edits
   addresses needs; granted to the roles that hold `MasterData.Relations.Create` or `.Update` today and
   later to order entry, without tying it to either.

### Integrations

**`PdokAddressSuggestionProvider`** in `OpenTms.Integrations.Application/Addresses/Pdok/`: the adapter
for PDOK Locatieserver (the Dutch national address register BAG behind a public, keyless search API;
assumption 2). `Name = "Pdok"`, `CountryCodes = ["NL"]`.

- `SuggestAsync` calls the `suggest` operation with the term, `fq=type:adres`, `rows=take`; maps each
  document's display name to `Label` and its id to `Reference`; `IsComplete = false` (the suggest answer
  has no coordinates or split fields).
- `ResolveAsync` calls the `lookup` operation with the id; maps street, house number, house letter plus
  addition (joined into `HouseNumberSuffix`), postal code (as the register writes it, "1234 AB"), city,
  the centroid to latitude/longitude, `CountryCode = "NL"`, `IsComplete = true`.
- A typed `HttpClient` through `IHttpClientFactory` with the standard resilience pipeline of
  `Microsoft.Extensions.Http.Resilience` (motivation: constitution rule 3 asks for retry with backoff on
  external calls and nothing in the solution provides it yet; the package is Microsoft's, already the
  ABP 10 era baseline, no third-party dependency): overall budget 1.5 s per call, one retry on a
  transient failure, a circuit breaker that skips the provider for 30 s after five consecutive failures.
  The base address is configuration (`AddressProviders:Pdok:BaseUrl`), never user input.
- Vendor shapes stay inside this class (anti-corruption layer): nothing of PDOK's JSON appears in a DTO.
- An ABP `IObjectMapper` is not used; the mapping is explicit and unit-tested against recorded answers.

Adding a second provider later (a paid, worldwide one; assumption 3) is a second class in the same
folder and a second allowed setting value; Master Data does not change.

### UI library

`EntityLookup` gains the **free-text mode** of the component map row (`mat-autocomplete` →
`EntityLookup allowFreeText`). The API, since `lookup.md` gives none:

```ts
| {
    allowFreeText: true;
    multiple?: false;
    value: string;                       // the text, owned by the caller like an Input's value
    onChange: (text: string) => void;    // every keystroke
    onPick: (item: T) => void;           // the user chose a suggestion (click, Enter, Tab)
  }
```

Free-text mode keeps the data layer (`useEntityLookupPages`, 250 ms debounce, 2 characters, the option
rows of `renderOption`, the `Spinner` and `NoItemsFound` rows, Tab commits the highlighted row per
UI-KEY-004) and differs in what the rule's entity mode does with the value: the input is the value;
blur or Escape keeps the text; no clear button, no edit pencil, no "Add new …", no multiple; **an empty
term requests nothing** (`DEVIATION(UI-LOOK-002): a free-text field has no "first page"; below two
characters it shows no list at all, approved in this design`). `fetchPage` returns one page
(`totalCount = items.length`); the sentinel never fires. The proposal to add this mode to `lookup.md`
goes to `docs/conventions/ui/proposals.md` by the build (the canonical rule is the PO's edit).

## API contract

Conventional controller, route root `master-data`, feature `MasterData.AddressAutocomplete`, permission
`MasterData.AddressSuggestions` on both methods. OpenAPI and the typed client are regenerated.

| Method | Route | Request | Response |
|---|---|---|---|
| GET | `/api/master-data/address-suggestion` | `term` (string, 2..128, required), `countryId` (Guid?), `take` (int, 1..10, default 10; the provider's share) | `AddressSuggestionDto[]` (own rows first, at most 5, then provider rows, at most `take`) |
| GET | `/api/master-data/address-suggestion/resolve` | `source` (`AddressSuggestionSource`), `reference` (string, 1..256) | `AddressSuggestionDto` (404 `OpenTms.MasterData:SuggestionNotFound` when unknown or expired) |

```csharp
public enum AddressSuggestionSource { Own = 0, Provider = 1 }

public class AddressSuggestionQuery   // provider-facing, in Application.Contracts
{
    public string Term { get; set; }          // trimmed, 2..128
    public string? CountryCode { get; set; }  // ISO alpha-2 or null
    public int Take { get; set; }             // 1..10
}

public class AddressSuggestionDto
{
    public AddressSuggestionSource Source { get; set; }
    public string Reference { get; set; }         // Own: the RelationAddress id; Provider: the provider's id
    public string Label { get; set; }             // one line for the list: "Dorpsstraat 12a, 1234 AB Ons Dorp"
    public bool IsComplete { get; set; }          // false: call resolve before filling the form
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? HouseNumberSuffix { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string CountryCode { get; set; }
    public Guid? CountryId { get; set; }          // the tenant's Country with that code; null when none
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? TimeZoneId { get; set; }       // the country's primary zone when CountryId is set
    public Guid? RelationId { get; set; }         // Own only
    public string? RelationName { get; set; }     // Own only: the second line of the row
}
```

Own rows are complete at suggest time (`IsComplete = true`); the client may fill without resolving.
Errors: `SuggestionNotFound` (404); validation errors (400) as the host maps them today. A provider
failure is never an error for the client: the answer simply holds own rows only.

## Migration strategy

One additive migration `MasterDataAddressSearchKey` in the Master Data context, after design 43's:
column `SearchKey` (not null, default `''`) on `RelationAddresses`, a backfill statement
`lower(concat_ws(' ', "Street", "HouseNumber", "HouseNumberSuffix", "PostalCode", "City"))` (plain SQL;
diacritics are folded by the entity on the next save, see assumption 7), and the trigram GIN index on
`SearchKey`. Expand only; no contract step; `Down` drops the index and the column. No data moves
between tables; the setting and the feature need no migration (ABP tables).

## UI design

**Where.** The `AddressFields` block of the relation editor (design 43): the **Postal code** and
**Street** fields become free-text lookups when the feature is on; with the feature off they stay the
plain `Input`s of today (the flag is read once per editor through `useIsFeatureEnabled`). City, house
number and the rest stay plain. No new screen, no navigation change, no shell change.

**Components.** A domain component `AddressSuggestionLookup` in `src/domains/master-data/` wraps
`EntityLookup allowFreeText` over the hook `useAddressSuggestions(countryId)` (generated client,
TanStack Query; the query key carries country and term). `fetchPage` maps `{ term }` to the suggest
call. `getLabel` returns `Label`; `renderOption` shows `Label` in `font-medium` and, for own rows, the
relation's name in `text-label text-muted-foreground` (UI-LOOK-003: two lines). Own rows carry a
`Building2` icon, provider rows a `MapPin`, so the user sees the source; the list is one list, own first.

**The pick.** `onPick` resolves when `IsComplete` is false (a `Spinner` in the field's right edge
meanwhile, the inputs stay enabled), then calls `AddressFields.onChange` with the row filled:
`countryId` (when the DTO has one; otherwise unchanged), `postalCode`, `city`, `street`, `houseNumber`,
`houseNumberSuffix`, `latitude`, `longitude` (as text, the row's representation), and the time zone
through the existing `onCountryPicked` path when the country changed. Every filled field stays
editable. The field that was typed in shows the filled value, not the label. Validation runs as today
on the filled row.

**States.** Loading: the `Spinner` row of the lookup while suggestions load, the field spinner while
resolving. Empty: `NoItemsFound` row at two or more characters with no own and no provider rows.
Error: a failed suggest call shows the `NoItemsFound` row (a transient read; no toast, UI-FB rules on
silent reads do not apply because nothing is lost); a failed resolve shows the toast of
`getApiErrorMessage()` and leaves the typed text. Permission denied: without `MasterData.AddressSuggestions`
the fields are plain inputs (the control is not rendered; `useIsGranted`), never a disabled lookup.
Provider down: not a state the user sees; own rows only.

**Keyboard.** Arrow keys move, Enter and Tab commit the highlighted row (UI-KEY-004), Escape closes
the list and keeps the text, typing on continues the text. No shortcut.

**Optimistic updates:** none (reads only).

**Manual:** `docs/manual/relaties.md` (Dutch) gets a paragraph under the Addresses block: typing in
*Postcode* or *Straat* offers the known addresses and, when the register is enabled, the national
addresses; picking fills the address; everything stays editable.

## Test risk analysis

| Part | Class | Mandatory coverage |
|---|---|---|
| Tenant isolation of own suggestions and of `resolve` with source `Own` | **Critical** | PostgreSQL negative tests: tenant B's term never returns tenant A's address; tenant A's address id resolved in tenant B is not found |
| Feature flag and permission on both methods | **Critical** | definition tests, flag-off and permission-off tests (architecture test covers the attributes) |
| Migration: column, backfill, trigram index | **Critical** | PostgreSQL migration test as run 1 and 2; index use on the contains query (`EXPLAIN` in the volume test) |
| No PII in logs: provider failure, cache, timing lines carry provider name, status, elapsed, count; never the term | **High** | log-capture tests on the service and the adapter |
| Provider adapter mapping (suggest and lookup) against recorded PDOK answers: house letter and addition, postal code shape, centroid order (longitude first in the source) | **High** | unit tests with recorded JSON fixtures; no live calls in the suite |
| Fallback: provider timeout, 5xx, malformed answer, circuit open: own rows still returned, no exception to the client | **High** | service tests with a faulting `HttpMessageHandler` |
| Country gating: provider skipped for a country it does not serve; asked when no country; code mapped to the tenant's country or null | **High** | service tests |
| Caching: second identical term hits the cache; a different country or term does not; expiry | **Medium** | service tests on the cache abstraction |
| Volume ×5: 150,000 addresses, suggest by fragment within the interactive budget | **High** | PostgreSQL volume test, cold and warm |
| Frontend: `EntityLookup` free-text mode (text ownership, Tab/Enter/Escape, no clear/pencil/add, two-character threshold, no request on empty), `AddressSuggestionLookup` fill on pick, resolve spinner, flag-off and permission-off rendering | **Medium** | vitest |
| Localization parity (new keys both languages) | **Low** | key test |

Idempotency: reads only; the resolve of the same reference twice returns the same record. No job, no
webhook, no outbox.

## Flag & rollout plan

- Flag `MasterData.AddressAutocomplete`, default off, per tenant: health tenant → friendly tenants → all.
- Setting `MasterData.AddressProvider`, default `None`: switched to `Pdok` per tenant **after** the flag,
  first on the health tenant for a week of provider latency and failure-rate observation, then per
  friendly tenant. A tenant can run with the flag on and the provider off indefinitely.
- Existing data: the backfill in the migration; no user action. Rollback: flag off (control disappears),
  provider `None` (no external calls); column and index stay.
- Ordering with design 43: this flag has no effect while `MasterData.Relations` is off, because the
  relation editor is the only screen with the control in this run.

## Cost & SLO impact

- **Cloud SQL:** one column and one trigram index on `RelationAddresses`: at 30,000 addresses about
  5 MB per large tenant; nothing elsewhere. No new tables, no job.
- **External calls:** PDOK is a public service without an account or a fee; fair use applies and is far
  above the volumes stated (assumption 2). Egress: a few hundred bytes per call; at 60,000 calls per day
  across all tenants under 50 MB per day. No Cloud Run min-instance change. **Per-tenant margin:
  unaffected** with PDOK. For the record at gate 1 (constitution rule 6): a paid worldwide provider
  costs in the order of a few euro per thousand address entries (session-priced autocomplete), which at
  order-entry volumes would be a visible per-tenant cost line and therefore its own design and PO
  decision (assumption 3).
- **Budgets:** interactive reads p95 < 300 ms. The own-data path is one trigram-indexed query with a
  limit of five and stays within it. The provider path adds the provider's latency (PDOK from a
  European region typically well under 300 ms, to verify on the health tenant); the service answers
  own rows even when the provider is slow, and the 1.5 s budget caps the worst case. The k6 scenario
  covers the own-data path; the provider path is observed, not load-tested (no live calls in CI).
- **Web frontend:** no new package; the free-text mode is a code path in an existing component. Bundle
  budget per route unchanged.
- **Availability:** no core flow depends on it; a provider outage degrades to own rows.

## Assumptions

1. **"Auto complete" means the free-text address autocomplete** of the component map
   (`mat-autocomplete` for addresses and postal codes, 19 uses in the old app, verdict "deliberate for
   address-like free entry"): suggestions while typing an address, filling the address on a pick. The
   searched entity picker (relations, drivers) already exists as `EntityLookup` (GAP-01, design 43).
   If the PO meant something else (for example model-based completion of free-text fields, which would
   be an agent design under `06`), this design is the wrong one and stops.
2. **PDOK Locatieserver is the first external provider**: the national address register of the
   Netherlands behind a keyless public API, free of charge under fair use, Dutch addresses only. It is a
   new external service not in `05-runtime-and-deployment.md`'s table; it needs no account, key,
   contract or IAM binding (constitution rule 7 has nothing to bind). The product is Dutch; a worldwide
   provider is not needed for the first tenants.
3. **A paid worldwide provider is out of scope** and a later design (its own cost line, keys as secrets,
   a data-processing position per constitution rule 5). The interface is shaped so that it is an adapter
   and a setting value, nothing more.
4. **The adapter lives in Integrations**, applying AP-16 (vendor adapters for ingest live in Integrations)
   to a read adapter, with the interface published by the owning module (the inverse dependency of
   AP-14). Integrations therefore references `OpenTms.MasterData.Application.Contracts`, which module
   rule 2 allows; the architecture test is extended to assert that Master Data references nothing of
   Integrations.
5. **The Master data app may show provider rows** although Integrations is not in its domain mapping:
   the app reads only the Master Data endpoint; the provider is an implementation detail behind it. No
   change to `apps.config.ts`.
6. **No settings screen in this run.** The provider setting is set by the host through ABP setting
   management; the health tenant first. The Administration app's settings view is a later design.
7. **The SQL backfill folds no diacritics** (it lower-cases and joins); the entity folds them on the next
   save. Acceptable because `MasterData.Relations` is not live on any tenant yet, so no address row
   predates the entity-maintained key in production.
8. **Own rows are capped at five and provider rows at ten**, in that order, one list. The old app had no
   own-data source in its autocompletes; this is the assessment's "match before create" idea applied to
   entry (§3.15), not a copy.
9. **Term normalisation for the provider is whitespace only**; PDOK handles case and postal-code spacing
   itself. The own search uses `MasterDataValues.SearchKey`.
10. **A single permission `MasterData.AddressSuggestions`** rather than reusing `MasterData.Relations`,
    so order entry can be granted it without relation rights later.
11. **The 10-minute cache is per tenant** (ABP's default key prefix), although the data is public; sharing
    across tenants would need a documented cache-key exception and saves little.
12. File number 44 stands in for the issue number.

## Security quickscan

- **Permissions:** one new definition `MasterData.AddressSuggestions`, both sides; both public methods
  attributed (architecture test); the feature as the second gate.
- **Input validation:** `term` 2..128 characters after trimming; `take` clamped 1..10; `countryId`
  must be a country of the tenant; `reference` 1..256 characters and, for `Own`, a Guid. The term is
  URL-encoded by `HttpClient`; the provider base URL is configuration, no part of a request URL comes
  from the user except the encoded query value (no SSRF surface).
- **Outbound call:** one host, HTTPS, no credentials, no tenant id, user id, relation name or any other
  tenant data in the request: the term and the row count only. The answer is treated as untrusted input:
  lengths are enforced on every mapped field, coordinates range-checked, unknown fields ignored.
- **Abuse:** the endpoint is authenticated and permissioned, debounced on the client and bounded per
  call; there is no per-tenant rate limit in this run (none exists in the solution), named as a risk
  for the health-tenant observation.
- **Tenant isolation:** the own search runs under the ABP data filter; `resolve` with `Own` is a
  tenant-filtered repository read; no filter bypass anywhere.
- **Logging:** provider name, HTTP status, elapsed time, row count, tenant id, correlation id; never the
  term, a label or an address (constitution rule 5: addresses are PII in logs).
- **Personal data (GDPR):** no new entity; no personal data is stored by this feature. What leaves: the
  typed term to PDOK, a Dutch public body, over HTTPS, when a tenant enabled the provider; the manual
  says so in the paragraph on address completion. Art. 15/17 answers of design 43 are unchanged; the
  cache holds provider answers (public data) keyed by term for ten minutes and nothing about a person.
- **Dependencies:** `Microsoft.Extensions.Http.Resilience` (Microsoft) in `OpenTms.Integrations.Application`;
  no frontend package.
