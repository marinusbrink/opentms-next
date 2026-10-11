import { Alert, AlertDescription } from "@/components/ui/alert";
import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";
import { FormSection } from "@/components/ui/form-section";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { LogbookDetailRows } from "@/apps/admin/components/LogbookDetailRows";
import { healthStatusKey, instanceStatusKey, useInstanceDetail } from "@/domains/platform/administration-maintenance";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

interface InstanceDetailDialogProps {
  instanceId: string;
  open: boolean;
  onClose: () => void;
}

/* One registered instance, read-only (UI-DLG-002 editor for viewing a record; the dialog adds
 * Close). Tabs: the instance and its process, and the health result of its last heartbeat. */
export function InstanceDetailDialog({ instanceId, open, onClose }: InstanceDetailDialogProps) {
  const { t } = useL();
  const f = useFormat();
  const query = useInstanceDetail(instanceId);
  const detail = query.data;

  const title = `${t("Maintenance:Instance.Title")} ${detail ? `${detail.role} ${detail.machineName}` : ""}`.trim();

  const status = (
    <>
      {query.isError && (
        <Alert variant="destructive">
          <AlertDescription>{getApiErrorMessage(query.error, t)}</AlertDescription>
        </Alert>
      )}
      {query.isLoading && (
        <div className="grid gap-3">
          <Skeleton className="h-6 w-1/2" />
          <Skeleton className="h-6 w-2/3" />
          <Skeleton className="h-6 w-1/3" />
        </div>
      )}
    </>
  );

  const generalTab: EntityDialogTab = {
    value: "general",
    label: t("General"),
    content: (
      <div className="grid grid-cols-1 gap-4">
        {status}
        {detail && (
          <>
            <FormSection title={t("Maintenance:Instance.Title")}>
              <LogbookDetailRows
                rows={[
                  { label: t("Maintenance:Instance.Status"), value: t(instanceStatusKey(detail.status)) },
                  { label: t("Maintenance:Instance.Role"), value: detail.role ?? "" },
                  { label: t("Maintenance:Machine"), value: detail.machineName ?? "" },
                  { label: t("Maintenance:Instance.ProcessId"), value: String(detail.processId ?? ""), tabular: true },
                  { label: t("Maintenance:StartedAt"), value: f.timestamp(detail.startedAt), tabular: true },
                  { label: t("Maintenance:Instance.LastHeartbeat"), value: f.timestamp(detail.lastHeartbeatAt), tabular: true },
                  ...(detail.stoppedAt ? [{ label: t("Maintenance:Instance.StoppedAt"), value: f.timestamp(detail.stoppedAt), tabular: true }] : []),
                  { label: t("Maintenance:Uptime"), value: f.durationSeconds(detail.uptimeSeconds), tabular: true },
                ]}
              />
            </FormSection>

            <FormSection title={t("Maintenance:Process")}>
              <LogbookDetailRows
                rows={[
                  { label: t("Maintenance:Version"), value: detail.version ?? "" },
                  { label: t("Maintenance:Environment"), value: detail.environment ?? "" },
                  { label: t("Maintenance:Runtime"), value: detail.runtimeVersion ?? "" },
                  { label: t("Maintenance:WorkingSet"), value: f.bytes(detail.workingSetBytes), tabular: true },
                  { label: t("Maintenance:ManagedMemory"), value: f.bytes(detail.managedMemoryBytes), tabular: true },
                  { label: t("Maintenance:Processors"), value: f.number(detail.processorCount), tabular: true },
                  {
                    label: t("Maintenance:OutboxDispatcher"),
                    value: t(detail.outboxDispatchEnabled ? "Maintenance:DispatchOn" : "Maintenance:DispatchOff"),
                  },
                ]}
              />
            </FormSection>
          </>
        )}
      </div>
    ),
  };

  const healthTab: EntityDialogTab = {
    value: "health",
    label: t("Maintenance:Health"),
    content: (
      <div className="grid grid-cols-1 gap-4">
        {status}
        {detail && (
          <FormSection title={t("Maintenance:Instance.LastHealth")}>
            {detail.healthStatus ? (
              <>
                <LogbookDetailRows
                  rows={[
                    { label: t("Maintenance:Status"), value: t(healthStatusKey(detail.healthStatus)) },
                    { label: t("Maintenance:Instance.HealthCheckedAt"), value: f.timestamp(detail.healthCheckedAt), tabular: true },
                  ]}
                />
                <Table className="mt-3">
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
                    {(detail.healthEntries ?? []).map((entry) => (
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
              </>
            ) : (
              <p className="text-sm text-muted-foreground">{t("Maintenance:Instance.NoHealthYet")}</p>
            )}
          </FormSection>
        )}
      </div>
    ),
  };

  return (
    <EntityDialog
      variant="editor"
      size="md"
      title={title}
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      tabs={[generalTab, healthTab]}
    />
  );
}
