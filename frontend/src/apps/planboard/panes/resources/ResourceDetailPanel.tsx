import { useState, type ReactNode } from "react";
import { Pencil, Plus, X } from "lucide-react";

import { UnavailabilityDialog } from "@/apps/planboard/panes/resources/UnavailabilityDialog";
import { WorkingPatternEditor } from "@/apps/planboard/panes/resources/WorkingPatternEditor";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import {
  RESOURCE_AVAILABILITY_MANAGE_PERMISSION,
  unavailabilityReasonKey,
  useResourceAvailability,
  weekdaysFromFlags,
  type ResourceKind,
  type ResourceUnavailabilityDto,
} from "@/domains/planning-execution/resource-availability";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The side panel of the selected driver or vehicle (design 70 "Resources"): its unavailability in the range (create,
 * edit, delete in an EntityDialog) and its weekly working pattern. */

export interface ResourceDetailPanelProps {
  kind: ResourceKind;
  resourceId: string;
  resourceName: string;
  range: { from: string; till: string };
  onClose: () => void;
}

function IconButton({ label, onClick, children }: { label: string; onClick: () => void; children: ReactNode }) {
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <Button type="button" variant="ghost" size="icon-sm" aria-label={label} onClick={onClick}>
            {children}
          </Button>
        }
      />
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}

export function ResourceDetailPanel({ kind, resourceId, resourceName, range, onClose }: ResourceDetailPanelProps) {
  const { t } = useL();
  const f = useFormat();
  const canManage = useIsGranted(RESOURCE_AVAILABILITY_MANAGE_PERMISSION);
  const availability = useResourceAvailability(kind, [resourceId], range);
  const [editing, setEditing] = useState<ResourceUnavailabilityDto | "new" | null>(null);
  const unavailabilities = [...(availability.data?.unavailabilities ?? [])].sort((a, b) => (a.from ?? "").localeCompare(b.from ?? ""));
  const patterns = (availability.data?.patterns ?? []).filter((p) => p.resourceId?.toLowerCase() === resourceId.toLowerCase());

  return (
    <aside aria-label={resourceName} className="flex h-full min-h-0 w-80 shrink-0 flex-col border-l border-border bg-background">
      <header className="flex items-center justify-between gap-2 border-b border-border px-3 py-2">
        <h3 className="truncate text-sm font-semibold text-brand">{resourceName}</h3>
        <IconButton label={t("Close")} onClick={onClose}>
          <X />
        </IconButton>
      </header>
      <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto p-3">
        {availability.isError && (
          <Alert variant="destructive">
            <AlertDescription>{getApiErrorMessage(availability.error, t)}</AlertDescription>
          </Alert>
        )}
        <section className="flex flex-col gap-2">
          <div className="flex items-center justify-between">
            <h4 className="text-label font-semibold text-brand">{t("Planboard:Resources.UnavailabilityInRange")}</h4>
            {canManage && (
              <IconButton label={t("Planboard:Resources.NewUnavailability")} onClick={() => setEditing("new")}>
                <Plus />
              </IconButton>
            )}
          </div>
          {availability.isLoading ? (
            <Skeleton className="h-16 w-full" />
          ) : unavailabilities.length === 0 ? (
            <p className="text-label text-muted-foreground">{t("Planboard:Resources.NoUnavailability")}</p>
          ) : (
            <ul className="flex flex-col gap-1">
              {unavailabilities.map((u) => (
                <li key={u.id} className="flex items-start justify-between gap-2 rounded-md border border-border-section bg-surface-section px-2 py-1.5">
                  <div className="min-w-0 text-label">
                    <p className="font-medium text-foreground">{t(unavailabilityReasonKey(u.reason))}</p>
                    <p className="tabular-nums text-muted-foreground">
                      {f.dateTime(u.from)} – {f.dateTime(u.till)}
                    </p>
                    {u.recurrenceWeekdays ? (
                      <p className="text-muted-foreground">
                        {t("Planboard:Resources.RepeatsOn")
                          .replace("{0}", weekdaysFromFlags(u.recurrenceWeekdays).map((d) => t(`Planboard:Resources.WeekdayShort.${d}`)).join(", "))
                          .replace("{1}", f.date(u.recurrenceUntil))}
                      </p>
                    ) : null}
                    {u.remarks && <p className="truncate text-muted-foreground">{u.remarks}</p>}
                  </div>
                  {canManage && (
                    <IconButton label={t("Planboard:Resources.EditUnavailability")} onClick={() => setEditing(u)}>
                      <Pencil />
                    </IconButton>
                  )}
                </li>
              ))}
            </ul>
          )}
        </section>
        <section className="flex flex-col gap-2">
          <h4 className="text-label font-semibold text-brand">{t("Planboard:Resources.WorkingPattern")}</h4>
          {availability.isLoading ? <Skeleton className="h-40 w-full" /> : <WorkingPatternEditor kind={kind} resourceId={resourceId} patterns={patterns} canManage={canManage} />}
        </section>
      </div>
      {editing && (
        <UnavailabilityDialog
          kind={kind}
          resourceId={resourceId}
          resourceName={resourceName}
          unavailability={editing === "new" ? undefined : editing}
          defaultDate={range.from}
          canDelete={canManage}
          onClose={() => setEditing(null)}
        />
      )}
    </aside>
  );
}
