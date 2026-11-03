# Design 71: Platform — predefined roles

**PBI:** Marinus, 2026-11-02 (chat): "pre-define roles on tenant and host level with their permissions".
**Status:** approved (PO 2026-11-02). Risk class **critical** (Platform change, authorization in every tenant).
**Date:** 2026-11-02

## PO decisions (2026-11-02)

1. **Roles:** five tenant roles (Order entry, Planner, Finance, Master data manager, Read-only) and two host roles
   (Platform operator, Tenant & edition manager), next to the existing `admin` and Customer Success Manager.
2. **Seed and add new:** a role is created with its full set; later releases add only permissions that are new to the
   definition. A grant a tenant administrator removed stays removed.
3. **Approve:** `Orders.TransportOrders.Approve` belongs to both Order entry and Finance.
4. **No feature flag** (constitution rule 4 decided by the PO for this change): a role assigned to nobody changes
   nothing for any user.
5. **Role names are localized.**

## Domain impact

**Module:** Platform, plus one grant file per module. No tables, no migration, no API change.

- `PlatformRoleNames` (Platform.Domain.Shared) names the roles. `PredefinedRoleOptions` (Platform.Domain.Shared) defines
  them and their side; `Grant(role, permissions…)` to an undefined role throws when the options are built (the seed).
- Every module grants **its own** permissions from its `Application.Contracts` module
  (`<Module>PredefinedRoles.Configure`), because the Platform references no domain module and the DbMigrator loads the
  contracts modules (it does not load the Application modules). Integrations.Application.Contracts now references
  Platform.Application.Contracts, as the other modules' contracts do.
- `PredefinedRoleDataSeedContributor` (Platform.Domain, which now depends on ABP's PermissionManagement.Domain) runs
  through ABP's data seeder: in the DbMigrator for the host and every tenant database, in the provisioning job for a new
  tenant. Per role of the seeding side:
  - missing → created **static** (no rename, no delete) and marked with the extra property `OpenTms.PredefinedRole`;
  - present but not marked (a tenant's own role of that name) → skipped with a warning, never widened;
  - grants the definition's permissions that the role's extra property `OpenTms.SeededPermissions` does not list yet,
    through `IPermissionManager.SetAsync` (an unknown permission or one of the wrong side fails the seed), then records
    them there.
- Flow: none of the three flows of `02-flows.md`; this is Platform set-up data.

## Localization

Role names are stored as in `PlatformRoleNames` and shown through `Role:<stored name>` in the Platform resource (nl, en),
also for `admin` (Beheerder / Administrator) and Customer Success Manager. The SPA's `roleDisplayName(name, t)`
(`domains/platform/administration-roles.ts`) falls back to the stored name for a tenant's own roles; used in the Roles
grid, the role dialog, the Users grid and the user dialog's Roles tab. Search and sort in the Roles grid and the order of
the user dialog's list still follow the stored name, and a tenant's own role whose name equals a seeded one (which the
seed skips) shows the localized name too.

## Out of scope

- A screen to change a role's permissions (none exists yet; the seeded sets are the only way grants change today).
- The Customer Success Manager seed: it lived in Platform.Application, which the DbMigrator does not load, so the host
  role was not seeded by the DbMigrator. Fixed separately (`fix/csm-role-seed`: moved to Platform.Domain).

## Test risk analysis (critical)

- Definitions (`PredefinedRoleDefinitionTests`): every grant exists and fits its role's side, a child only with its
  parent, Read-only holds views only, Approve only for Order entry and Finance, no role holds user/role/organisation-unit
  administration, `Tenants.Delete`, `Tenants.Impersonation`, `Editions.Delete` or `Drivers.Anonymise`; counts per role
  equal the lists below.
- Seed against PostgreSQL (`PredefinedRoleSeedTests`): tenant A (own database), the shared tenant and the host have
  exactly their roles and grants; a second run changes nothing; a removed grant stays removed; a permission new to the
  definition is granted; a tenant's own role of the same name is untouched; negative tenant isolation (a new tenant's
  seed leaves the shared tenant and the host unchanged).
- Frontend: `roleDisplayName`, and the user dialog shows the localized name while saving the stored one.

## Flag & rollout plan

No flag (decision 4). The first DbMigrator run after the merge creates the roles everywhere; nobody is assigned to them.

## Cost & SLO impact

Seven roles and at most 134 grants per role per database, written once; later runs read one role row each.

## Permissions per role (in full)

`admin` keeps every permission of its side (ABP's own seed). Customer Success Manager keeps
`Platform.Tenants.Impersonation` only (design 63).

### Order entry (tenant, 51 permissions)

- `Integrations.AddressLookup`
- `MasterData.AddressTypes`
- `MasterData.AddressTypes.Create`
- `MasterData.AddressTypes.Update`
- `MasterData.Countries`
- `MasterData.Countries.Create`
- `MasterData.Countries.Update`
- `MasterData.Currencies`
- `MasterData.Drivers`
- `MasterData.GeographicalRegions`
- `MasterData.ProductRelationCodes`
- `MasterData.Products`
- `MasterData.Products.Create`
- `MasterData.Products.Update`
- `MasterData.RelationCategories`
- `MasterData.Relations`
- `MasterData.Relations.Create`
- `MasterData.Relations.Update`
- `MasterData.Temperatures`
- `MasterData.Temperatures.Create`
- `MasterData.Temperatures.Update`
- `MasterData.TransportCharacteristics`
- `MasterData.TransportCharacteristics.Create`
- `MasterData.TransportCharacteristics.Update`
- `MasterData.TransportUnits`
- `MasterData.TransportUnits.Create`
- `MasterData.TransportUnits.Update`
- `MasterData.VehicleCombinations`
- `MasterData.VehicleTypes`
- `MasterData.Vehicles`
- `Orders.ConsignmentNoteFixedDescriptions`
- `Orders.ConsignmentNoteProfiles`
- `Orders.OrderAttributeDefinitions`
- `Orders.TimeSlots`
- `Orders.TransportOrderTemplates`
- `Orders.TransportOrderTemplates.Create`
- `Orders.TransportOrderTemplates.Update`
- `Orders.TransportOrderTypes`
- `Orders.TransportOrders`
- `Orders.TransportOrders.Approve`
- `Orders.TransportOrders.Cancel`
- `Orders.TransportOrders.Confirm`
- `Orders.TransportOrders.Create`
- `Orders.TransportOrders.Print`
- `Orders.TransportOrders.Revise`
- `Orders.TransportOrders.Update`
- `Orders.WaypointTypes`
- `PlanningExecution.TransportTripTypes`
- `PlanningExecution.TransportTrips`
- `PlanningExecution.TripAttributeDefinitions`
- `PlanningExecution.ViaDepotRules`

### Planner (tenant, 50 permissions)

- `Integrations.AddressLookup`
- `Integrations.Routing`
- `MasterData.AddressTypes`
- `MasterData.Countries`
- `MasterData.Currencies`
- `MasterData.Drivers`
- `MasterData.Drivers.Create`
- `MasterData.Drivers.Update`
- `MasterData.GeographicalRegions`
- `MasterData.ProductRelationCodes`
- `MasterData.Products`
- `MasterData.RelationCategories`
- `MasterData.Relations`
- `MasterData.Temperatures`
- `MasterData.TransportCharacteristics`
- `MasterData.TransportUnits`
- `MasterData.VehicleCombinations`
- `MasterData.VehicleCombinations.Create`
- `MasterData.VehicleCombinations.Update`
- `MasterData.VehicleTypes`
- `MasterData.Vehicles`
- `MasterData.Vehicles.Create`
- `MasterData.Vehicles.Update`
- `Orders.ConsignmentNoteFixedDescriptions`
- `Orders.ConsignmentNoteProfiles`
- `Orders.OrderAttributeDefinitions`
- `Orders.TimeSlots`
- `Orders.TransportOrderTemplates`
- `Orders.TransportOrderTypes`
- `Orders.TransportOrders`
- `Orders.TransportOrders.Print`
- `Orders.TransportOrders.Update`
- `Orders.WaypointTypes`
- `PlanningExecution.Legs`
- `PlanningExecution.Legs.Update`
- `PlanningExecution.ResourceAvailability`
- `PlanningExecution.ResourceAvailability.Manage`
- `PlanningExecution.TransportTripTypes`
- `PlanningExecution.TransportTrips`
- `PlanningExecution.TransportTrips.Archive`
- `PlanningExecution.TransportTrips.AssignLegs`
- `PlanningExecution.TransportTrips.Cancel`
- `PlanningExecution.TransportTrips.Complete`
- `PlanningExecution.TransportTrips.Create`
- `PlanningExecution.TransportTrips.Delete`
- `PlanningExecution.TransportTrips.ManageStops`
- `PlanningExecution.TransportTrips.Start`
- `PlanningExecution.TransportTrips.Update`
- `PlanningExecution.TripAttributeDefinitions`
- `PlanningExecution.ViaDepotRules`

### Finance (tenant, 63 permissions)

- `Financial.AdditionalServices`
- `Financial.AdditionalServices.Create`
- `Financial.AdditionalServices.Delete`
- `Financial.AdditionalServices.Update`
- `Financial.DiscountsOrSurcharges`
- `Financial.DiscountsOrSurcharges.Create`
- `Financial.DiscountsOrSurcharges.Delete`
- `Financial.DiscountsOrSurcharges.Update`
- `Financial.InboundInvoiceTypes`
- `Financial.InboundInvoiceTypes.Create`
- `Financial.InboundInvoiceTypes.Delete`
- `Financial.InboundInvoiceTypes.Update`
- `Financial.OutboundInvoiceTypes`
- `Financial.OutboundInvoiceTypes.Create`
- `Financial.OutboundInvoiceTypes.Delete`
- `Financial.OutboundInvoiceTypes.Update`
- `Financial.PriceLists`
- `Financial.PriceLists.Adjust`
- `Financial.PriceLists.Create`
- `Financial.PriceLists.Delete`
- `Financial.PriceLists.Simulate`
- `Financial.PriceLists.Update`
- `Financial.RateLines`
- `Financial.RateLines.Update`
- `Financial.RelationFinancialSettings`
- `Financial.RelationFinancialSettings.Update`
- `Financial.VatPercentages`
- `Financial.VatPercentages.Create`
- `Financial.VatPercentages.Delete`
- `Financial.VatPercentages.Update`
- `MasterData.AddressTypes`
- `MasterData.Countries`
- `MasterData.Currencies`
- `MasterData.Drivers`
- `MasterData.GeographicalRegions`
- `MasterData.ProductRelationCodes`
- `MasterData.Products`
- `MasterData.RelationCategories`
- `MasterData.Relations`
- `MasterData.Relations.Create`
- `MasterData.Relations.Update`
- `MasterData.Temperatures`
- `MasterData.TransportCharacteristics`
- `MasterData.TransportUnits`
- `MasterData.VehicleCombinations`
- `MasterData.VehicleTypes`
- `MasterData.Vehicles`
- `Orders.ConsignmentNoteFixedDescriptions`
- `Orders.ConsignmentNoteProfiles`
- `Orders.OrderAttributeDefinitions`
- `Orders.TimeSlots`
- `Orders.TransportOrderTemplates`
- `Orders.TransportOrderTypes`
- `Orders.TransportOrders`
- `Orders.TransportOrders.Approve`
- `Orders.TransportOrders.Archive`
- `Orders.TransportOrders.Print`
- `Orders.WaypointTypes`
- `PlanningExecution.TransportTripTypes`
- `PlanningExecution.TransportTrips`
- `PlanningExecution.TripAttributeDefinitions`
- `PlanningExecution.ViaDepotRules`
- `Platform.DerivedValues.Recalculate`

### Master data manager (tenant, 134 permissions)

- `Financial.AdditionalServices`
- `Financial.AdditionalServices.Create`
- `Financial.AdditionalServices.Delete`
- `Financial.AdditionalServices.Update`
- `Financial.DiscountsOrSurcharges`
- `Financial.DiscountsOrSurcharges.Create`
- `Financial.DiscountsOrSurcharges.Delete`
- `Financial.DiscountsOrSurcharges.Update`
- `Financial.InboundInvoiceTypes`
- `Financial.InboundInvoiceTypes.Create`
- `Financial.InboundInvoiceTypes.Delete`
- `Financial.InboundInvoiceTypes.Update`
- `Financial.OutboundInvoiceTypes`
- `Financial.OutboundInvoiceTypes.Create`
- `Financial.OutboundInvoiceTypes.Delete`
- `Financial.OutboundInvoiceTypes.Update`
- `Financial.VatPercentages`
- `Financial.VatPercentages.Create`
- `Financial.VatPercentages.Delete`
- `Financial.VatPercentages.Update`
- `Integrations.AddressLookup`
- `MasterData.AddressTypes`
- `MasterData.AddressTypes.Create`
- `MasterData.AddressTypes.Delete`
- `MasterData.AddressTypes.Update`
- `MasterData.Countries`
- `MasterData.Countries.Create`
- `MasterData.Countries.Delete`
- `MasterData.Countries.Update`
- `MasterData.Currencies`
- `MasterData.Currencies.Create`
- `MasterData.Currencies.Delete`
- `MasterData.Currencies.Update`
- `MasterData.Drivers`
- `MasterData.Drivers.Create`
- `MasterData.Drivers.Delete`
- `MasterData.Drivers.Update`
- `MasterData.GeographicalRegions`
- `MasterData.GeographicalRegions.Create`
- `MasterData.GeographicalRegions.Delete`
- `MasterData.GeographicalRegions.Update`
- `MasterData.ProductRelationCodes`
- `MasterData.ProductRelationCodes.Create`
- `MasterData.ProductRelationCodes.Delete`
- `MasterData.ProductRelationCodes.Update`
- `MasterData.Products`
- `MasterData.Products.Create`
- `MasterData.Products.Delete`
- `MasterData.Products.Update`
- `MasterData.RelationCategories`
- `MasterData.RelationCategories.Create`
- `MasterData.RelationCategories.Delete`
- `MasterData.RelationCategories.Update`
- `MasterData.Relations`
- `MasterData.Relations.Create`
- `MasterData.Relations.Delete`
- `MasterData.Relations.Update`
- `MasterData.Temperatures`
- `MasterData.Temperatures.Create`
- `MasterData.Temperatures.Delete`
- `MasterData.Temperatures.Update`
- `MasterData.TransportCharacteristics`
- `MasterData.TransportCharacteristics.Create`
- `MasterData.TransportCharacteristics.Delete`
- `MasterData.TransportCharacteristics.Update`
- `MasterData.TransportUnits`
- `MasterData.TransportUnits.Create`
- `MasterData.TransportUnits.Delete`
- `MasterData.TransportUnits.Update`
- `MasterData.VehicleCombinations`
- `MasterData.VehicleCombinations.Create`
- `MasterData.VehicleCombinations.Delete`
- `MasterData.VehicleCombinations.Update`
- `MasterData.VehicleTypes`
- `MasterData.VehicleTypes.Create`
- `MasterData.VehicleTypes.Delete`
- `MasterData.VehicleTypes.Update`
- `MasterData.Vehicles`
- `MasterData.Vehicles.Create`
- `MasterData.Vehicles.Delete`
- `MasterData.Vehicles.Update`
- `Orders.ConsignmentNoteFixedDescriptions`
- `Orders.ConsignmentNoteFixedDescriptions.Create`
- `Orders.ConsignmentNoteFixedDescriptions.Delete`
- `Orders.ConsignmentNoteFixedDescriptions.Update`
- `Orders.ConsignmentNoteProfiles`
- `Orders.ConsignmentNoteProfiles.Create`
- `Orders.ConsignmentNoteProfiles.Delete`
- `Orders.ConsignmentNoteProfiles.Update`
- `Orders.OrderAttributeDefinitions`
- `Orders.OrderAttributeDefinitions.Create`
- `Orders.OrderAttributeDefinitions.Delete`
- `Orders.OrderAttributeDefinitions.Update`
- `Orders.TimeSlots`
- `Orders.TimeSlots.Create`
- `Orders.TimeSlots.Delete`
- `Orders.TimeSlots.Update`
- `Orders.TransportOrderTemplates`
- `Orders.TransportOrderTemplates.Create`
- `Orders.TransportOrderTemplates.Delete`
- `Orders.TransportOrderTemplates.Update`
- `Orders.TransportOrderTypes`
- `Orders.TransportOrderTypes.Create`
- `Orders.TransportOrderTypes.Delete`
- `Orders.TransportOrderTypes.Update`
- `Orders.WaypointTypes`
- `Orders.WaypointTypes.Create`
- `Orders.WaypointTypes.Delete`
- `Orders.WaypointTypes.Update`
- `PlanningExecution.TransportTripTypes`
- `PlanningExecution.TransportTripTypes.Create`
- `PlanningExecution.TransportTripTypes.Delete`
- `PlanningExecution.TransportTripTypes.Update`
- `PlanningExecution.TripAttributeDefinitions`
- `PlanningExecution.TripAttributeDefinitions.Create`
- `PlanningExecution.TripAttributeDefinitions.Delete`
- `PlanningExecution.TripAttributeDefinitions.Update`
- `PlanningExecution.ViaDepotRules`
- `PlanningExecution.ViaDepotRules.Create`
- `PlanningExecution.ViaDepotRules.Delete`
- `PlanningExecution.ViaDepotRules.Update`
- `Platform.DocumentTypes`
- `Platform.DocumentTypes.Create`
- `Platform.DocumentTypes.Delete`
- `Platform.DocumentTypes.Update`
- `Platform.EmailTemplates`
- `Platform.EmailTemplates.Create`
- `Platform.EmailTemplates.Delete`
- `Platform.EmailTemplates.Update`
- `Platform.ReportDefinitions`
- `Platform.ReportDefinitions.Create`
- `Platform.ReportDefinitions.Delete`
- `Platform.ReportDefinitions.EditTemplate`
- `Platform.ReportDefinitions.Update`

### Read-only (tenant, 40 permissions)

- `Financial.AdditionalServices`
- `Financial.DiscountsOrSurcharges`
- `Financial.InboundInvoiceTypes`
- `Financial.OutboundInvoiceTypes`
- `Financial.PriceLists`
- `Financial.RateLines`
- `Financial.RelationFinancialSettings`
- `Financial.VatPercentages`
- `MasterData.AddressTypes`
- `MasterData.Countries`
- `MasterData.Currencies`
- `MasterData.Drivers`
- `MasterData.GeographicalRegions`
- `MasterData.ProductRelationCodes`
- `MasterData.Products`
- `MasterData.RelationCategories`
- `MasterData.Relations`
- `MasterData.Temperatures`
- `MasterData.TransportCharacteristics`
- `MasterData.TransportUnits`
- `MasterData.VehicleCombinations`
- `MasterData.VehicleTypes`
- `MasterData.Vehicles`
- `Orders.ConsignmentNoteFixedDescriptions`
- `Orders.ConsignmentNoteProfiles`
- `Orders.OrderAttributeDefinitions`
- `Orders.TimeSlots`
- `Orders.TransportOrderTemplates`
- `Orders.TransportOrderTypes`
- `Orders.TransportOrders`
- `Orders.WaypointTypes`
- `PlanningExecution.Legs`
- `PlanningExecution.ResourceAvailability`
- `PlanningExecution.TransportTripTypes`
- `PlanningExecution.TransportTrips`
- `PlanningExecution.TripAttributeDefinitions`
- `PlanningExecution.ViaDepotRules`
- `Platform.DocumentTypes`
- `Platform.EmailTemplates`
- `Platform.ReportDefinitions`

### Platform operator (host, 21 permissions)

- `Integrations.ServiceSettings`
- `Integrations.ServiceSettings.Test`
- `Integrations.ServiceSettings.Update`
- `Platform.Administration.EmailSettings`
- `Platform.Administration.EmailSettings.SendTest`
- `Platform.Administration.EmailSettings.Update`
- `Platform.Administration.Logbook`
- `Platform.Administration.Logbook.Functional`
- `Platform.Administration.Logbook.Technical`
- `Platform.Administration.Maintenance`
- `Platform.Administration.Maintenance.BackgroundJobs`
- `Platform.Administration.Maintenance.BackgroundJobs.Manage`
- `Platform.Administration.Maintenance.Caches`
- `Platform.Administration.Maintenance.Caches.Clear`
- `Platform.Administration.Maintenance.Databases`
- `Platform.Administration.Maintenance.Health`
- `Platform.Administration.Maintenance.Outbox`
- `Platform.Administration.Maintenance.Outbox.Dispatch`
- `Platform.DerivedValues.Recalculate`
- `Platform.TenantProvisioning.Manage`
- `Platform.Tenants`

### Tenant & edition manager (host, 9 permissions)

- `Platform.Editions`
- `Platform.Editions.Create`
- `Platform.Editions.ManageFeatures`
- `Platform.Editions.Update`
- `Platform.TenantProvisioning.Manage`
- `Platform.Tenants`
- `Platform.Tenants.Create`
- `Platform.Tenants.ResetToDataset`
- `Platform.Tenants.Update`
