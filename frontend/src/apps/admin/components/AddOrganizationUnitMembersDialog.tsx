import { useCallback, useMemo, useRef, useState } from "react";
import type { ColDef } from "ag-grid-community";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { BoolCell } from "@/components/ui/grid-cells";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import type { GridRequest, GridSelectionDto } from "@/components/ui/opentms-grid";
import { fetchAllPages, fetchUnaddedUsers } from "@/domains/platform/administration-organization-units";
import type { UserRow } from "@/domains/platform/administration-users";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface AddOrganizationUnitMembersDialogProps {
  unitId: string;
  unitName: string;
  /** Users already in the editor's staged member list: the picker hides them. */
  excludeIds: ReadonlySet<string>;
  open: boolean;
  onClose: () => void;
  /** The picked users; the editor stages them and applies the change on Save. */
  onPicked: (users: UserRow[]) => void;
}

const EMPTY_SELECTION: GridSelectionDto = { mode: "Explicit", explicitIds: [], filterRequest: null, excludedIds: [] };

/* "Add members": an action dialog (UI-DLG-002) with a picker grid of the users not yet in the
 * unit; a row click toggles the selection and the primary shows the count (UI-GRID-016).
 * Nothing is saved here: the picked users go to the editor, which applies them on Save. */
export function AddOrganizationUnitMembersDialog({
  unitId,
  unitName,
  excludeIds,
  open,
  onClose,
  onPicked,
}: AddOrganizationUnitMembersDialogProps) {
  const { t } = useL();
  const [selection, setSelection] = useState<GridSelectionDto>(EMPTY_SELECTION);
  const [lastFilteredCount, setLastFilteredCount] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  /* Every row the grid loaded, so an explicit selection resolves to users without another call. */
  const loadedRows = useRef(new Map<string, UserRow>());

  const fetchUsers = useCallback(
    async (req: GridRequest) => {
      const result = await fetchUnaddedUsers(unitId, req);
      const rows = result.rows.filter((row) => !excludeIds.has(row.id ?? ""));
      rows.forEach((row) => loadedRows.current.set(row.id ?? "", row));
      setLastFilteredCount(result.filteredCount);
      return { ...result, rows };
    },
    [unitId, excludeIds],
  );

  const selectionCount =
    selection.mode === "Explicit"
      ? selection.explicitIds.length
      : Math.max(0, lastFilteredCount - selection.excludedIds.length);

  const columnDefs = useMemo<ColDef<UserRow>[]>(
    () => [
      { field: "userName", headerName: t("Administration:Username"), sortable: true, filter: false, flex: 1, minWidth: 140 },
      { field: "email", headerName: t("Administration:Email"), sortable: true, filter: false, flex: 1, minWidth: 180 },
      {
        colId: "fullName",
        headerName: t("Administration:FullName"),
        valueGetter: (params) => [params.data?.name, params.data?.surname].filter(Boolean).join(" "),
        sortable: false,
        filter: false,
        flex: 1,
        minWidth: 140,
      },
      { field: "isActive", headerName: t("Administration:Active"), cellRenderer: BoolCell, sortable: false, filter: false, minWidth: 90 },
    ],
    [t],
  );

  const resolvePicked = async (): Promise<UserRow[]> => {
    if (selection.mode === "Explicit") {
      return selection.explicitIds.map((id) => loadedRows.current.get(id)).filter((row): row is UserRow => row != null);
    }
    /* Select-all over a filter: fetch every matching user the way the server sees the filter. */
    const excluded = new Set([...selection.excludedIds, ...excludeIds]);
    const all = await fetchAllPages<UserRow>((startRow, endRow) =>
      fetchUnaddedUsers(unitId, {
        startRow,
        endRow,
        sortModels: [],
        columnFilters: {},
        wildcardSearch: selection.filterRequest?.wildcardSearch ?? null,
        rowGroupCols: [],
        groupKeys: [],
      }),
    );
    return all.filter((row) => !excluded.has(row.id ?? ""));
  };

  const submit = async () => {
    if (selectionCount === 0 || busy) return;
    setError(null);
    setBusy(true);
    try {
      const picked = await resolvePicked();
      onPicked(picked);
      onClose();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    } finally {
      setBusy(false);
    }
  };

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="md"
      title={t("Administration:AddMembersTo").replace("{0}", unitName)}
      isBusy={busy}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={busy}>
            {t("Cancel")}
          </Button>
          <Button onClick={() => void submit()} disabled={selectionCount === 0 || busy}>
            {busy ? t("Administration:Adding") : `${t("Administration:AddSelected")} (${selectionCount})`}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <div className="h-96 rounded-lg border bg-background">
          <LazyOpenTmsGrid<UserRow>
            gridId="platform.administration.organizationUnitUnaddedUsers"
            columnDefs={columnDefs}
            fetchRows={fetchUsers}
            onSelectionChange={setSelection}
            clickSelects
          />
        </div>
      </div>
    </EntityDialog>
  );
}
