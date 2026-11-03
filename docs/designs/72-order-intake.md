# PBI: Order intake from mailboxes and the API, with learned import profiles

<!-- Written from the PO's instruction of 2026-11-02/03 ("correct implementation within our application architecture for a
     mail-integration that monitors n mailboxes for incoming transport orders … mail is just a source … AI only for unknown
     file content; once mapped correctly, AI tokens should no longer be burned") and his answers that day: the layered
     design in chat "sounds perfect"; the language-model vendor is Anthropic (AO-05 decided, more vendors may follow);
     configuration in a host tab "Integrations"; AI mapping must be first time right; traceability from original file to
     transport order for tenant users and Customer Success Managers; first sources Gmail, Microsoft 365 and an API endpoint;
     mailboxes connect by OAuth consent and are read from one label or folder; the API takes a JSON order and documents,
     authenticated with OAuth client credentials; Customer Success Managers see a tenant's trace through impersonation.
     No GitHub issue; "72" is the next free number. -->

## Story

As an order clerk I want transport orders that customers send me — by e-mail in Gmail or Microsoft 365, or from their own
systems through our API — to become orders in OpenTMS without retyping them, and where the system is not sure, I want to
check and complete them in one place. As a tenant administrator I want to connect our order mailboxes and our customers'
API access myself. As the business I want a document layout to be understood once (by AI where needed) and from then on
read without AI cost. As a tenant user or a Customer Success Manager I want to follow every order back to the file it came
from: which mailbox, which mail, which attachment, which rule read which value, and what became of it.

## Affected area

- **Integrations** (anti-corruption layer, AP-16): the mailbox connections (Gmail, Microsoft 365) with their OAuth consent
  and synchronisation, the API clients and the intake API, the inbound message store and its raw files, the import
  pipeline (extract, fingerprint, profile, AI), the import profiles, the trace, the host settings tab, AI metering.
- **Orders** ("intake via all channels", domain map): the canonical intake model (`OrderIntakeDto`), the order intake
  record with its resolution against master data, the review queue, learned aliases, creating the order (Draft or
  Confirmed) and the link from an order to its intake.
- **Master Data**: a published match query that finds relations and relation addresses by what a document says.
- **Platform**: the app features of the new nav nodes; nothing else.
- **Frontend**: Administration → Services tab *Integrations*; Master data app group *Integrations* (Mailboxes, API
  clients, Import profiles); Integrations app view *Received*; Transport app view *Intake*; the order page's source link.

Out of scope: FTP/SFTP, WhatsApp, SMS and other sources (the channel layer is built so each is one adapter more); push
notifications from Gmail (Pub/Sub watch) and Microsoft Graph (subscriptions), polling is used; importing the mailbox's
backlog from before the connection; a rule editor for import profiles (they are read-only in the UI, corrected through
the review); a second language-model vendor; carrier invoices, proof of delivery and other targets than the transport
order (the pipeline is target-generic, only the transport-order target exists); the LOS app and decision agents.

## Tenant dimension

Everything is tenant data in the tenant's own database except: the OAuth client registrations and the Anthropic key (host
settings, global); the OpenIddict application of an API client (host database, as ABP keeps them); the AI usage records
(host database, AP-05). The tenant comes from the **credentials**, never from a payload (07-ingest rule): a mailbox sync
runs per connection row inside its tenant; the OAuth callback carries the tenant in a data-protected `state`; an API call
carries it in the access token, put there by the token server from the API client's application, never from `__tenant`.

## Expected volumes

Per tenant: 1–20 mailboxes, 10–2,000 order documents a day, a spreadsheet up to 5,000 rows, a PDF up to 50 pages.
New layouts: a handful a week per tenant after the first month. Fleet target 100–1,000 tenants (AD-03).

## Known edge cases

A mail with a body "see attachment" and two PDFs, one of them general terms; a spreadsheet with 300 orders of 1–4 rows
each; a scanned PDF without a text layer; an XML with namespaces; the same mail delivered twice (Gmail label removed and
added again; Graph moves change ids); a refresh token revoked by the user or an admin; a customer changing their layout;
an address that is not in master data yet; a customer sending from several addresses; an API party retrying after a
timeout; a document that is not an order (newsletter, out-of-office); a tenant without the AI feature.

## Privacy note

Order documents contain names, addresses, phone numbers and e-mail addresses of contact persons (personal data,
constitution rule 5). They are stored as raw files in the tenant's blob container and as extracted values in the tenant's
database. With the tenant feature `Integrations.OrderIntake.AiMapping` on, **the content of an unknown document and the
names of the tenant's order types, waypoint types and transport units are sent to Anthropic** (Messages API; no training
on API data; Anthropic's standard retention). Relation and address master data are never sent. Switching the feature on
is the tenant's decision. Logs carry ids, never addresses, subjects or file content. The OAuth refresh tokens are
encrypted at rest (`IStringEncryptionService`) and never leave the backend. Retention: see *Assumptions*.

---

# Design: order intake

## Domain impact

Architecture: AD-01 (the core reads no mailbox — Integrations' channels do, outside the order core), AD-04 (the import
pipeline is a **boundary agent**: unstructured input in, a command out; no autonomy ladder), AD-15 (an incoming order is
*to be interpreted*, so it is **not ingest**: it goes through the boundary agent to a command — flow 1, steps 1–3 of
`02-flows.md`), AD-09 (documents in blob storage; the database blob provider is still wired, so raw files land in the
tenant database until Cloud Storage is built), AD-11 (the pipeline belongs to the Agents role; until roles exist it runs as
Hangfire jobs in the one host), AD-13 (Hangfire), AD-16 (events through the outbox), AD-18 (application services are the
command layer), **AD-21 (new, this design): the language-model vendor is Anthropic** (AO-05 closed by the PO on
2026-11-03; a vendor abstraction keeps a second vendor a configuration and adapter change).

Flow extended: **flow 1** (the agent loop), steps 1–3. Decision agents are not part of this design.

Module boundary rule 3 — the synchronous cross-module calls this design states:
1. Integrations → Orders `IOrderIntakeService.SubmitAsync` (published, in-process): hand a mapped intake to Orders in
   the pipeline's transaction. Chosen over an event because the pipeline must know at once whether Orders accepted it
   and the API returns that result to the caller.
2. Integrations → Orders `IOrderIntakeService.GetReviewedPayloadAsync` (published, in-process): read the values a clerk
   corrected, to repair an import profile.
3. Integrations → Orders `IOrderIntakeService.GetLearningContextAsync`: the names of the tenant's order types, waypoint
   types and transport units the AI may choose from.
4. Integrations → Orders `ITransportOrderLookup.GetAsync` (exists): the order number for the trace.
5. Orders → Master Data `IMasterDataMatchQuery` (new, published, in-process) and `IMasterDataReferenceLookup` (exists).

Events (AP-06 names, ETOs carry ids only): Orders publishes `OpenTms.Orders.OrderIntakeCompleted`
(`OrderIntakeCompletedEto { IntakeId, SourceDocumentId, IntakeIndex, TransportOrderId, Confirmed, Corrected }`),
`OpenTms.Orders.OrderIntakeRejected` (`{ IntakeId, SourceDocumentId, IntakeIndex }`) and
`OpenTms.Orders.IntakeOrderConfirmed` (`{ IntakeId, SourceDocumentId, TransportOrderId, EditedAfterIntake }`, when a user
confirms a Draft that came from an intake). Integrations handles them (idempotently, on the step log and the profile
counters). Integrations publishes `OpenTms.Integrations.InboundMessageReceived` (`{ InboundMessageId }`) for later
consumers; nothing handles it in this run.

Project references: Integrations.Application → Orders.Application.Contracts (exists for no one yet; added),
Integrations.Domain.Shared → Orders.Domain.Shared (for the ETOs). Orders never references Integrations.

### Assumptions (PROPOSED items relied on)

- **AP-04** (agents send commands through application services, not a database): the pipeline calls Orders' published
  intake service in-process; when the Agents role is split off, that call becomes an HTTP call to the Api role.
- **AP-05** (every agent run metered, data in the host database): built here as `integrations."AiUsageRecords"`.
- **AP-16** (vendor adapters in Integrations).
- **AP-15** (events raised by ingest go through the outbox) is not relied on: this is not ingest.

## The pipeline

```
 Channel adapter            Inbound store                Import pipeline (job per message)              Orders
 ───────────────            ─────────────                ─────────────────────────────────              ──────
 Gmail (history API)   ─┐   InboundMessage               1 Extract each document → ExtractedDocument
 Microsoft 365 (delta) ─┼─▶ InboundDocument(s)      ──▶  2 Fingerprint + sender key
 API: documents        ─┘   raw file in blob             3 Match an import profile → run it (no AI)
                            step "Received"              4 No profile: one AI call per message
 API: JSON order ───────────────────────────────────▶      (classify, extract, propose profile) → verify
                                                         5 Map → OrderIntakeDto[] with provenance     ─▶ OrderIntake
                                                         6 SubmitAsync per intake                        resolve → order
                                                         step log on every step ◀── events ───────────  (Draft/Confirmed)
                                                                                                         or NeedsReview
```

### 1. Channels (Integrations `Application/OrderIntake/Channels`)

**Mailbox connection** (`MailboxConnection`, aggregate, `IMultiTenant`, full audited):
`Provider` (`MailProvider { Gmail = 1, Microsoft365 = 2 }`), `DisplayName` (128), `EmailAddress` (320, from the
provider after consent), `FolderId` (256)?, `FolderName` (256)?, `RefreshTokenEncrypted` (4096)?, `Status`
(`MailboxStatus { AwaitingConsent = 1, AwaitingFolder, Active, Paused, Reconnect }`), `SyncCursor` (2048)?,
`ConnectedAt`?, `LastSyncAt`?, `LastErrorCode` (128)?, `LastErrorAt`?, `DefaultCustomerRelationId`?,
`DefaultTransportOrderTypeId`?, `AutoConfirm` (bool, default false). Unique (TenantId, Provider, EmailAddress, FolderId)
among undeleted rows.

- **Connect**: `POST /api/integrations/mailbox-connection` creates the row (`AwaitingConsent`) and returns the
  provider's authorize URL. `state` = data-protected (purpose `OpenTms.Integrations.MailboxOAuth`, 15 minutes) `{tenantId,
  connectionId, userId, nonce}`. The browser goes to Google/Microsoft; the anonymous callback
  `GET /api/integrations/mailbox-oauth/callback?code&state` unprotects the state, changes to its tenant, exchanges the
  code, reads the address, stores the encrypted refresh token, sets `AwaitingFolder` and redirects to
  `{App:ClientUrl}/master-data/integrations/mailboxes?connected={id}` (or `?connectError={code}`). A state that does not
  unprotect, is expired, or names a connection not in `AwaitingConsent`/`Reconnect` is refused (400 page, no redirect to
  the SPA with data).
- **Choose folder**: `GET …/{id}/folders` lists the provider's labels (Gmail: user labels and INBOX) or mail folders
  (Microsoft: recursive to depth 3, `displayName` paths "Inbox/Orders"); `PUT …/{id}` sets folder, name, defaults,
  auto-confirm. Setting a folder takes the provider's current cursor (Gmail `historyId` of `users/me/profile`; Graph: the
  delta link after paging the folder's current contents **without importing them**) and sets `Active`: only mail that
  arrives in the folder after activation is imported.
- **Scopes**: Gmail `https://www.googleapis.com/auth/gmail.readonly` (offline access, `prompt=consent`); Microsoft
  `offline_access https://graph.microsoft.com/Mail.Read https://graph.microsoft.com/User.Read`, authority `common`.
  Read-only: nothing is moved, labelled or marked read.
- **Sync** (`MailboxSyncWorker`, periodic, `Integrations:OrderIntake:SyncPeriod` default 60 s,
  `Integrations:OrderIntake:SyncEnabled` per instance; visits every tenant like `TemplateMaterialisationWorker`; nothing
  runs for a tenant without `Integrations.OrderIntake`). Per `Active` connection, under a distributed lock
  `mailbox-sync:{id}`: refresh the access token (Microsoft rotates the refresh token: store the new one), list new message
  ids since the cursor (Gmail `users/me/history?startHistoryId&labelId&historyTypes=messageAdded,labelAdded`; Graph delta
  link), fetch each raw RFC 822 message (Gmail `format=raw`; Graph `/me/messages/{id}/$value`), store it, advance the
  cursor after each stored message. Gmail 404 on history (cursor too old) → list `label:<id> after:<lastSync − 1 day>` and
  dedupe. `invalid_grant` → `Reconnect` (the UI shows *Reconnect*; nothing is lost, the cursor stays). Other errors →
  `LastErrorCode`, retried next period. Each connection runs in its own unit of work; one failing connection never stops
  the others.
- **Dedupe**: `InboundMessage` unique (TenantId, Channel, SourceId, ExternalId); ExternalId = Gmail message id, or the
  `Message-ID` header (Graph ids change on move), falling back to the Graph id.

**API client** (`ApiClient`, aggregate, `IMultiTenant`, full audited): `Name` (128), `ClientId` (64, `otms_` + 24 random
base32 chars, unique globally), `IsActive`, `CustomerRelationId`? (orders from this client are this customer's),
`DefaultTransportOrderTypeId`?, `AutoConfirm` (default false), `SecretLastFour` (4), `SecretCreatedAt`, `LastUsedAt`?.
Creating one creates an **OpenIddict application** in the host database (client type confidential, grant type
`client_credentials`, permission scope `OpenTms`, properties `opentms_tenant_id` and `opentms_api_client_id`) and grants
the client the permission `Integrations.IntakeApi.Submit` (ABP client permission, provider `C`, stored host side). The
secret (40 random chars) is shown once in the create/regenerate answer and never again. Deactivate removes the client
permission grant and sets `IsActive=false`; delete also deletes the OpenIddict application. Regenerate replaces the
secret.

**Tokens for API clients**: `ApiClientTenantClaimsPrincipalHandler` (an `IAbpOpenIddictClaimsPrincipalHandler`, host)
runs for every token: when the grant is `client_credentials` it reads the application's `opentms_tenant_id`, **removes
any tenant claim** and adds `tenantid` from the application, plus `opentms_api_client_id`. A client without that property
(the SPA, Swagger) is untouched. The API resolves the tenant from the token's claim (ABP's claims resolver runs first).

**Intake API** (`IntakeApiController`, route root `/api/integrations/intake`, authorize
`Integrations.IntakeApi.Submit`, feature `Integrations.OrderIntake`, the current principal must carry
`opentms_api_client_id` of an active client of the current tenant):
- `POST /api/integrations/intake/orders` — body `IntakeOrderRequestDto { ExternalId (required, ≤ 200, the caller's
  idempotency key), Order: OrderIntakeDto }`. Recorded as an `InboundMessage` (channel `Api`) with one document (kind
  `ApiJson`, format `Json`, the body as the raw file) and processed **in the request** (no AI, no profile: the body is
  already the canonical model). Answer `IntakeResultDto { InboundMessageId, Status, Intakes: [{ IntakeId?, Status,
  TransportOrderId?, OrderNumber?, Unresolved: [fieldPath] }] }`, 200. The same ExternalId again returns the stored result
  (200, nothing new).
- `POST /api/integrations/intake/documents` — multipart `file` (≤ 25 MB) + `externalId` (required). Recorded like a mail
  with one attachment and processed by the pipeline job; answer 202 `{ InboundMessageId, Status }`.
- `GET /api/integrations/intake/messages/{externalId}` — the same `IntakeResultDto` for that message.

### 2. Inbound store (Integrations `Domain/OrderIntake`)

`InboundMessage` (aggregate, `IMultiTenant`, creation audited): `Channel` (`InboundChannel { Gmail = 1, Microsoft365,
Api }`), `SourceId` (Guid: connection or API client), `ExternalId` (512), `From` (320)?, `Subject` (512)?, `ReceivedAt`,
`Status` (`InboundMessageStatus { Received = 1, Processing, Completed, NeedsAttention, Failed, NotAnOrder }`),
`RawBlobName` (256)?, `SizeBytes`. Status is the roll-up of its documents.

`InboundDocument` (aggregate, `IMultiTenant`, creation audited): `InboundMessageId`, `Kind` (`InboundDocumentKind {
MailBody = 1, Attachment, ApiJson, ApiFile }`), `FileName` (256)?, `ContentType` (128), `SizeBytes`, `Sha256` (64),
`BlobName` (256), `Format` (`DocumentFormat { Unknown = 0, Text, Html, Pdf, ScannedPdf, Xlsx, Csv, Xml, Json }`),
`SenderKey` (320)?, `Fingerprint` (64)?, `ImportProfileId`?, `ImportProfileVersion`?, `Status`
(`InboundDocumentStatus { Pending = 1, Mapped, Submitted, Completed, NeedsReview, NeedsAttention, NotAnOrder, Skipped,
Failed }`), `MappingMethod` (`MappingMethod { None = 0, Profile, AiVerifiedProfile, AiExtraction, Canonical }`),
`Mapping` (jsonb: `DocumentMapping { Intakes: [ { Index, Payload: OrderIntakeDto, Provenance: { fieldPath →
SourceLocation { Kind, Ref, Snippet (≤ 120) } }, Confidence } ] }`), `IntakeCount`, and the child collection **`Steps`**
(`InboundDocumentStep`: `At`, `Step` (enum below), `Outcome` (`StepOutcome { Info = 1, Success, Warning, Error }`),
`MessageKey` (localization key), `Parameters` (jsonb, string map, no file content), `UserId`?, `ModelId` (64)?,
`InputTokens`?, `OutputTokens`?, `CostMicroUsd`?, `IntakeIndex`?, `IntakeId`?, `TransportOrderId`?, `OrderNumber`
(32)?).

`IntakeStep { Received = 1, Stored, Extracted, Fingerprinted, ProfileMatched, ProfileNotFound, ProfileFailed,
AiRequested, AiCompleted, AiFailed, ProfileProposed, ProfileVerified, ProfileVerificationFailed, NotAnOrder, Mapped,
Submitted, OrderCreated, NeedsReview, Resolved, Rejected, OrderConfirmed, Skipped, Failed, Reprocessed }`.

Body documents: the text part (or the HTML part converted to text) of a mail is a document of kind `MailBody` when it
has more than 40 non-whitespace characters after removing the quoted reply and signature separator (`-- `). Attachments
of a supported format become documents; others (images, calendar invites, `.eml` inside) are listed as `Skipped` with
their name. Inline images are ignored.

Blob container `InboundDocumentContainer` (`opentms-inbound`), name `{messageId}/{documentId}`; the raw mail as
`{messageId}/message.eml`. Limits: 25 MB per document, 40 MB per mail (larger: `Failed`, step with the reason).

### 3. Extraction and fingerprint (Integrations `Application/OrderIntake/Extraction`)

`IDocumentExtractor` per format produces an `ExtractedDocument`:
- **Pdf**: text with PdfPig (`UglyToad.PdfPig`), words grouped into lines top-to-bottom, left-to-right, columns separated
  by two spaces; pages separated by a form feed line. Fewer than 20 characters per page on average → `ScannedPdf`.
- **Xlsx**: ClosedXML; per sheet the used range as rows of cell texts (formatted value), with A1 references.
- **Csv/Txt**: delimiter sniffed (`;`, `,`, tab, `|`), encoding detected (BOM, else UTF-8, else Windows-1252).
- **Xml**: the DOM (namespaces kept), XXE off, DTD processing prohibited, max 20 MB.
- **Html/Text**: normalised text lines.
- **Json** (API): the canonical model, no extraction.

The **normalised text** (lines numbered from 1) is the only text rules and the AI refer to, so a profile run and the AI see
the same thing. **Fingerprint** (for the trace and for profile lookup): xlsx/csv: SHA-256 of the sheet names and the
normalised header row; xml: of the root's namespace and name plus the sorted distinct element paths; text/pdf: of the
sorted set of "label lines" (lines whose text without digits and punctuation is 2–40 letters, occurring once); stored with
a short description in the step. **Sender key**: the lowercased From address; `api:{clientId}` for the API.

### 4. Import profiles (Integrations `Domain/OrderIntake/Profiles`)

`ImportProfile` (aggregate, `IMultiTenant`, full audited): `Name` (128), `SenderKey` (320)? (an address, `@domain`, or
null for any sender), `Format`, `Target` (64, `Orders.TransportOrder`), `Status` (`ImportProfileStatus { Proposed = 1,
Active, Retired }`), `Version`, `Origin` (`ProfileOrigin { Ai = 1, Repair }`), `ModelId`?, `Rules` (jsonb,
`ImportProfileRules`), `Signature` (jsonb, string list: the headers, element paths or labels that must all be present),
`ConfirmedCount`, `CorrectedCount`, `LastUsedAt`?, `SourceDocumentId` (the document it was learned from). A version change
creates the next version on the same row (`Version++`, counters reset, previous rules kept in `PreviousRules` jsonb for
the trace).

**Matching**: candidates = profiles not `Retired` with the document's format and `SenderKey` ∈ {sender address,
`@` + its domain, null}, most specific first, then `Active` before `Proposed`, then highest `ConfirmedCount`; the first
whose whole `Signature` is present in the document and whose run passes validation is used.

**Rules** (`ImportProfileRules`, executed by `ImportProfileExecutor`, deterministic, no I/O):

```json
{
  "orders":  { "scope": { "kind": "document" | "rowGroup" | "xpath" | "textSplit", "value": "…", "groupBy": ["Ordernr"] } },
  "lines":   { "scope": { "kind": "single" | "row" | "xpath" | "textBlock", "value": "…" } },
  "stops":   [ { "role": "Pickup" | "Delivery", "scope": { "kind": "inherit" | "xpath", "value": "…" } } ],
  "fields":  [ { "target": "order.customerReference" | "line.weightKg" | "stop[Pickup].address.postalCode" | …,
                 "source": { "kind": "column" | "cell" | "xpath" | "label" | "regex" | "constant",
                             "value": "…", "group": 1, "occurrence": 1 },
                 "transforms": [ { "kind": "trim" | "date" | "time" | "number" | "map" | "split" | "join" | "upper",
                                   "format": "dd-MM-yyyy", "culture": "nl-NL", "map": { "EUR-pallet": "Europallet" },
                                   "separator": " ", "index": 0 } ] } ],
  "sheet": "Orders",
  "headerRow": 1,
  "namespaces": { "o": "urn:…" }
}
```

`column` = header text of the scope row (tabular); `cell` = an A1 reference (form-like spreadsheets); `xpath` = relative
to the scope node; `label` = the value after a label on the same line (or the next line when the label ends the line),
up to two spaces or the line end; `regex` = .NET regex (timeout 100 ms, `RegexOptions.None`) with a capture group over
the scope text; `constant` = a literal. Targets are the `OrderIntakeDto` paths (below). The executor returns the intakes
plus, per field, the `SourceLocation` it read (`B7`, `/Order[2]/Lines/Line[1]/Weight`, `line 14`) — the provenance of the
trace.

**Validation** of a run: every intake has a customer reference or a customer, at least one line, a Pickup and a
Delivery stop each with a postal code or city and a date; dates parse; numbers are ≥ 0. A run that fails validation on a
matched profile is `ProfileFailed` (layout drift) and the document goes to the AI path; the profile stays as it is.

**Activation**: a profile starts `Proposed`. Each `IntakeOrderConfirmed` with `EditedAfterIntake = false` and each
`OrderIntakeCompleted` resolved by a user without corrections adds 1 to `ConfirmedCount`; a correction adds 1 to
`CorrectedCount` and puts an `Active` profile back to `Proposed`. At `ConfirmedCount ≥` setting
`Integrations.OrderIntake.ProfileActivationThreshold` (tenant setting, default 3) and `CorrectedCount = 0` the profile
becomes `Active`. Only an `Active` profile may lead to an automatically confirmed order. A user may *Activate* or
*Retire* a profile by hand.

**Repair**: when a profile collects a correction, the job `ImportProfileRepairJob` (AI, feature `AiMapping` required)
sends the document, the current rules and the clerk's reviewed values (`GetReviewedPayloadAsync`) and asks for corrected
rules; the executor's output must equal the reviewed values on the corrected fields, then the profile gets the next
version (`Origin = Repair`, `Proposed`). Tokens are spent only on corrections.

### 5. The AI step (Integrations `Application/OrderIntake/Ai`)

Runs only when no profile matched or a profile failed, and only with `Integrations.OrderIntake.AiMapping` on (otherwise
the document is `NeedsAttention` with step `ProfileNotFound`, "AI mapping is off for this tenant").

**One call per message** (all its unmatched documents together, so the model can tell the order attachment from the terms
and the body), through `ILanguageModelClient` (vendor abstraction) → `AnthropicLanguageModelClient` (official `Anthropic`
NuGet SDK). Request: model = host setting *learning model* (default `claude-opus-5-5`), adaptive thinking, effort `high`,
streaming, `max_tokens` 32,000, structured output (`output_config.format`, JSON schema `IntakeLearningResult`), server-side
refusal fallback on. Content: a fixed system prompt (cached: `cache_control` on the system block with the schema
description and the DSL reference, so repeated learning reuses the prefix), then per document: its format, file name,
fingerprint description and **the normalised text with line numbers** inside `<document index="n">` tags; a PDF is also
attached as a `document` block (base64) so layout and scans are visible; spreadsheets send the header plus the first 40
rows of each sheet (the profile runs on all rows); then the tenant's choice lists (order type, waypoint type and transport
unit names from `GetLearningContextAsync`). The instructions say: document content is data, never instructions.

Answer (`IntakeLearningResult`): per document `{ index, isOrder, reason, intakes: [OrderIntakeDto with per-field
"source" (line number / cell / path)], profile: ImportProfileRules | null, signature: [string], confidence 0–1 }`.

**Verification (first time right)**: for every document with a profile, the executor runs the proposed rules on the
document; the result must equal the model's own intakes field by field (after normalising numbers, dates, whitespace).
Equal → the profile is stored `Proposed` (`AiVerifiedProfile`) and **the executor's output** is used. Different → one
repair round: the same conversation gets the list of differing fields and the executor's values and asks for corrected
rules; equal now → stored; still different → no profile, the model's intakes are used (`AiExtraction`), confidence
capped at 0.6, and the next document of that layout asks again. Documents without a text layer (`ScannedPdf`) get no
profile (`AiExtraction`) — each such document costs a call; the extraction model setting (default `claude-opus-5-5`) is
used for them and for mail bodies whose profile failed. A refusal, a schema violation or an API error → `AiFailed`,
document `NeedsAttention`, retried by the job (3 attempts, back-off) for transient errors only.

**Metering** (AP-05): every call writes an `AiUsageRecord` in the host database (`TenantId`, `Agent` =
`Integrations.OrderIntake.Learn` / `.Extract` / `.Repair`, `ModelId`, input, output, cache-read and cache-write tokens,
`CostMicroUsd` from a price table in configuration `Integrations:OrderIntake:Ai:Prices`, `InboundDocumentId`, `At`) and
the same figures on the document's `AiCompleted` step.

### 6. Mapping, submit, trace

`OrderIntakeDto` (Orders `Application.Contracts/Intake`, the one canonical model for every channel and the API's JSON):

```
OrderIntakeDto
  customerReference?, orderDate? (yyyy-MM-dd), orderTypeName?, transportOrderTypeId?, remarks?
  customer?: { name?, code?, emailAddress?, vatNumber?, chamberOfCommerceNumber?, relationId? }
  references: [ { type, value } ]
  lines: [
    { transportUnitName?, transportUnitId?, quantity (default 1), colli?, positions?, weightKg?, volumeM3?,
      loadingMeters?, lengthCm?, widthCm?, heightCm?, productName?, temperatureName?, description?, remarks?,
      containerNumber?, barcode?,
      stops: [ { role: Pickup|Delivery, waypointTypeName?, date (yyyy-MM-dd)?, timeFrom? (HH:mm), timeTill?,
                 reference?, remarks?, contactName?, contactPhone?,
                 address: { name?, street?, houseNumber?, postalCode?, city?, countryCode?, fullText? },
                 relationId?, relationAddressId? } ] } ]
```

Field paths in provenance, resolution and the API's `Unresolved` list use `order.customerReference`,
`lines[0].stops[1].address.postalCode`, etc. Ids are optional everywhere: an API party that knows our ids may send them.

The pipeline calls `IOrderIntakeService.SubmitAsync(OrderIntakeSubmissionDto { SourceDocumentId, IntakeIndex, Channel,
SourceKind (`Import`|`Api`), SenderAddress?, Subject?, FileName?, Payload, MappingConfidence, ProfileTrusted (profile
Active), AutoConfirm (channel setting), DefaultCustomerRelationId?, FixedCustomerRelationId? (API client),
DefaultTransportOrderTypeId? })` once per intake, in the job's transaction. The answer (`IntakeId`, status, order id and
number, unresolved paths) becomes the `Submitted` and `OrderCreated`/`NeedsReview` steps.

**Reprocess** (`POST /api/integrations/inbound-documents/{id}/reprocess`): runs the document through the pipeline again
(for example after the AI feature was switched on or a profile retired); intakes Orders already holds are not created twice
(Orders' unique key returns the existing intake).

## Orders: intake, resolution, review

`OrderIntake` (aggregate, `IMultiTenant`, full audited): `SourceDocumentId`, `IntakeIndex` (unique with it),
`Channel` (32), `Source` (`OrderSource`), `SenderAddress` (320)?, `Subject` (512)?, `FileName` (256)?, `Payload`
(jsonb, `OrderIntakeDto`, as received), `ReviewedPayload` (jsonb)? (the clerk's corrected values), `Resolution` (jsonb:
per field path `{ Status: Resolved|Ambiguous|Unresolved, TargetId?, Method: Given|Exact|Alias|Match|Default|Pattern|User,
Candidates: [{ id, name, detail }] (≤ 5) }`), `Status` (`OrderIntakeStatus { NeedsReview = 1, OrderCreated, Rejected }`),
`TransportOrderId`?, `MappingConfidence`, `ProfileTrusted`, `AutoConfirm`, `DefaultCustomerRelationId`?,
`FixedCustomerRelationId`?, `DefaultTransportOrderTypeId`?, `RejectReason` (512)?, `Corrected` (bool).

`OrderSource` gains `Import = 5` (mail and documents); the API's JSON and documents use `Api`. `TransportOrder` gains
`SourceIntakeId`? and `EditedAfterIntake` (bool; set by every update command while the order is a Draft that came from an
intake). Both on the order DTO.

**Resolution** (`OrderIntakeResolver`, deterministic, no AI):
- *Customer*: `FixedCustomerRelationId` → given `relationId` (validated) → alias → relation code → VAT number → chamber of
  commerce number → e-mail address (relation e-mail or a contact person's e-mail, exact; then the domain when one relation
  owns it) → normalised name (exactly one) → `DefaultCustomerRelationId` → unresolved with candidates (name similarity).
- *Order type*: given id → alias → name (exact, any language) → channel default → the tenant's only active type →
  unresolved.
- *Stop address*: given ids → alias (scoped to the customer, then tenant-wide) → postal code + house number (+ country) →
  name + city (exactly one) → unresolved with candidates. The relation is the address's relation.
- *Waypoint type*: given name/alias → the order type's default waypoints: role Pickup = first waypoint whose activity is
  Load, Delivery = first with Unload → any waypoint type with that activity (exactly one) → unresolved.
- *Transport unit, product, temperature*: given id → alias → code or name exact → unresolved (transport unit is required;
  product and temperature are left empty when unresolved and reported as a warning).
- *Dates*: a stop without a date takes the order date when the profile/AI gave one, else unresolved.
- `IMasterDataMatchQuery` (Master Data, published): `FindRelationsAsync(RelationMatchQuery { Code?, VatNumber?,
  ChamberOfCommerceNumber?, EmailAddress?, Name? })` and `FindRelationAddressesAsync(AddressMatchQuery { RelationId?,
  PostalCode?, HouseNumber?, CountryCode?, Name?, City?, Street? })`, each returning ≤ 10 candidates with a match kind;
  both tenant-filtered, archived rows excluded, inputs bounded.

All required fields resolved → the order is created through the existing `TransportOrderCommands.CreateAsync` with
`Source`, `DraftConfidence = MappingConfidence`, `SourceIntakeId`, confirmed at once **only when** `ProfileTrusted &&
AutoConfirm && every field resolved by Given/Exact/Alias/Pattern/Default` and the Confirm rules pass (a Confirm failure
leaves a Draft); otherwise a Draft. Intake status `OrderCreated`, event `OrderIntakeCompleted`. Anything unresolved →
`NeedsReview` (no order yet).

**Review** (Transport app, view *Intake*): the queue of `NeedsReview` intakes and the history. Opening one shows the
values as received with the resolution per field; the clerk picks a relation, address, type or unit (lookups with *Add
new…*, UI-LOOK-004; *Add new…* on an address opens the relation dialog prefilled with the received name and address) and
may correct received values. *Create order* / *Create and confirm* re-runs the resolver with the picks; every pick on a
field that was Ambiguous or Unresolved is stored as an **alias** (`IntakeAlias { Kind (Customer, Address, OrderType,
WaypointType, TransportUnit, Product, Temperature), ScopeRelationId?, NormalizedText (512), TargetId, TargetRelationId?,
UseCount }`, unique per tenant, kind, scope and text), so the next intake with the same text resolves without a person.
A corrected received value sets `Corrected` and stores `ReviewedPayload`. *Reject* (with a reason) closes it
(`OrderIntakeRejected`). *View source* opens the Integrations trace.

Orders API (`/api/orders/order-intake`): `GET` list (status, received from/to, search over sender, subject, file name,
customer reference), `GET /{id}`, `POST /{id}/resolve` `{ picks: { fieldPath → id }, reviewedPayload?, confirm }`,
`POST /{id}/reject` `{ reason }`, `GET /aliases` + `DELETE /aliases/{id}` (master data: *Intake aliases* is not a nav
node in this run; the list is reachable from the review page).

## Permissions (all new; full list)

Integrations (`IntegrationsPermissions`):
- `Integrations.Mailboxes` (tenant) — see mailbox connections
- `Integrations.Mailboxes.Manage` (tenant) — connect, reconnect, change, pause, delete
- `Integrations.ApiClients` (tenant) — see API clients
- `Integrations.ApiClients.Manage` (tenant) — create, change, regenerate secret, deactivate, delete
- `Integrations.ImportProfiles` (tenant) — see import profiles
- `Integrations.ImportProfiles.Manage` (tenant) — activate, retire, delete
- `Integrations.InboundMessages` (tenant) — see received messages, their trace, download originals
- `Integrations.InboundMessages.Reprocess` (tenant) — reprocess a document
- `Integrations.IntakeApi.Submit` (both sides; granted only to API clients, never to a role) — use the intake API

Orders (`OrdersPermissions`):
- `Orders.OrderIntakes` (tenant) — see the intake queue
- `Orders.OrderIntakes.Resolve` (tenant) — resolve and create orders, delete aliases
- `Orders.OrderIntakes.Reject` (tenant) — reject an intake

Host settings use the existing `Integrations.ServiceSettings`, `Integrations.ServiceSettings.Update`,
`Integrations.ServiceSettings.Test`.

Predefined roles: *Order entry* gets `Orders.OrderIntakes`, `Orders.OrderIntakes.Resolve`, `Orders.OrderIntakes.Reject`,
`Integrations.InboundMessages`; *Planner* gets `Orders.OrderIntakes`, `Integrations.InboundMessages`; *Master data
manager* gets `Integrations.Mailboxes`, `Integrations.Mailboxes.Manage`, `Integrations.ApiClients`,
`Integrations.ApiClients.Manage`, `Integrations.ImportProfiles`, `Integrations.ImportProfiles.Manage`,
`Integrations.InboundMessages`, `Integrations.InboundMessages.Reprocess`; *Read-only* gets `Orders.OrderIntakes`,
`Integrations.InboundMessages`, `Integrations.Mailboxes`, `Integrations.ApiClients`, `Integrations.ImportProfiles`. The
tenant `admin` role gets all tenant-side ones (seeded like every module's). The *Customer Success Manager* works through
impersonation and needs none.

## Features (full list)

Behaviour (constitution rule 4, default **off**, per tenant):
- `Integrations.OrderIntake` — mailboxes sync, the intake API answers, the pipeline runs
- `Integrations.OrderIntake.AiMapping` (child) — unknown documents may be sent to Anthropic

App features (one per new nav node, default on like their siblings; a node only shows when its ancestors are on and
the screens additionally check `Integrations.OrderIntake`):
- `Apps.Integrations.Received` — Integrations app → Received
- `Apps.Transport.Intake` — Transport app → Intake
- `Apps.MasterData.Integrations` — Master data app → group Integrations
- `Apps.MasterData.Integrations.Intake` — group Order intake under it
- `Apps.MasterData.Integrations.Intake.Mailboxes` — Mailboxes
- `Apps.MasterData.Integrations.Intake.ApiClients` — API clients
- `Apps.MasterData.Integrations.Intake.ImportProfiles` — Import profiles

## Host settings: Administration → Services → Integrations

A new tab **Integrations** (the PO's word) next to E-mail, Documents and Routing, same model as the others (global
settings in the host database, secrets encrypted and write-only, empty = deployment configuration, saved values apply
without a redeploy through `ServiceSettingsOverlay`, *Test* per section):

| Section | Settings (`Integrations.Services.…`) | Test |
|---|---|---|
| AI (Anthropic) | `Ai.ApiKey` (encrypted), `Ai.LearningModel` (default `claude-opus-5-5`), `Ai.ExtractionModel` (default `claude-opus-5-5`) | `GET /v1/models/{learning model}` |
| Google (Gmail) | `Google.ClientId`, `Google.ClientSecret` (encrypted) | token endpoint with a dummy code: `invalid_grant` = client accepted, `invalid_client` = wrong |
| Microsoft 365 | `Microsoft.ClientId`, `Microsoft.ClientSecret` (encrypted) | the same against `login.microsoftonline.com/common` |

The page shows the redirect URI to register at Google and Microsoft (read-only):
`{App:SelfUrl}/api/integrations/mailbox-oauth/callback`. The tenant setting
`Integrations.OrderIntake.ProfileActivationThreshold` (1–20, default 3) is set per tenant through ABP settings (no screen
in this run).

## UI design

- **Master data → Integrations → Order intake → Mailboxes** (`OpenTmsGrid` `integrations.mailboxes`): name, provider,
  address, folder, status (badge), last sync, last error. Commands *Connect Gmail*, *Connect Microsoft 365* (each asks a
  display name in a small dialog, then navigates to the provider). Returning with `?connected=` opens the editor at the
  folder step. Editor (`EntityDialog`): name, folder (select from the provider's list), default customer (relation
  lookup with *Add new…*), default order type, auto-confirm switch with its explanation, status. Row actions: *Reconnect*
  (for `Reconnect`), *Pause*/*Resume*, *Delete* (`ConfirmDialog`; revokes nothing at the provider, says so).
- **API clients** (`integrations.apiClients`): name, client id, customer, active, last used. Create/regenerate shows a
  one-time dialog with the client id, the secret (copy buttons), the token URL `{SelfUrl}/connect/token`, the scope
  `OpenTms` and an example `curl`. Row actions *Regenerate secret*, *Deactivate*/*Activate*, *Delete*.
- **Import profiles** (`integrations.importProfiles`): name, sender, format, status, version, confirmed, corrected,
  last used. Editor (read-only rules): a table *field ← source → transforms* and the signature, the document it was
  learned from (link to the trace), *Activate*, *Retire*, rename.
- **Integrations app → Received** (`integrations.inboundMessages`): received, channel, mailbox or API client, from,
  subject, documents, status; filters status and channel. Opening a row opens the **trace page**
  `/integrations/received/{messageId}`: header (channel, source, from, subject, received), the documents as tabs; per
  document: download original, format, fingerprint, profile used (link), the **timeline of steps** (time, step text,
  outcome icon, tokens and cost on AI steps, user on manual steps), and per intake a **field table**: field, value,
  source location (cell, line + snippet, path), resolution status, with links *Intake* (`/transport/intake/{id}`) and
  *Order* (`/transport/orders/{id}`). Command *Reprocess* (permission). This page is what a CSM sees through
  impersonation.
- **Transport app → Intake** (`orders.orderIntakes`): received, channel, sender, subject/file, customer (resolved name or
  received text), status, open fields; default filter *Needs review*. Review page `/transport/intake/{id}`: received
  values grouped as order / lines / stops, each with its resolution (green resolved, amber ambiguous with candidates,
  red unresolved) and a picker; *Create order*, *Create and confirm*, *Reject*, *View source*.
- **Order page**: the source chip (*Import*, *API*) links to the intake and the trace.

Every string through ABP localization (Integrations and Orders resources, nl and en). Manual pages (Dutch):
`docs/manual/orderinvoer-mailboxen-en-api.md` (connecting, API clients, profiles, the trace) and the *Intake* section of
`docs/manual/transport.md`.

## Migration strategy

Additive only (expand): Integrations context `AddOrderIntake` (MailboxConnections, ApiClients, InboundMessages,
InboundDocuments, InboundDocumentSteps, ImportProfiles, AiUsageRecords); Orders context `AddOrderIntake` (OrderIntakes,
IntakeAliases, `TransportOrders.SourceIntakeId` nullable, `TransportOrders.EditedAfterIntake` default false). Indexes:
InboundMessages (TenantId, Channel, SourceId, ExternalId) unique, (TenantId, ReceivedAt desc), (TenantId, Status);
InboundDocuments (TenantId, InboundMessageId), (TenantId, ImportProfileId); ImportProfiles (TenantId, Format, SenderKey);
MailboxConnections (TenantId, Status); ApiClients (ClientId) unique; AiUsageRecords (TenantId, At); OrderIntakes
(TenantId, SourceDocumentId, IntakeIndex) unique, (TenantId, Status, CreationTime desc); IntakeAliases (TenantId, Kind,
ScopeRelationId, NormalizedText) unique `NULLS NOT DISTINCT`; TransportOrders (TenantId, SourceIntakeId).

## Test risk analysis

| Area | Risk | Tests |
|---|---|---|
| API client token gets the tenant from its application, never from `__tenant`; a client of tenant A cannot submit for B | **Critical** | host integration test through `/connect/token` (A's client with `__tenant=B` still lands in A); permission without grant → 403; inactive client → 403 |
| OAuth state: tampered, expired, other tenant's connection | **Critical** | unit tests on the state protector and callback service |
| Tenant isolation of every new repository and app service (connections, clients, messages, documents, profiles, intakes, aliases, match query) | **Critical** | negative tests: tenant B sees none of A's rows, by id or by list |
| Refresh tokens encrypted, never in a DTO or log | High | test on the stored value and the DTOs |
| Profile executor across formats (xlsx row groups, cells, csv, xml with namespaces, pdf label/regex, transforms) | High | unit tests with fixture documents |
| AI verification: equal → profile; different → repair round; still different → extraction; refusal/schema error → NeedsAttention | High | fake `ILanguageModelClient` |
| Resolver: every rule in order, aliases, auto-confirm only when trusted and complete | High | SQLite tests in Orders |
| Idempotency: same mail twice, same API ExternalId twice, reprocess, event handled twice | High | tests per case |
| Gmail/Graph adapters: paging, cursor, 404 history, invalid_grant → Reconnect, token rotation | High | fake `HttpMessageHandler` |
| Extraction: scanned PDF detection, encodings, XXE refused, size limits | Medium | unit tests |
| Screens (trace, review, mailboxes, API clients, profiles, services tab) | Medium | vitest view tests |

## Flag & rollout plan

Both behaviour features default off. Rollout: the PO registers the Google OAuth client (Gmail API enabled, consent screen,
**restricted scope `gmail.readonly` needs Google's verification and a CASA security assessment before production use
beyond test users**) and the Entra multi-tenant app registration (`Mail.Read`, `User.Read`, `offline_access`; publisher
verification advised), enters the ids, secrets and the Anthropic key in the host tab, then switches the features on per
tenant. Until Google's verification is done, Gmail works for up to 100 test users added to the consent screen.

## Cost & SLO impact

- **Tokens**: learning a new layout ≈ 10–20k input + 4–8k output tokens on Opus 5.5 ($4 / $20 per MTok) ≈ $0.10–0.25,
  a repair round about the same again; a known layout costs nothing. Scanned PDFs and free-text mails without a working
  profile cost one extraction call each (≈ $0.05–0.15). Estimates to verify with the metering table. Constitution rule 6:
  the tenant feature and the per-call metering make the cost visible per tenant.
- **Request budgets**: the intake API's JSON call resolves in the request (target p95 < 500 ms, a handful of indexed
  lookups); everything else is background. Screens read stored rows only.
- **Sync load**: one sweep per minute over all tenants with connections; a quiet mailbox costs one history/delta call.
  Known limit, as the outbox dispatcher's: beyond a few hundred connections the sweep needs partitioning or push
  notifications (follow-up).

## Assumptions

1. Raw files and extracted values are kept like other order data (no purge in this run); a retention period and purge
   job are a follow-up PO decision (privacy note).
2. Only mail arriving after a connection's activation is imported.
3. A spreadsheet row group or XML node is one order; a line has exactly one Pickup and one Delivery stop unless the
   profile defines more stop scopes.
4. AI usage records live in the host database, readable only by host operators (no screen in this run).
5. `App:SelfUrl` is the public URL of the API (used for the OAuth redirect and shown on the settings tab).

## Build notes (decided during the build, 2026-11-03)

1. **Routes follow the repo's singular convention**: `/api/integrations/mailbox-connection`, `/api/integrations/api-client`,
   `/api/integrations/import-profile`, `/api/integrations/inbound-message`; reprocess is
   `POST /api/integrations/inbound-documents/{id}/reprocess` (its own conventional service); aliases are
   `GET /api/orders/order-intake/aliases` and `DELETE /api/orders/order-intake/aliases/{aliasId}`.
2. **Matching is conservative**: a single exact match on a customer's normalised name or on an address's name + city is
   `Match`, never an automatic confirm; postal code + house number stays `Exact`. When a user confirms a Draft from an
   intake without editing it, every `Match` of that intake is stored as an alias (address aliases scoped to the customer),
   so the next intake with the same texts resolves by `Alias` and may auto-confirm.
3. **Create and confirm** in the review also requires `Orders.TransportOrders.Confirm`; Create (a Draft) requires only
   `Orders.OrderIntakes.Resolve`.
4. **A stop's contact name and phone** are appended to the stop's remarks ("Contactpersoon: …"); the order has no contact
   fields by text. A line's description goes into its remarks, the order's remarks into the internal remarks.
5. **Master Data match query** also offers `FindReferencesByNameAsync` (transport units, products, temperatures by name)
   and `GetReferenceNamesAsync` (the AI's choice lists). New index `Relations (TenantId, Email)`; trigram indexes on the
   intake list's search columns; `OrderIntakes` carries `CustomerReference`, `CustomerRelationId`, `CustomerName` and
   `OpenFieldCount` so the list reads no jsonb.
6. **The AI answer** returns each intake as a flat list of field path, value and source, and the profile rules as JSON
   text; the executor checks every path and rule. The AI call runs **outside** a database transaction (profiles and submit
   first, AI second, apply and submit third). Its usage figures go on the first document of the call. The model's free-text
   reason is not stored (personal data).
7. **Validation**: a stop without its own date passes when the intake has an order date. A spreadsheet the model saw only
   in part (40 rows) without a verified profile goes to NeedsAttention, never partly submitted.
8. **API JSON intakes count as trusted** (`ProfileTrusted = true`), so an API client with auto-confirm gets confirmed
   orders when everything resolves without a `Match`.
9. **A Draft confirmed after editing** (`EditedAfterIntake = true`) counts as a correction of its profile and queues a
   repair; the repair does nothing when there is no reviewed payload.
10. **Transient AI failures** are retried by the job twice more (counted on the step log); the third failure leaves the
    document NeedsAttention.
11. **API client tenant**: besides the token handler, `ApiClientTenantResolveContributor` runs first among the tenant
    resolvers and takes an API client's tenant from its token claim, because ABP's claim resolver only answers for a
    signed-in user. The intake API also checks that the token's `client_id` equals the client's own.
12. **Names in the mailbox and API client lists** (default customer, order type) are resolved by the screen from the
    lookups the Master data app already reads; the Integrations DTOs carry ids.
13. **OAuth redirect** goes to the SPA URL (`App:RedirectAllowedUrls`, first entry, as elsewhere); `App:SelfUrl` builds
    the redirect URI and the token URL.
14. **From an intake to its trace**: `GET /api/integrations/inbound-message/by-document/{documentId}` returns the message a
    document belongs to, so the review page and the order's source chip can open the trace. The trace page shows each
    intake's outcome from the step log; the field-level resolution stays on the review page (the Integrations app reads
    only the Integrations domain).
15. **AI usage records are host data without a tenant filter** (they carry the tenant id of the usage, AP-05). A shared-tier
    tenant lives in the host database, so no tenant-side code may ever read `AiUsageRecords`; a later usage screen is a
    host screen, or filters on `TenantId` explicitly.
