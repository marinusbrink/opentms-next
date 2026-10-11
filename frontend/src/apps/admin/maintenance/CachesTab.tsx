import type { ColDef } from "ag-grid-community";
import { Eraser, RefreshCw, Trash2, TriangleAlert } from "lucide-react";
import { useCallback, useMemo, useState } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { BytesCell, TimestampCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { ChoiceFilter } from "@/apps/admin/components/LogbookFilters";
import { StatCards, type StatCard } from "@/apps/admin/components/StatCards";
import {
  timeSpanToMinutes,
  useCacheEntriesGridFetcher,
  useCacheNames,
  useCacheStatus,
  useClearCaches,
  useRemoveCacheEntry,
  type CacheEntryRow,
} from "@/domains/platform/administration-maintenance";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

export const CACHE_ENTRIES_GRID_ID = "platform.maintenance.cacheEntries";

const ALL_CACHES = "all";

/* The Caches tab: what the application cache of this instance is (cards), every entry it holds
 * (grid, filterable by named cache) and "Clear caches" or "Remove entry" for those allowed to. */
export function CachesTab() {
  const { t } = useL();
  const f = useFormat();
  const canClear = useIsGranted("Platform.Administration.Maintenance.Caches.Clear");
  const status = useCacheStatus();
  const names = useCacheNames();
  const [cacheName, setCacheName] = useState<string>(ALL_CACHES);
  const [refreshKey, setRefreshKey] = useState(0);
  const [confirmOpen, setConfirmOpen] = useState(false);

  const fetchRows = useCacheEntriesGridFetcher(cacheName === ALL_CACHES ? undefined : cacheName, refreshKey);

  const refresh = useCallback(() => {
    setRefreshKey((k) => k + 1);
    void status.refetch();
    void names.refetch();
  }, [status, names]);

  const clear = useClearCaches({
    onSuccess: (result) => {
      setConfirmOpen(false);
      toast.success(
        result.clearedEntryCount == null
          ? t("Maintenance:CachesClearedUnknownCount")
          : t("Maintenance:CachesCleared").replace("{0}", f.number(result.clearedEntryCount)),
      );
      refresh();
    },
    onError: (error) => toast.error(getApiErrorMessage(error, t)),
  });

  const remove = useRemoveCacheEntry({
    onSuccess: () => {
      toast.success(t("Maintenance:CacheEntryRemoved"));
      refresh();
    },
    onError: (error) => toast.error(getApiErrorMessage(error, t)),
  });

  const commands = useMemo<AppCommandBarCommand[]>(
    () => [
      ...(canClear
        ? [
            {
              id: "clear-caches",
              labelKey: "Maintenance:ClearCaches",
              icon: Eraser,
              isPrimary: true,
              disabled: clear.isPending,
              onClick: () => setConfirmOpen(true),
            } satisfies AppCommandBarCommand,
          ]
        : []),
      { id: "refresh", labelKey: "Maintenance:Refresh", icon: RefreshCw, onClick: refresh },
    ],
    [canClear, clear.isPending, refresh],
  );

  const rowActions = useCallback(
    (rows: CacheEntryRow[]): GridRowAction[] =>
      rows.length === 1 && canClear
        ? [
            {
              id: "remove",
              labelKey: "Maintenance:RemoveEntry",
              icon: Trash2,
              destructive: true,
              disabled: remove.isPending,
              onClick: () => remove.mutate(rows[0].key ?? ""),
            },
          ]
        : [],
    [canClear, remove],
  );

  const data = status.data;
  const instance = data?.instanceName ?? "";

  const cards = useMemo<StatCard[]>(() => {
    if (!data) return [];
    const totalSize = (names.data ?? []).reduce((sum, n) => sum + (n.sizeBytes ?? 0), 0);
    return [
      { id: "provider", label: t("Maintenance:Provider"), value: data.isPerInstance ? t("Maintenance:ProviderInMemory") : (data.providerName ?? "") },
      { id: "entries", label: t("Maintenance:Entries"), value: data.entryCount == null ? "" : f.number(data.entryCount) },
      { id: "caches", label: t("Maintenance:Caches"), value: f.number(names.data?.length ?? 0) },
      { id: "size", label: t("Maintenance:TotalSize"), value: f.bytes(totalSize) },
      ...(data.keyPrefix ? [{ id: "prefix", label: t("Maintenance:KeyPrefix"), value: data.keyPrefix }] : []),
      { id: "instance", label: t("Maintenance:Instance"), value: instance },
    ];
  }, [data, names.data, t, f, instance]);

  const nameChoices = useMemo<readonly string[]>(() => [ALL_CACHES, ...(names.data ?? []).map((n) => n.name ?? "")], [names.data]);
  const nameLabel = useCallback(
    (value: string) => {
      if (value === ALL_CACHES) return t("Maintenance:AllCaches");
      const entry = names.data?.find((n) => n.name === value);
      return entry ? t("Maintenance:Caches.Count").replace("{0}", value).replace("{1}", f.number(entry.entryCount)) : value;
    },
    [names.data, t, f],
  );

  const columnDefs = useMemo<ColDef<CacheEntryRow>[]>(
    () => [
      { field: "cacheName", headerName: t("Maintenance:Cache"), sortable: true, filter: false, flex: 1, minWidth: 220 },
      { field: "itemKey", headerName: t("Maintenance:CacheKey"), sortable: true, filter: false, flex: 2, minWidth: 260, cellClass: "font-mono" },
      { field: "sizeBytes", headerName: t("Maintenance:Size"), cellRenderer: BytesCell, type: "rightAligned", sortable: true, filter: false, minWidth: 100 },
      { field: "createdAt", headerName: t("Maintenance:CreatedAt"), cellRenderer: TimestampCell, sortable: true, filter: false, minWidth: 170 },
      { field: "absoluteExpiresAt", headerName: t("Maintenance:ExpiresAt"), cellRenderer: TimestampCell, sortable: true, filter: false, minWidth: 170 },
      {
        colId: "slidingExpiration",
        headerName: t("Maintenance:SlidingExpiration"),
        valueGetter: (params) => f.duration(timeSpanToMinutes(params.data?.slidingExpiration)),
        sortable: false,
        filter: false,
        minWidth: 110,
        hide: true,
      },
      { field: "key", headerName: t("Maintenance:CacheKey"), sortable: false, filter: false, minWidth: 320, hide: true, cellClass: "font-mono" },
    ],
    [t, f],
  );

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} />

      <div className="grid gap-3 px-6 pb-3">
        {status.isError && (
          <Alert variant="destructive">
            <AlertDescription>{getApiErrorMessage(status.error, t)}</AlertDescription>
          </Alert>
        )}
        {cards.length > 0 && <StatCards cards={cards} />}
        {data?.isPerInstance && (
          <Alert variant="warning">
            <TriangleAlert aria-hidden="true" />
            <AlertDescription>{t("Maintenance:PerInstanceNotice").replace("{0}", instance)}</AlertDescription>
          </Alert>
        )}
      </div>

      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<CacheEntryRow>
          gridId={CACHE_ENTRIES_GRID_ID}
          columnDefs={columnDefs}
          fetchRows={fetchRows}
          rowActions={rowActions}
          filters={
            <ChoiceFilter label={t("Maintenance:Cache")} value={cacheName} options={nameChoices} optionLabel={nameLabel} onChange={setCacheName} />
          }
        />
      </div>

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title={t("Maintenance:ClearCaches")}
        description={t("Maintenance:ClearCachesQuestion").replace("{0}", instance)}
        confirmLabel={t("Maintenance:ClearCaches")}
        busyLabel={t("Maintenance:Clearing")}
        onConfirm={() => clear.mutateAsync().then(() => undefined, () => undefined)}
      />
    </div>
  );
}
