# Design 69: Master Data — published resource query for the Planboard

**PBI:** design 66 (Planboard run), PO decision D6.
**Status:** approved (PO 2026-10-31, D6).
**Date:** 2026-10-31

## Domain impact

**Module:** Master Data. No tables, no migration. A new published interface in
`OpenTms.MasterData.Application.Contracts/Published/IMasterDataResourceQuery.cs`, implemented in Application, in-process
only (`[RemoteService(false)]`), so Planning's Scheduler endpoint and Resources pane compose rows without a cross-schema
join.

```
Task<MasterDataResourcePageDto<MasterDataVehicleResourceDto>> GetVehiclesAsync(MasterDataVehicleResourceQuery query);
Task<MasterDataResourcePageDto<MasterDataDriverResourceDto>> GetDriversAsync(MasterDataDriverResourceQuery query);
```

- `MasterDataVehicleResourceQuery`: `Kinds` (VehicleTypeKind[], required, non-empty), `OrganizationUnitIds` (empty =
  all), `Ids` (optional explicit set, max 500), `Search` (name, licence plate), `IncludeArchived` (false), `Skip`, `Take`
  (max 500).
- `MasterDataVehicleResourceDto`: `Id`, `Name`, `LicencePlate`, `Kind`, `VehicleTypeName`, `HomeDepotRelationId`,
  `OwnerRelationId`, `DefaultDriverIds` (ordered), `OrganizationUnitIds`, `IsArchived`.
- `MasterDataDriverResourceQuery`: `Ids`, `Search` (code, name), `IncludeArchived`, `Skip`, `Take` (max 500).
- `MasterDataDriverResourceDto`: `Id`, `Code`, `Name` (full name), `EmployerRelationId`, `IsArchived`. No phone, e-mail
  or qualifications (no PII beyond the name the planner needs, rule 5).
- `MasterDataResourcePageDto<T>`: `Items`, `TotalCount`.
- Sort: vehicles by `NormalizedName`, drivers by `Code`. Both are one SQL projection with an index on the sort column
  (vehicle `(TenantId, NormalizedName)`, driver `(TenantId, Code)`; an expand-only migration adds whichever is missing).

## Test risk analysis (high)

Kind and OU filters, search, paging limits, archived exclusion, explicit ids, and a negative tenant-isolation test.

## Security quickscan

In-process only; tenant filter applies; only names and ids leave the module.
