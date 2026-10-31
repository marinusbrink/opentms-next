# Design: GCP infrastructure as code — staging and prod

**PBI:** none — built on the PO's direct instruction on 2026-10-30 (move opentms-next to GCP, infrastructure as
code, staging and prod, environments defined in code, no running costs outside prod)
**Status:** in review
**Date:** 2026-10-30

<!-- All nine sections below are mandatory (design §4.2). "Not applicable" is an
     acceptable answer only with a one-line reason; a missing section is not. -->

> **Platform change — risk class critical by default** (design §3.1): this is how every module reaches production.
> The design and the implementation travel in one PR because the PO asked for the build directly; gate 1 and gate 2
> are the same review. It lifts "Deploy workflows / Cloud Run config" from the *Deliberately absent* list in
> `/CLAUDE.md` (PO decision 2026-10-30).

## PO decisions this design builds on (2026-10-30)

| # | Decision |
|---|---|
| G1 | Infrastructure as code, Terraform, everything reproducible from `infra/`. |
| G2 | Two environments to start, **staging** and **prod**. A further environment is a code change only. |
| G3 | No running costs outside prod (preferred). Staging's database is **stoppable**: it keeps its data and costs only disk while stopped. |
| G4 | GCP organisation **brink.nu**; shared resources in project **ryd-platform**; **one project per environment**, created by Terraform. |
| G5 | No domain yet: the services run on their generated `*.run.app` addresses. A domain is a later variable. DNS for brink.nu stays at the registrar. |
| G6 | **Every merge to `main` deploys to prod with a canary** (department design §5), after staging. Infra changes: plan on the PR, apply to staging on merge, apply to prod after the PO's approval. |
| G7 | Region **europe-west4** (Eemshaven, NL). |

## Domain impact

No domain module changes. Platform host changes only, all of them configuration plumbing:

- **Forwarded headers always on.** Cloud Run terminates TLS and forwards plain HTTP with `X-Forwarded-Proto: https`.
  The host processed that header only when `AuthServer:RequireHttpsMetadata` was false; with it true (production),
  OpenIddict saw an HTTP request and refused it. The header is now processed in every environment, with the known
  networks cleared because Cloud Run's front end has no fixed addresses.
- **OpenIddict certificate from PEM.** `AuthServer:SigningCertificatePem` + `AuthServer:SigningKeyPem` (from Secret
  Manager) replace the `openiddict.pfx` file when set. Terraform generates the key and certificate, so no file is
  handed around. Without them the existing `openiddict.pfx` behaviour is unchanged.
- **Host administrator from configuration.** The DbMigrator seeds the host administrator with
  `Seed:HostAdminEmail` and `Seed:HostAdminPassword` when set (a generated password from Secret Manager), instead of
  the public development defaults `admin@abp.io` / `1q2w3E*` that every public environment would otherwise get.
  Without the settings local development keeps the defaults. Seeding never changes an existing user.
- **Container images.** Both Dockerfiles move from the SDK image to the ASP.NET runtime image, run as the non-root
  `app` user and listen on 8080 (Cloud Run's port). A new `frontend/Dockerfile` serves the SPA through nginx with
  the SPA fallback.

Architecture references: AD-02 (modular monolith, one backend), AD-11 (one image in roles: **only the Api role is
deployed**; it runs the Hangfire server and the outbox dispatcher in-process until the Jobs role is built, AP-12),
AD-13 (Hangfire stays the job engine), AD-10 (pooler needed — deferred, see *Assumptions*), constitution rule 7
(Workload Identity, one role per resource). Flow: none of the three flows in `02-flows.md` changes; this is the
runtime they run on.

### Layout of `infra/`

```
infra/
  bootstrap/            applied by hand by the PO, once and when an environment is added
                        (organisation rights: creates projects, links billing, enables APIs)
    environments.auto.tfvars   ← the list of environments (G2)
  env/                  one environment; applied by CI per environment, own state prefix
  modules/
    cloud-sql/          PostgreSQL 17 instance, host database, the two logins
    cloud-run-service/  a Cloud Run service that CI deploys revisions into
  environments/
    staging.tfvars
    prod.tfvars
  scripts/              bootstrap.sh, canary.sh, staging-database.sh
```

**Adding an environment:** one entry in `bootstrap/environments.auto.tfvars` (PO applies bootstrap: project, billing,
APIs, CI service-account rights) and one `environments/<name>.tfvars`. The workflows read the list of
`environments/*.tfvars`; nothing else changes.

### What runs where

| Project | Resources |
|---|---|
| `ryd-platform` (shared) | Terraform state bucket (created by `bootstrap.sh`, the one resource not under Terraform because Terraform's own state lives in it); Artifact Registry `opentms` (Docker, europe-west4, keeps the last 30 images per name); Workload Identity Federation pool `github` with a provider that accepts only `marinusbrink/opentms-next`; CI service accounts `gh-terraform-plan`, `gh-terraform-apply`, `gh-deployer` |
| `ryd-opentms-<env>` (one per environment) | Cloud SQL instance; Cloud Run services `opentms-api` and `opentms-web`; Cloud Run job `opentms-migrator`; runtime service accounts; Secret Manager secrets; uptime check and alert (prod) |

### Per environment (`environments/<env>.tfvars`)

| Setting | staging | prod |
|---|---|---|
| Cloud SQL tier | `db-f1-micro` (shared core), `max_connections` 50 | `db-custom-1-3840` (1 vCPU, 3.75 GB) |
| Database mode | **stoppable** — Terraform ignores the activation policy; workflows start and stop it | always on |
| Backups / point-in-time recovery | off | on, 7 days |
| Deletion protection | off | on |
| `opentms-api` instances | min 0, max 1 | min 1, max 1 |
| `opentms-web` instances | min 0, max 1 | min 0, max 2 |
| Uptime check + alert | no (it would keep staging awake) | yes |

`max 1` for the API is deliberate: one instance needs no Redis backplane, distributed cache or lock (05 "More than
one instance"). During a canary two revisions run side by side — see *Assumptions*.

### Database access

Cloud Run reaches Cloud SQL through the built-in Cloud SQL connector (a Unix socket under `/cloudsql/`), not a VPC:
no network, connector or NAT to pay for. The instance has a public IP with **no authorised networks** and
`connector_enforcement = REQUIRED`, so the only way in is an IAM-authorised connector. Tenant connection strings
inherit the socket host from the host connection string, so tenant databases provisioned by `TenantProvisioningJob`
land on the same instance.

Two logins per environment, as the provisioner requires: `opentms_app` (host database, used by every request) and
`opentms_provisioner` (`Tenancy:Provisioning:ConnectionString`, creates tenant databases and roles). Passwords are
generated by Terraform and stored in Secret Manager as complete connection strings.

### Secrets

Per environment in Secret Manager, generated by Terraform, mounted as environment variables, readable only by the
service account that needs them:

| Secret | Read by |
|---|---|
| `connection-string-default` | api, migrator |
| `connection-string-provisioning` | api, migrator |
| `openiddict-certificate-pem`, `openiddict-key-pem` | api |
| `string-encryption-passphrase` | api, migrator |
| `host-admin-password` | migrator (`Seed:HostAdminPassword`); the PO reads it with `gcloud secrets versions access` |

The passphrase is generated once and never rotated by Terraform (`keepers` absent): rotating it would make
already-encrypted values unreadable. The values in `appsettings.json` stay for local development only.

### Deploy pipeline (`.github/workflows/deploy.yml`)

Runs on every push to `main` (after the merge; CI already ran on the PR):

1. Build: `dotnet publish` (ReadyToRun, `linux-x64`) of host and migrator, `abp install-libs`, Docker build; push
   `opentms-api` and `opentms-migrator` tagged with the commit SHA. The web image is built per environment because
   `VITE_API_URL` is fixed at build time (`opentms-web:<sha>-<env>`).
2. **Staging:** start the database if it is stopped, run `opentms-migrator`, deploy both services at 100%, check
   `/health/ready`.
3. **Prod:** run `opentms-migrator`, deploy the API as a new revision **without traffic** (tag `canary`), check its
   `/health/ready` on the tag URL, send it **10%** of traffic, observe **10 minutes**; promote to 100% when the new
   revision logged no 5xx responses above the threshold (default 1% of its requests, minimum 5 errors) and stays
   ready; otherwise put 100% back on the previous revision and fail the run. The web service is static and switches
   at once after the API is promoted.

Session affinity is on for the API so a browser's SignalR negotiate and connect land on the same revision during a
canary.

### Infra pipeline (`.github/workflows/infra.yml`)

| Event | Action |
|---|---|
| PR touching `infra/**` | `terraform fmt -check`, `validate`, and `plan` (no lock) for every environment, as the read-only `gh-terraform-plan` |
| Push to `main` touching `infra/**` | `apply` staging; then `apply` prod in the GitHub environment `prod-infra`, which requires the PO's approval |
| Nightly | `plan -detailed-exitcode` per environment; a drift fails the run |
| Manual | apply one environment |

`staging-database.yml` starts or stops staging's database on demand and stops it every night at 20:00 UTC.

## API contract

Not applicable: no endpoint or DTO changes.

## Migration strategy

No schema change. The pipeline itself makes the existing rule operational: migrations run **before** the new revision
takes traffic and the previous revision keeps serving on the new schema during the canary, so every migration must be
expand-only (coding conventions, migrations). A migration that breaks the previous revision now breaks prod during
the canary window — the CI migration test is the guard.

First rollout: bootstrap (PO, by hand), `apply` staging and prod, first deploy. The first migrator run creates the host
database schemas and the host admin; tenants are created afterwards through the Tenants app.

## UI design

Not applicable: no screen changes.

## Test risk analysis

Risk class **critical** (Platform, authentication path).

| Area | Test |
|---|---|
| PEM certificate loading | Unit tests: a generated PEM pair loads as a certificate with its private key; missing or half-configured settings fall back / fail with a clear message |
| Host administrator seed | Unit tests: configured e-mail and password are used; blank or missing settings keep the development defaults |
| Forwarded headers | Unit test: with `X-Forwarded-Proto: https` the request scheme becomes https in a non-development host configuration |
| Terraform | `terraform fmt -check` and `terraform validate` for bootstrap and env in CI; the PR plan is the review artefact |
| Pipeline | The first staging deploy is the end-to-end test: migrator, `/health/ready`, sign-in through the SPA |
| Tenant isolation | Unchanged code paths; the provisioning login and the socket host are exercised by creating a tenant on staging |

## Flag & rollout plan

No feature flag (constitution rule 4 covers user-visible behaviour; this changes none). The canary *is* the rollout
mechanism for every later release. Rollback: `gcloud run services update-traffic --to-revisions=<previous>=100`,
done automatically by the canary step.

## Cost & SLO impact

Rough monthly list prices in europe-west4, excluding VAT, as of the design date:

| Item | staging | prod |
|---|---|---|
| Cloud SQL compute | €0 while stopped (≈ €9 if left running) | ≈ €50 (`db-custom-1-3840`, no HA) |
| Cloud SQL disk 10 GB SSD + backups | ≈ €2 | ≈ €3 |
| Cloud Run API | €0 idle; pay per use | ≈ €45 (1 vCPU, 1 GiB, always on) |
| Cloud Run web | €0 idle | pay per use, ≈ €0–2 |
| Secret Manager, Artifact Registry, state bucket, uptime check | < €1 | < €2 |
| **Total** | **≈ €2–3** | **≈ €100** |

High availability for Cloud SQL (doubles its compute) and a load balancer with a domain (≈ €18) are not included.
SLO: prod keeps one warm instance, so no cold start for users; staging's first request after idle waits for a cold
start (budget 4 s) and, when its database is stopped, for the database to be started.

## Assumptions

- **Single API instance (AP-13 not taken).** Redis/Memorystore is not built; each revision runs one instance. During
  the 10-minute canary two revisions run side by side: a SignalR push computed on the other revision can be missed
  and master data cached on the old revision can be stale until it expires. Accepted for now; scaling beyond one
  instance needs the AP-13 design.
- **No connection pooler (AD-10 / AO-06 open).** With one instance per revision the connection count stays far below
  the instance limit. AO-06 must be decided before the API scales out or tenants grow beyond ~20 per instance.
- **AP-12 (role selection) not built:** the Api role carries Hangfire and the outbox dispatcher in-process. On
  staging they only run while an instance is up (instance-based billing keeps CPU on until the instance idles out).
- **Both database logins are members of `cloudsqlsuperuser`.** Cloud SQL gives every login created through its API
  that membership, including `CREATEDB` and `CREATEROLE`. The provisioner needs them; the app login does not
  (constitution rule 7, `PostgreSqlTenantDatabaseProvisioner` comment). Stripping them needs a SQL step against the
  instance; that is a follow-up (IAM database authentication or a hardening step in the migrator).
- The canary threshold (1% / 5 errors / 10 minutes) is a starting value, not an agreed SLO.
- `PublishReadyToRun` is passed on the `dotnet publish` command line: the publish profile `/CLAUDE.md` names does not
  exist in the repository.

## Security quickscan

- **Identity:** GitHub Actions authenticates through Workload Identity Federation — no service-account keys exist.
  The provider accepts only tokens from `marinusbrink/opentms-next`; `gh-terraform-apply` and `gh-deployer` accept
  only the `main` branch or their GitHub environment, `gh-terraform-plan` any ref of the repository (read-only).
- **Least privilege (rule 7):** runtime service accounts get `roles/cloudsql.client` and `secretAccessor` on their own
  secrets only; the deployer gets `run.developer`, `iam.serviceAccountUser` on the runtime accounts and
  `artifactregistry.writer` on the one repository; the planner gets `viewer` plus read on the state bucket and secrets.
  The applier needs administrative roles on the environment projects (it manages Cloud Run, Cloud SQL, secrets and IAM
  there) but none on the organisation; project creation stays with the PO's own account in bootstrap.
- **Secrets** live in Secret Manager and in Terraform state; the state bucket is private, versioned and readable only
  by the CI accounts and the PO.
- **Network:** Cloud SQL has no authorised networks and accepts only connector connections. Both Cloud Run services are
  public (the SPA and the API must be reachable by browsers); the migrator job has no ingress.
- **PII in logs (rule 5):** `App:DisablePII` is set to `true` in every deployed environment.
