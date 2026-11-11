# Design 76: CI in parallel jobs, and a performance gate before prod

**PBI:** none. Built on the PO's direct instruction of 2026-11-11 ("Bouw het zo maar"), after `main` went red twice on a
timing in a volume test while the same code passed on another runner.
**Status:** in review
**Date:** 2026-11-11

<!-- All nine sections below are mandatory (design §4.2). "Not applicable" is an
     acceptable answer only with a one-line reason; a missing section is not. -->

> **Platform change: risk class critical by default** (design §3.1). This is how every module reaches production. The
> design and the build travel in one PR because the PO asked for the build directly, so gate 1 and gate 2 are one review.

## PO decisions this design builds on (2026-11-11)

| # | Decision |
|---|---|
| P1 | Deploying must **guarantee performance** and keep a CI that **guarantees correctness**, without CI taking 35 to 52 minutes. |
| P2 | CI checks correctness only, in parallel jobs. Timing moves to a gate between staging and prod, measured on a machine like prod's. |
| P3 | Staging's database gets **prod's machine type**. It stays stoppable, so it costs only while a deploy or a test runs. |
| P4 | The prod canary also compares response times and rolls back a slower revision. |

## The problem

- **One CI job did everything in sequence:** build, about 5,600 tests, migrations and the OpenAPI check, 35 to 52 minutes.
- **Timing on a shared runner:** the volume tests assert p95 budgets on a shared GitHub runner. Its speed varies by about 1.5 times
  between runs. On 2026-11-11 "five years of orders by month" took a median of 252 ms on one runner and passed on the PR's runner
  with the same code. A red `main` skips the deploy (`deploy.yml` starts only on a green CI), so nothing reached prod for days.
- **Not prod's question:** whether a runner is fast says little about prod, which runs on Cloud Run with Cloud SQL `db-custom-1-3840`.

## Design

### CI (`.github/workflows/ci.yml`): correctness, in parallel

| Job | What |
|---|---|
| Backend unit | Build; Domain, Architecture and Application tests (SQLite and in-memory). |
| Backend host | Build; the host integration tests, against a PostgreSQL service. |
| Backend database `<shard>` | Build; `OpenTms.EntityFrameworkCore.Tests` split by namespace over parallel jobs, each with its own PostgreSQL service. |
| Backend migrations | Build; the DbMigrator on a fresh host and tenant database, the login-page assets, OpenAPI freshness. |
| Frontend | Unchanged. |

- **Every test still runs** on every PR and every push to `main`.
- **Volume tests in CI:** they run with `OPENTMS_VOLUME_BUDGETS=report`. Their data, their answers and their printed timings are as
  before; only the budget is not enforced there. `VolumeBudget` is the one switch, and every timing assertion of the ten volume test
  classes goes through it. Each class carries the trait `Category=Volume`.
- **One required result:** the deploy still starts only when the whole CI workflow is green.

### Performance gate (`deploy.yml`, job `performance`)

The order becomes **build → staging → performance → prod**.

- **Image:** the build job builds `OpenTms.EntityFrameworkCore.Tests` into the image `opentms-performance`
  (`backend/test/performance-gate/Dockerfile`).
- **Job:** the Cloud Run job `opentms-performance` in the staging project runs `dotnet test --filter Category=Volume` with the budgets
  enforced, against staging's Cloud SQL instance, which now has prod's machine type.
- **Its own databases:** the tests create, fill and drop databases of their own, with the suffix `perfgate`, as the login
  `opentms_provisioner`. That login already creates tenant databases. Staging's application data is not touched.
- **A missed budget fails the job, and prod does not start.** The workflow prints every timing and failure from the job's log.
- **Why these tests and not k6:** they hold the budgets the designs set, make their own volume, and need no long-lived token.
  A k6 gate would need a connector or user token kept valid in a seeded staging tenant: a secret that expires and a standing
  account with data access. The k6 scripts in `tests/load/` stay for nightly and manual runs over HTTP.

### Canary (`infra/scripts/canary.sh`)

- **The comparison:** after the observation minutes, the script compares the canary's p95 response time with the previous
  revision's over the same minutes, from Cloud Run's request log. SignalR and health probes are left out.
- **Rollback:** it rolls back when the canary is more than `LATENCY_FACTOR` (1.5) times slower **and** above `LATENCY_FLOOR_MS`
  (300, the read budget).
- **Too little traffic:** with fewer than `MIN_LATENCY_SAMPLES` (50) requests on either revision, the comparison says nothing; the
  script says so and skips it.
- **A failed log read:** counts as no samples, so the script never stops halfway with the canary on part of the traffic.

## Domain impact

No module changes. Test code only (`VolumeBudget`, a trait per volume class), CI and deploy workflows, Terraform and a shell script.
**Decisions:** none of `docs/architecture/decisions.md` changes. **Flows:** not applicable (no product behaviour).

## API contract

Not applicable: no endpoint or DTO changes.

## Migration strategy

**Schema:** no change.

**Terraform:** `infra.yml` applies staging on merge, as for every infra change.
- **Staging changes:** its database tier (`db-f1-micro` to `db-custom-1-3840`; Cloud SQL restarts the instance once) and
  `max_connections`, which is no longer set, the same as prod. New resources: the service account `opentms-performance`, its
  Cloud SQL client role, the secret `connection-string-performance` (the provisioner login without a database), and the job.
- **Prod:** no resources change. `performance_gate` is false by default, and the new module output creates nothing.

## UI design

Not applicable: no screen.

## Test risk analysis

| Area | Risk class | Proof |
|---|---|---|
| CI split | **High** | The first CI run of this PR runs every test across the new jobs. Each project's count matches the one-job run. |
| `VolumeBudget` | **High** | Every timing assertion of the ten classes goes through it; a local run with the budgets enforced and one with `report`. |
| Performance job | **Critical** | Cannot run before merge: it needs the staging resources this PR creates. Its first run is the first deploy after the merge. |
| Canary comparison | **High** | The p95 and comparison helpers are checked on sample input. The real comparison needs prod traffic. |

## Flag & rollout plan

No per-tenant flag: there is no user-visible behaviour (constitution rule 4 concerns product behaviour).
- **Off switch:** the gate goes off by setting `performance_gate = false` for staging and removing the job from `deploy.yml`.
- **First deploy:** the gate runs for the first time on the first deploy after the merge. If it fails for a reason other than a budget
  (a right the provisioner lacks on Cloud SQL, for example), prod does not deploy until that is fixed. A manual `workflow_dispatch`
  of the deploy runs the gate too.

## Cost & SLO impact

- **Staging database:** `db-custom-1-3840` instead of `db-f1-micro`, only while the instance runs. It is still stopped every night
  and started by a deploy.
- **The job:** 2 vCPU and 4 GiB for the minutes it runs, once per deploy.
- **CI:** about 4 to 6 parallel jobs instead of one. More runner minutes in total, less waiting.
- **Deploy time:** the gate adds its run time between staging and prod; CI time drops by more than that.

## Assumptions

- **Cloud SQL rights:** the login `opentms_provisioner`, a Cloud SQL built-in user and so a member of `cloudsqlsuperuser`, may create
  and drop databases, create the extensions the migrations need, and run `VACUUM`. The volume tests need nothing beyond that.
- **Npgsql:** reaches Cloud SQL through the Unix socket under `/cloudsql/`, as the migrator does.
- **The first run:** proves both of the above.
