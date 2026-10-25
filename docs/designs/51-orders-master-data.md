# PBI: Orders master data: transport order type, waypoint type, time slot

<!-- Written on the PO's instruction of 2026-10-22 ("bouw stamgegevens die een order en trip nodig hebben; hou het
     Engels"). Assessment docs/input/masterdata-rebuild-assessment.md §2 row 3, §2.1 (Orders row, notes 3), §2.2
     (Orders seeds a default order type and time slots), §3.3, §3.8, §3.9, §5 "Reference lists". -->

## Story

As an order clerk I want every transport order to have a type (its number format, the stops a new order starts with,
the fields that must be filled), every stop a waypoint type (load, unload, via, couple…, with the type that follows it)
and delivery windows picked from named time slots, because the order screen, the import and the planboard all need
these before the first order can be entered.

## Affected area

Orders module, schema `orders`, first entities of the module: `TransportOrderType` (+ default waypoints),
`WaypointType` (+ fleet-management codes), `TimeSlot`; their services under `/api/orders/...`; seed; three lists in
the Master data app under a new domain group **Orders → Transport orders**. Master Data gains one published,
non-remote lookup (`IMasterDataReferenceLookup`) that Orders and Financial call to validate and name ids.

## Tenant dimension

Configurable per tenant. Features: `Apps.MasterData.Orders` (on), `Apps.MasterData.Orders.TransportOrders` (on),
`….TransportOrderTypes`, `….WaypointTypes`, `….TimeSlots` (off by default).

## Expected volumes

Per tenant: up to 50 order types (default waypoints ≤ 10 each), up to 100 waypoint types (≤ 20 FMS codes each), up to
100 time slots. Busiest read later: the lookups from the order screen.

## Known edge cases

- A waypoint type whose successor is itself, or a cycle Load → Unload → Load (allowed: the successor is only a
  proposal for the next stop; a self-reference is refused).
- Deleting a waypoint type that an order type's default stops or another type's successor use.
- An overnight slot 22:00–06:00; a slot of zero length; DST nights.
- A relation category or default relation that is deleted in Master Data later.
- Two defaults; deleting the default order type.

## Privacy note

None: no personal data; a default relation is an id.

---

# Design: Orders master data

**PBI:** above (this file)
**Status:** approved (gate 1, PO instruction 2026-10-22; Assumptions are the open details)
**Date:** 2026-10-22
**Depends on:** design 50 (reference-data base, number sequences); Master Data relations (design 43).

## Domain impact

**Module:** Orders, schema `orders`; migration `OrdersMasterData`. **Decisions relied on:** AD-02, AD-06
(`LocalizedName` jsonb), AD-18. No event; extends none of the flows. AD-07/AD-08 untouched (no money).

**Cross-module (rule 3, the stated synchronous call):** Orders validates and names Master Data ids through
`IMasterDataReferenceLookup` (Master Data `Application.Contracts`, implemented in Master Data `Application`,
`[RemoteService(IsEnabled = false)]`, `[Authorize]` for any authenticated user, returns id + name only):
`GetRelationsAsync(ids)`, `GetRelationCategoriesAsync(ids)`. Ids carry no database constraint (assessment §2.2); a
referenced row deleted later shows as "(not found)" and is refused on the next save.

### WaypointType (OpenTMS WaypointType; the stop type)

| Field | Type | Rule |
|---|---|---|
| `Code` | string(16) | upper case, unique per tenant |
| `Name` | `LocalizedName` | default culture required |
| `Activity` | enum `WaypointActivity { Load = 1, Unload, Via, CoupleTrailer, UncoupleTrailer, Customs, Cleaning, Rest, Refuel, Other }` | the stop's semantics (§3.9: typed, never matched on name); Load and Unload move freight |
| `SuccessiveWaypointTypeId` | Guid? FK → WaypointTypes (restrict) | the type proposed for the next stop; not itself |
| `RelationCategoryId` | Guid? (Master Data id) | OpenTMS `WaypointTypeCategory`: the address picker of a stop of this type offers relations of this category first |
| `DefaultRelationId` | Guid? (Master Data id) | OpenTMS `StationingRelationId`: the relation a new stop of this type starts with (e.g. the own depot) |
| `DefaultDurationMinutes` | int? 0–1440 | planned time at the stop |
| `IsArchived` | bool | |
| child **WaypointTypeFleetManagementCode** | `Vendor` string(64), `Code` string(64) | the code an on-board computer uses for this activity; unique per type and vendor; at most 20 (OpenTMS FMS codes child table) |

### TimeSlot

| Field | Type | Rule |
|---|---|---|
| `Code` | string(16) | unique per tenant |
| `Name` | `LocalizedName` | |
| `StartTime`, `EndTime` | `TimeOnly` | local time **at the stop** (the address's IANA time zone); equal is refused; `EndTime < StartTime` means the window ends the next day (§5: OpenTMS's overnight rule was wrong) |
| `IsArchived` | bool | |

`TimeSlot.ToWindow(DateOnly date, string ianaTimeZoneId)` → `(DateTimeOffset From, DateTimeOffset Till)`; a local time
that does not exist (spring DST gap) moves forward to the first valid instant, an ambiguous one (autumn) takes the
earlier offset. Unit-tested over the Amsterdam DST nights.

### TransportOrderType

| Field | Type | Rule |
|---|---|---|
| `Code`, `Name` | as above | |
| `IsDefault` | bool | one per tenant (§3.8); not archivable or deletable while default; `set-default` |
| `Numbering` | `NumberSequenceFormat` | design 50; key `Orders.TransportOrderType:<id>` |
| `RequiredFields` | `[Flags] TransportOrderRequiredFields { CustomerReference = 1, LoadingReference = 2, UnloadingReference = 4, LoadingTimeWindow = 8, UnloadingTimeWindow = 16, ProductPerLine = 32, TransportUnitPerLine = 64, QuantityPerLine = 128, WeightPerLine = 256, VolumePerLine = 512, LoadingMetersPerLine = 1024, Temperature = 2048 }` | §3.9: "required fields as an enum set" instead of reflection property names; enforced by the order design |
| `IsArchived` | bool | |
| child **TransportOrderTypeDefaultWaypoint** | `WaypointTypeId` FK (restrict), `Position` 1… | the stops a new order of this type starts with (§3.3: a child table, not a GUID string); at most 10 |

**Delete rules:** a waypoint type used as a default stop or as another type's successor is refused (`InUse`, with the
using type named). Order-level in-use checks arrive with the TransportOrder design.

### Deliberate differences from OpenTMS

| Dropped or changed | Reason |
|---|---|
| `DefaultWaypoints` GUID string | child table (§3.3) |
| Required-field list of property names | flags enum (§3.9) |
| Copied number-sequence columns | Platform format and generator (§3.9) |
| Intake mailbox on the order type | Integrations (inbound mailboxes, §2 row 6) |
| Questionnaire ids on the waypoint type | questionnaire run (§3.10) |
| `TimeSlot` overnight bug, no time zone | explicit overnight rule and stop-local time (§5) |
| `FindOrCreate` of time slots by name | none (§3.15) |

## API contract

Route root `orders`; design-41/50 shape per service (`GET` list, `GET /{id}`, `GET /lookup`, `POST`, `PUT /{id}`,
`DELETE /{id}`, `POST /bulk-delete`), list filter `includeArchived`.

| Service | Route | Extra | Row DTO |
|---|---|---|---|
| `TransportOrderTypeAppService` | `/api/orders/transport-order-type` | `POST /{id}/set-default` | `id, code, name, numberingExample, defaultWaypoints (names joined), isDefault, isArchived` |
| `WaypointTypeAppService` | `/api/orders/waypoint-type` | — | `id, code, name, activity, successiveWaypointTypeName, relationCategoryName, defaultRelationName, defaultDurationMinutes, isArchived` |
| `TimeSlotAppService` | `/api/orders/time-slot` | — | `id, code, name, startTime, endTime, isOvernight, isArchived` |

Details carry `names`, all fields, children (`defaultWaypoints[] { waypointTypeId, name, position }`,
`fleetManagementCodes[] { vendor, code }`), audit and stamp; numbered types also `numbering`, `nextNumber`,
`nextNumberPreview`. Errors: Platform `DuplicateKey`, `InUse`, `ReferenceNotFound`, `DefaultRequired`,
`TooManyChildren`, `InvalidNumberSequence`, `NextNumberTooLow`; Orders `Orders:SuccessorIsSelf`,
`Orders:EmptyTimeSlot`. Permissions (`OrdersPermissions`): `Orders.TransportOrderTypes`, `Orders.WaypointTypes`,
`Orders.TimeSlots`, each with `.Create`, `.Update`, `.Delete`.

## Migration strategy

Expand only: tables `orders."TransportOrderTypes"`, `"TransportOrderTypeDefaultWaypoints"`, `"WaypointTypes"`,
`"WaypointTypeFleetManagementCodes"`, `"TimeSlots"`; per-tenant unique codes and the one-default index
(`NULLS NOT DISTINCT`, soft-delete filter); FK indexes for the in-use checks. Seed (idempotent on `Code`):

| Entity | Rows |
|---|---|
| WaypointType | `LOAD` Laden/Load (Load, 30 min, successor `UNLOAD`), `UNLOAD` Lossen/Unload (Unload, 30 min), `VIA` Via/Via (Via), `COUPLE` Aankoppelen/Couple trailer, `UNCOUPLE` Afkoppelen/Uncouple trailer, `CUSTOMS` Douane/Customs |
| TimeSlot | `MORNING` Ochtend/Morning 08:00–12:00, `AFTERNOON` Middag/Afternoon 12:00–17:00, `DAY` Dag/Day 08:00–17:00, `EVENING` Avond/Evening 17:00–22:00, `NIGHT` Nacht/Night 22:00–06:00 |
| TransportOrderType | `STD` Standaard/Standard, default, numbering `TO`, year, `-`, 5 digits, default stops `LOAD`, `UNLOAD` |

## UI design

Nav: domain group **Orders** (`/master-data/orders`) → group **Transport orders** (`/master-data/transport-orders`) →
**Transport order types**, **Waypoint types**, **Time slots** (`/master-data/transport-order-types`,
`/master-data/waypoint-types`, `/master-data/time-slots`).

- **Transport order types:** list Code, Name, Number example, Default stops, Default badge, Archived (hidden); row
  actions Edit, Set as default, Delete. Editor (`md`): *General* (Code, Name per language, Archived), *Default stops*
  (`LinesGrid`: position, waypoint type `ReferenceCombobox`), *Required fields* (checkbox group, one per flag),
  *Numbering* (`NumberingFields`), *Remarks*, Record identification.
- **Waypoint types:** list Code, Name, Activity, Next type, Duration; editor (`md`): *General* (Code, Name, Activity
  `Select`, Next type `ReferenceCombobox`, Default duration, Archived), *Defaults* (Relation category
  `ReferenceCombobox`, Default relation `EntityLookup` over relations), *Fleet management codes* (`LinesGrid`: vendor,
  code), *Remarks*, Record identification.
- **Time slots:** list Code, Name, From, Till, Overnight (icon); editor (`sm`): Code, Name, From, Till (time inputs),
  an "ends the next day" hint when Till < From, Archived, Remarks.

`DEVIATION(UI-LOOK-001)`: waypoint types and relation categories (≤ 100 per tenant) use `ReferenceCombobox` over their
lookups, as design 48 did for vehicle types.

## Test risk analysis

| Part | Class | Coverage |
|---|---|---|
| Tenant isolation of the three entities, children, lookups and `IMasterDataReferenceLookup` | **Critical** | PostgreSQL negative tests |
| Permissions and features | **Critical** | definition, flag-off, architecture tests |
| Unique codes, one default, unique FMS code per vendor | **Critical** | PostgreSQL index tests |
| Default rules, numbering, required-field flags round trip | **High** | SQLite service tests |
| Successor not self; in-use refusals (default stop, successor); cross-module ids refused when unknown | **High** | SQLite service tests |
| `TimeSlot.ToWindow`: overnight, DST gap and overlap, zero length refused | **High** | unit tests |
| Seed idempotent; migration | **Critical** | PostgreSQL seed and migration tests |
| Frontend lists and editors | **Medium** | vitest |
| Localization parity | **Low** | key test |

## Flag & rollout plan

List features default off; the seed fills every tenant. Rollback: features off; tables stay.

## Cost & SLO impact

Negligible storage; list and lookup p95 < 300 ms; mutations < 500 ms; one extra lookup call (one `IN` query) per save
that sets a relation or category.

## Assumptions

1. The waypoint activities listed are the complete set the order and trip screens need; adding one later is additive.
2. The required-fields set is the one the order screen will enforce; the order design may add flags (additive).
3. Time slots are local to the stop's address; they carry no time zone of their own.
4. `DefaultRelationId` replaces OpenTMS's *stationing relation*; `RelationCategoryId` its *waypoint type category*.
5. Product and transport-unit transaction times per waypoint type (assessment §2.1 note 3) are Master Data children
   and are not built in this run.
6. A Master Data row deleted while Orders references it is not refused by Master Data (no cross-module in-use check);
   Orders shows it as not found and refuses it on the next save.

## Security quickscan

Twelve permissions plus the feature chain; lengths, enums, time ranges and children bounded (10 stops, 20 codes);
cross-module ids checked through the published lookup; tenant-scoped; no raw SQL in the module; no personal data;
logs ids and counts.
