# infra — OpenTMS on GCP

Design and reasoning: [docs/designs/gcp-infrastructure.md](../docs/designs/gcp-infrastructure.md).

| Folder | What | Applied by |
|---|---|---|
| `bootstrap/` | Environment projects, billing, APIs, CI identities (Workload Identity Federation), Artifact Registry | The PO, by hand (`scripts/bootstrap.sh`) |
| `env/` | One environment: Cloud SQL, Cloud Run services and migrator job, secrets, monitoring | CI (`.github/workflows/infra.yml`) |
| `modules/` | `cloud-sql`, `cloud-run-service` | — |
| `environments/` | One `.tfvars` per environment | — |
| `scripts/` | `bootstrap.sh`, `canary.sh`, `staging-database.sh` | Workflows / the PO |

## First time

```bash
gcloud auth login marinus@brink.nu
gcloud auth application-default login --account=marinus@brink.nu
TF_VAR_org_id=<org id> TF_VAR_billing_account=<billing account> infra/scripts/bootstrap.sh
```

Then, in GitHub: set the repository variable `GCP_WORKLOAD_IDENTITY_PROVIDER` to bootstrap's
`workload_identity_provider` output, create the environment `prod-infra` with the PO as required reviewer, and
(optional) the secret `AG_GRID_LICENSE_KEY`. Apply each environment once, then run the deploy:

```bash
cd infra/env
terraform init -reconfigure -backend-config="prefix=opentms/env/staging"
terraform apply -var-file=../environments/staging.tfvars
```

## Adding an environment

1. Add it to `bootstrap/environments.auto.tfvars` and run `scripts/bootstrap.sh` (creates the project).
2. Add `environments/<name>.tfvars`. Merging applies it through `infra.yml`.
3. To deploy the app there, add a job to `.github/workflows/deploy.yml`.

## Everyday

- Staging database: started by every deploy, stopped at 20:00 UTC; start it by hand with the
  *Staging database* workflow or `infra/scripts/staging-database.sh start ryd-opentms-staging`.
- Host administrator password of an environment:
  `gcloud secrets versions access latest --secret=host-admin-password --project=ryd-opentms-<env>`.
- Rolling back prod: `gcloud run services update-traffic opentms-api --to-revisions=<revision>=100 --project=ryd-opentms-prod --region=europe-west4`.
