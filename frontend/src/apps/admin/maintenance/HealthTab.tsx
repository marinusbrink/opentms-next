import { RefreshCw } from "lucide-react";
import { useMemo } from "react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { FormSection } from "@/components/ui/form-section";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { LogbookDetailRows } from "@/apps/admin/components/LogbookDetailRows";
import { StatCards, type StatCard } from "@/apps/admin/components/StatCards";
import { healthStatusKey, useHealthReport } from "@/domains/platform/administration-maintenance";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

function toneOf(status: string | null | undefined): StatCard["tone"] {
  if (status === "Unhealthy") return "destructive";
  if (status === "Degraded") return "warning";
  return "default";
}

/* The Health tab: the registered health checks as /health/ready sees them, plus the process. */
export function HealthTab() {
  const { t } = useL();
  const f = useFormat();
  const report = useHealthReport();

  const commands = useMemo<AppCommandBarCommand[]>(
    () => [{ id: "refresh", labelKey: "Maintenance:Refresh", icon: RefreshCw, onClick: () => void report.refetch() }],
    [report],
  );

  const data = report.data;
  const cards = useMemo<StatCard[]>(() => {
    if (!data) return [];
    return [
      { id: "status", label: t("Maintenance:Status"), value: t(healthStatusKey(data.status)), tone: toneOf(data.status) },
      { id: "duration", label: t("Maintenance:Duration"), value: f.number(data.totalDurationMilliseconds) },
      { id: "checks", label: t("Maintenance:Checks"), value: f.number(data.entries?.length ?? 0) },
      { id: "uptime", label: t("Maintenance:Uptime"), value: f.duration((data.process?.uptimeSeconds ?? 0) / 60) },
    ];
  }, [data, t, f]);

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} />

      <div className="min-h-0 flex-1 overflow-auto px-6 pb-6">
        <div className="grid gap-4">
          {report.isError && (
            <Alert variant="destructive">
              <AlertDescription>{getApiErrorMessage(report.error, t)}</AlertDescription>
            </Alert>
          )}
          {report.isLoading && (
            <div className="grid gap-3">
              <Skeleton className="h-16 w-full" />
              <Skeleton className="h-6 w-2/3" />
              <Skeleton className="h-6 w-1/3" />
            </div>
          )}
          {data && (
            <>
              <StatCards cards={cards} />

              <FormSection title={t("Maintenance:Checks")}>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>{t("Maintenance:Check")}</TableHead>
                      <TableHead>{t("Maintenance:Status")}</TableHead>
                      <TableHead className="text-right">{t("Maintenance:Duration")}</TableHead>
                      <TableHead>{t("Maintenance:Tags")}</TableHead>
                      <TableHead>{t("Maintenance:Error")}</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {(data.entries ?? []).map((entry) => (
                      <TableRow key={entry.name}>
                        <TableCell className="whitespace-normal">
                          <div>{entry.name}</div>
                          {entry.description && <div className="text-label text-muted-foreground">{entry.description}</div>}
                        </TableCell>
                        <TableCell
                          className={cn(
                            entry.status === "Unhealthy" && "text-destructive",
                            entry.status === "Degraded" && "text-tone-warning",
                          )}
                        >
                          {t(healthStatusKey(entry.status))}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">{f.number(entry.durationMilliseconds)}</TableCell>
                        <TableCell>{(entry.tags ?? []).join(", ")}</TableCell>
                        <TableCell className="whitespace-normal text-destructive">{entry.error ?? ""}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </FormSection>

              {data.process && (
                <FormSection title={t("Maintenance:Process")}>
                  <LogbookDetailRows
                    rows={[
                      { label: t("Maintenance:Application"), value: data.process.applicationName ?? "" },
                      { label: t("Maintenance:Version"), value: data.process.version ?? "" },
                      { label: t("Maintenance:Environment"), value: data.process.environment ?? "" },
                      { label: t("Maintenance:Machine"), value: data.process.machineName ?? "" },
                      { label: t("Maintenance:StartedAt"), value: f.timestamp(data.process.startedAt), tabular: true },
                      { label: t("Maintenance:Runtime"), value: data.process.runtimeVersion ?? "" },
                      { label: t("Maintenance:WorkingSet"), value: f.bytes(data.process.workingSetBytes), tabular: true },
                      { label: t("Maintenance:ManagedMemory"), value: f.bytes(data.process.managedMemoryBytes), tabular: true },
                      { label: t("Maintenance:Processors"), value: f.number(data.process.processorCount), tabular: true },
                      {
                        label: t("Maintenance:OutboxDispatcher"),
                        value: t(data.process.outboxDispatchEnabled ? "Maintenance:DispatchOn" : "Maintenance:DispatchOff"),
                      },
                    ]}
                  />
                </FormSection>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
