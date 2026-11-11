# Design 74: AI connector — opentms-next as a custom connector in a user's AI tool

**PBI:** Marinus, 2026-11-05 (chat): a user adds opentms-next as a custom connector in Claude, ChatGPT, Gemini, Cowork and
similar tools, and signs in to opentms-next from there with the correct tenant scope. The connection then stays
established, and the AI tool answers questions or runs tasks based on data from opentms-next.
**Status:** built; gate 1 and 2 with the PR. Risk class **critical** (authorization server, tenant isolation, a new public endpoint).
**Date:** 2026-11-05

## PO decisions (2026-11-05)

Recorded as AD-22 in `docs/architecture/decisions.md`; it closes AP-01 and the MCP part of AO-01.

1. **Read-only first.** "Run tasks" means analysing, summarising, comparing and drafting from TMS data in the AI tool.
   Changes in the TMS come in a later design, with a confirmation step.
2. **Only approved AI tools can connect:** Claude (including Claude Code and Cowork if testing confirms they work),
   ChatGPT and Gemini. Adding a tool is a configuration change, not open registration.
3. **Money only through `get_order_rates` and `get_customer_revenue`** (added by the PO on 2026-11-06), which both require
   `Financial.RateLines`. Amounts are removed from the order, trip and leg tools. Design 75 adds `list_metrics` and
   `query_metrics`, whose money metrics are behind the same permission.
4. **Minimal personal data:** no driver names, contact persons, e-mail or phone numbers, remarks or driver instructions.
   Company names, cities and licence plates stay. The tenant administrator accepts the data sharing when switching the
   connector on.
5. **Nine fixed read tools,** behind feature `Apps.Integrations.AiConnector` (off by default per tenant) and permission
   `Integrations.AiConnector.Use`. One standard connector for all tools; Gemini is set up by an administrator.

6. **Connections are visible and can be switched off in the Integrations app** (added by the PO on 2026-11-06). Every
   connected AI tool shows there as live, so a tenant always knows which integrations are live, and it can be switched
   off from the same screen.

**PO instruction, 2026-11-06:** build designs 74 and 75 into one PR ready for production; where a question comes up, take
the best suggestion and state it in the PR description with its reasoning. Gate 2 (merge) stays with the PO.

**For gate 1: added by this design, beyond the five decisions.** Each follows from them, but none was put to the PO:
permission `Integrations.AiConnector.Manage` and the screen it guards, the entity `AiConnection`, the registration
endpoint that hands out a fixed client per tool, the rate limit, the fields left out under *Tool contract* beyond the
categories named in decision 4 (for example VIN, KvK and VAT numbers, PIN codes), and the definition of revenue under
`get_customer_revenue`.

## The flow

1. The user adds the connector URL (`https://<api host>/mcp`) in the AI tool. Gemini: an administrator enters the URL
   and a pre-registered client instead.
2. The AI tool calls `/mcp`, gets a 401 with `WWW-Authenticate` pointing to the protected-resource metadata, and finds
   the authorization server.
3. The AI tool registers at `/connect/register` and receives the fixed client of that tool, or a refusal.
4. The browser opens `/connect/authorize` (authorization code with PKCE S256). A user who is not signed in gets the
   normal sign-in page of design 62, and the tenant follows from the credentials. A user who exists in several places
   chooses the organisation.
5. The **connect screen** always appears: "Connect Claude to organisation X as user Y". Allow creates the connection,
   Deny returns `access_denied`, and "Use a different account" signs out of the session and restarts the request.
6. The AI tool gets an access token and a refresh token for that user and tenant, and renews by itself.
7. Every tool call runs as that user in that tenant, through the existing application services.

## Domain impact

**Architecture references.** AD-22 (this connector), AD-18 (application services are the command layer), AD-03 (a
database per tenant), AD-21 does **not** apply (no model call is made by us). Rules for any agent design in `06` apply:
no database access, the same validation and permissions, a flag per tenant. **Flows:** this feature extends none of the
three flows in `02-flows.md`, because it only reads and sends no command. Write tools in a later design extend flow 1.

**Module home: Integrations** (the domain map's "partner links; anti-corruption layer").

| Part | Project |
|---|---|
| Tools, tool DTOs and field allowlists; `AiConnection`; connector status and connection endpoints | Integrations (`Domain`, `Application.Contracts`, `Application`, `EntityFrameworkCore`) |
| Permissions `Integrations.AiConnector.Use`, `Integrations.AiConnector.Manage` | `OpenTms.Integrations.Application.Contracts/Permissions/` |
| Feature `Apps.Integrations.AiConnector` | Platform `AppFeatures` (the tree of features lives there; no behaviour) |
| `/mcp` mapping, metadata, `/connect/register`, connect screen, token handling | `OpenTms.HttpApi.Host`, next to the sign-in pages of design 62 |
| Seeding of the approved clients and the scope | `OpenTms.Domain` `OpenIddictDataSeedContributor`, run by the DbMigrator |

**Cross-module synchronous calls (module boundary rule 3): stated here on purpose.** The tools in Integrations call these
published interfaces of other modules, read methods only:

| Interface | Project Integrations now references |
|---|---|
| `ITransportOrderAppService` | `OpenTms.Orders.Application.Contracts` (already referenced) |
| `ITransportTripAppService`, `ILegAppService` | `OpenTms.PlanningExecution.Application.Contracts` (new) |
| `IRelationAppService`, `IVehicleAppService` | `OpenTms.MasterData.Application.Contracts` (new) |
| `IRateLineAppService` | `OpenTms.Financial.Application.Contracts` (new) |
| `ICustomerRevenueReader` (new, see `get_customer_revenue`) | `OpenTms.Orders.Application.Contracts` (already referenced) |

Each call runs through ABP's interceptors, so each service's own `[Authorize]`, `[RequiresFeature]` and tenant filter
apply. A read in another module's service may not be bypassed, and no tool reads a repository.

**New entity `AiConnection`** (`integrations."AiConnections"`, in the tenant's database; `IMultiTenant`):

| Field | Type | Note |
|---|---|---|
| `Id` | `Guid` | |
| `TenantId` | `Guid?` | |
| `UserId` | `Guid` | The user who connected |
| `ClientId` | `string(100)` | Fixed client, for example `OpenTms_Mcp_Claude` |
| `AuthorizationId` | `string(100)` | The OpenIddict authorization, unique |
| `ConnectedAt` | `DateTime` | |
| `LastUsedAt` | `DateTime?` | Updated at most once per five minutes per connection |
| `RevokedAt` | `DateTime?` | |
| `RevokedByUserId` | `Guid?` | |

The host writes and reads this entity only through `IAiConnectionChecker` and `IAiConnectionRecorder` (`FindReusable` and
`TryRecord` from the connect screen), both published in Integrations `Application.Contracts`.

**Why this entity exists.** Measured on the local database: tokens and authorizations of a signed-in tenant user are
stored in the **host** database, and the tenant database's tables are empty. Those records name a user but no tenant.
A tenant-owned record gives the tenant administrator a list without a cross-database read, and gives `/mcp` a revocation
check in the tenant's own database.

**Acceptance of data sharing:** two tenant settings, `Integrations.AiConnector.AcceptedAt` and
`Integrations.AiConnector.AcceptedByUserId`, not visible to clients other than through the status endpoint below.

**Events:** none published. One consumed: ABP's local `EntityChangedEventData<IdentityUser>` (see *GDPR*). AO-01's
commands and events stay open.

**Out of scope:** write tools; the in-app chat (AP-02) and a Chat app (AO-04); commands and events in AO-01; order
intake, inbound messages and documents; drivers; exports; users and tenants; Reporting (it has no endpoints yet);
metering (the customer's own AI subscription pays for tokens, so AP-05 does not apply); free-form queries; other totals
and breakdowns (design 75).

## API contract

### Connector protocol (host; not in the OpenAPI document)

| Endpoint | Auth | Behaviour |
|---|---|---|
| `POST /mcp` | Bearer, connector token only | MCP Streamable HTTP in **stateless** mode: no server session, so no instance affinity. Methods `initialize`, `tools/list`, `tools/call`. `GET /mcp` and `DELETE /mcp` answer 405. |
| `GET /.well-known/oauth-protected-resource` | Anonymous | RFC 9728: resource `https://<api host>/mcp`, authorization server, scopes `OpenTmsMcp` and `offline_access` (without the second the AI tool gets no refresh token and the connection drops). |
| `GET /.well-known/openid-configuration`, `GET /.well-known/oauth-authorization-server` | Anonymous | OpenIddict's metadata plus `registration_endpoint`. |
| `POST /connect/register` | Anonymous, rate limited | See *Registration*. |
| `GET/POST /connect/authorize` | Browser session | ABP's endpoint, with the connect screen for connector clients. |
| `POST /connect/token` | PKCE (Gemini: client secret) | Grant types `authorization_code` and `refresh_token`. |

**Registration.** `/connect/register` compares every `redirect_uris` entry with the approved tools. When all entries belong
to one tool, it answers with that tool's client and `token_endpoint_auth_method: none`; otherwise `400 invalid_redirect_uri`.
A tool with fixed URIs has one fixed client and nothing is written. ChatGPT's callback differs per connector
(`https://chatgpt.com/connector/oauth/<id>`), and OpenIddict compares redirect URIs exactly, so an approved URI of that form
gets an application of its own: its id is `OpenTms_Mcp_ChatGPT_` plus a hash of the URI, so registering again returns the same
client. The registration rate limit and a host-wide cap on these applications bound growth (see *Build notes*), the URI must match the
approved pattern, and the name on the connect screen comes from our configuration, never from the caller.

**Approved tools** (`OpenIddict:Applications`, seeded by the DbMigrator into the host database; consent type explicit;
only scopes `OpenTmsMcp` and `offline_access`):

| Client id | Display name | Client type | Redirect URIs |
|---|---|---|---|
| `OpenTms_Mcp_Claude` | Claude | Public | `https://claude.ai/api/mcp/auth_callback`, `https://claude.com/api/mcp/auth_callback` |
| `OpenTms_Mcp_ChatGPT_<hash>` | ChatGPT | Public; created at registration | Exactly one, matching `^https://chatgpt\.com/connector/oauth/[A-Za-z0-9_-]{1,100}$`, or `https://chatgpt.com/connector_platform_oauth_redirect` |
| `OpenTms_Mcp_Gemini` | Gemini | Confidential; secret generated by Terraform into Secret Manager; not seeded without a secret | `https://vertexaisearch.cloud.google.com/oauth-redirect` |

Claude Code and Cowork: Cowork is added under `OpenTms_Mcp_Claude` if it uses the same callback. Claude Code uses a
callback on the user's own machine, which a fixed list cannot name per port; it stays out of the first version
(`/connect/register` refuses a loopback URI), because the security quickscan's loopback question was not answered with test
results. A confidential client (Gemini) is never handed out by `/connect/register`: its administrator enters it.

**Token separation.** A new scope `OpenTmsMcp` with resource `OpenTms.Mcp`, added to the validation audiences. Middleware
in the host enforces both directions: a token whose audience is `OpenTms.Mcp` is refused with 403 on every path other than
`/mcp`, and `/mcp` refuses every other token. `/mcp` authenticates with **the bearer scheme only**: the host's default
falls back to the Identity cookie when no bearer is sent (`ForwardIdentityAuthenticationForBearer`), so without this a
signed-in browser could reach it. A connector token can therefore never call the REST API, and neither a browser token
nor a browser session can call `/mcp`.

**Checks on every `/mcp` request,** after token validation and in this order, each with 403 and no data:

1. A tenant is resolved from the token (ABP's `CurrentUserTenantResolveContributor`). A host user is refused.
2. No impersonator claim (`AbpClaimTypes.ImpersonatorUserId`) is present.
3. Features `Apps.Integrations` and `Apps.Integrations.AiConnector` are both on (the tree rule: a node needs its ancestors,
   `RequiresAll`), and acceptance is recorded.
4. Permission `Integrations.AiConnector.Use` is granted.
5. The user is active (`IdentityUserManager`, in the host). Deactivating a user stops the connector at once, and
   reactivating resumes it.
6. An `AiConnection` with the token's authorization id exists, belongs to this user and has no `RevokedAt`
   (`IAiConnectionChecker.IsActiveAsync(authorizationId, userId)`, published in Integrations `Application.Contracts`, so the
   host never touches the entity or the `DbContext`).

The connect screen runs checks 1 to 5 before it offers Allow. It explains the refusal without naming other tenants, and
issues no code. The features are read after sign-in because no tenant is known before it (as in design 62).

### Tool contract

Every tool result is built from an **allowlist**: a tool DTO in `Application.Contracts/AiConnector/` that names each field.
A field added to a source DTO later therefore stays out until a design adds it. Tools are read-only
(`readOnlyHint: true`). `tools/list` shows only the tools this user may call. Lists take `skip` (default 0) and `take`
(default 25, maximum 50) and return `{ total, rows }`, mapped onto `StartRow`/`EndRow`. `total` is the grid's
`FilteredCount`, because `TotalCount` ignores filters on the trip list. A list never answers more than 50 rows.

| Tool | Input | Service call | Permission | Features the service requires |
|---|---|---|---|---|
| `search_orders` | `status?`, `dateFrom?`, `dateTo?`, `customerRelationId?`, `text?`, `skip?`, `take?` | `ITransportOrderAppService.GetListAsync` | `Orders.TransportOrders` | `Apps.Transport`, `Apps.Transport.Orders` |
| `get_order` | `id` | `ITransportOrderAppService.GetAsync` | `Orders.TransportOrders` | `Apps.Transport`, `Apps.Transport.Orders` |
| `search_trips` | `status?`, `dateFrom?`, `dateTo?`, `carrierRelationId?`, `text?`, `skip?`, `take?` | `ITransportTripAppService.GetListAsync` | `PlanningExecution.TransportTrips` | `Apps.Transport` or `Apps.Planboard`; `Apps.Transport.Trips` or `Apps.Planboard` |
| `get_trip` | `id` | `ITransportTripAppService.GetAsync`, `.GetSummaryAsync` | `PlanningExecution.TransportTrips` | `Apps.Transport` or `Apps.Planboard`; `Apps.Transport.Trips` or `Apps.Planboard` |
| `get_legs` | `orderId` or `tripId` (one required), `skip?`, `take?` | `ILegAppService.GetListAsync` | `PlanningExecution.Legs` | `Apps.Transport` or `Apps.Planboard`; `Apps.Transport.Trips` or `Apps.Planboard` |
| `search_relations` | `roles?`, `text?`, `skip?`, `take?` | `IRelationAppService.GetListAsync` | `MasterData.Relations` | `Apps.MasterData`, `Apps.MasterData.General`, `Apps.MasterData.General.Relations`, `Apps.MasterData.General.Relations.Relations` |
| `search_vehicles` | `vehicleTypeId?`, `text?`, `skip?`, `take?` | `IVehicleAppService.GetListAsync` | `MasterData.Vehicles` | `Apps.MasterData`, `Apps.MasterData.General`, `Apps.MasterData.General.Fleet`, `Apps.MasterData.General.Fleet.Vehicles` |
| `get_order_rates` | `orderId`, `side?` | `IRateLineAppService.GetAsync` | `Financial.RateLines` | `Apps.Transport` |
| `get_customer_revenue` | `customerRelationId`, `dateFrom`, `dateTo` | `ICustomerRevenueReader.GetAsync` | `Financial.RateLines` and `Orders.TransportOrders`, both checked by the tool | `Apps.Transport`, `Apps.Transport.Orders`, checked by the tool |

**`get_customer_revenue`** adds up the revenue of one customer in one SQL aggregate, so the model never sums anything itself.
`ICustomerRevenueReader` is a new in-process interface in `OpenTms.Orders.Application.Contracts/TransportOrders/Published/`,
in the style of `IRateLineReader`, with no HTTP route. It has no permission attribute of its own, which is why the tool
checks the permissions and features above.

- **Source:** the stored `TransportOrder.RevenueAmount` (AD-07), grouped by the order's currency. It never calculates.
- **Definition:** calculated revenue on orders, not invoiced revenue. Orders count by `OrderDate`, with status `Open`,
  `ReadyForInvoicing`, `Invoiced` or `Archived`. `Draft` and `Cancelled` do not count, and the answer states how many there
  were.
- **Answer:** a stated figure, not a guess: every number comes with its definition, and an order still being calculated is
  counted separately instead of as zero.

**Fields returned:**

| Tool | Fields |
|---|---|
| `search_orders` | `id`, `number`, `status`, `orderDate`, `customerRelationId`, `customerName`, `customerReference`, `transportOrderTypeName`, `source`, `firstStopName`, `firstStopCity`, `firstStopDate`, `lastStopName`, `lastStopCity`, `lastStopDate`, `lineCount`, `stopCount`, `tripNumbers`, `executionProgress`, `co2Kg`, `hasTripRequest` |
| `get_order` | `id`, `number`, `status`, `orderDate`, `customerRelationId`, `customerName`, `customerReference`, `transportOrderTypeName`, `source`; from `summary` `firstStopName`, `firstStopCity`, `firstStopDate`, `lastStopName`, `lastStopCity`, `lastStopDate`, `lineCount`, `stopCount`, `tripNumbers`, `executionProgress`, `co2Kg`; `references[type, value]`; `defaultStops[position, relationName, waypointTypeName, windowFrom, windowTill]`; `lines[position, productName, transportUnitName, quantity, colli, weightKg, volumeM3, loadingMeters, temperatureName, vehicleTypeName, requiredTransportCharacteristicNames, shipperRelationName]` |
| `search_trips` | `id`, `number`, `status`, `execution`, `tripDate`, `transportTripTypeName`, `carrierRelationId`, `carrierName`, `customers`, `orderNumbers`, `vehicles` (the vehicle names of the trip list, as text; `get_trip` gives the licence plates), `firstStopCity`, `lastStopCity`, `plannedDeparture`, `plannedArrival`, `actualDeparture`, `actualArrival`, `legCount`, `stopCount`, `loadFactor`, `loadingMeters`, `plannedDistanceM`, `actualDistanceM`, `plannedDurationS`, `co2Kg`, `warnings[code]` |
| `get_trip` | `id`, `number`, `status`, `execution`, `tripDate`, `transportTripTypeName`, `carrierRelationId`, `carrierName`, `vehicleCombinationName`, `vehicles[name, licencePlate]`, `plannedDeparture`, `plannedArrival`, `actualDeparture`, `actualArrival`, `loadFactor`, `loadingMeters`, `plannedDistanceM`, `actualDistanceM`, `plannedDurationS`, `co2Kg`, `warnings[code]`; `stops[position, name, relationName, city, plannedArrival, plannedDeparture, estimatedArrival, estimatedDeparture, actualArrival, actualDeparture]`; from the summary `legCount`, `stopCount`, `orderCount`, `totalQuantity`, `totalWeightKg` |
| `get_legs` | `id`, `orderId`, `orderNumber`, `tripId`, `tripNumber`, `tripStatus`, `position`, `customerName`, `origin[name, city, countryCode, plannedDate, windowFrom, windowTill]`, `destination[the same]`, `quantity`, `weightKg`, `volumeM3`, `loadingMeters`, `distanceM`, `durationS`, `estimatedCo2Kg`, `isViaLeg` |
| `search_relations` | `id`, `code`, `name`, `city`, `countryCode`, `roles`, `categoryNames`, `isBlocked`, `isArchived` |
| `search_vehicles` | `id`, `name`, `kind`, `licencePlate`, `registrationCountryCode`, `vehicleTypeName`, `fuelType`, `euroClass`, `ownerName`, `homeDepotName`, `combinationName`, `isArchived` |
| `get_customer_revenue` | `customerRelationId`, `dateFrom`, `dateTo`, `definition` (fixed text), `totals[currencyCode, revenueAmount, orderCount]`, `ordersWithoutAmount`, `excludedDraftCount`, `excludedCancelledCount` |
| `get_order_rates` | `currencyCode`, `state`, `totals`, `lines[side, kind, additionalServiceName, discountOrSurchargeName, priceListCode, quantity, unitPrice, amount, currencyCode, isManual, tripNumber]` |

**Left out on purpose** (the sensitive fields, for gate 1; the allowlist above is what binds, so any other field stays out
too):

| Source | Fields |
|---|---|
| Orders | `costOfRevenueAmount`, `marginAmount`, `revenueAmount`, `financialTerms`, `tripRequest`, `remarks`, `internalRemarks`, `invoicingRemarks`, `description`, `attributes`, `customerContactPersonId`, `creatorId`, `lastModifierId`, `references[description]` |
| Order lines | `remarks`, `internalRemarks`, `specialAgreements`, `shipperInstructions`, `paymentInstruction`, `pinCode`, `barcode`, `containerNumber`, `invoiceReference`, `attachedDocuments`, `attributes` |
| Trips | `costAmount`, `revenueAmount`, `marginAmount`, `drivers`, `driverInstructions`, `remarks`, `name`, `attributes`, `carrierContactPersonId`, `warnings[data]` |
| Trip stops | `remarks`, `latitude`, `longitude`, `relationAddressId` |
| Legs | `allocatedCostAmount`, `allocatedRevenueAmount`, `remarks`, `origin[reference, latitude, longitude]`, `destination[reference, latitude, longitude]` |
| Relations | `email`, `phone`, `fullName`, `blockReason`, `chamberOfCommerceNumber`, `vatNumber` |
| Vehicles | `vin` |
| Rate lines | `description`, `descriptionKey` |

### Connector administration (conventional controller, in the OpenAPI document and the typed client)

Route root `/api/integrations/ai-connector`, features `Apps.Integrations` and `Apps.Integrations.AiConnector` on each
(`RequiresAll`).

| Method and path | Permission | Request | Response |
|---|---|---|---|
| `GET /status` | `Integrations.AiConnector.Use` or `Integrations.AiConnector.Manage` | | `AiConnectorStatusDto { ConnectorUrl, IsAccepted, AcceptedAt?, AcceptedByUserName? }` |
| `POST /accept` | `Integrations.AiConnector.Manage` | | `AiConnectorStatusDto` |
| `POST /withdraw` | `Integrations.AiConnector.Manage` | | `AiConnectorStatusDto`. Revokes every connection of the tenant. |
| `GET /connections` | `Integrations.AiConnector.Use` (own) or `Integrations.AiConnector.Manage` (all) | Grid request | `GridResponse<AiConnectionRowDto { Id, UserId, UserName, ToolName, ConnectedAt, LastUsedAt?, RevokedAt?, Status }>`. `Status` is the enum `AiConnectionStatus`, computed at read time: `Live` = 1, `NotWorking` = 2, `Expired` = 3, `SwitchedOff` = 4 (see *Build notes*) |
| `POST /connections/{id}/revoke` | Own connection: `Integrations.AiConnector.Use`. Anyone's: `Integrations.AiConnector.Manage` | | 204; idempotent. A repeat on a row already off retries the host step and answers 500 if that did not complete (see *Build notes*) |

**Revoke** commits `RevokedAt` in the tenant's database, which blocks `/mcp` at once, and after that commit revokes the OpenIddict
authorization and its tokens in the host database, which blocks renewal (see *Build notes*).

## Migration strategy

Expand-only. One migration on `IntegrationsDbContext` adds `integrations."AiConnections"` with a unique index on
`AuthorizationId` and an index on `UserId`. One migration on `OrdersDbContext` adds an index on `orders` transport orders
(`TenantId`, `CustomerRelationId`, `OrderDate`) for `get_customer_revenue`. The DbMigrator applies both to the host and
every tenant database. The
DbMigrator seeds scope `OpenTmsMcp` and the approved clients in the host database. There is no contract step, no existing
data changes, and the previous revision ignores the new table during a canary. The orders index blocks writes to that table briefly; see
*Assumptions*.

## UI design

**Connect screen** (`/connect/authorize`, Razor, account layout of design 62): tool name, organisation name, user name,
the sentence that this tool can read the listed data on the user's behalf, buttons **Allow** and **Deny**, and the link
**Use a different account**. A refusal state replaces the buttons for: host user, impersonation, feature off, not
accepted, permission missing, user inactive; it offers **Back to <tool>** (the same as Deny, so the AI tool stops waiting) and
the link. No record picker or grid. Strings in nl and en.

**Screen `/integrations/ai-connector`** in the Integrations app, nav node `Integrations:Nav.AiConnector`, feature
`Apps.Integrations.AiConnector`, a sibling of the group Order intake. Visible with `Integrations.AiConnector.Use` or
`Integrations.AiConnector.Manage`.

| Part | Shows | Components |
|---|---|---|
| Status | Connector URL with a copy button; whether sharing is accepted, by whom and when; **Accept**/**Withdraw** for `Integrations.AiConnector.Manage` | `FormSection` (built here per the UI rule), `ConfirmDialog` |
| Connections | Status **Live**, **Not working**, **Expired** or **Switched off** (`StatusBadge`, from the server's `Status`; the screen derives nothing), user, tool, connected, last used, switched off; row action **Switch off** (the revoke endpoint) on every row that is not switched off, because a Not working connection may become live again. Own rows only without `Integrations.AiConnector.Manage`. Live rows sort first | `OpenTmsGrid` with `rowActions`, `useFormat()`, `ConfirmDialog` |

- **Accept dialog** lists which data leaves, to which kind of vendor, and that the tenant's own agreement with that vendor
  applies. **Withdraw** says that all connections stop.
- **States:** loading via the grid and a skeleton; empty "No connections yet" with the connector URL; error via
  `getApiErrorMessage()`; not accepted shows status only, with a note for users without `Manage`; permission-denied
  follows the app's standard route handling.
- **Optimistic updates:** none. Accept, withdraw and revoke are security actions, so they wait for the server.

### UI deviations

Taken on the PO's instruction of 2026-11-06 ("take the best suggestion and state it"); the PO approves them with the PR at gate 1 and 2. The markers in
`AiConnectorView.tsx` cite this section.

| Rule | Deviation | Reason |
|---|---|---|
| `UI-PAGE-001` | A status section sits between the command bar and the grid | The connector URL and the acceptance belong to the tenant, not to a row, and a user needs them before the list means anything |
| `UI-GRID-005` | The grid's search box is hidden | `GET /connections` does not search, and a tenant's connections are a short list |
| `UI-GRID-007` | Double click and Enter do not open a row (no `onOpen`) | A connection has no record dialog: the row shows all its fields, and its only action is **Switch off** |

**Manual:** new Dutch page `docs/manual/ai-koppeling.md`, for users (connecting in Claude and ChatGPT) and administrators
(accepting, revoking, Gemini set-up); links from `docs/manual/navigatiepaneel.md` as that page does for other apps.

## Test risk analysis

| Part | Risk | Must prove |
|---|---|---|
| Tenant scope of tokens and tools | **Critical** | On PostgreSQL, for a pair of tenants with their own databases **and** a pair of shared-tier tenants in the host database (no connection string, so only the data filter separates them): a token of tenant A returns only A's data for each of the nine tools; A's token with `__tenant=B`, a tenant cookie or query parameter still returns only A; `__tenant=B` on `/connect/token` during a refresh keeps tenant A; an id of B's record gives not-found; a connection row of A is not visible or revocable from B |
| Token separation | **Critical** | A connector token gets 403 on a REST endpoint; a browser token, a client-credentials token, a token of another audience and a signed-in browser session without a token all get refused on `/mcp` |
| Refusals on `/mcp` and connect screen | **Critical** | Each check refused separately: host user, impersonation, either feature off, not accepted, no `Use`, user deactivated, connection revoked; tenant deleted (see *Build notes*); refresh refused after revoke, withdraw and deactivation; a tenant-side failure of revoke or withdraw leaves the host's authorization untouched, and a host-side failure leaves the row switched off with an error logged. Signing out in the browser leaves the connection working, or the result is reported to the PO |
| Connect flow | **Critical** | Existing session in tenant A shows organisation A on the screen and issues no code without Allow; Deny issues none; a second connect by the same user and tool still shows the screen and, while the connection is on, reuses the same `AiConnection` without a unique-index failure; a connect after a switch-off makes a new authorization and row and leaves the old one off; PKCE is required; only `OpenTmsMcp` and `offline_access` are grantable; the advertised scopes yield a refresh token |
| Registration | **High** | Claude's URIs return its fixed client and write nothing; an approved ChatGPT URI returns the same client when repeated; mixed, unknown, `http` and look-alike URIs (`chatgpt.com.evil.example`, extra path segments) get 400 and write nothing; at the cap a new ChatGPT URI gets 400, writes nothing and logs an error, while one already registered still returns its client |
| Field allowlists | **High** | A test seeds every left-out field with a marker value, and the serialized result of each tool contains no marker. A second architecture test keeps tool DTOs free of names like `Amount` outside `get_order_rates` and `get_customer_revenue`, and of `email`, `phone`, `driver` and `remarks` |
| `get_customer_revenue` | **High** | The total equals the sum of the orders' stored revenue, per currency; `Draft` and `Cancelled` stay out and are counted; an order without an amount is counted as such, not as zero; refused without either permission or feature; one query per call. Volume: p95 < 300 ms at 1,000,000 orders in a tenant with 100,000 for one customer, on PostgreSQL |
| Permissions and features | **High** | Each tool is refused without its permission and feature, and is absent from `tools/list`; `Integrations.AiConnector.Use` alone returns no business data |
| Limits | **Medium** | `take` over 50 is capped; `total` respects the filters; rate limit answers 429; logs and audit records carry ids only, with no tool arguments (`/mcp` is excluded from ABP's request auditing if it would store them) |
| Administration endpoints and screen | **Medium** | Own versus all connections; idempotent revoke; states above; negative tenant test per endpoint |
| Platform compatibility | **High, manual** | A real connect and a tool call from Claude web, ChatGPT and Gemini Enterprise on staging; Cowork and Claude Code tested to confirm or exclude them. CLI success does not prove the web connector works |

Prompt injection is covered by being read-only (see *Security quickscan*); a test proves that no tool method maps to a
write verb of a service.

## Flag & rollout plan

- **Feature:** `Apps.Integrations.AiConnector` under `Apps.Integrations`, default off per tenant, set by the host in the
  Tenants app. Rule 4 is met by this feature: without it, the connect screen refuses, `/mcp` refuses and the nav node is
  hidden.
- **Second gate:** the tenant administrator accepts data sharing, and grants `Integrations.AiConnector.Use` to roles.
  `Integrations.AiConnector.Use` and `Integrations.AiConnector.Manage` are not added to predefined roles other than
  `admin`, which holds every permission.
- **Order:** staging with `demo` → the health tenant in prod → friendly tenants, one per approved tool → all on request.
  Existing tenant data does not change.

## Cost & SLO impact

- **Tokens:** €0 for us. The customer's own AI subscription pays. No model call is made by us, and AP-05 does not apply.
- **Infra:** no new service. `/mcp` runs in the API on Cloud Run, stateless, so a future second instance needs no
  affinity. Each tool call wakes the service from zero like any other request, and staging's stopped database still needs
  starting for a test.
- **Database:** per tool call, one indexed read of `AiConnection` and one read of the user, plus the service's usual
  queries.
- **Budgets touched:** interactive reads (p95 < 300 ms, unchanged services; the `/mcp` overhead is measured with a k6
  scenario) and availability of core flows (the connector is not a core flow). `get_trip` makes two service calls.
- **Load control:** 60 requests per minute per connection on `/mcp` (every request counts, because the limiter answers at
  the HTTP level; a session's few `initialize` and `tools/list` requests change little) and 10 registrations per minute per
  IP address (the last value of `X-Forwarded-For`, which Cloud Run's proxy appends), through ASP.NET Core's built-in rate
  limiter, in process. With one instance per revision that is exact; it needs Redis (AP-13) as a shared store once several
  instances run. An AI tool can call in bursts, so the limit protects other users of the tenant.
- **Margin per tenant:** no direct cost per order. Not metered in this version.

## Assumptions

Verified at gate 1 or first thing at build; a wrong one comes back to the PO.

- **AP-13 is not relied on:** the limiter is per instance by design.
- **Vendor callbacks:** Claude's and ChatGPT's come from the vendors' documentation, Gemini's from Google's documentation and
  several vendor guides. They are not tested against the real tools until staging. Vendor rules may change, and Gemini
  Enterprise's custom MCP support is a preview, which an administrator may need to enable by organisation policy.
- **Consumer Gemini app:** no custom connector route was found. "Gemini" means Gemini Enterprise, set up by an
  administrator, until shown otherwise. ChatGPT's plan and workspace rules are the vendor's, and we do not work around
  them.
- **Claude's registration:** Claude accepts a registration answer with a shared `client_id` and no secret. If not, the
  fallback is entering the client id by hand, which Claude supports. Community reports describe failures on web connectors
  that CLI does not show, which is why the compatibility test uses the real tools.
- **OpenIddict (read in its source at build, version 7.5):** the metadata also answers on `oauth-authorization-server`;
  custom metadata can add `registration_endpoint`. A `resource` parameter is **not** ignored: OpenIddict rejects one that is
  not registered and permitted (`invalid_target`), and MCP clients send it (RFC 8707). The host therefore drops exactly its
  own `<api host>/mcp` from authorize and token requests, and any other resource is still refused. A connector token's
  audience always comes from the scope. ABP's `AuthorizeController` is replaced by a subclass that handles connector clients
  and leaves the rest to ABP, so the connect screen always appears; it has a view of its own, `Views/AiConnector/Connect.cshtml`,
  and ABP's `Authorize.cshtml` stays as it is. A refresh token depends on `offline_access`, a permission on these clients only.
- **Read in ABP's source at build (10.6):** ABP does reuse a valid permanent authorization and skip consent, which is why the
  subclass exists, and Allow records `AiConnection` (never reactivating one that is off, see *Build notes*). ABP's auditing does store the arguments of every application-service call
  of a request, also with `/mcp` excluded by URL (its interceptor then opens a scope of its own); so the audit record of a
  `/mcp` request stays and its actions' parameters are emptied. ABP's open-source identity has no session manager, so signing
  out in the browser does not touch a token, and a connector token carries no `sid` in any case. `OpenIddictDbContext`
  is `[IgnoreMultiTenancy]`, so its rows are in the host database whatever the tenant.
- **Revenue definition:** the PO asked for revenue per customer without fixing the definition; calculated revenue by
  `OrderDate`, with the statuses named under `get_customer_revenue`, is this design's reading. The order's currency is
  a column of the orders table (`FinancialTerms.CurrencyCode`, owned in the same table), so SQL groups on it.
- **Different account:** the existing `SwitchAccount` page redeems tickets from the user menu (design 63) and is not
  reusable here, so the link signs out and restarts the authorize request.
- **SDK:** the official `ModelContextProtocol.AspNetCore` package, version 2.3.1, fits .NET 10 and stateless mode. Motivation
  for the new dependency: it implements the protocol, including its versions, which we would otherwise maintain ourselves.
- **Reading a cost-side rate line** through `get_order_rates` is acceptable with `Financial.RateLines` alone, because that
  is what the screens require today.
- **Margin on existing endpoints:** the order and trip endpoints return amounts with only the read permission of the
  order or trip. This design does not change them; the tools remove the amounts. Whether the screens should change is a
  separate question.
- **The orders index blocks writes briefly.** `OrdersCustomerRevenueIndex` is a plain `CREATE INDEX` in the migration's transaction, because this repository has no
  pattern for `CONCURRENTLY` and `suppressTransaction`: every index in it, the trigram ones on inbound messages included, is built this way. The previous revision
  serves meanwhile, so on each database reads go on and writes to the orders table wait. Size assumed: at most 1,000,000 orders in one tenant, as in the volume test;
  the sizes in prod were not read. Measured on local PostgreSQL 17 on a scratch table of 1,000,000 rows (781 MB): 0.35 seconds. Cloud SQL was not measured; the
  assumption is under 30 seconds at that volume. Writes wait and do not fail. Design 75 *Assumptions* states the same for its two migrations.
- **Interpretation:** "tenant administrator accepts" is a recorded action on the Integrations screen, while switching the
  feature on stays with the host, as for every feature.

## Security quickscan

**New permissions, in full:**

| Permission | Side | Guards |
|---|---|---|
| `Integrations.AiConnector.Use` | Tenant | Connecting and using the connector; seeing and revoking own connections |
| `Integrations.AiConnector.Manage` | Tenant | Accepting and withdrawing data sharing; seeing and revoking all connections |

**New features, in full:** `Apps.Integrations.AiConnector` (the app's nav node and the connector).

**Existing permissions the tools need, in full:** `Orders.TransportOrders`, `PlanningExecution.TransportTrips`,
`PlanningExecution.Legs`, `MasterData.Relations`, `MasterData.Vehicles`, `Financial.RateLines`.

**Attack surface:** three new anonymous or public paths (`/mcp`, its metadata, `/connect/register`), plus new clients on
the existing authorization server.

- **Wrong tenant:** the tenant comes from the credentials and is shown on the connect screen before anything is issued;
  there is no `__tenant` route into a connection.
- **Stolen connector token:** it reaches only the nine read tools, for one user in one tenant, and revocation works at
  once because `/mcp` checks the connection on every request.
- **Client impersonation:** an approved tool's redirect URIs are exact, and PKCE S256 is required.
- **Loopback question (Claude Code):** a client whose callback is any local port lets any local program ask for the same
  consent. It stays out unless its risk is accepted at gate 1 with test results.
- **Prompt injection:** text from ingested documents can still reach the model through allowed fields such as
  `customerName` or `customerReference`. The first version has no write tool, so such text cannot change the TMS through
  this connector. It could still influence what the AI tool does with its other connectors; that is why free text is left
  out, and the manual warns about it.
- **Search on hidden values (for gate 1):** `search_relations` passes `text` to the relation search, which also matches
  VAT and KvK numbers, so a hit confirms a number the tool does not show. The user may read those numbers on the screen
  anyway, so this design accepts it. The search keys were checked at build: orders match number, customer reference, customer,
  first and last stop and trip numbers; trips match number, name, carrier, combination, vehicles, licence plates, customers,
  order numbers and stop cities (no driver names). No personal field is hit. `search_vehicles` also matches the VIN, which
  the tool does not show; it is accepted for the same reason and is reported to the PO in the PR.
- **Input:** tool inputs have typed schemas with limits; `text` has a maximum of 200 characters; ids are `Guid`; the
  services validate as usual.
- **Logging (constitution rule 5):** tenant id, user id, client id, tool name, duration and row count. No tool arguments or
  results, because a search text can hold a name. Prompts never reach us. The audit record of a `/mcp` request keeps who
  called which service and when, without its parameters (see *Assumptions*).
- **What a connector token shows:** ABP does not encrypt access tokens, so the AI vendor can read them. A connector token
  carries the user id and the tenant only: no e-mail, name, phone or roles. The host adds those per request.

**GDPR**

- **Data leaving:** to the AI vendor the user connected, on the instruction of that user and with the administrator's
  acceptance. The tenant's agreement with its vendor covers it. We are not a sub-processor of that vendor, and no vendor
  of ours receives this data. The legal wording of the accept dialog is for the PO to confirm before the first tenant goes
  live.
- **`AiConnection` holds a user id and timestamps.** Art. 15: no user-wide export route exists today (`IPersonalDataSource`
  belongs to Master Data and has no production caller), so the answer is `GET /connections`, which the user and the
  administrator can both read. When a platform-wide export arrives, connections join it. Art. 17: a local handler on
  `EntityChangedEventData<IdentityUser>`, the event `UserSignInLookupSynchronizer` already uses, revokes and removes a
  deleted user's connections after commit. The acceptance settings hold a user id, which is cleared the same way.
- **New personal data otherwise:** none stored. Data already sent to an AI vendor is outside our reach, and the manual says
  so.

## Build notes: where this design was silent

Taken on the PO's instruction of 2026-11-06 ("take the best suggestion and state it").

- **A connection stays established:** a connector client's refresh token lasts 90 days and rolls on with every renewal.
  OpenIddict's default of 14 days would drop a connection after two quiet weeks; revoking, withdrawing and deactivating stop it
  far sooner.
- **Scopes:** a connector token always gets exactly `OpenTmsMcp` and `offline_access`, whatever the AI tool asked for. A tool
  that asks too little would get no audience or no refresh token. A scope the client has no permission for is still refused.
- **`prompt=none`** answers `consent_required` (or `login_required`), because the screen must appear. `prompt=login` and
  `max_age` work as in ABP. `S256` is required of a public client; Gemini is confidential and authenticates with its secret.
- **Use a different account** is a form post, not a link in the HTTP sense: a plain GET could sign someone out from another site.
  The connect screen answers `X-Frame-Options: DENY` and `frame-ancestors 'none'`, because it is a screen that gives access.
- **Approved tools configuration** lives in the DbMigrator's `OpenIddict:Applications`: every entry whose key starts with
  `OpenTms_Mcp_` is seeded. `/connect/register` finds fixed clients by their seeded URIs, so the host needs no copy of the list.
  ChatGPT's pattern and name are constants, because its clients arrive at registration. A registration holds at most 10 URIs of
  at most 2,048 characters in a body of at most 16 KB, answers 201 in the shape of RFC 7591, and a body that is not JSON gets
  `invalid_client_metadata`.
- **Clients may also revoke** their own tokens (`/connect/revocat`), as a tool does on disconnect. The connection then still
  shows as live until the tool reconnects, someone switches it off or it expires. A disconnect on the vendor's side stays
  undetectable, so the manual names *last used* as the hint for that.
- **A row's status is what `/mcp` would do, read per row (PO decision 6).** A row that read Live while `/mcp` refused it broke the
  decision, so `AiConnectionAppService` computes `Status` at read time and the screen shows it. `SwitchedOff`: `RevokedAt` is set.
  `Expired`: neither `LastUsedAt` nor `ConnectedAt` is within `AiConnectorConsts.RefreshTokenLifetime`, the constant the token
  lifetime itself uses (moved there from `AiConnectorClients`, because Integrations cannot see the host's `OpenTms.Domain`).
  `NotWorking`: the user is inactive or missing, or does not hold `Integrations.AiConnector.Use`. `Live`: none of these. The
  other refusals of `/mcp` need no state: a host user or impersonator never gets a row, without the feature this endpoint refuses
  too, and withdrawing the acceptance switches every row off.
- **Expired wins over NotWorking** when both hold: reactivating the user does not bring a lapsed token back, so the user has to
  connect again whatever is fixed first.
- **Expired is a reading of `LastUsedAt`, not of the token,** because the token is in the host database and names no tenant. A tool
  that renews after its last call keeps a token some hours beyond the row's Expired; its next call turns the row Live again.
- **The permission check is the one `/mcp` makes:** `IPermissionChecker.IsGrantedAsync(principal, name)` with a principal built
  from the user's id, the tenant and the roles, so the screen and `/mcp` cannot disagree. Read in ABP's source (10.6): no permission
  API spans users; the batch overloads of `IPermissionChecker`, `IPermissionStore` and `IPermissionManager` take many permissions
  for one provider key. So the page's roles come from one batched `IIdentityUserRepository.GetRoleNamesAsync(userIds)`, which includes
  the roles of organisation units as `/mcp`'s claims do, and each distinct active user of the page gets one check, skipped for rows
  already switched off or expired. A check reads ABP's permission cache and reaches the database only on a cold cache, once per
  user or role. Querying `PermissionGrants` directly would be one query but a second definition of "granted".
- **Sorting:** SQL puts switched-off rows last and expired rows before them, then the column the grid asks for. Whether a user still
  qualifies lives in Platform's tables, so SQL cannot order on it, and each page then puts Live before NotWorking. Across the grid's
  blocks of 100 rows a NotWorking row can therefore precede a Live row of the next block; ordering across all rows would mean
  checking every connected user of the tenant per request.
- **Metadata:** the protected-resource document also answers on `/.well-known/oauth-protected-resource/mcp`, the address RFC 9728
  derives from `/mcp`, and the server metadata adds `none` to the token endpoint's authentication methods.
- **Refusals on `/mcp`** answer 403 with `{ error: "access_denied", reason }`, the reason being the check's name: the holder of
  the token learns nothing about anyone else, and support can tell the cases apart. A tool that fails answers a tool result with
  `isError`, stating no data, so the model can act on it. The server's instructions tell the model that text in results is data.
- **A tenant stops by being deleted, not deactivated.** ABP's open-source tenant has no activation state and this codebase adds
  none, so the *Test risk analysis* row names tenant deletion. `/mcp` refuses a connection of a deleted tenant and returns no data.
  On a deleted tenant `/mcp` answers ABP's HTML 404 "Tenant not found!", not the 403 of the other refusals, because tenant resolution
  stops the request first; it holds no data. The token endpoint reads no tenant state, so a refresh after only the tenant record is
  deleted answers 200 and issues a token that reaches nothing on `/mcp`. With the database dropped as well, as the Tenants app's deletion
  does, a refresh answers 400 `invalid_grant`. Observed on the host tests, which delete the record and drop the database themselves;
  they do not run the Tenants app's deletion.
- **Registering one ChatGPT URI twice at the same moment gives one client,** on every instance and revision, because a canary
  runs two at once. `OpenIddictApplications.ClientId` has a plain index, so nothing in the table stops a second row. Find-then-create
  therefore runs under `pg_advisory_xact_lock`, keyed on the client id, in a read-committed transaction on the host database. This
  is the one raw SQL statement here and it reads no data. A unique index was not chosen: it would change the mapping of a table ABP
  owns, in every database the DbMigrator migrates, to protect one code path. The find after the lock uses the repository, not the
  OpenIddict manager, because the manager can answer from a per-instance cache.
- **A cached miss does not refuse a newly registered client on another instance.** Read in the source (ABP 10.6, OpenIddict 7.5): `AbpOpenIddictApplicationCache.FindByClientIdAsync`
  caches a miss as JSON `null`, and ABP's `DistributedCache.GetOrAddAsync` treats a null value as absent, so it asks the store again. `/connect/authorize` and `/connect/token`
  find a client that way, so the client registered on one instance is found on the other. `FindByRedirectUriAsync` does remember a miss, as an empty array, on one instance, for
  ABP's default cache lifetime. Only `/connect/register`'s path for fixed clients reads it, a ChatGPT URI never takes that path, and a new revision starts with an empty cache,
  so a URI seeded by a deploy is not affected. No change was needed.
- **A cap on ChatGPT applications.** Registration is anonymous and a new approved URI writes a permanent row, so the rate limit alone leaves about 14,400 rows per day per IP
  address. `AiConnector:Registration:MaxChatGptClients` (default 10,000) caps the applications whose id starts with `OpenTms_Mcp_ChatGPT_`, host-wide. At the cap a new URI gets
  the usual `400 invalid_redirect_uri`, so an anonymous caller learns nothing about the count, and an error is logged with the count and the cap, never the URI. A URI already
  registered returns its client, without counting. The count runs inside the same lock path, only for a new URI. That lock is per client id, so registrations of different URIs at
  the same moment can pass the cap by those few. A second, global lock would serialise every new registration to make an approximate limit exact, which nothing needs.
- **`search_relations` and `search_vehicles` include archived rows:** the answer states `isArchived`, so the model tells them
  apart instead of missing them.
- **`get_customer_revenue`:** `orderCount` counts the orders in that total, so orders without an amount are only in
  `ordersWithoutAmount`.
- **Accepting twice** keeps the first acceptance on record. Art. 17 clears the accepting user's id and leaves the acceptance standing.
- **Switching off commits in the tenant first (corrected in review).** The tenant's and host's databases share no transaction. Withdraw and revoke commit the settings and
  `RevokedAt` first, which makes `/mcp` refuse at once, and revoke in the host only after that commit (`IUnitOfWork.OnCompleted`). A failure on the tenant's side therefore has
  revoked nothing, so no row can read Live with a dead token. A failure in the host leaves the connection switched off: the error is logged with the authorization id, the tool can
  renew a token that `/mcp` refuses, and `POST /connections/{id}/revoke` retries it, also on a row already off (the screen offers **Switch off** only on a row that is not).
  A caller hanging up does not stop the host step. Art. 17 stays as it was: it revokes before removing the row, because nothing there may stay usable and the user is already gone.
- **A switched-off connection's tokens never work again, whatever the host's state (corrected after a failure test).** A row that is off never comes back: nothing clears `RevokedAt`, and
  `IAiConnectionRepository.TryConnectAgainAsync` updates `ConnectedAt` only `WHERE RevokedAt IS NULL`, in the database, so even a switch-off committing mid-call is never overwritten. Allow reuses an authorization only when `IAiConnectionRecorder.FindReusableAsync` finds its connection on. In every other case it creates a new authorization and
  a new row, so the old tokens keep failing `/mcp` check 6 on the old id, also if the host still holds that authorization as valid. The grid then shows the old row as Switched off and the new row as Live. The connect
  also tries to revoke the old authorization again, and a failure there is logged and does not stop it. A valid authorization without any row is not reused either and is left alone: it may be another connect's, a moment before its row
  commits. `TryRecordAsync` answers false on a row switched off between that read and its write, and the connect goes on with a new authorization.
- **Why a retry used to do nothing, read in the source (OpenIddict 7.5, ABP 10.6).** `OpenIddictAuthorizationManager.UpdateAsync` calls `Cache.AddAsync` with the changed model right after `Store.UpdateAsync`, so before the transaction commits. ABP's
  `AbpOpenIddictAuthorizationCache` keeps it in `IDistributedCache`, which is in memory on one instance without Redis, for 20 minutes sliding, and outside the unit of work (`considerUow` defaults to false). When the tokens update then failed, the
  database rolled back and the cache kept `revoked`. On the retry `FindByIdAsync` returned that entry, `TryRevokeAsync` saw revoked and answered true without writing, and the status stayed valid. `/connect/token` read the same entry, which is why a refresh answered 400.
  `AiConnectionAuthorizationRevoker` therefore reads and writes through the stores, not the managers, removes the cache entry afterwards whatever the outcome, and reads the committed state back: the authorization and every token of it revoked. Anything else logs an error with the
  authorization id and returns false. As a result a refresh before the retry answers 200 as stated above, with a token `/mcp` refuses.
- **A retry that did not complete does not look complete.** The first attempt still answers 204, because the switching off itself succeeded and `/mcp` refuses at once; the error log is what says the host side is open. A repeated `POST .../revoke` on a row already off has nothing else to do, so it runs
  the host step inside the request and answers 500, without detail, when that did not complete.
- **Without Redis the instances do not share this cache.** Another instance can hold `valid` for an authorization for up to the sliding 20 minutes after it was revoked, and renew a token there. That token names the old authorization, so `/mcp` refuses it. The API runs one instance per revision until AP-13.
- **Routes:** the connections are a service of their own (`IAiConnectionAppService`) so that the convention gives
  `/ai-connector/connections` and `/ai-connector/connections/{id}/revoke`; `IAiConnectorAppService` serves the rest.
- **Secret:** Terraform generates `ai-connector-gemini-client-secret` into Secret Manager, readable by the migrator's service
  account only; an administrator reads it with `gcloud` to enter it in Gemini Enterprise (`infra/README.md`).
- **`get_customer_revenue` reads only its index (PO 2026-11-11).** On main, CI answered it in p95 345 ms, median 98 ms, against the
  300 ms budget. The 100,000 orders of one customer lie on every page of a 434 MB table, so each call read the whole table, and a
  runner's page cache gave the peaks. A large customer's orders spread the same way in production. `IX_TransportOrders_CustomerRevenue`
  on (`TenantId`, `CustomerRelationId`, `OrderDate`) includes `Status`, `CurrencyCode`, `RevenueAmount` and `IsDeleted`, every column the
  query reads, and replaces the plain index: measured 81 ms to 7 ms on a vacuumed copy. It is SQL in the migration
  `OrdersCustomerRevenueCoveringIndex`, because EF cannot include a column of an owned type; it is created before the old one is
  dropped. A read from the index alone needs rows every transaction sees, so the volume test now commits and vacuums its 1,000,000
  orders and deletes them afterwards (PO decision 2026-11-11); the budget and the volume did not change. Locally p95 56 ms became 10 ms.
  The index makes each order write slightly wider; `CREATE INDEX` blocks writes per tenant database for about a second per million orders.
- **The 401 of `/mcp` reaches the AI tool in production (2026-11-11).** After the first deploy, `/mcp` without a token answered
  302 to `/Error?httpStatusCode=401`: outside Development the host's `UseErrorPage` redirects every 4xx without a body, so an AI
  tool never saw the challenge that starts its sign-in. The host tests run in Development, where that page is skipped. The gate
  now switches the status code page off on `/mcp` only, and `McpErrorPageTests` runs a Production pipeline with the error page.
