<!-- Appendix to docs/input/transport-rebuild-assessment.md and pricing-rebuild-assessment.md. Produced 2026-10-25 by reading the OpenTMS source at ~/dev/OpenTMS (branch Development, commit 5f547cc6b); not available to a cloud session, which is why this copy lives in the repository. Citations are path:line in that checkout. F = fact read in code, I = inferred. -->

# Legacy OpenTMS – Transport screens inventory (Angular)

Source: `OpenTMS/angular/src/app` (paths below are relative to it). Dutch labels come from
`aspnet-core/src/FiLogic.Core/Localization/FiLogic/FiLogic-nl.xml`; a key with no Dutch text in that file is
marked *(no nl text)* and falls back to the English key at runtime. Every statement is read from the source;
items marked **inferred** are my interpretation.

Note on the tab name: there is no resource text "Tevens vrachtdossier". The tab the PO means is
`l('AssignTransportFile')` = **Vrachtdossier** (`transport_order/transport_order_details/transport.order.details.component.html`,
`transport-order-assigned-file-tab`). I use "Vrachtdossier tab" below.

---

## 1. Navigation and layout

**Route and shell.** `/app/transport` lazy-loads `TransportModule` (`transport-routing.module.ts`). `TransportComponent`
renders a `mat-toolbar` (command bar) plus a left `mat-sidenav` with two expansion panels, each with filter sub-items that
set `?filter=` on the child route:

| Panel (nl) | Route | Sub-items (nl) → filter | Permission (route `data.permission`) |
|---|---|---|---|
| Transportopdrachten | `transportorder` → `TransportOrderWrapperComponent` | Te accepteren (`pendingAcceptance`, orderState −1, multi-select), Vandaag, Vandaag & Morgen, Deze week, Open (state 0), Alles (free date range), Templates *(no nl text)* (`isTemplate`) | `Applications.OpenTmsApp.TransportApp.TransportOrders` |
| Vrachtdossiers | `transportfile` → `TransportFileWrapperComponent` | Vandaag, Deze week, Open (`isArchived=false`), Alles (free date range) | `Applications.OpenTmsApp.TransportApp.TransportFiles` |

Each sub-item is itself a permission (`Applications.OpenTmsApp.TransportApp.TransportOrdersToday`, …, see §7);
`TransportComponent.applyPermissions` removes sub-items the user lacks. Default filter is `today`
(`transport-order-wrapper.component.ts`, `transport-file-wrapper.component.ts`). A `Finance` parent app reuses the same
order wrapper with filter `readyfor` (state 2).

**Grid library: ag-grid (Alpine theme), server-side row model**, 40-row blocks (`global/filogic-grid-base/filogic-grid-base.ts`).
Columns are **not hard-coded**: they come from the server (`getAllAttributes` → `EntityAttributeDto[]`) and the user's
displayed-fields setting, so the column set equals the list DTO. Order grid columns (`TransportOrderListDto`): flags,
description, creationTime, emailDeliveryConfirmationStatus, taskStatus, orderState, nrOfAttachments, relation,
relationContact, transportOrderType, organizationUnit, orderNumber, orderDate, orderReference, nrOfWaypoints,
waypointDates/Names/Cities/References (all, origin, destination), invoiceReferences, totalDuration, totalDistance,
revenue, costOfRevenue (+profitMargin, profitMarginPercentage), transportFiles, primary/secondary/third vehicles,
discountsAndSurcharges, currency. File grid columns (`GetAllTransportFileDto`): flags, fileNumber, fileDate, fileName,
transportFileType, organizationUnit, departureRelation, scheduled/actual departure & arrival, scheduled/actual distance,
estimated/actual CO₂, transportCompany, vehicles (3), drivers, loadFactor, loadingMeters, epq, amount, positions, colli,
weight, isPublishedToFleetManagementSystem, isCanceled, isArchived, fleetManagementSystemDeviceStatus, nrOfStops,
nrOfLegs, customers, transportOrders, first/last/all stop names & cities, transportOrderReferences, barcodes,
invoiceReferences, containerNumbers, revenue, cost, expectedPrice, actualPrice, warnings, sensorCodes.
Cell renderers: `orderState`, `taskStatus` (colours from `App.ColorSettings.*` tenant settings), `flags`,
`emailDeliveryConfirmationStatus`, `loadFactor`, `warnings`. Columns flagged `displayTotalsInFooter` get `aggFunc: sum`.

**Sorting/grouping/filters/saved views.** Any column sortable except `actualCarbonFootprint`,
`estimatedCarbonFootprint` (orders) and description/actualPrice/expectedPrice/weight/barcodes/containerNumbers/
transportOrderReferences/invoiceReferences (files) (`common/ag-grid-defaults/ag-column-restrictions.ts`). Row grouping via
the ag-grid Columns tool panel (server-side `Group_By_Columns`; grouped columns are hidden; `(leeg)` group). No default
sort is set in code (server order). Filters live in `ag-preferences-panel`: free text search (min 3 chars), **Periode**
date range (only on Alles/Templates), **Afdelingen** (organisation units, persisted per grid), flag filter, refresh,
**Exporteren naar Microsoft Excel** (background job), gear menu with **Instellen voor alle gebruikers / alle
administraties** (needs `Applications.Administration.Tenant.ApplyGridProfileToAllUsers/Tenants`). Column state
(order, width, pin, sort, visibility, expanded groups) is saved per user under `Filogic.User.<App>.TransportOrder.AgGrid.State`.

**Command bar (toolbar, `transport.component.html`).** Not selection-aware except *Accepteren*:

| Button (nl) | Action |
|---|---|
| **Nieuw** ▸ Transportopdracht ▸ *per order type* / Nieuw sjabloon / Van bestand (import, out of scope) / Van sjabloon (generate wizard) | Opens order modal pre-typed, template modal, import dialog, or wizard |
| **Nieuw** ▸ Vrachtdossier ▸ *per file type* / Meerdere vrachtdossiers aanmaken | Creates a file **immediately** (no dialog, `createTransportFile`) or opens the generate-files dialog |
| **Accepteren** (only on Te accepteren with >1 selected) | Accept dialog for selected ids |
| **Printen of E-mailen** | `PrintEmailWizardComponent` (shared, out of scope) |
| **Transportopdrachten Goedkeuren voor Facturatie** (hidden for Arrowline tenant) | `TransportOrderApproveWizardComponent` (shared) |
| **AI Transport Order Importeren** (feature `ChainFill.AiPdfImport`) | Chainfill import wizard (out of scope) |
| **Andere Wizards** ▸ sorted alphabetically | Boordcomputer-gevoede vrachtdossiers herverdelen, Proforma-overzicht Wizard (out of scope), Kortingen en Toeslagen (master-data wizard), Transportopdrachten herverdelen, Vrachtdossiers Archiveren, Verstuur ETA Notificaties (out of scope), Transportorders importeren (out of scope) |

**Row actions = ag-grid context menu** (right click selects the row first):

| Grid | Menu items (nl) |
|---|---|
| Orders | Bewerken, Verwijderen (confirm "Weet je zeker…"), Copy *(no nl)* / **Nieuw van sjabloon** (on Templates) → generate wizard with the row as source, Vlag, CMR Afdrukken; on Te accepteren additionally **Accepteren** first |
| Files | Bewerken, Verwijderen, Vlag, E-mail versturen (`EmailSendingDialogComponent`), Archiveren (confirm "Weet u zeker dat u dit vrachtdossier wilt archiveren?"), CMR Afdrukken |

**Double-click** on a data row opens the details **modal** (`rowDoubleClicked → editData`). Both details screens are
ngx-bootstrap modals (`modal-lg`, `backdrop: static`) hosted in the global header (`shared/layout/header.component.ts`),
not pages: `TransportOrderDetailsModalComponent`, `TransportFileDetailsModalComponent`. Esc closes the order modal unless a
child dialog is open (`@HostListener document:keydown.escape`). There is **no unsaved-changes guard** on close (fact: `close()`
just emits).

---

## 2. Transport order details (`transport_order/transport_order_details/`)

Title = order number (or new). Top: `entity-operations-bar-e` (ribbon) with operations per state
(`entity_operations_bar_enhanced/operation-sets/transport-order-operations.ts`):

| State | Buttons (English labels, *no nl text for "Save & Close"/"Save only"*) |
|---|---|
| Template | Save & Close, Save only, Close, Cancel (= close) |
| −1 Te accepteren | Accept (Accepteren), Close |
| 0 Open | Save & Close, Save only, Close, Cancel, Approve (Goedkeuren) with dropdown **Goedkeuren voor Facturatie / Direct archiveren** |
| 1,2,4,8 | Save & Close, Save only, Close, Cancel, Revise (Herzien) |

Rules (fact): Save/Approve are disabled while no relation is selected or the form is invalid; "Close" has condition
`hasChanges === false` but `formState` always returns `hasChanges: true` once forms exist, so **Close is effectively never
shown** — the user closes via the header X / Esc / "Cancel". `isReadOnly = orderState > 0` (every field read-only after
approval; `save()` returns early). Right side (not for templates): **Afdrukken & Downloaden** (toggles the hidden
print-or-download tab), e-mail button, and the state badge(s) (bit flags decoded into names).

### Tabs, in order

**1. Algemeen** (`orderForm`)

| Field (nl) | Key | Control | Rules |
|---|---|---|---|
| Ordernr. | OrderNumber | text | readonly; hidden on template |
| Orderdatum | OrderDate | date (owl) | hidden on template; change re-queries relation financial defaults and D&S weekday rules |
| Naam | Name (`templateName`) | text | template only |
| Afdeling | OrganizationUnit | org-unit select | |
| Soort transportopdracht | TransportOrderType | select | **disabled unless template**; change loads default/required freight fields, default waypoint types, default duration |
| Relatie | Relation | relation lookup (customer categories) | **required**; change → clears contact, sets first waypoint relation from relation default, re-calculates distance, loads financial defaults (chargeBy flags, price lists, VAT, currency, payment term, D&S) |
| Contactpersoon | ContactPerson | contact lookup | disabled until relation |
| *prominent custom properties* | | per type | |
| Referentie | Reference | textarea (max 2500) | |

Below it a nested tab group:

**1a. Vrachtregels** (freight entry + grid). A horizontal **waypoint strip** (`waypoints.component`), one row per waypoint
(defaults from order type, min 2, +/− icons): Activiteit (waypoint type, autocomplete, **create inline**), Transactietijd
(duration), Relatie (lookup, required, filtered by waypoint type, create inline), Contactpersoon, Datum, Timeslot
*(no nl)* (autocomplete, create inline), Referentie, Opmerkingen. Rule: when all waypoints have a relation, distance and
duration are queried (`queryDistance`) into the freight form; `App.TransportOrder.PreventChangingWaypointRelationWhenLegAssigned`
locks waypoints whose leg is already on a file ("legIsAssigned"). Then the **new-freight form**; visibility and required-ness
of every field come from the order type (`isVisibleField/isRequiredField`):

| nl | key | control | behaviour |
|---|---|---|---|
| Klantcode | ProductRelationCode | lookup (create inline) | fills product / transport unit |
| Aantal | Amount | number | blur → positions = amount; recalculates EPQ/LM/load factor from unit conversion factors |
| Transporteenheid / ladingdrager | TransportUnit | lookup (create inline) | defaults EPQ, loading meters, load factor via conversion factors; transaction times |
| Soort voertuig | VehicleType | autocomplete (create inline) | |
| Produkt | Product | lookup (create inline) | default amounts, transaction times |
| Transportvereisten | RequiredCapabilities | multi lookup | |
| Posities, Colli, Gewicht, EPQ, Laadmeters, Beladingsgraad, Volume, Afstand | number | | |
| Duur | Duration | text `DD.HH:MM` with validator | |
| Geschatte CO₂ | EstimatedCarbonFootprint | text + spinner | only when a transport segment is configured in tenant settings |
| Opmerkingen, Lengte, Breedte, Hoogte, Barcode, Ref. factuur, Gewogen volume 1-3, Gewogen gewicht 1-3, ISO, Speciale overeenkomsten, Interne opmerkingen, Instructies afzender, Rederij, Verzender (relation lookup), Containernummer, Bijgevoegde documenten, Pinecode | text/number | | **bug (fact):** the Width field is shown under `isVisibleField('height')` and Height under `isVisibleField('width')` |
| Frankeringsvoorschrift | FreightPaymentInstruction | select | |
| Vlaggen | Flags | flags fieldset | |
| custom freight properties | | | |
| **Toevoegen** | AddNewFreight | button / Enter key | adds the line, waypoints are copied per freight, legs generated |

**Freight grid** (`transport-order-freight`, ag-grid, client side; columns from server attributes, i.e. every
`TransportOrderFreightDto` field incl. origin*/destination* waypoint columns and `freightPrice`). Inline editable cells:
amount, destinationDate, distance, loadFactor, width, colli, containerNumber, duration, epq, weight, height,
loadingMeters, length, remarks, product, temperatureRange, originTimeSlot, destinationTimeSlot, transportUnit,
originDate, volume, customProperty0-9; others show toast "Het is niet mogelijk om deze waarde direct in de cell te
wijzigen…". Pinned left edit column (pencil) opens the **edit-order-freight dialog**; context menu Bewerken / Verwijderen
(delete hidden in states 2 and 4; confirm text "Weet u zeker dat u deze transportopdracht wilt verwijderen?" — wrong
wording for a freight line). Deleting a freight removes its waypoints, legs and prices and re-syncs freight stops.

**Edit-order-freight dialog** (`dialogs/edit-order-freight-dialog`, 1245×700, `disableClose`, old `entity-operations-bar`
type TransportOrderFreight: Save / Cancel / Close). Outer tabs: **Vrachtdetails**, **Trajecten**, **Tarieven**, plus three
disabled icon tabs. Vrachtdetails holds three tab groups: (Lading: transport unit, soort voertuig select, klantcode,
produkt, temperatuur select, barcode, transportvereisten, ref. factuur · Vrachtbrief: frankeringsvoorschrift, bijgevoegde
documenten, instructies afzender, speciale overeenkomsten, verzender · Container: rederij, ISO, containernummer, pinecode ·
Opmerkingen · Eigen Velden) + (Aantallen: aantal…hoogte, geschatte CO₂, vlaggen · Metingen: gewogen volume/gewicht 1-3) +
(**Stops** = waypoints strip per freight, +/− with min 2). Rule (fact): any waypoint relation/type change calls
`generateDefaultTransportOrderFreightLegs` and **rebuilds the legs** in the Trajecten tab, then recalculates distance,
duration and prices. **Trajecten** = `planboard/manage-legs`: a form list of legs, each with Vertrekdatum, Activiteit,
Vertreklokatie, Vertrektijdslot, Laadreferentie, Opmerkingen van herkomstlokatie, Aankomstdatum, Activiteit,
Eindbestemming, Aankomsttijd (timeslot), Losreferentie, Opmerkingen van bestemming, Afstand, Duur, Afdelingen, Vlaggen;
add/delete rows; neighbouring legs are kept consistent. **Tarieven** = `transport-order-freight-price-panel`: two
mat-tables **Verkooptarieven** and **Inkooptarieven** (same columns: + / delete, Prijsspecificatie select (Prijs per
stuk/positie/colli/M3/KG/EPQ/LM, op basis van beladingsgraad, per KM, per uur, soort voertuig, Vast bedrag),
Vrachtdossier (purchase: pick the file the cost belongs to), Opmerkingen, Omschrijving, Aantal, Stukprijs, Bedrag
(readonly = amount×unit), footer Totaal). Manual add/edit/delete; `amount` of a new row is pre-filled from the freight
field matching the specification. Read-only when the order has a fixed price. Purchase table shows a warn icon when
purchase prices are stale. There is **no explicit requery button in the panel**; prices are recalculated by the parent on
waypoint/amount changes and by the Financieel tab's recalculate event (`recalculatePricesEvent`) **(inferred from wiring)**.

**1b. Additionele diensten**: entry row Datum, Omschrijving (autocomplete from the additional-services master list,
fills ledger/cost center/unit price), Grootboekrekening, Kostenplaats, Aantal, Stukprijs, Bedrag (= aantal×stukprijs),
add icon; below an ag-grid of services (columns = DTO: date, ledgerAccount, costCenter, description, amount, unitPrice,
price), context menu Verwijderen.
**1c. Opmerkingen voor planner** (`InternalTransportOrderRemarks`): rich-text editor.
**1d. Overige referenties**: table add/delete rows: Type referentie (required), Referentie, Levering (`ReferenceDescription`).
**1e. Eigen Velden** (only when non-prominent custom properties exist).

**2. Financieel** (`financial.component`)

| Block (nl) | Fields |
|---|---|
| Prijsberekening | radio **"Deze transportopdracht heeft een vast bedrag…"** + Vast bedrag (number) vs **"Vrachtregels … zijn individueel geprijst."**; then 12 slide toggles (Prijs op basis van aantallen / colli / EPQ / gewicht / afstand / posities / Vaste prijs voor vrachtregel / volume / laadmeters / beladingsgraad / reistijd / soort voertuig), each with a **Prijslijst** lookup enabled only when its toggle is on; toggles disabled when fixed price; "Bij meerdere tarieven, enkel het hoogste tarief toepassen" toggle |
| Omzetoverzicht | read-only lines: Vast bedrag, Vrachtregel prijs, Additionele diensten, Korting en/of toeslag, **Totale Omzet** (D&S are evaluated client-side incl. weekday / geographical-region rules and `applyAfterOtherDiscountAndSurcharges`) |
| Facturatie | Valuta (select), BTW percentage (select), Korting en/of toeslag (multi-select; hidden on templates; relation defaults merged with manual picks), Betalingstermijn (number), "Stuur vrachtdocumenten mee met factuur" toggle |
| Opmerkingen m.b.t. facturatie | rich-text `internalInvoicingRemarks` |

Defaults: all of these come from `getFinancialDefaults(orderDate, relationId)` when the relation (or order date)
changes. Changing a price list fires `recalculatePricesEvent` → the parent re-queries freight prices.

**3. Facturen** (not on template): read-only invoices grid (`Finance.Invoice` block) for this order.
**4. Callbacks** *(no nl)*: grid of `TransportOrderCallbackDto` rows (auto-saved, debounce 600 ms) — webhook-style
callbacks per order **(inferred)**.
**5. Schema** (template only): **Herhalen**: Herhalen iedere *n* [Week/Maand]; Week → day buttons Mon–Sun; Maand → Op dag
(1–31); plus a nested **Vrachtdossier** panel (same component as below).
**6. Vrachtdossier** (`AssignTransportFile`, not on template) — the "Tevens vrachtdossier" tab:

| nl | key / control | enabled when |
|---|---|---|
| Subtitle "In het geval dat u direct een vrachtdossier wilt aanmaken wanneer dit sjabloon wordt omgezet…" | static text (written for templates, shown on every order) | always |
| **"Maak samen met deze transportopdracht een transportbestand aan"** | slide toggle `createTransportFileAlongWithTemplate` | always; switching off nulls all fields below |
| Naam | text (writes `templateName`) | toggle on |
| Soort vrachtdossier | select with clear icon | toggle on, not when a file already exists |
| Vervoerder | relation lookup (category transport company) | toggle on |
| Inkoopprijs | number 2 dec. | toggle on and carrier is not "me" and the linked file has ≤1 incoming invoice |
| Contactpersoon | contact lookup | carrier chosen |
| Korting en/of toeslag | select (per-km ones excluded) + hint "Inkoopprijs na korting/toeslag: {0}" | buying price enabled |
| Trekkende eenheid / Oplegger / Aanhanger | vehicle-select ×3 | toggle on; choosing the primary vehicle sets carrier, default trailer(s), default drivers, departure/arrival from stationing |
| Vertreklokatie + Gepland vertrek, Eindbestemming + Geplande aankomsttijd | relation lookups + datetime | toggle on; defaults from vehicle stationing → file type → default file type |
| Chauffeurs | multi relation lookup | toggle on |
| **Stops schema** (`freight-stops-schema`) | table: #, up/down, Activiteit, Relatie, Timeslot, Referenties, Opmerkingen; "+" adds a manual stop per waypoint type; derived stops are read-only | toggle on |
| second sub-tab (template scheduler only wraps the same component) | | |
| "Transportopdracht invullen op basis van activiteiten dat de boordcomputer registreert" | slide toggle `transportFileFillsTransportOrder` + Begin/Stop met invullen vanaf activiteit (waypoint type selects) | FMS flag on |

What it creates (fact from `buildTransportOrderEditDtoEntity` + service): on save the order DTO carries
`freightStops` (derived from legs, de-duplicated unless the file type's `defaultStopCreationMode` = 1, plus manual stops)
and the `templateTransportFile*` fields; the **backend** creates/updates the linked transport file. When the order already
has a file, the file type is locked and the buying price is shown from the file's incoming invoice.

**7–9. Icon tabs**: Bijlagen (`documents`), Opmerkingen (`remarks`, used also by Herzien to store the reason),
Record Identification. **10. hidden** print-or-download tab (see §4).

**Goedkeuren dialog** (`approve-transport-order-dialog`, 500px): title "Transportopdracht goedkeuren voor facturatie" or
"Transportopdracht archiveren"; first validates (`validateApprove`, polled background job) and shows **aandachtspunten**
(warnings), **fouten** (blocking) and "Nadat deze transportopdracht is goedgekeurd, is de order status: …"; Ok runs the
approve job and closes the order. **Herzien**: confirm-with-input "Waarom wilt u deze transportopdracht herzien?" → remark
saved, state back to Open. **Accepteren dialog** (`dialogs/accept-transport-order`): "Wilt u de geselecteerde
transportopdrachten accepteren?", toggle **Opdrachtbevestiging versturen** with E-mailsjabloon and Bestandlayouts selects.

---

## 3. Transport file details (`transport_file/transport-file-details/`)

Header "Vrachtdossier: <nr>|Nieuw", old `entity-operations-bar` type TransportFile: **Opslaan** (disabled when archived
or form invalid), **Annuleren**, **Archief / Terughalen uit archief**, other: Afdrukken & Downloaden, Send email *(no nl)*,
**Inkoopprijzen vernieuwen** (only when the file has legs), captions: *Gearchiveerd*, "Load rate: n%" (red >100),
"n Laadmeters (max. m)" (red when over). Archived files are read-only on most fields.

**Algemeen**

| nl | control | notes |
|---|---|---|
| Dossiernaam, Afdelingen, Dossiernummer (readonly), Dossierdatum | text/select/date | |
| Vertreklokatie, Gepland vertrek, Werkelijke vertrektijd, Geplande afstand, Geschatte CO₂ | lookup / datetime ×2 / number ×2 | changing distance recalculates incoming invoices (costs) |
| Eindbestemming, Geplande aankomsttijd, Werkelijke aankomsttijd, Werkelijke afstand, Werkelijke CO₂ | | |
| prominent custom properties | | |
| Vervoerder + Contactpersoon | lookups | carrier change recalculates incoming invoices, clears contact |
| Trekkende eenheid, Oplegger, Aanhanger | vehicle-select ×3 | default trailers/drivers from primary vehicle |
| Chauffeurs | multi lookup | |

Nested tabs: **Vrachtregels** (legs grid `planboard/transport-file-legs`, ag-grid with server attributes = every
`GetAllTransportFileLegListDto` column: order, origin/destination waypoint type, relation, timeslot, reference, remarks,
dates, amounts, cost…; inline-editable: transportOrderName, transportOrderReference, origin/destination reference and
remarks, both timeslots, transport unit, product, temperature range; double-click a non-editable cell opens the order;
context menu Bewerken, **Voer dit traject separaat uit**, **Vertrekactiviteit uitbesteden**, **Retouropdracht maken**) ·
**Stops** (`file-stops`: a card list, not a grid — default columns relation (bold) + waypoint type on row 1, order number
on row 2, configurable via "Kolommen weergeven of verbergen" from all `TransportFileStopDto` fields incl. cumulative
amounts, calculated/estimated/actual times; drag-and-drop reorder (disabled once actual times exist; auto ETA recalculation
if the file has `recalculateOptimalRouteAutomaticly`); toolbar: add stop per waypoint type, **Geschatte aankomsttijden
berekenen**, **Stops optimaliseren** (both with route-definition dropdown), Opties (columns, Excel), Vernieuwen; warning
icons with tooltip; context menu Voltooien / Delete / Vertrekactiviteit uitbesteden; double-click → **stop details dialog**
(1300×520): Activiteit (readonly), Naam (relation lookup; locked when the stop has a leg), Opmerkingen, read-only address
and contact fields, Berekende/Geschatte/Werkelijke aankomst- en vertrektijd + cumulatieve afstand, **Google map**
(`stops-map`, one marker), tabs Trajecten (legs of this stop), **Vragenpad** (questionnaire results: Tijdstip, Sleutel,
ValueType, Waarde with file download), attachments, notes, record id) · **Opmerkingen aan chauffeur** (rich text) ·
**Interne opmerkingen** (rich text) · **Eigen Velden**.

**Kosten** → **Inkomende facturen** (`incoming-invoices`, out of scope: one line — purchase lines per carrier relation,
recalculated server-side from legs/distance/carrier). **Sensormetingen** (deferred chart of vehicle sensor events over the
trip window). Icon tabs: Bijlagen, Opmerkingen, Record Identification; hidden print-or-download tab.
FMS fields (out of scope, one line): `isPublishedToFleetManagementSystem`, `fleetManagementSystemDeviceStatus`,
`transportFileFMSSourced` exist on the DTO/grid but are not editable in this screen.

**Meerdere vrachtdossiers aanmaken** (`generate-transport-files-dialog`, 660px): "Vrachtdossiers met de volgende
eigenschappen aanmaken": Dossierdatum, Soort vrachtdossier (autocomplete; sets carrier, relations, default departure
time), Naam, Afdelingen, Vervoerder (+Inkoopprijs, Contactpersoon), Trekkende eenheid/Oplegger/Aanhanger, Vertreklokatie +
Gepland vertrek, Eindbestemming + Geplande aankomsttijd; then "Hoeveel vrachtdossiers wilt u aanmaken?" radio: **aantal**
(1–99) / **ieder kenteken van soort voertuig** / **de volgende voertuigen** (multi-select) / **ieder voertuig gestationeerd
op depot** (Standplaats lookup); toggle **Vrachtdossiers zijn boordcomputer-gevoed** + Opdrachtgever (also creates one
transport order per file with that customer's financial defaults). Ok creates N files (and orders when FMS-sourced) and
refreshes the list; warns when a vehicle type/stationing has no vehicles.
**Publish dialog** (one line): confirm "PublishToFleetManagementDevice…" Ok/Annuleren → publish to on-board computer
(opened from the planboard, not from this app). **Push-to-vehicle** (one line): "Wilt u deze vrachtdossiers naar uw
boordcomputers versturen?", radio Direct verzenden / Later verzenden + "Verstuur naar boordcomputer om" datetime; shows
unsupported file numbers.

---

## 4. Wizards in scope

**Van sjabloon / Nieuw van sjabloon / Copy** (`dialogs/generate-transport-order-wizard`, 98vw×95vh, linear stepper):
1 Welkom · 2 **Selecteer sjabloon**: radio "Gebruik een sjabloon…" / "Gebruik een bestaande transportopdracht…";
server-side grid of templates (or open orders with date range) multi-select, max 10 ("U kunt maximaal 10
transportopdracht-sjablonen tegelijk verwerken"); when launched from a row the source is preselected ·
3 **Herhalen** "Hoeveel transportopdrachten wilt u maken?": mode **Per weekdag** (Periode range, default 14 days, checkbox
per weekday + number of copies) or **Aangepaste dagen** (month calendar, click days, copies per day) ·
4 **Vrachtdetails** "Zorg ervoor dat de orderdatum(s), -referenties en andere details correct zijn ingevuld": master/detail
grid of generated orders, editable Orderreferentie, Aantal, Gewicht, Opmerkingen, Ref. factuur · 5 **Bevestig**: task list
with per-order progress; creates via `createOrUpdate` per order and refreshes the grid.

**Vrachtdossiers Archiveren** (`archive-transport-files-wizard`): Welkom · Vrachtdossiers (multi-select file grid incl.
select-all, own saved view `Wizard.ArchiveTransportFiles.SelectFiles`) · Bevestig ("De volgende vrachtdossiers zullen
worden gearchiveerd" + list, task list per file, Invoeren).

**Transportopdrachten herverdelen** (`redistribute-transport-orders-wizard`): Welkom · Transportopdrachten (select open
orders of **the same customer**, "Toon alle open transportopdrachten" / within date interval) · "Redistribute by"
(select a freight attribute from `getTransportOrderFreightAttributes`) · Bevestig: preview grid of freight items grouped
by the attribute value, checkbox "Archive transport orders that are left empty" (English only), Invoeren →
`redistributeByTransportOrderFreight`. Several texts are hard-coded English.

**Boordcomputer-gevoede vrachtdossiers herverdelen** (`redistributing-transport-file-stops-wizard`): Welkom · select one
FMS-sourced file · **Stops herverdelen**: the file's stop list with checkboxes, drag selected successive stops onto an
existing open order ("Select orders" dialog) or onto "Sleep hier naartoe om een nieuwe transportopdracht te maken";
per target: Ordernr./Nieuw, Opdrachtgever, Orderdatum, Orderreferentie, Totale afstand, Total hours, warning "Stops zijn
niet opeenvolgend" · Finalize *(no nl)*: master/detail preview, "Sluit de wizard niet, maar begin opnieuw".

**CMR Afdrukken** (`dialogs/print-cmr-dialog`, 850px, from order or file context menu): **CMR profielen** ng-select with
add (dialog "CMR profiel naam") and delete; "Selecteer welke informatie u in de verschillende CMR velden wilt tonen.
Wijzigingen zullen worden opgeslagen in het geselecteerde CMR profiel." **Veldconfiguratie**: Aantal delen (1–5, default
4), toggles Arcering toevoegen, Groepeer op laad- en losadres, QR-code weergeven; one picker per CMR box: Vak 1 Afzender,
Vak 2 Geadresseerde, Vak 3 Plaats aflevering, Vak 4 Plaats inontvangstneming, Vak 6 Goederen (multi), Vak 13 Instructies
afzender (multi), Vak 16 Vervoerder, Vak 17 Opvolgende vervoerders, Vak 18 Opmerkingen van de vervoerder (multi), Vak 21
Opgemaakt op, Vak 22/23 handtekeningen — options are address fields, goods-description, shipper-instruction and
carrier-remark field options from the server (the "fixed descriptions"). **Vrachtregels**: checkbox table of the file's
legs (`freights-selectable-table`) or the order's lines (`orders-selectable-table`) to include. Buttons **Afdrukken**,
**Downloaden**, **E-mail versturen** (disabled for multi-file). **Afdrukken & Downloaden tab** (`print-or-download`):
Documenten: toggle group of export-file definitions (report layouts) for TransportOrder/TransportFile, Afdrukken /
Downloaden; **Weergave**: embedded PDF preview ("Weergave niet beschikbaar" for non-PDF). An open order is auto-saved
first (`printOrDownloadAction`).

---

## 5. Lookups and pickers

| Picker | Component | Create in place? |
|---|---|---|
| Relation (customer, carrier, departure/arrival, drivers, shipper) | `global/relation-select-dropdown` (ng-select `addTag`) | **Yes** → relation dialog, pre-set category/waypoint type; pencil opens edit |
| Contact person | `contact-person-select-dropdown` | **Yes** (needs relation) |
| Product, Transport unit, Product relation code (Klantcode), Price list | `*-select-dropdown` via `ModalDialogService.dialogs.new*` | **Yes** |
| Vehicle (3 slots) | `vehicle-select` | **Yes** ("AddNewVehicle" → vehicles dialog) |
| Waypoint type (Activiteit), Time slot | autocomplete + `create-inline` | **Yes** (inline mini-form, metadata from `Filogic.InlineCreationMetadata.*`) |
| Vehicle type (new-freight form) | autocomplete + `create-inline` | **Yes** (inline); in the freight dialog it is a plain select: **No** |
| Temperature range, Capabilities, D&S, Currency, VAT, Order type, File type, Organisation unit, Payment instruction | mat-select / autocomplete | **No** |
| CMR profile | ng-select + add dialog | **Yes** (name only) |
| Address | not a picker: address comes from the relation; stop dialog shows it read-only | — |

---

## 6. UX problems visible in the code

1. **Vrachtdossier tab** (`transport-order-assigned-file*`): template-oriented copy shown on every order ("…wanneer dit
   sjabloon wordt omgezet…"), a toggle whose label says "transportbestand", 15 fields that silently null themselves when the
   toggle goes off, hidden defaults cascade (vehicle → carrier → relations → drivers), buying-price rules (carrier is me,
   >1 incoming invoice) only visible as disabled state/tooltip in English, and the stop schema whose ordering rule is
   enforced by disabled arrows without explanation. The same component is also embedded in the template **Schema** tab.
2. **Overloaded General tab** (`transport.order.details.component.html`, 1318 lines): header, waypoint strip, a 40-field
   freight entry form, the freight grid and a second-level tab group all on one tab; freight-field visibility is driven
   by the order type so the form shape changes per type. Width/Height visibility flags are swapped (bug).
3. **Duplicated inputs**: freight fields exist three times (new-freight form, inline grid cells, edit dialog with four
   tab groups); waypoints exist on the order strip and per freight in the dialog; legs are regenerated on every waypoint
   change, discarding manual leg edits (`edit-order-freight-dialog.getDefaultLegs`).
4. **Modal stacking**: bootstrap order modal → material freight dialog (1245px) → inline create dialogs → confirm dialogs;
   the code manipulates overlay DOM to hide wizards behind the order modal (`hideGenerateInvoiceWizard`) and tracks
   `isEditOrConfirmDialogOpen` to stop Esc from closing the parent.
5. **Hidden rules / unclear states**: Close button never appears (`formState` always `hasChanges`); no unsaved-changes
   warning on X/Esc; Save disabled without saying why (no relation / invalid duration format); the freight-line delete
   confirmation uses the *order* text; "Save & Close", "Save only", Copy, Templates, Callbacks, Timeslot have no Dutch text;
   several wizard texts are hard-coded English.
6. **Financial tab** (`financial.component`): 12 toggle+lookup pairs in one column; the fixed-price radio disables them
   all; discounts/surcharges are recalculated client-side from weekday/region rules the user cannot see.
7. **Command bar is not selection aware**: edit/delete/print/archive live only in the right-click menu; "Nieuw ▸
   Vrachtdossier ▸ type" creates a record instantly without a form.
8. **Stops as a card list** (`file-stops`) with its own column-visibility dialog and setting key, unlike every other grid;
   drag-reorder silently triggers a route recalculation job.
9. **Print/Download as a hidden tab** toggled from the ribbon (`printOrDownloadPosition`) rather than a dialog.

---

## 7. Permission keys and status labels

Permissions checked in these screens (route data + `TransportComponent.setPermissionList`):
`Applications.OpenTmsApp.TransportApp`, `.TransportOrders`, `.TransportOrdersPendingAcceptance`, `.TransportOrdersToday`,
`.TransportOrdersTodayAndTomorrow`, `.TransportOrdersThisWeek`, `.TransportOrdersOpen`, `.TransportOrdersAll`,
`.TransportOrdersTemplates`, `.TransportFiles`, `.TransportFilesToday`, `.TransportFilesThisWeek`, `.TransportFilesOpen`,
`.TransportFilesAll`; grid chrome: `Applications.Administration.Tenant.ApplyGridProfileToAllUsers`,
`…ApplyGridProfileToAllTenants`. Feature: `FiLogicAndFriends.ChainFill.AiPdfImport`. No `isGranted` in the details
components themselves (fact): all per-action gating is state-based, not permission-based.

Order state (`global/order-state.ts`, bit flags, `OrderState<n>`): −1 **Te accepteren**, 0 **Open**, 1 **Geannuleerd**,
2 **Klaar voor facturatie** (menu text: Goedgekeurd voor facturatie), 4 **Gefactureerd**, 8 **Gearchiveerd**.
Task status (`TaskStatus<n>`, orders and files): 0 Onbekend, 1 Aangevraagd, 2 Geaccepteerd, 3 Gepland, 4 Onderweg,
5 Afgeleverd. File status is otherwise shown as booleans: Gearchiveerd caption, `isCanceled`,
`isPublishedToFleetManagementSystem`, `fleetManagementSystemDeviceStatus` (−1,0,1,2,3,5,6; no nl texts found).

---

## 8. Functional scenario catalogue (UI point of view)

F = fact from code, I = inferred.

1. Order clerk — opens Transport app — lands on Transportopdrachten / Vandaag grid (default filter). F
2. Order clerk — clicks Te accepteren — grid switches to multi-select, toolbar shows Accepteren when >1 selected. F
3. Order clerk — Nieuw ▸ Transportopdracht ▸ type — order modal opens with that type's default waypoints, fields and required flags. F
4. Order clerk — picks a Relatie — first waypoint relation, financial defaults (price lists, VAT, currency, payment term, D&S) are filled; Save becomes enabled. F
5. Order clerk — changes Orderdatum — relation defaults and weekday D&S are re-evaluated; outdated defaults removed, manual D&S kept. F
6. Order clerk — fills all waypoint relations — distance and duration of the new freight are queried automatically. F
7. Order clerk — types Aantal and picks a Transporteenheid — positions, EPQ, laadmeters, beladingsgraad computed from conversion factors. F
8. Order clerk — presses Enter in the freight form — line is added (if valid and no dropdown open), waypoints copied, legs generated, prices re-queried. F
9. Order clerk — edits Gewicht in the freight grid cell — value saved in memory and prices recalculated. F
10. Order clerk — double-clicks a non-editable cell — toast tells to use Bewerken. F
11. Order clerk — opens the pencil on a line — freight dialog; changes a waypoint relation — legs are regenerated and distance/prices recalculated. F
12. Order clerk — in Tarieven adds a Verkooptarief line "Prijs per KG" — amount prefilled with the freight weight, Bedrag = aantal × stukprijs. F
13. Order clerk — ticks fixed price on Financieel — all chargeBy toggles disabled, Tarieven tables become read-only. F
14. Order clerk — selects a Prijslijst for "Prijs op basis van gewicht" — order prices are re-queried server-side. F
15. Order clerk — adds an Additionele dienst by description — ledger, cost center, unit price prefilled from master data; appears in Omzetoverzicht. F
16. Order clerk — ticks "Maak samen met deze transportopdracht een transportbestand aan" and picks a Vervoerder — on save the order is stored with freight stops and the backend creates the linked transport file with those stops. F (creation server-side: I)
17. Order clerk — picks a Trekkende eenheid in the Vrachtdossier tab — carrier, default Oplegger/Aanhanger, drivers and depot relations are filled. F
18. Order clerk — selects a carrier that "is me" — Inkoopprijs and D&S are cleared and disabled. F
19. Order clerk — adds a manual stop in the stop schema — appended; move arrows blocked when a leg's origin would end after its destination. F
20. Order clerk — switches the toggle off — all file fields are nulled; nothing is deleted until save. F; file handling on save: I
21. Order clerk — Save only on a new order — order persisted, number assigned, title updated, grid refreshed, modal stays open. F
22. Order clerk — Save & Close — saved and closed; Esc/X closes without a prompt even with changes. F
23. Order clerk — Afdrukken & Downloaden — open order auto-saved, hidden tab shows layouts + PDF preview, print/download. F
24. Order clerk — CMR Afdrukken from the grid — chooses profile, box contents, lines; prints or downloads a CMR. F
25. Order clerk — Copy from the grid — generate wizard opens with that order preselected as source. F
26. Order clerk — Nieuw ▸ Nieuw sjabloon — template modal: name instead of order number, Schema tab with weekly/monthly recurrence; no D&S. F
27. Order clerk — Van sjabloon wizard, Per weekdag Mon/Wed ×2 for two weeks — 8 orders previewed, references editable, created on Bevestig. F
28. Order clerk — Transportopdrachten herverdelen — picks same-customer open orders and an attribute; freight items split into new orders; empty ones optionally archived. F
29. Order clerk (pending acceptance) — Accepteren with "Opdrachtbevestiging versturen" — orders accepted, confirmation mailed with chosen template/layout. F
30. Planner/Finance — Goedkeuren ▸ Goedkeuren voor Facturatie — validation shows warnings/errors and target state; Ok sets state 2 and closes. F
31. Finance — Goedkeuren ▸ Direct archiveren — same dialog in archive mode; state 8. F
32. Finance — Herzien on an approved order — reason required, stored as remark; state back to Open; fields editable again. F
33. Anyone — opens an order with state > 0 — every field read-only, Save disabled; context-menu delete hidden for states 2/4. F
34. Order clerk — Verwijderen on an order row — confirm "Weet je zeker…", order deleted, grid refreshed. F
35. Planner — Nieuw ▸ Vrachtdossier ▸ type — a file is created immediately with the type's defaults and appears in the list. F
36. Planner — Meerdere vrachtdossiers aanmaken, "ieder voertuig gestationeerd op depot" — one file per vehicle at that depot with vehicle, trailers, drivers; warning if none. F
37. Planner — same dialog with "boordcomputer-gevoed" + Opdrachtgever — one order per file is created too. F
38. Planner — opens a file — Algemeen shows header, carrier, vehicles; legs grid, stops list, driver remarks. F
39. Planner — changes Vervoerder or Geplande afstand — incoming invoices (purchase costs) recalculated. F
40. Planner — Inkoopprijzen vernieuwen — purchase prices refreshed from the file's legs. F
41. Planner — drags a stop to a new position — order saved; ETA recalculated when auto-route is on; costs refreshed. F
42. Planner — Stops optimaliseren / Geschatte aankomsttijden berekenen — route job runs, success toast, costs refreshed. F
43. Planner — double-clicks a stop — stop dialog with address, calculated/estimated/actual times, map, questionnaire answers, legs. F
44. Planner — context menu Voltooien on a stop — stop completed (actual times set). I
45. Planner — Vertrekactiviteit uitbesteden on a leg/stop — origin activity outsourced to another file/carrier. I
46. Planner — Retouropdracht maken on a leg — a return order is created from that leg. I
47. Planner — edits Laadreferentie inline in the legs grid — value saved to the leg. F
48. Planner — Archief in the file ribbon — confirm, file archived and closed; Terughalen uit archief reverses. F
49. Planner — E-mail versturen from grid or ribbon — email dialog for the file. F
50. Planner — Vrachtdossiers Archiveren wizard — multi-select files, confirm, per-file task list. F
51. Planner — Boordcomputer-gevoede vrachtdossiers herverdelen — select FMS file, drag successive stops into existing/new orders, confirm. F
52. Planner — Push to vehicle (planboard) — chooses now/later, files sent to the on-board computer. F
53. Any user — groups the grid by Relatie via the Columns panel — server-side groups with counts; state saved per user. F
54. Admin — gear ▸ Instellen voor alle gebruikers — current column layout pushed to all users (permission required). F
55. Any user — export icon — Excel export runs as a background job and downloads when ready. F
56. Any user — flag icon in a row's context menu — flag dialog sets order/file flags shown in the flags column. F
