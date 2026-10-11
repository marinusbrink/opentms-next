import { lazy, Suspense, useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { FileSearch, Globe } from "lucide-react";

import { AppCommandBar } from "@/components/ui/app-command-bar";
import { TimestampCell } from "@/components/ui/grid-cells";
import type { GridRowAction, OpenTmsGridProps } from "@/components/ui/opentms-grid";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import { FunctionalLogbookEntryDialog } from "@/apps/admin/components/FunctionalLogbookEntryDialog";
import { ChangeTypeFilter, PeriodFilter } from "@/apps/admin/components/LogbookFilters";
import { TechnicalLogbookEntryDialog } from "@/apps/admin/components/TechnicalLogbookEntryDialog";
import {
  changeTypeKey,
  readStoredPeriod,
  storePeriod,
  useFunctionalLogbookGridFetcher,
  type FunctionalLogbookRow,
  type LogbookChangeType,
  type LogbookPeriod,
} from "@/domains/platform/administration-logbook";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

const _OpenTmsGrid = lazy(() =>
  import("@/components/ui/opentms-grid").then((m) => ({ default: m.OpenTmsGrid })),
);

function OpenTmsGrid<TRow>(props: OpenTmsGridProps<TRow>) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return <_OpenTmsGrid {...(props as any)} />;
}

export const FUNCTIONAL_LOGBOOK_GRID_ID = "platform.administration.functionalLogbook";

/* The functional logbook (UI-PAGE-001): every recorded entity change, newest first. Read-only:
 * the command bar carries no commands, rows open in a dialog (UI-GRID-007), and the period
 * and change-type filters sit in the grid toolbar (UI-GRID-005). */
export function FunctionalLogbookView() {
  const { t } = useL();
  const canView = useIsGranted("Platform.Administration.Logbook.Functional");
  const canViewTechnical = useIsGranted("Platform.Administration.Logbook.Technical");

  const [period, setPeriod] = useState<LogbookPeriod>(() => readStoredPeriod(FUNCTIONAL_LOGBOOK_GRID_ID));
  const [changeType, setChangeType] = useState<LogbookChangeType | undefined>(undefined);
  const [openEntry, setOpenEntry] = useState<FunctionalLogbookRow | undefined>();
  const [openRequestId, setOpenRequestId] = useState<string | undefined>();

  const fetchRows = useFunctionalLogbookGridFetcher({ period, changeType });

  const changePeriod = useCallback((next: LogbookPeriod) => {
    storePeriod(FUNCTIONAL_LOGBOOK_GRID_ID, next);
    setPeriod(next);
  }, []);

  const openRecord = useCallback((row: FunctionalLogbookRow) => setOpenEntry(row), []);

  const rowActions = useCallback(
    (rows: FunctionalLogbookRow[]): GridRowAction[] => {
      const single = rows.length === 1 ? rows[0] : undefined;
      if (!single) return [];
      return [
        { id: "open", labelKey: "Open", icon: FileSearch, onClick: () => openRecord(single) },
        ...(canViewTechnical && single.auditLogId
          ? [
              {
                id: "open-request",
                labelKey: "Logbook:OpenRequest",
                icon: Globe,
                onClick: () => setOpenRequestId(single.auditLogId),
              },
            ]
          : []),
      ];
    },
    [canViewTechnical, openRecord],
  );

  const columnDefs = useMemo<ColDef<FunctionalLogbookRow>[]>(
    () => [
      {
        field: "changeTime",
        headerName: t("Logbook:Time"),
        cellRenderer: TimestampCell,
        sortable: true,
        filter: false,
        minWidth: 170,
      },
      {
        field: "changeType",
        headerName: t("Logbook:ChangeType"),
        valueGetter: (params) => (params.data ? t(changeTypeKey(params.data.changeType)) : ""),
        sortable: true,
        filter: false,
        minWidth: 110,
      },
      {
        field: "entityTypeName",
        headerName: t("Logbook:EntityType"),
        filter: "agTextColumnFilter",
        sortable: true,
        flex: 1,
        minWidth: 160,
      },
      {
        field: "entityId",
        headerName: t("Logbook:EntityId"),
        filter: "agTextColumnFilter",
        sortable: true,
        flex: 1,
        minWidth: 200,
      },
      {
        field: "userName",
        headerName: t("Logbook:User"),
        filter: "agTextColumnFilter",
        sortable: true,
        flex: 1,
        minWidth: 140,
      },
      {
        field: "entityTypeFullName",
        headerName: t("Logbook:EntityTypeFullName"),
        filter: "agTextColumnFilter",
        sortable: true,
        flex: 1,
        minWidth: 240,
        hide: true,
      },
    ],
    [t],
  );

  if (!canView) {
    return <PermissionDenied />;
  }

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={[]} />

      <div className="min-h-0 flex-1 px-6 pb-6">
        <Suspense fallback={<Skeleton className="h-full w-full" />}>
          <OpenTmsGrid<FunctionalLogbookRow>
            gridId={FUNCTIONAL_LOGBOOK_GRID_ID}
            columnDefs={columnDefs}
            fetchRows={fetchRows}
            onOpen={openRecord}
            rowActions={rowActions}
            filters={
              <>
                <PeriodFilter value={period} onChange={changePeriod} />
                <ChangeTypeFilter value={changeType} onChange={setChangeType} />
              </>
            }
          />
        </Suspense>
      </div>

      {openEntry && (
        <FunctionalLogbookEntryDialog
          key={openEntry.id}
          entryId={openEntry.id!}
          open
          onClose={() => setOpenEntry(undefined)}
          onOpenRequest={canViewTechnical ? (auditLogId) => setOpenRequestId(auditLogId) : undefined}
        />
      )}

      {openRequestId && (
        <TechnicalLogbookEntryDialog
          key={openRequestId}
          entryId={openRequestId}
          open
          onClose={() => setOpenRequestId(undefined)}
        />
      )}
    </div>
  );
}
