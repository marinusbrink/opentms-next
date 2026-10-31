# PBI: E-mail delivery with tracking through Postmark

<!-- Written from the PO's instruction of 2026-10-30 ("implement email delivery (including tracking) via postmarkapp api
     according to the solution architecture outline") and answers given that day: AO-12 decided (Platform owns the send
     log and its delivery status), track Delivered, Bounced, Spam complaint and Opened/Clicked, backend only, default
     sending domain ryd-software.com with From noreply@ryd-software.com. One Postmark Server API token for all tenants,
     handed over in chat and kept out of the repository. No GitHub issue; "64" is the next free number. -->

## Story

As the platform I want every e-mail the system sends to go out through Postmark from a send log row, and every
delivery report Postmark sends back to land on that row, so that a later screen can show per message whether it was
delivered, bounced, marked as spam, opened or clicked, and so that a module or agent can react to a bounce or a spam
complaint. As a developer of the Orders, Planning and Financial runs I want one call that queues a mail in the same
transaction as my fact.

## Affected area

- **Platform** (owns mail, CLAUDE.md domain map; send log and status owner per PO decision AO-12, 2026-10-30): the
  `EmailMessage` send log, the provider abstraction `IEmailDeliveryProvider`, the published `IEmailQueue` and
  `IEmailStatusSink`, the send job, ABP's `IEmailSender` routed through the send log, the bounce and spam events, the
  feature `Platform.EmailDelivery`.
- **Integrations** (external links, anti-corruption layer): the Postmark adapter, its configuration, the webhook
  processor and a health check.
- **Host** (`OpenTms.HttpApi`): the anonymous webhook endpoint `POST /api/integrations/postmark/webhook`.

Out of scope: a send-log screen (PO: backend only), rendering e-mail templates into mails (the module that sends a
purpose does that with its run, design 54), attachments, inbound mail, per-tenant sending domains, configuring the
webhook in Postmark (an account setting, see *Rollout*), a retention purge (see *Assumptions*).

## Tenant dimension

The Postmark server and its token are FiLogic's, one for all tenants: host configuration, not tenant data. Each send
log row lives in the tenant's own database (the host database for host mail and shared-tier tenants). Postmark posts
every delivery report to one URL for the whole server, so the report itself cannot be trusted to name its tenant
(07-ingest: "the tenant comes from the endpoint or the credentials, never from the payload alone"). The send job
therefore writes a **host-owned route** `Postmark MessageID → tenant, send log id` after Postmark accepted the mail;
the webhook finds the tenant in that route, our own record, and only then switches to the tenant. A report whose
MessageID has no route (mail another system sent from the same Postmark server) is acknowledged and ignored.

Feature `Platform.EmailDelivery` (default **off**, constitution rule 4) decides per tenant whether its mail goes through
the send log and Postmark. Host mail (no tenant) goes through it whenever Postmark is configured: the host has no
feature values and its only mail today is ABP's own (password reset).

## Expected volumes

Today a handful of mails a day (invitations, password resets). The later runs: order confirmations, invoices and
reminders, up to a few thousand mails per tenant per day at month end. Delivery reports: about two to four per mail
(delivery, opens, clicks).

## Known edge cases

- Postmark is not configured (no token: local development, a new environment): mail falls back to "not sent, logged"
  exactly as the null sender did before; nothing throws.
- Postmark rejects a mail for good (HTTP 422: invalid address, inactive recipient, sender signature not confirmed):
  the row becomes Failed with Postmark's error code and message; no retry.
- Postmark is busy or down (429, 5xx, network): the row goes back to Queued and Hangfire retries the job.
- The call times out after the request left: Postmark may have sent it. The row stays Sending; the next attempt finds
  Sending and marks it Failed with "outcome unknown" instead of risking a second copy (at most once on doubt).
- Webhooks arrive twice, late or out of order (an open before the delivery, a spam complaint after the delivery).
- The From address must be on the verified sending domain: a template's company address cannot be the From.

## Privacy note

A send log row holds personal data: recipient addresses, subject and body. Postmark is a processor (US); the later
runs' designs state the GDPR position per purpose. Opens and clicks are recorded as first and last time only: no IP
address, user agent, location or clicked link is stored. Logs carry send log ids, Postmark MessageIDs, tenant ids,
status codes and error codes only; never addresses, subjects, bodies or the token.

# Design: e-mail delivery through Postmark

**Status:** built on the PO's instruction of 2026-10-30; assumptions below open for confirmation
**Date:** 2026-10-30
**Depends on:** the outbox (`docs/designs/outbox-per-tenant.md`), design 54 (e-mail templates; not used in this run).

## Domain impact

**Modules:** Platform (send log, job, sink, events, feature) and Integrations (Postmark adapter, webhook processor).
Integrations references Platform's `Application.Contracts` and `Domain.Shared` only (rule 2); Platform finds the
provider through dependency injection and references no domain module (rule 4). One additive migration in the Platform
context.

**Flows:** sending extends **flow 1** (a command writes its fact and, in the same transaction, the send log row and an
event; the mail leaves afterwards). Delivery reports are **flow 3** (a report from outside, through ingest).
**Decisions relied on:** AD-15 (delivery reports are ingest), AD-16 (the queue event and the bounce/spam events go
through the outbox), AD-13 (Hangfire sends), AD-18; **AO-12 is decided by the PO on 2026-10-30: Platform owns e-mail
status** (this PR records it in `decisions.md` as AD-20).

### Send log (Platform `Domain/Emailing`)

`EmailMessage` (aggregate, `IMultiTenant`, creation audited), table `platform."EmailMessages"`:

| Field | Notes |
|---|---|
| `To`, `Cc`, `Bcc` | comma-separated addresses, up to 2,000 characters each; `To` required |
| `ReplyTo` | up to 512 |
| `Subject` | up to 998 (RFC 5322 line limit) |
| `HtmlBody`, `TextBody` | text; at least one |
| `Tag` | up to 100: a category for Postmark's statistics (`TenantInvitation`, later `Invoice`) |
| `Status` | `Queued` → `Sending` → `Sent` → `Delivered`; `Bounced`, `SpamComplaint`, `Failed` |
| `Attempts`, `LastError` | the send attempts and the last error code + message (up to 1,000) |
| `ProviderMessageId` | Postmark's MessageID, indexed |
| `SentAt`, `DeliveredAt`, `BouncedAt`, `BounceType`, `BounceDescription`, `SpamComplaintAt` | from the reports |
| `FirstOpenedAt`, `LastOpenedAt`, `FirstClickedAt`, `LastClickedAt` | earliest and latest report times |

`EmailMessageRoute` (host-owned, written and read in the host database only like `UserSignInLookup`), table
`platform."EmailMessageRoutes"`: `ProviderMessageId` (unique), `MessageTenantId`, `EmailMessageId`. It holds ids only.

**Status rules** (each report applies idempotently; a repeated report changes nothing and raises no second event):
Delivered moves Sent → Delivered. A bounce moves Sent or Delivered → Bounced. A spam complaint moves Sent, Delivered or
Bounced → SpamComplaint. Opens and clicks set the first/last times only and never change the status. Failed, Queued and
Sending rows ignore reports.

### Sending

- **`IEmailQueue`** (published, Platform `Application.Contracts/Emailing`): `QueueAsync(QueuedEmail)` → send log id.
  Writes the row (Queued) in the current tenant and publishes `EmailQueuedEto { EmailMessageId }` through the outbox,
  in the caller's unit of work (it must be transactional: the outbox rule). Not an application service; consumers in
  later runs call it next to their fact.
- **`EmailQueuedHandler`** enqueues the Hangfire job `EmailSendJob { TenantId, EmailMessageId }` (ids only), like
  design 49's request handler: the job is only enqueued once the fact is committed.
- **`EmailSendJob`**: in the row's tenant; skips a row that is not Queued (idempotent), except Sending, which it marks
  Failed with `OutcomeUnknown`. It marks the row Sending in a committed unit of work, calls the provider, then records
  Sent + MessageID and writes the host route. A permanent rejection marks it Failed (no retry); a transient failure puts
  it back to Queued and rethrows for Hangfire's retry; a timeout leaves Sending and rethrows.
- **ABP's `IEmailSender`** is replaced by `SendLogEmailSender`: when Postmark is configured and the mail is the host's
  or the tenant has `Platform.EmailDelivery`, it queues through `IEmailQueue` (in a unit of work of its own when the
  caller has none); otherwise it logs "not sent" as the null sender did. The Debug-only null-sender replacement in
  `OpenTmsDomainModule` is removed. ABP's settings-based From is ignored: the provider sets the From.
- **`IEmailDeliveryProvider`** (Platform `Application.Contracts/Emailing`): `IsConfigured`,
  `SendAsync(OutgoingEmail) → ProviderMessageId`; throws `EmailRejectedException` (permanent) or
  `EmailDeliveryUnavailableException` (transient, with `OutcomeUnknown` when the request may have been delivered).

### Postmark adapter (Integrations `Application/Emailing/Postmark`)

- `POST https://api.postmarkapp.com/email`, header `X-Postmark-Server-Token`; body `From`, `To`, `Cc`, `Bcc`,
  `ReplyTo`, `Subject`, `HtmlBody`, `TextBody`, `Tag`, `MessageStream`, `TrackOpens`, `TrackLinks`,
  `Metadata { opentms-message-id, opentms-tenant-id }` (for support in Postmark's activity view; never used for
  routing). Answer `MessageID`.
- **From** is always `"<FromName>" <FromAddress>` from configuration (`OpenTMS <noreply@ryd-software.com>`): only the
  verified domain can send. A sender's own address belongs in `ReplyTo`.
- **Errors:** 422 → `EmailRejectedException` with Postmark's `ErrorCode` and `Message`; 401, 403, 429, 5xx and network
  errors → transient; a timeout → transient with `OutcomeUnknown`. One attempt per job run (Hangfire retries); 30 s
  timeout.
- **Configuration** `Integrations:Postmark`: `BaseUrl`, `ServerToken`, `MessageStream` (`outbound`), `FromAddress`,
  `FromName`, `TrackOpens` (true), `TrackLinks` (`HtmlAndText`), `WebhookUsername`, `WebhookPassword`. The token and the
  webhook password come from the secret store (Secret Manager; `dotnet user-secrets` locally): never in a database or
  appsettings, never returned, never logged. No token → not configured.
- **Health check** `postmark` (tag `external`): `GET /server`; Degraded "not configured" without a token.

### Delivery reports (ingest)

- `POST /api/integrations/postmark/webhook`, anonymous at the ASP.NET level, authenticated by HTTP Basic credentials
  that Postmark sends (configured in the webhook URL); compared in constant time. Without configured credentials the
  endpoint refuses everything (401).
- **`PostmarkWebhookProcessor`** (Integrations) knows Postmark's format and is the only place that does: `RecordType`
  `Delivery` (`DeliveredAt`), `Bounce` (`BouncedAt`, `Type`, `Description`), `SpamComplaint` (`BouncedAt`), `Open`
  (`ReceivedAt`), `Click` (`ReceivedAt`) → `EmailDeliveryReport`. Other record types are acknowledged and ignored; a
  body that is not a Postmark report is 400.
- **`IEmailStatusSink`** (published, Platform; the email-status sink of AP-14): `RecordAsync(EmailDeliveryReport)` finds
  the route in the host database, switches to its tenant, applies the status rules in one transaction and, on the
  transition only, publishes the events on the fixed list. Answers whether the report matched a message.
- **Fixed list** (AO-09 is open; the known part is used): `EmailBouncedEto { EmailMessageId, BounceType }` and
  `EmailSpamComplaintReceivedEto { EmailMessageId }`, in Platform `Domain.Shared/Events`. Delivered, opened and clicked
  raise nothing. No handler exists yet: a later design adds the rule ("block this address for invoices").

### Host e-mail settings (PO 2026-10-30)

The PO asked for the mail service's key to be configured on the host, applying to every tenant, and for a way to send a
test mail. Placement: a host-only view **E-mail** in the Administration app (`/admin/email`), next to Maintenance: the
Administration app is where the host configures the platform, and Maintenance is about running it.

- **Storage:** ABP global settings in the host database (`Platform.Email.*`, provider `G` only, so no tenant or user can
  override them): server token, From address, From name, track opens, track links, webhook user name and password. The
  token and the webhook password are **encrypted at rest** (ABP's setting encryption) and write-only: the API says
  whether one is stored, never what it is; an empty field keeps it, a "remove" checkbox clears it.
- **Fallback:** every empty setting falls back to the deployment's configuration (`Integrations:Postmark`, the secret
  store), field by field. `PostmarkSettingsResolver` combines both per call, in the host's context, so a saved change
  applies at once to every tenant. `MessageStream` and `BaseUrl` stay configuration only.
- **Page** (`EmailSettingsAdministrationController`, `api/platform/administration/email-settings`): a settings page with
  one Save (UI-PAGE-006); sections Mail service, Sender, Tracking, Delivery reports (with the webhook URL to enter in
  Postmark, from App:SelfUrl); a status line "configured / logged, not sent"; each empty field shows the server default
  as its hint.
- **Test mail:** `POST …/email-settings/test { to }` writes a host send log row (tag `TestEmail`) and sends it at once
  through the same dispatcher as the job, without a retry, and returns Postmark's answer (accepted with its message id,
  or the error). The command is disabled while the form has unsaved changes or nothing is configured (UI-PERM-005).
- **Permissions** (host side only): `Platform.Administration.EmailSettings`, `.Update`, `.SendTest`. **Feature**
  `Apps.Administration.Email` (every nav node has one; default off; the host ignores feature values).
- **Encryption key:** ABP encrypts with `StringEncryption:DefaultPassPhrase`, which `appsettings.json` holds in plain
  text. Every deployment must set its own pass phrase from the secret store, or the token is only obfuscated. Changing
  the pass phrase later makes the stored token unreadable (enter it again).

## Test risk analysis

| Part | Class | Coverage |
|---|---|---|
| Webhook authentication, tenant routing from our own route, tenant isolation (a report never touches another tenant's row) | **Critical** | sink tests on SQLite with two tenants; processor tests for credentials |
| Send job state machine: idempotent, at most once on doubt, permanent vs transient | **Critical** | job tests with a fake provider |
| Status rules and events once per transition | **High** | sink tests incl. repeats and out-of-order reports |
| Postmark adapter: request shape, From, headers, error mapping, token never logged | **High** | adapter tests with a scripted HTTP handler and log capture |
| `IEmailSender` routing: feature, host, not configured | **High** | sender tests |
| Report parsing | **Medium** | processor tests per record type |
| Host e-mail settings: host only, secrets write-only and encrypted at rest, fallback to configuration, test mail | **Critical** | settings service tests (SQLite), resolver tests, view tests (vitest) |

No live call in automated tests: the token is FiLogic's production server.

## Flag & rollout plan

`Platform.EmailDelivery` default off. Before switching a tenant on: the deployment holds `ServerToken`,
`WebhookUsername` and `WebhookPassword`; in Postmark, the server's webhook for the `outbound` stream points at
`https://<host>/api/integrations/postmark/webhook` with those credentials and the record types Delivery, Bounce, Spam
complaint, Open and Click; `ryd-software.com` is a verified sending domain. Rollback: switch the feature off (mail is
then logged, not sent).

## Cost & SLO impact

Postmark bills per sent mail; tracking is free. No Cloud SQL or Cloud Run change beyond two small tables. Sending runs
in jobs, outside the request budgets; queueing adds one insert and one outbox row to the caller's transaction. The
webhook does one indexed host read and one tenant transaction per report.

## Assumptions

1. **Retention (constitution rule 5, to confirm):** rows, bodies included, are kept until the send-log screen's design
   adds a purge; proposed: bodies cleared after 30 days, rows deleted after 13 months.
2. Host mail goes through Postmark without a feature check (the host has no feature values).
3. At most once on doubt: a mail whose send timed out is marked Failed `OutcomeUnknown`, not sent again.
4. The fixed list is bounce and spam (the known part of AO-09).
5. PROPOSED items relied on: AP-11 (send from a status row), AP-14 (sink shape), AP-15 (ingest events through the
   outbox), AP-16 (vendor adapters in Integrations). The webhook runs in the Api role until the Ingest role exists
   (AD-11).
