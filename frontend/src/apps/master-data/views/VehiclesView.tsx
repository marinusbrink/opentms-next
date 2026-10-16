import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Archive, ArchiveRestore } from "lucide-react";
import { toast } from "sonner";

import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { BoolCell, LicencePlateCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { VehicleEditor } from "@/apps/master-data/editors/VehicleEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import {
  EMPLOYER_ROLES,
  VEHICLE_TYPE_KINDS,
  euroClassKey,
  fuelTypeKey,
  relationLookupPage,
  useArchiveVehicle,
  useUnarchiveVehicle,
  vehicleTypeKindKey,
  vehicles,
  type RelationOption,
  type VehicleListExtra,
  type VehicleRow,
} from "@/domains/master-data/entities";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Vehicles list (design 48, UI design): Name, Licence plate, Type, Kind, Owner, Home depot, Combination;
 * Fuel type, Euro class, VIN and Archived hidden. Filters Kind (Select), Owner (EntityLookup over the relations
 * a vehicle may belong to) and Show archived; Archive / Unarchive as confirmed row actions. */

const ALL_KINDS = "all";

export function VehiclesView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const [kind, setKind] = useState<string>(ALL_KINDS);
  const [owner, setOwner] = useState<RelationOption | null>(null);
  const extra = useMemo<VehicleListExtra>(
    () => ({ includeArchived: showArchived, kinds: kind === ALL_KINDS ? undefined : Number(kind), ownerRelationId: owner?.id }),
    [showArchived, kind, owner],
  );
  const fetchRows = vehicles.useGridFetcher(extra);
  const deleteOne = vehicles.useDelete();
  const bulk = vehicles.useBulkDelete();
  const archive = useArchiveVehicle();
  const unarchive = useUnarchiveVehicle();
  const canUpdate = useIsGranted("MasterData.Vehicles.Update");
  /* The owner carries the Carrier or OwnCompany role, as a driver's employer (design 48). */
  const ownerPage = useMemo(() => relationLookupPage(EMPLOYER_ROLES), []);
  const kindItems = useMemo(
    () => [{ value: ALL_KINDS, label: t("MasterData:AllKinds") }, ...VEHICLE_TYPE_KINDS.map((k) => ({ value: String(k.value), label: t(k.key) }))],
    [t],
  );

  const [archiveTarget, setArchiveTarget] = useState<{ row: VehicleRow; refresh: () => void } | null>(null);
  const [busy, setBusy] = useState(false);

  const columnDefs = useMemo<ColDef<VehicleRow>[]>(
    () => [
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 140 },
      { field: "licencePlate", headerName: t("MasterData:LicencePlate"), cellRenderer: LicencePlateCell, flex: 1, minWidth: 120 },
      { field: "vehicleTypeName", headerName: t("MasterData:VehicleType"), flex: 2, minWidth: 140 },
      { field: "kind", headerName: t("MasterData:Kind"), valueFormatter: (p) => t(vehicleTypeKindKey(p.value)), flex: 1, minWidth: 110 },
      { field: "ownerName", headerName: t("MasterData:Owner"), flex: 2, minWidth: 160 },
      { field: "homeDepotName", headerName: t("MasterData:HomeDepot"), flex: 2, minWidth: 160, sortable: false },
      { field: "combinationName", headerName: t("MasterData:Combination"), flex: 2, minWidth: 160, sortable: false },
      { field: "fuelType", headerName: t("MasterData:FuelType"), valueFormatter: (p) => { const key = fuelTypeKey(p.value); return key ? t(key) : ""; }, flex: 1, minWidth: 120, sortable: false, hide: true },
      { field: "euroClass", headerName: t("MasterData:EuroClass"), valueFormatter: (p) => { const key = euroClassKey(p.value); return key ? t(key) : ""; }, flex: 1, minWidth: 110, sortable: false, hide: true },
      { field: "vin", headerName: t("MasterData:Vin"), flex: 2, minWidth: 180, sortable: false, hide: true },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 100, hide: true },
    ],
    [t],
  );

  const extraRowActions = useCallback(
    (row: VehicleRow, refresh: () => void): GridRowAction[] =>
      canUpdate
        ? [
            {
              id: "archive",
              labelKey: row.isArchived ? "MasterData:Unarchive" : "MasterData:Archive",
              icon: row.isArchived ? ArchiveRestore : Archive,
              onClick: () => setArchiveTarget({ row, refresh }),
            },
          ]
        : [],
    [canUpdate],
  );

  const toggleArchive = async () => {
    if (!archiveTarget?.row.id) return;
    const { row, refresh } = archiveTarget;
    setBusy(true);
    try {
      await (row.isArchived ? unarchive : archive).mutateAsync(row.id!);
      setArchiveTarget(null);
      toast.success(t(row.isArchived ? "MasterData:VehicleUnarchived" : "MasterData:VehicleArchived"));
      refresh();
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    } finally {
      setBusy(false);
    }
  };

  const targetArchived = archiveTarget?.row.isArchived === true;

  return (
    <>
      <MasterDataListView<VehicleRow>
        entityKey="Vehicle"
        permission="MasterData.Vehicles"
        gridId="masterData.vehicles"
        columnDefs={columnDefs}
        fetchRows={fetchRows}
        rowName={(row) => row.name ?? ""}
        deleteOne={deleteOne}
        bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
        filters={
          <>
            <div className="w-44">
              <Select items={kindItems} value={kind} onValueChange={(next) => setKind((next as string | null) ?? ALL_KINDS)}>
                <SelectTrigger aria-label={t("MasterData:Kind")} className="h-9 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {kindItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="w-64">
              <EntityLookup<RelationOption>
                id="vehicles-owner-filter"
                value={owner}
                onChange={setOwner}
                fetchPage={ownerPage}
                getLabel={(r) => r.name}
                placeholder={t("MasterData:Owner")}
              />
            </div>
            <label className="flex items-center gap-2 text-sm">
              <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
              {t("MasterData:ShowArchived")}
            </label>
          </>
        }
        extraRowActions={extraRowActions}
        renderEditor={(state, onClose, onSaved) => (
          <VehicleEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
        )}
      />
      <ConfirmDialog
        open={archiveTarget != null}
        onOpenChange={(open) => {
          if (!open && !busy) setArchiveTarget(null);
        }}
        title={t(targetArchived ? "MasterData:UnarchiveVehicle" : "MasterData:ArchiveVehicle")}
        description={t(targetArchived ? "MasterData:UnarchiveVehicleQuestion" : "MasterData:ArchiveVehicleQuestion").replace("{0}", archiveTarget?.row.name ?? "")}
        confirmLabel={t(targetArchived ? "MasterData:Unarchive" : "MasterData:Archive")}
        onConfirm={toggleArchive}
      />
    </>
  );
}
