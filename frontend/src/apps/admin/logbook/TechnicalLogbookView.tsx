import { lazy, Suspense, useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { FileSearch } from "lucide-react";

import { AppCommandBar } from "@/components/ui/app-command-bar";
import { BoolCell, NumberCell, TimestampCell } from "@/components/ui/grid-cells";
import type { GridRowAction, OpenTmsGridProps } from "@/components/ui/opentms-grid";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import { ErrorsOnlyFilter, PeriodFilter } from "@/apps/admin/components/LogbookFilters";
import { TechnicalLogbookEntryDialog } from "@/apps/admin/components/TechnicalLogbookEntryDialog";
import {
  readStoredPeriod,
  storePeriod,
  useTechnicalLogbookGridFetcher,
  type LogbookPeriod,
  type TechnicalLogbookRow,
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

export const TECHNICAL_LOGBOOK_GRID_ID = "platform.administration.technicalLogbook";

/* The technical logbook (UI-PAGE-001): every audited request, newest first. Read-only; the
 * period and "errors only" filters sit in the grid toolbar (UI-GRID-005). */
export function TechnicalLogbookView() {
  const { t } = useL();
  const canView = useIsGranted("Platform.Administration.Logbook.Technical");

  const [period, setPeriod] = useState<LogbookPeriod>(() => readStoredPeriod(TECHNICAL_LOGBOOK_GRID_ID));
  const [errorsOnly, setErrorsOnly] = useState(false);
  const [openEntry, setOpenEntry] = useState<TechnicalLogbookRow | undefined>();

  const fetchRows = useTechnicalLogbookGridFetcher({ period, errorsOnly });

  const changePeriod = useCallback((next: LogbookPeriod) => {
    storePeriod(TECHNICAL_LOGBOOK_GRID_ID, next);
    setPeriod(next);
  }, []);

  const openRecord = useCallback((row: TechnicalLogbookRow) => setOpenEntry(row), []);

  const rowActions = useCallback(
    (rows: TechnicalLogbookRow[]): GridRowAction[] =>
      rows.length === 1 ? [{ id: "open", labelKey: "Open", icon: FileSearch, onClick: () => openRecord(rows[0]) }] : [],
    [openRecord],
  );

  const columnDefs = useMemo<ColDef<TechnicalLogbookRow>[]>(
    () => [
      {
        field: "executionTime",
        headerName: t("Logbook:Time"),
        cellRenderer: TimestampCell,
        sortable: true,
        filter: false,
        minWidth: 170,
      },
      {
        field: "userName",
        headerName: t("Logbook:User"),
        filter: "agTextColumnFilter",
        sortable: true,
        flex: 1,
        minWidth: 130,
      },
      {
        field: "httpMethod",
        headerName: t("Logbook:Method"),
        filter: "agTextColumnFilter",
        sortable: true,
        minWidth: 100,
      },
      {
        field: "url",
        headerName: t("Logbook:Url"),
        filter: "agTextColumnFilter",
        sortable: true,
        flex: 2,
        minWidth: 240,
      },
      {
        field: "httpStatusCode",
        headerName: t("Logbook:StatusCode"),
        /* A status code is an identifier, not a quantity: plain text, no grouping (UI-FMT-006). */
        valueGetter: (params) => (params.data?.httpStatusCode == null ? "" : String(params.data.httpStatusCode)),
        sortable: true,
        filter: false,
        minWidth: 90,
      },
      {
        field: "executionDuration",
        headerName: t("Logbook:Duration"),
        cellRenderer: NumberCell,
        type: "rightAligned",
        sortable: true,
        filter: false,
        minWidth: 110,
      },
      {
        field: "hasException",
        headerName: t("Logbook:HasException"),
        cellRenderer: BoolCell,
        sortable: false,
        filter: false,
        minWidth: 80,
      },
      {
        field: "entityChangeCount",
        headerName: t("Logbook:EntityChanges"),
        cellRenderer: NumberCell,
        type: "rightAligned",
        sortable: true,
        filter: false,
        minWidth: 110,
      },
      {
        field: "clientIpAddress",
        headerName: t("Logbook:ClientIp"),
        sortable: true,
        filter: false,
        minWidth: 130,
        hide: true,
      },
      {
        field: "applicationName",
        headerName: t("Logbook:Application"),
        sortable: true,
        filter: false,
        minWidth: 130,
        hide: true,
      },
      {
        field: "correlationId",
        headerName: t("Logbook:CorrelationId"),
        filter: "agTextColumnFilter",
        sortable: false,
        minWidth: 200,
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
          <OpenTmsGrid<TechnicalLogbookRow>
            gridId={TECHNICAL_LOGBOOK_GRID_ID}
            columnDefs={columnDefs}
            fetchRows={fetchRows}
            onOpen={openRecord}
            rowActions={rowActions}
            filters={
              <>
                <PeriodFilter value={period} onChange={changePeriod} />
                <ErrorsOnlyFilter value={errorsOnly} onChange={setErrorsOnly} />
              </>
            }
          />
        </Suspense>
      </div>

      {openEntry && (
        <TechnicalLogbookEntryDialog
          key={openEntry.id}
          entryId={openEntry.id!}
          open
          onClose={() => setOpenEntry(undefined)}
        />
      )}
    </div>
  );
}
