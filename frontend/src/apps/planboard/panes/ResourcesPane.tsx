import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, GripVertical, RefreshCw, Search } from "lucide-react";

import { setDragPayload } from "@/apps/planboard/dnd";
import { usePaneFilters } from "@/apps/planboard/filters";
import { PLANBOARD_RECORD_TYPES, usePlanboardLiveUpdates } from "@/apps/planboard/live-updates";
import type { PlanboardPaneProps } from "@/apps/planboard/panes/registry";
import { AvailabilityStrip, type StripTrip } from "@/apps/planboard/panes/resources/AvailabilityStrip";
import { ResourceDetailPanel } from "@/apps/planboard/panes/resources/ResourceDetailPanel";
import { PaneFilterControls } from "@/apps/planboard/panes/scheduler/PaneFilterControls";
import { usePaneSettings } from "@/apps/planboard/settings-store";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { MAX_SCHEDULER_DAYS, rangeWindow, usePlanboardBarsByIds, type PlanboardBlockDto } from "@/domains/planning-execution/planboard-scheduler";
import {
  RESOURCE_AVAILABILITY_PERMISSION,
  RESOURCE_KIND,
  resourceAvailabilityKeys,
  usePlanboardResourceDrivers,
  usePlanboardResourceVehicles,
  type ResourceKind,
} from "@/domains/planning-execution/resource-availability";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* Design 70, the Resources pane: a Drivers tab (search, paged, a compact availability strip over the pane's range with
 * blocks and the drivers' trips as dots) and a Vehicles tab with the same. Selecting a resource opens its side panel
 * (unavailability, weekly working pattern). A driver row is draggable with the driver payload: dropped on a trips-pane
 * row or a Scheduler bar it adds the driver to that trip. */

const PERMISSION = "PlanningExecution.TransportTrips";
const PAGE_SIZE = 50;

type ResourcesTab = "drivers" | "vehicles";

interface ResourcesPaneSettings {
  tab: ResourcesTab;
  search: string;
}

const DEFAULT_SETTINGS: ResourcesPaneSettings = { tab: "drivers", search: "" };

function sanitize(stored: Record<string, unknown>): Partial<ResourcesPaneSettings> {
  const result: Partial<ResourcesPaneSettings> = {};
  if (stored.tab === "drivers" || stored.tab === "vehicles") result.tab = stored.tab;
  if (typeof stored.search === "string") result.search = stored.search.slice(0, 128);
  return result;
}

interface ResourceItem {
  id: string;
  title: string;
  subtitle: string;
  blocks: PlanboardBlockDto[];
  tripIds: string[];
}

export default function ResourcesPane(props: PlanboardPaneProps) {
  const canView = useIsGranted(PERMISSION);
  if (!canView) return <PermissionDenied />;
  return <ResourcesPaneBody {...props} />;
}

function ResourcesPaneBody({ instanceId }: PlanboardPaneProps) {
  const { t } = useL();
  const f = useFormat();
  const queryClient = useQueryClient();
  const canViewAvailability = useIsGranted(RESOURCE_AVAILABILITY_PERMISSION);
  const canDragDriver = useIsGranted(`${PERMISSION}.Update`);
  const { settings, update, isLoaded } = usePaneSettings<ResourcesPaneSettings>(instanceId, DEFAULT_SETTINGS, sanitize);
  const paneFilters = usePaneFilters(instanceId, { maxDays: MAX_SCHEDULER_DAYS });
  const { range, organizationUnitIds } = paneFilters.filters;
  const isDrivers = settings.tab === "drivers";

  const [search, setSearch] = useState(settings.search);
  useEffect(() => setSearch(settings.search), [settings.search]);
  useEffect(() => {
    if (search === settings.search) return;
    const timer = setTimeout(() => update({ search }), 300);
    return () => clearTimeout(timer);
  }, [search, settings.search, update]);

  const [page, setPage] = useState(0);
  const pageKey = `${settings.tab}|${range.from}|${range.till}|${organizationUnitIds.join(",")}|${settings.search}`;
  const [pageFor, setPageFor] = useState(pageKey);
  if (pageFor !== pageKey) {
    setPageFor(pageKey);
    setPage(0);
  }
  const [selected, setSelected] = useState<{ kind: ResourceKind; id: string; name: string } | null>(null);

  const ready = isLoaded && paneFilters.isLoaded;
  const drivers = usePlanboardResourceDrivers(range, settings.search, page * PAGE_SIZE, PAGE_SIZE, ready && isDrivers);
  const vehicles = usePlanboardResourceVehicles(range, organizationUnitIds, settings.search, page * PAGE_SIZE, PAGE_SIZE, ready && !isDrivers);
  const list = isDrivers ? drivers : vehicles;

  const items = useMemo<ResourceItem[]>(() => {
    if (isDrivers) {
      return (drivers.data?.items ?? [])
        .filter((d) => d.id)
        .map((d) => ({ id: d.id!, title: d.name || d.code || "", subtitle: d.name && d.code ? d.code : "", blocks: d.blocks ?? [], tripIds: d.tripIds ?? [] }));
    }
    return (vehicles.data?.items ?? [])
      .filter((v) => v.id)
      .map((v) => ({
        id: v.id!,
        title: v.name || v.licencePlate || "",
        subtitle: [v.licencePlate && v.name ? v.licencePlate : null, v.typeName].filter(Boolean).join(" · "),
        blocks: v.blocks ?? [],
        tripIds: v.tripIds ?? [],
      }));
  }, [isDrivers, drivers.data, vehicles.data]);
  const total = Number(list.data?.totalCount ?? 0);

  const allTripIds = useMemo(() => [...new Set(items.flatMap((i) => i.tripIds))], [items]);
  const bars = usePlanboardBarsByIds(allTripIds, allTripIds.length > 0);
  const tripsById = useMemo(() => {
    const map = new Map<string, StripTrip>();
    for (const bar of bars.data?.bars ?? []) if (bar.id && bar.plannedDeparture) map.set(bar.id.toLowerCase(), { id: bar.id, number: bar.number ?? "", departure: bar.plannedDeparture });
    return map;
  }, [bars.data]);

  const stripWindow = useMemo(() => {
    const w = rangeWindow(range);
    return { start: new Date(w.from).getTime(), end: new Date(w.to).getTime() };
  }, [range]);

  /* Live updates: availability or trips of a resource on the page refetch the page (and the open side panel). */
  const resourceIds = useMemo(() => items.map((i) => i.id), [items]);
  const refetchList = list.refetch;
  const refetchBars = bars.refetch;
  usePlanboardLiveUpdates({
    targetType: PLANBOARD_RECORD_TYPES.resourceAvailability,
    filter: { range, resourceIds },
    enabled: items.length > 0,
    onIds: () => {
      void refetchList();
      void queryClient.invalidateQueries({ queryKey: resourceAvailabilityKeys.all });
    },
    onResync: () => void refetchList(),
  });
  usePlanboardLiveUpdates({
    targetType: PLANBOARD_RECORD_TYPES.trip,
    filter: { range, resourceIds, knownIds: allTripIds },
    enabled: items.length > 0,
    onIds: () => {
      void refetchList();
      void refetchBars();
    },
  });

  const idPrefix = `resources-${instanceId}`;
  const firstRow = total === 0 ? 0 : page * PAGE_SIZE + 1;
  const lastRow = Math.min(total, (page + 1) * PAGE_SIZE);

  let body: ReactNode;
  if (list.isError && !list.data) {
    body = (
      <div className="p-3">
        <Alert variant="destructive">
          <AlertDescription className="flex items-center justify-between gap-2">
            <span>{getApiErrorMessage(list.error, t)}</span>
            <Button type="button" size="sm" variant="outline" onClick={() => void list.refetch()}>
              <RefreshCw />
              {t("Planboard:Scheduler.Retry")}
            </Button>
          </AlertDescription>
        </Alert>
      </div>
    );
  } else if (!list.data) {
    body = (
      <div className="flex flex-col gap-2 p-2">
        <Skeleton className="h-8 w-full" />
        <Skeleton className="h-8 w-full" />
        <Skeleton className="h-8 w-full" />
      </div>
    );
  } else if (items.length === 0) {
    body = (
      <div role="status" className="flex h-full items-center justify-center p-6 text-sm text-muted-foreground">
        {t(settings.search ? "Planboard:Resources.NoneForSearch" : isDrivers ? "Planboard:Resources.NoDrivers" : "Planboard:Resources.NoVehicles")}
      </div>
    );
  } else {
    body = (
      <ul className="flex flex-col" aria-label={t(isDrivers ? "Planboard:Resources.Drivers" : "Planboard:Resources.Vehicles")}>
        {items.map((item) => {
          const isSelected = selected?.id === item.id;
          const trips = item.tripIds.map((id) => tripsById.get(id.toLowerCase())).filter((x): x is StripTrip => x != null);
          const draggable = isDrivers && canDragDriver;
          return (
            <li
              key={item.id}
              draggable={draggable}
              onDragStart={draggable ? (e) => setDragPayload(e.dataTransfer, { kind: "driver", driverId: item.id }) : undefined}
              className={cn("flex items-center gap-2 border-b border-border px-2 py-1.5", isSelected && "bg-brand-subtle")}
            >
              <span className={cn("text-muted-foreground", !draggable && "invisible")} aria-hidden="true">
                <GripVertical className={cn("size-4", draggable && "cursor-grab")} />
              </span>
              <button
                type="button"
                className="flex w-48 min-w-0 shrink-0 flex-col text-left"
                aria-pressed={isSelected}
                onClick={() => setSelected(isSelected ? null : { kind: isDrivers ? RESOURCE_KIND.Driver : RESOURCE_KIND.Vehicle, id: item.id, name: item.title })}
              >
                <span className="truncate text-sm font-medium">{item.title}</span>
                {item.subtitle && <span className="truncate text-label text-muted-foreground">{item.subtitle}</span>}
              </button>
              <AvailabilityStrip windowStart={stripWindow.start} windowEnd={stripWindow.end} blocks={item.blocks} trips={trips} className="flex-1" />
            </li>
          );
        })}
      </ul>
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex flex-wrap items-center gap-2 border-b border-border px-2 py-2">
        <Tabs
          value={settings.tab}
          onValueChange={(value) => {
            setSelected(null);
            update({ tab: value as ResourcesTab });
          }}
        >
          <TabsList>
            <TabsTrigger value="drivers">{t("Planboard:Resources.Drivers")}</TabsTrigger>
            <TabsTrigger value="vehicles">{t("Planboard:Resources.Vehicles")}</TabsTrigger>
          </TabsList>
        </Tabs>
        <div className="relative">
          <Search className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input
            id={`${idPrefix}-search`}
            className="h-8 w-44 pl-7"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t("Planboard:Resources.Search")}
            aria-label={t("Planboard:Resources.Search")}
          />
        </div>
        <PaneFilterControls idPrefix={idPrefix} paneFilters={paneFilters} showUnits={!isDrivers} />
      </div>
      <div className="flex min-h-0 flex-1">
        <div className="flex min-h-0 min-w-0 flex-1 flex-col">
          <div className="min-h-0 flex-1 overflow-y-auto">{body}</div>
          {total > 0 && (
            <div className="flex items-center justify-end gap-1 border-t border-border px-2 py-1 text-label text-muted-foreground">
              <span className="tabular-nums" aria-live="polite">
                {t("Planboard:Scheduler.RowsOf").replace("{0}", f.number(firstRow)).replace("{1}", f.number(lastRow)).replace("{2}", f.number(total))}
              </span>
              <PagerButton label={t("Planboard:Scheduler.PreviousPage")} disabled={page === 0} onClick={() => setPage((p) => Math.max(0, p - 1))}>
                <ChevronLeft />
              </PagerButton>
              <PagerButton label={t("Planboard:Scheduler.NextPage")} disabled={lastRow >= total} onClick={() => setPage((p) => p + 1)}>
                <ChevronRight />
              </PagerButton>
            </div>
          )}
        </div>
        {selected && canViewAvailability && (
          <ResourceDetailPanel kind={selected.kind} resourceId={selected.id} resourceName={selected.name} range={range} onClose={() => setSelected(null)} />
        )}
      </div>
    </div>
  );
}

function PagerButton({ label, disabled, onClick, children }: { label: string; disabled: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <Button type="button" variant="ghost" size="icon-xs" aria-label={label} disabled={disabled} onClick={onClick}>
            {children}
          </Button>
        }
      />
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}
