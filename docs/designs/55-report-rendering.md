# PBI: Report rendering through a provider abstraction, with pdfgeneratorapi.com as the first provider

<!-- Written from the PO's instruction of 2026-10-21 ("build your suggestions 1 to 4") and answers: one FiLogic
     pdfgeneratorapi account for all tenants; a shared default workspace plus one workspace per tenant, as OpenTMS;
     keep pdfgeneratorapi for now but the abstraction must allow other providers or in-house rendering later.
     Research of the OpenTMS integration (FiLogic.Application/DataExporting/ExportFileGenerators/PdfReportDefinition/,
     ReportDefinitionAppService.cs) of 2026-10-21. No GitHub issue; "50" is the next free number. -->

## Story

As a tenant administrator I want to open a report definition's layout in the pdfgeneratorapi editor, copy one of
FiLogic's standard layouts into my own workspace to adapt it, and render a test document, so that my invoices,
order confirmations and trip lists look the way my company wants before the modules that print them go live. As a
developer of the Orders, Planning and Financial runs I want one rendering service and a known data shape per report
purpose, so that printing a record is one call.

## Affected area

- **Platform** (owns document generation, CLAUDE.md domain map): the provider abstraction
  `IReportRenderingProvider`, the published `IDocumentRenderer`, the report data catalogue, and three operations on
  report definitions (open template editor, copy template, test render).
- **Integrations** (owns external links and their anti-corruption layer): `PdfGeneratorApiRenderingProvider`, its
  configuration, and a health check.
- **Master data app**: three ribbon commands in the report definition editor.

Out of scope: storing generated documents (AD-09, attachments GAP-05), sending them, rendering real records (the
entities arrive with the Orders, Planning and Financial runs), a template picker over the provider's template list,
in-house rendering (the abstraction allows it; a later design adds it).

## Tenant dimension

The provider account is FiLogic's, one for all tenants (PO 2026-10-21): host configuration, not tenant data. Each
tenant works in its own provider workspace; the host works in the default workspace. Feature
`Platform.DocumentRendering` (default **off**) gates every provider call per tenant (constitution rule 4). The
feature is defined in Platform, not Integrations, because Platform's renderer checks it and Platform references no
domain module (CLAUDE.md, module boundary rule 4).

## Expected volumes

Template editor opens and copies: a few per tenant per month. Test renders: tens per tenant per month, marked as
tests so they do not count against the account's monthly merges. Real renders (later runs): up to 2,000 documents a
day per tenant, batched per job.

## Known edge cases

- The configuration is missing (local development, a new environment): every command is disabled with the reason;
  nothing throws.
- A FiLogic standard template (organization template) must never be edited in place: it is shared with the production
  OpenTMS. Editing requires a copy in the tenant's workspace first.
- The provider answers 429 or 5xx: retry with backoff; 4xx: fail at once with the provider's message.
- A template id that no longer exists in the provider.

## Privacy note

Test renders send sample data only. Real renders (later runs) will send customer and order data to pdfgeneratorapi
(US region, `us1`): a processor relationship FiLogic already has for OpenTMS; the later runs' designs state the GDPR
position per purpose. Logs carry template ids, report definition ids and tenant ids only, never payloads or the
generated file.

# Design: report rendering

**Status:** built on the PO's instruction of 2026-10-21; assumptions below open for confirmation
**Date:** 2026-10-21
**Depends on:** design 54 (ReportDefinition, `ReportRenderingProvider`, `ReportPurpose`).

## Domain impact

**Modules:** Platform (contracts, renderer, catalogue, report-definition operations) and Integrations (provider
adapter). Integrations references Platform's `Application.Contracts` and `Domain.Shared` only (rule 2); Platform
references no domain module (rule 4) and finds the providers through dependency injection. No migration: nothing is
stored that design 54 did not already store, except that a copied template becomes a new report definition row.

**Flow:** none of the three flows yet; `IDocumentRenderer` is the building block the outbound-document steps of the
order and invoice flows will call. **Decisions relied on:** AD-09 (a generated document is stored; not in this run:
the renderer returns bytes and the caller stores them), AD-16 (no event here), AD-18.

### Provider abstraction (Platform, `Application.Contracts/DocumentRendering`)

```csharp
public interface IReportRenderingProvider
{
    ReportRenderingProvider Provider { get; }                 // the enum of design 54
    Task<ReportRenderingProviderStatus> GetStatusAsync(CancellationToken ct);   // Configured, Reachable?, message
    Task<RenderedDocument> RenderAsync(RenderRequest request, CancellationToken ct);       // one or more templates
    bool SupportsTemplateEditor { get; }
    Task<string> GetTemplateEditorUrlAsync(string templateId, JsonElement? sampleData, string? language, CancellationToken ct);
    bool SupportsTemplateCopy { get; }
    Task<ProviderTemplate> CopyTemplateAsync(string templateId, string newName, CancellationToken ct);
    Task<ProviderTemplate?> GetTemplateAsync(string templateId, CancellationToken ct);     // name, owned by this workspace?
    Task<JsonElement?> GetTemplateSampleDataAsync(string templateId, CancellationToken ct);
}
```

`RenderRequest { Templates: [{ TemplateId, Data (JsonElement) }], Format (Pdf | Html | Zip | Xlsx), FileName, IsTest, CallMode (Background | Interactive) }`;
`RenderedDocument { Content (byte[]), FileName, ContentType }`. The workspace is the current tenant's (the provider
decides from `ICurrentTenant`), never passed by the caller. A later in-house provider implements the same interface
and reports `SupportsTemplateEditor = false`.

**`IDocumentRenderer`** (published, Platform `Application.Contracts`, implemented in Platform.Application):
`RenderAsync(Guid reportDefinitionId, JsonElement data, ReportRenderFormat format = Pdf)` and a batch overload. It
reads the definition in the tenant, refuses an archived one, picks the provider by `Provider`, composes the file name
from `FileNamePattern`, checks the feature, and calls the provider with `CallMode = Background` (its renders run in
jobs). Not an application service (no HTTP endpoint); consumers in later runs call it.

**File name.** The pattern is Scriban rendered with one field, `number`: the payload's `Entity.EntityNumber`, and for
the transport-unit balance, whose entity has no number (OpenTMS's `GetSingleOutput<TransportUnitTransactionPeriodOverview>`),
the period's name `Entity.Entity.Period.Name` (PO 2026-10-23; OpenTMS read a non-existent `EntityNumber` there, so every
balance file was "saldo-.pdf"). `Entity.EntityNumber` wins wherever it exists. `{{ number }}` is the **only** field a
pattern may use: on create and update the report definition service renders the pattern against a model holding only
`number` (sample "12345") with strict variables and strict member access, under the render limits (100 loop
iterations, recursion depth 10), and refuses any other name (`{{ invoice.number }}`, `{{ number.foo }}`) with
`FileNamePatternUnknownField` (HTTP 400, naming the name; PO 2026-10-23). Scriban's built-in functions
(`string.upcase`, `date.now`) are not fields and stay available. A row saved before this check whose pattern fails at
render time still gets a random name, as in OpenTMS, and the renderer logs a warning with the definition id.

### pdfgeneratorapi adapter (Integrations, `Application/DocumentRendering/PdfGeneratorApi`)

- **API v4**, base `https://us1.pdfgeneratorapi.com/api/v4` (configurable). OpenTMS mixed v3 and v4; this uses v4
  throughout: `POST documents/generate` and `documents/generate/batch` (`output: base64`, `testing` for test renders),
  `POST templates/{id}/editor` (`data`, `language` → `response` URL), `POST templates/{id}/copy` (`name` → template),
  `GET templates/{id}` (name, owner), `GET templates/{id}/data` (the template's sample data).
- **Authentication:** a JWT per request, HS256, `iss` = API key, `sub` = workspace, `exp` = now + 60 s (OpenTMS used
  15 minutes and standard Base64 for the signature; this uses Base64Url as JWT requires).
- **Workspace:** host → `DefaultWorkspace`; tenant → `string.Format(TenantWorkspace, tenantId)`. Defaults as OpenTMS:
  `default-workspace@opentms.nl` and `workspace-tenant-{0}@opentms.nl` (tenant ids are GUIDs here, so they never meet
  OpenTMS's numeric workspaces).
- **Organization templates are read-only:** the editor is only opened for a template the tenant's workspace owns
  (`GET templates/{id}` → `owner`); for FiLogic's standard templates the command says "copy it first". This replaces
  OpenTMS's hard-coded list of 13 template ids. The v4 reference documents `owner` on the items of `GET templates`,
  not on `GET templates/{id}`: the adapter reads it from the single template when present, else from the list
  filtered by the template's name; unknown counts as not owned.
- **Resilience:** typed `HttpClient` from `IHttpClientFactory`, TLS validation on (OpenTMS switched it off). Two call
  modes (`RenderCallMode`, PO 2026-10-23), timed per request with a token linked to the caller's, because one client
  serves both; the client's own 30 s timeout stays as the ceiling of any one attempt:
  - **Interactive** (a user waits): the editor URL, the copy, the template lookup, the template data, the health check
    and the test render. **One attempt, 15 s timeout, no retries.** A 429 fails at once as `DocumentRenderingFailed`
    with "the document service is busy, try again in a moment" (nl/en); a 5xx fails at once with the provider's text.
    The template operations are always interactive; a render is interactive only when its `RenderRequest.CallMode`
    says so (set explicitly, never inferred from `IsTest`).
  - **Background** (`IDocumentRenderer`'s real renders, for the later jobs): 30 s per attempt, retry on 429 and 5xx up
    to 4 retries with 3, 6, 12, 24 s backoff (OpenTMS's schedule).

  **Command budget** (PO 2026-10-23): each interactive command of the report definition service (rendering status,
  open editor, copy template, test render) shares ONE budget of **20 s** across all its provider calls, a token linked
  to the request's and passed to every call; the 15 s per call stays. The budget is host configuration,
  `Platform:DocumentRendering:InteractiveCommandBudget` (`DocumentRenderingOptions`, default 20 s). A command that runs out of budget fails as
  `DocumentRenderingFailed` with "the document service did not answer in time" (nl/en); for the rendering status,
  which is advisory, that text becomes the status message. The copy's new report definition is written with the
  request's token, outside the budget. A copy interrupted after the provider created it (budget exhausted, request
  aborted) leaves an unlinked template in the tenant's workspace: harmless, and removable in the provider (PO
  2026-10-23).

  No retry on other 4xx in either mode; the provider's error text is surfaced as Platform's `DocumentRenderingFailed`
  (the adapter throws Platform's codes so the caller sees one set). Correlation id on the request.
- **Configuration** (`Integrations:PdfGeneratorApi`): `BaseUrl`, `DefaultWorkspace`, `TenantWorkspace`, `Key`,
  `Secret`. Key and secret come from the deployment's secret store (environment variables injected from Secret
  Manager; `dotnet user-secrets` locally); never in a database, never in `appsettings*.json`, never returned by an
  endpoint, never logged. Missing key or secret → status *not configured*.
- **Health check** `pdfgeneratorapi` (tag `external`, not part of `/health/ready`): `GET templates?per_page=1` in the
  default workspace; shown on demand in the host's Maintenance → Health tab. This is the connection test.

### Report-definition operations (Platform `ReportDefinitionAppService`)

| Operation | Route | Permission | Behaviour |
|---|---|---|---|
| `GetRenderingStatusAsync(id)` | `GET report-definition/{id}/rendering-status` | `Platform.ReportDefinitions` | provider configured? feature on? template exists, its name, owned by the workspace? Drives the commands' enabled state and reasons |
| `OpenTemplateEditorAsync(id)` | `POST report-definition/{id}/template-editor` | `Platform.ReportDefinitions.EditTemplate` | refuses a template the workspace does not own (`TemplateNotOwned`); passes the template's sample data (else the catalogue sample) and the definition's language; returns the URL, opened in a new tab |
| `CopyTemplateAsync(id)` | `POST report-definition/{id}/copy-template` | `Platform.ReportDefinitions.EditTemplate` + `.Create` | copies the template into the tenant's workspace named "<name> (copy)", creates a new report definition with the same purpose, language, provider and file name pattern, not default, pointing at the new template; returns it |
| `TestRenderAsync(id)` | `POST report-definition/{id}/test-render` | `Platform.ReportDefinitions` | renders with the template's sample data (else the catalogue sample), `testing: true`; returns the file (PDF) for download |

All four require the screen's feature chain; the three commands also require `Platform.DocumentRendering`. The
status needs only the screen's chain so it can report "feature off" as a reason; with the feature off it does not call
the provider. New permission
`Platform.ReportDefinitions.EditTemplate` (both sides, seeded for admin). New error codes: `RenderingNotConfigured`,
`RenderingProviderUnavailable`, `TemplateNotOwned`, `TemplateNotFound`, `DocumentRenderingFailed`, and
`ReportDefinitionArchived` for "refuses an archived one" (added in the build; HTTP 409, 503, 409, 404, 502 and 409);
`FileNamePatternUnknownField` on create and update (PO 2026-10-23; HTTP 400, see "File name").

### Report data catalogue (Platform `Domain.Shared/DocumentRendering`)

`ReportDataCatalogue`: per `ReportPurpose` the JSON a template receives, as a field list (path, type, short English
description for developers, sample value). **Shape (Assumption 1):** the OpenTMS payload, so FiLogic's existing
templates render unchanged: envelope `{ CompanyLogo, Me, Entity, EntityType }`, PascalCase property names, ISO dates
with the `yyyy-MM-dd` date-only fields OpenTMS marked, enums as integers, OpenTMS's formatted-number fields
(`Formatted*`, `CurrencyFormatted*`). Purposes: TransportOrderConfirmation (OpenTMS TransportOrder), TransportFile,
Invoice, PurchaseOrder, TransportUnitBalance (OpenTMS's extra `Entity.Entity` wrapper kept). `SampleFor(purpose)`
builds a complete sample JSON from the catalogue (used when a template has no sample data of its own). The modules
that print a purpose produce exactly this shape (their runs add a contract test against the catalogue). The
catalogue is developer-facing: it has no screen in this run.

Built from the OpenTMS request DTOs and entity creators: `ReportDataCatalogue.FieldsFor(purpose)` (flattened
`ReportDataField` list: dot path with `[]` for array elements, type, description, sample), `SampleFor(purpose)` and
`Validate(purpose, json)` (the contract check: unknown fields, wrong JSON kinds or formats, wrong `EntityType`; nulls
and missing fields allowed). Each OpenTMS class is defined once as a fragment and composed, so a relation looks the
same wherever it appears; `Vehicle.DefaultSecondaryVehicle`/`DefaultThirdVehicle` (always null in OpenTMS) are
catalogued as objects without children. **OpenTMS quirks kept for compatibility:** `Invoice.NetPrice` is the total
*including* VAT (TotalPrice + VatPrice); the transport-unit balance keeps the `GetSingleOutput` wrapper
(`Entity.Entity.…`, `EntityType` ``GetSingleOutput`1``) and has no `EntityNumber`: its file-name number is
`Entity.Entity.Period.Name`; `InvoiceType.InvoiceNumberCreationOption` and `Vehicle.FuelType`
are text, every other enum an integer; `FileDate` is a `yyyy-MM-dd` string while `PurchaseOrder.OrderDate` and
`TripOverview[].FileDate` are full date-times; `TransportUnitTransaction.SourceLocation`/`DestinationLocation` repeat
the source/destination relation; `TransportFile.TransportFileType` and `OrganizationUnit` are listed although OpenTMS
never filled them; `CompanyLogo` is a `data:image/png;base64,…` URL as OpenTMS stored it. **Not reproduced in the
samples, documented in the field descriptions:** `FreightItems[].OrderNumber` was always 1 on the order path (the
sample numbers 1, 2; a producer should number sequentially), `IncomingInvoices[].Id` was the empty GUID on the
transport-file path, and `OrderCustomProperty0-9` on freight were null on the order path (the sample fills them).
Samples fill every field (except the two recursion stops) with consistent Dutch data; collections have two elements,
except where OpenTMS derived them (`Revenue`, `FreightItemsByLeg`: one group of two goods). Field counts:
TransportOrderConfirmation 1,817; TransportFile 3,890; Invoice 874; PurchaseOrder 309; TransportUnitBalance 611.

## UI design

Report definition editor ribbon, in a group *Template*: **Edit template** (opens the provider's editor in a new tab),
**Copy template** (confirms, then opens the new definition), **Test document** (downloads the test PDF). Each is
disabled with its reason as tooltip (UI-PERM-005): not configured, feature off, no permission, archived, or for Edit
"FiLogic standard template: copy it first". Under the provider template id the editor shows the template's name from
the provider, or "not found". Manual: a section in `docs/manual/stamgegevens.md`.

## Test risk analysis

| Part | Class | Coverage |
|---|---|---|
| JWT, workspace choice, never editing an organization template, secrets never returned or logged | **Critical** | unit tests on the adapter with a fake HTTP handler: claims, Base64Url signature, workspace per tenant and host, `TemplateNotOwned`, log capture without key/secret/payload |
| Retry/backoff, timeouts, error mapping, not configured | **High** | adapter unit tests (429, 5xx, 4xx, timeout) with a fake clock, per call mode: interactive one attempt within 15 s and "busy" on 429, background the retry schedule |
| File-name pattern: only `{{ number }}`, balance number from the period | **High** | composer unit tests against the catalogue; service tests refusing unknown fields on create and update |
| Report-definition operations: permissions, feature gating, tenant isolation (another tenant's definition is not found) | **High** | SQLite service tests with a fake provider; authorization tests |
| Catalogue and sample JSON | **Medium** | unit tests: every purpose's sample serializes to the documented shape |
| Editor commands | **Medium** | vitest |

No live call in automated tests: the account is FiLogic's production account.

## Flag & rollout plan

`Platform.DocumentRendering` default off; switched on per tenant or edition once the deployment holds the key
and secret. Rollback: switch off.

## Cost & SLO impact

pdfgeneratorapi bills per merge (generated document). Test renders use `testing` and are not billed. Editor opens and
copies are not merges. Real renders (later runs) add one merge per document: those runs state the volume per tenant.
No Cloud SQL or Cloud Run change. Render p95 is the provider's (OpenTMS: 1–3 s per document), outside the request
budgets because real renders run in jobs. **Interactive commands stay within 20 s:** each provider call is one
attempt of at most 15 s, no backoff, and all calls of one command share a 20 s budget, so a click never holds a
request (and a Cloud Run request slot) for minutes, even when a command makes up to four calls (editor: template
lookup, the owner list when the template answer lacks it, template data, editor URL; test render: template data,
render). **Background renders** keep the retry schedule: up to 5 × 30 s plus 45 s of backoff
(about 195 s) per call in the worst case, which only a job can afford.

## Assumptions

1. **The data catalogue follows the OpenTMS payload** so FiLogic's existing templates keep working. The alternative,
   a new shape (e.g. the snake_case vocabulary of the e-mail tags), would need every template rebuilt.
2. **Key and secret are deployment secrets**, not a screen: one FiLogic account for all tenants (PO 2026-10-21); the
   connection test is the health check in the host's Maintenance view.
3. **The editor needs a template owned by the workspace**, decided by the provider's `owner` flag, not a fixed list.

## Security quickscan

- **Secrets:** read from configuration only; no endpoint returns them; log redaction tested; the OpenTMS repository
  holds the live key and secret in plain text (six places): rotate them after the cut-over (assessment §6).
- **Outbound:** one host, TLS validation on, typed client, timeout; the editor URL comes from the provider and is
  opened by the browser in a new tab (`noopener`).
- **Permissions:** `Platform.ReportDefinitions.EditTemplate` new; the four operations attributed; feature gate.
- **Tenant isolation:** the workspace follows `ICurrentTenant`; a definition of another tenant is not found before
  any provider call.
