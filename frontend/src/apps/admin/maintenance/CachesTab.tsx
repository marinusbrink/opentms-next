import { Eraser, RefreshCw, TriangleAlert } from "lucide-react";
import { useMemo, useState } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { FormSection } from "@/components/ui/form-section";
import { Skeleton } from "@/components/ui/skeleton";
import { LogbookDetailRows } from "@/apps/admin/components/LogbookDetailRows";
import { useCacheStatus, useClearCaches } from "@/domains/platform/administration-maintenance";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Caches tab: what the application cache is, and "Clear caches" for those allowed to. */
export function CachesTab() {
  const { t } = useL();
  const f = useFormat();
  const canClear = useIsGranted("Platform.Administration.Maintenance.Caches.Clear");
  const status = useCacheStatus();
  const [confirmOpen, setConfirmOpen] = useState(false);

  const clear = useClearCaches({
    onSuccess: (result) => {
      setConfirmOpen(false);
      toast.success(
        result.clearedEntryCount == null
          ? t("Maintenance:CachesClearedUnknownCount")
          : t("Maintenance:CachesCleared").replace("{0}", f.number(result.clearedEntryCount)),
      );
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
      { id: "refresh", labelKey: "Maintenance:Refresh", icon: RefreshCw, onClick: () => void status.refetch() },
    ],
    [canClear, clear.isPending, status],
  );

  const data = status.data;
  const instance = data?.instanceName ?? "";

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} />

      <div className="min-h-0 flex-1 overflow-auto px-6 pb-6">
        <div className="grid max-w-dialog-md gap-4">
          {status.isError && (
            <Alert variant="destructive">
              <AlertDescription>{getApiErrorMessage(status.error, t)}</AlertDescription>
            </Alert>
          )}
          {status.isLoading && (
            <div className="grid gap-3">
              <Skeleton className="h-6 w-1/2" />
              <Skeleton className="h-6 w-2/3" />
              <Skeleton className="h-6 w-1/3" />
            </div>
          )}
          {data && (
            <>
              <FormSection title={t("Maintenance:Caches")}>
                <LogbookDetailRows
                  rows={[
                    {
                      label: t("Maintenance:Provider"),
                      value: data.isPerInstance ? t("Maintenance:ProviderInMemory") : (data.providerName ?? ""),
                    },
                    { label: t("Maintenance:Entries"), value: data.entryCount == null ? "" : f.number(data.entryCount), tabular: true },
                    { label: t("Maintenance:KeyPrefix"), value: data.keyPrefix ?? "" },
                    { label: t("Maintenance:Instance"), value: instance },
                  ]}
                />
              </FormSection>
              {data.isPerInstance && (
                <Alert variant="warning">
                  <TriangleAlert aria-hidden="true" />
                  <AlertDescription>{t("Maintenance:PerInstanceNotice").replace("{0}", instance)}</AlertDescription>
                </Alert>
              )}
            </>
          )}
        </div>
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
