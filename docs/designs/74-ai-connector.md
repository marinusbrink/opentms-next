# Design 74: AI connector — opentms-next as a custom connector in a user's AI tool

**PBI:** Marinus, 2026-11-05 (chat): a user adds opentms-next as a custom connector in Claude, ChatGPT, Gemini, Cowork and
similar tools, and signs in to opentms-next from there with the correct tenant scope. The connection then stays
established, and the AI tool answers questions or runs tasks based on data from opentms-next.
**Status:** in review. Risk class **critical** (authorization server, tenant isolation, a new public endpoint).
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

The host writes and reads this entity only through `IAiConnectionChecker` and `IAiConnectionRecorder` (`Upsert` from the
connect screen), both published in Integrations `Application.Contracts`.

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
client. The registration rate limit bounds growth, the URI must match the approved pattern, and the name on the connect screen
comes from our configuration, never from the caller.

**Approved tools** (`OpenIddict:Applications`, seeded by the DbMigrator into the host database; consent type explicit;
only scopes `OpenTmsMcp` and `offline_access`):

| Client id | Display name | Client type | Redirect URIs |
|---|---|---|---|
| `OpenTms_Mcp_Claude` | Claude | Public | `https://claude.ai/api/mcp/auth_callback`, `https://claude.com/api/mcp/auth_callback` |
| `OpenTms_Mcp_ChatGPT_<hash>` | ChatGPT | Public; created at registration | Exactly one, matching `^https://chatgpt\.com/connector/oauth/[A-Za-z0-9_-]{1,100}$`, or `https://chatgpt.com/connector_platform_oauth_redirect` |
| `OpenTms_Mcp_Gemini` | Gemini | Confidential; secret generated by Terraform into Secret Manager; not seeded without a secret | `https://vertexaisearch.cloud.google.com/oauth-redirect` |

Claude Code and Cowork: Cowork is added under `OpenTms_Mcp_Claude` if it uses the same callback. Claude Code uses a
callback on the user's own machine, which a fixed list cannot name per port; it gets its own client only after the
security quickscan's loopback question is answered at build, and otherwise stays out of the first version.

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
| `search_trips` | `id`, `number`, `status`, `execution`, `tripDate`, `transportTripTypeName`, `carrierRelationId`, `carrierName`, `customers`, `orderNumbers`, `vehicles` (licence plates, as text), `firstStopCity`, `lastStopCity`, `plannedDeparture`, `plannedArrival`, `actualDeparture`, `actualArrival`, `legCount`, `stopCount`, `loadFactor`, `loadingMeters`, `plannedDistanceM`, `actualDistanceM`, `plannedDurationS`, `co2Kg`, `warnings[code]` |
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
| `GET /connections` | `Integrations.AiConnector.Use` (own) or `Integrations.AiConnector.Manage` (all) | Grid request | `GridResponse<AiConnectionRowDto { Id, UserId, UserName, ToolName, ConnectedAt, LastUsedAt?, RevokedAt? }>` |
| `POST /connections/{id}/revoke` | Own connection: `Integrations.AiConnector.Use`. Anyone's: `Integrations.AiConnector.Manage` | | 204; idempotent |

**Revoke** sets `RevokedAt`, which blocks `/mcp` at once, and then revokes the OpenIddict authorization and its tokens in
the host database, which blocks renewal.

## Migration strategy

Expand-only. One migration on `IntegrationsDbContext` adds `integrations."AiConnections"` with a unique index on
`AuthorizationId` and an index on `UserId`. One migration on `OrdersDbContext` adds an index on `orders` transport orders
(`TenantId`, `CustomerRelationId`, `OrderDate`) for `get_customer_revenue`. The DbMigrator applies both to the host and
every tenant database. The
DbMigrator seeds scope `OpenTmsMcp` and the approved clients in the host database. There is no contract step, no existing
data changes, and the previous revision ignores the new table during a canary.

## UI design

**Connect screen** (`/connect/authorize`, Razor, account layout of design 62): tool name, organisation name, user name,
the sentence that this tool can read the listed data on the user's behalf, buttons **Allow** and **Deny**, and the link
**Use a different account**. A refusal state replaces the buttons for: host user, impersonation, feature off, not
accepted, permission missing. No record picker or grid. Strings in nl and en.

**Screen `/integrations/ai-connector`** in the Integrations app, nav node `Integrations:Nav.AiConnector`, feature
`Apps.Integrations.AiConnector`, a sibling of the group Order intake. Visible with `Integrations.AiConnector.Use` or
`Integrations.AiConnector.Manage`.

| Part | Shows | Components |
|---|---|---|
| Status | Connector URL with a copy button; whether sharing is accepted, by whom and when; **Accept**/**Withdraw** for `Integrations.AiConnector.Manage` | `FormSection` (built here per the UI rule), `ConfirmDialog` |
| Connections | Status **Live** or **Switched off**, user, tool, connected, last used, switched off; row action **Switch off** (the revoke endpoint). Own rows only without `Integrations.AiConnector.Manage`. Live rows sort first | `OpenTmsGrid` with `rowActions`, `useFormat()`, `ConfirmDialog` |

- **Accept dialog** lists which data leaves, to which kind of vendor, and that the tenant's own agreement with that vendor
  applies. **Withdraw** says that all connections stop.
- **States:** loading via the grid and a skeleton; empty "No connections yet" with the connector URL; error via
  `getApiErrorMessage()`; not accepted shows status only, with a note for users without `Manage`; permission-denied
  follows the app's standard route handling.
- **Optimistic updates:** none. Accept, withdraw and revoke are security actions, so they wait for the server.

**Manual:** new Dutch page `docs/manual/ai-koppeling.md`, for users (connecting in Claude and ChatGPT) and administrators
(accepting, revoking, Gemini set-up); links from `docs/manual/navigatiepaneel.md` as that page does for other apps.

## Test risk analysis

| Part | Risk | Must prove |
|---|---|---|
| Tenant scope of tokens and tools | **Critical** | On PostgreSQL, for a pair of tenants with their own databases **and** a pair of shared-tier tenants in the host database (no connection string, so only the data filter separates them): a token of tenant A returns only A's data for each of the nine tools; A's token with `__tenant=B`, a tenant cookie or query parameter still returns only A; `__tenant=B` on `/connect/token` during a refresh keeps tenant A; an id of B's record gives not-found; a connection row of A is not visible or revocable from B |
| Token separation | **Critical** | A connector token gets 403 on a REST endpoint; a browser token, a client-credentials token, a token of another audience and a signed-in browser session without a token all get refused on `/mcp` |
| Refusals on `/mcp` and connect screen | **Critical** | Each check refused separately: host user, impersonation, either feature off, not accepted, no `Use`, user deactivated, connection revoked; tenant deactivated; refresh refused after revoke, withdraw and deactivation. Signing out in the browser leaves the connection working, or the result is reported to the PO |
| Connect flow | **Critical** | Existing session in tenant A shows organisation A on the screen and issues no code without Allow; Deny issues none; a second connect by the same user and tool still shows the screen and reuses the same `AiConnection` without a unique-index failure; PKCE is required; only `OpenTmsMcp` and `offline_access` are grantable; the advertised scopes yield a refresh token |
| Registration | **High** | Claude's URIs return its fixed client and write nothing; an approved ChatGPT URI returns the same client when repeated; mixed, unknown, `http` and look-alike URIs (`chatgpt.com.evil.example`, extra path segments) get 400 and write nothing |
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
- **Load control:** 60 tool calls per minute per connection and 10 registrations per minute per IP address, through ASP.NET
  Core's built-in rate limiter, in process. With one instance per revision that is exact; it needs Redis (AP-13) as a
  shared store once several instances run. An AI tool can call in bursts, so the limit protects other users of the tenant.
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
- **OpenIddict:** the metadata also answers on `oauth-authorization-server`; custom metadata can add
  `registration_endpoint`; `resource` parameters are accepted or ignored without an error. ABP's `AuthorizeController` can
  be extended for connector clients, or replaced for them, so that the connect screen always appears, also when an
  authorization already exists. ABP ships `Views/Authorize/Authorize.cshtml`, which is overridden. Refresh-token issuing
  depends on `offline_access`, which is to be registered as a permission on these clients only.
- **Not read in ABP's source, so proven by test first at build:** ABP may reuse a valid permanent authorization on a second
  connect, so Allow upserts `AiConnection`. ABP's request auditing may store `/mcp` arguments. ABP's identity sessions may
  invalidate connector tokens when the browser signs out. `OpenIddictDbContext` being host-only is inferred from local
  data: tenant databases hold the tables but they are empty.
- **Revenue definition:** the PO asked for revenue per customer without fixing the definition; calculated revenue by
  `OrderDate`, with the statuses named under `get_customer_revenue`, is this design's reading. The order's currency is
  assumed to be stored in a column that SQL can group on, which is checked at build.
- **Different account:** the existing `SwitchAccount` page redeems tickets from the user menu (design 63) and is not
  reusable here, so the link signs out and restarts the authorize request.
- **SDK:** the official `ModelContextProtocol.AspNetCore` package (2.x) fits .NET 10 and stateless mode. Motivation for the
  new dependency: it implements the protocol, including its versions, which we would otherwise maintain ourselves. The
  version is checked at build.
- **Reading a cost-side rate line** through `get_order_rates` is acceptable with `Financial.RateLines` alone, because that
  is what the screens require today.
- **Margin on existing endpoints:** the order and trip endpoints return amounts with only the read permission of the
  order or trip. This design does not change them; the tools remove the amounts. Whether the screens should change is a
  separate question.
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
  anyway, so this design accepts it. The search keys of the order, trip and vehicle lists are checked at build; a hit on
  a personal field such as a driver name is then filtered out, or reported to the PO.
- **Input:** tool inputs have typed schemas with limits; `text` has a maximum of 200 characters; ids are `Guid`; the
  services validate as usual.
- **Logging (constitution rule 5):** tenant id, user id, client id, tool name, duration and row count. No tool arguments or
  results, because a search text can hold a name. Prompts never reach us.

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
