import { Alert, AlertDescription } from "@/components/ui/alert";
import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";
import { FormSection } from "@/components/ui/form-section";
import { Skeleton } from "@/components/ui/skeleton";
import { LogbookDetailRows } from "@/apps/admin/components/LogbookDetailRows";
import { PropertyChangesList } from "@/apps/admin/components/PropertyChangesList";
import { changeTypeKey, useTechnicalLogbookEntry } from "@/domains/platform/administration-logbook";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface TechnicalLogbookEntryDialogProps {
  entryId: string;
  open: boolean;
  onClose: () => void;
}

/* One audited request, read-only (UI-DLG-002 editor for viewing a record; UI-PERM-004: no Save,
 * the dialog adds Close). Tabs: the request itself, its actions, the entity changes it made. */
export function TechnicalLogbookEntryDialog({ entryId, open, onClose }: TechnicalLogbookEntryDialogProps) {
  const { t } = useL();
  const f = useFormat();
  const query = useTechnicalLogbookEntry(entryId);
  const entry = query.data;

  const title = `${t("Administration:TechnicalLogbookEntry")} ${entry ? f.timestamp(entry.executionTime) : ""}`.trim();

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
        {entry && (
          <>
            <FormSection title={t("Logbook:Request")}>
              <LogbookDetailRows
                rows={[
                  { label: t("Logbook:Time"), value: f.timestamp(entry.executionTime), tabular: true },
                  { label: t("Logbook:Method"), value: entry.httpMethod ?? "" },
                  { label: t("Logbook:Url"), value: <code className="font-mono text-label">{entry.url ?? ""}</code> },
                  { label: t("Logbook:StatusCode"), value: entry.httpStatusCode == null ? "" : String(entry.httpStatusCode) },
                  { label: t("Logbook:Duration"), value: f.number(entry.executionDuration), tabular: true },
                  { label: t("Logbook:User"), value: entry.userName ?? "" },
                  ...(entry.impersonatorUserName ? [{ label: t("Logbook:Impersonator"), value: entry.impersonatorUserName }] : []),
                  { label: t("Logbook:Application"), value: entry.applicationName ?? "" },
                  { label: t("Logbook:CorrelationId"), value: <code className="font-mono text-label">{entry.correlationId ?? ""}</code> },
                ]}
              />
            </FormSection>

            <FormSection title={t("Logbook:Client")}>
              <LogbookDetailRows
                rows={[
                  { label: t("Logbook:ClientIp"), value: entry.clientIpAddress ?? "" },
                  { label: t("Logbook:ClientName"), value: entry.clientName ?? "" },
                  { label: t("Logbook:ClientId"), value: entry.clientId ?? "" },
                  { label: t("Logbook:Browser"), value: entry.browserInfo ?? "" },
                ]}
              />
            </FormSection>

            {entry.exceptions && (
              <FormSection title={t("Logbook:Exception")}>
                <pre className="max-h-64 overflow-auto whitespace-pre-wrap font-mono text-label">{entry.exceptions}</pre>
              </FormSection>
            )}

            {entry.comments && (
              <FormSection title={t("Logbook:Comments")}>
                <p className="whitespace-pre-wrap text-sm">{entry.comments}</p>
              </FormSection>
            )}
          </>
        )}
      </div>
    ),
  };

  const actionsTab: EntityDialogTab = {
    value: "actions",
    label: t("Logbook:Actions"),
    content: (
      <div className="grid grid-cols-1 gap-4">
        {status}
        {entry && (entry.actions?.length ?? 0) === 0 && (
          <p className="text-sm text-muted-foreground">{t("Logbook:NoActions")}</p>
        )}
        {entry?.actions?.map((action) => (
          <FormSection key={action.id} title={`${action.serviceName ?? ""}.${action.methodName ?? ""}`}>
            <LogbookDetailRows
              rows={[
                { label: t("Logbook:Time"), value: f.timestamp(action.executionTime), tabular: true },
                { label: t("Logbook:Duration"), value: f.number(action.executionDuration), tabular: true },
                {
                  label: t("Logbook:Parameters"),
                  value: <pre className="max-h-48 overflow-auto whitespace-pre-wrap font-mono text-label">{action.parameters ?? ""}</pre>,
                },
              ]}
            />
          </FormSection>
        ))}
      </div>
    ),
  };

  const changesTab: EntityDialogTab = {
    value: "changes",
    label: t("Logbook:EntityChanges"),
    content: (
      <div className="grid grid-cols-1 gap-4">
        {status}
        {entry && (entry.entityChanges?.length ?? 0) === 0 && (
          <p className="text-sm text-muted-foreground">{t("Logbook:NoEntityChanges")}</p>
        )}
        {entry?.entityChanges?.map((change) => (
          <FormSection
            key={change.id}
            title={`${t(changeTypeKey(change.changeType))} · ${change.entityTypeName ?? ""} ${change.entityId ?? ""}`.trim()}
          >
            <PropertyChangesList changes={change.propertyChanges ?? []} />
          </FormSection>
        ))}
      </div>
    ),
  };

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="editor"
      size="lg"
      title={title}
      commands={[]}
      tabs={[generalTab, actionsTab, changesTab]}
    />
  );
}
