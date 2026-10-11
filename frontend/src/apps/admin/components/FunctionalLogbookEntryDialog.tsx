import { Globe } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { EntityDialog, type EntityDialogTab, type RibbonCommand } from "@/components/ui/entity-dialog";
import { FormSection } from "@/components/ui/form-section";
import { Skeleton } from "@/components/ui/skeleton";
import { LogbookDetailRows } from "@/apps/admin/components/LogbookDetailRows";
import { PropertyChangesList } from "@/apps/admin/components/PropertyChangesList";
import { changeTypeKey, useFunctionalLogbookEntry } from "@/domains/platform/administration-logbook";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface FunctionalLogbookEntryDialogProps {
  entryId: string;
  open: boolean;
  onClose: () => void;
  /** Present when the user may open the technical logbook: adds the "Open request" command. */
  onOpenRequest?: (auditLogId: string) => void;
}

/* One entity change, read-only (UI-DLG-002 editor for viewing a record; UI-PERM-004 read-only:
 * no Save, the dialog adds Close). The record's identity is the timestamp. */
export function FunctionalLogbookEntryDialog({ entryId, open, onClose, onOpenRequest }: FunctionalLogbookEntryDialogProps) {
  const { t } = useL();
  const f = useFormat();
  const query = useFunctionalLogbookEntry(entryId);
  const entry = query.data;

  const title = `${t("Administration:FunctionalLogbookEntry")} ${entry ? f.timestamp(entry.changeTime) : ""}`.trim();

  const commands: RibbonCommand[] =
    onOpenRequest && entry?.auditLogId
      ? [{ id: "open-request", labelKey: "Logbook:OpenRequest", icon: Globe, groupKey: "Administration:Logbook", onClick: () => onOpenRequest(entry.auditLogId!) }]
      : [];

  const generalTab: EntityDialogTab = {
    value: "general",
    label: t("General"),
    content: (
      <div className="grid grid-cols-1 gap-4">
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

        {entry && (
          <>
            <FormSection title={t("Administration:FunctionalLogbookEntry")}>
              <LogbookDetailRows
                rows={[
                  { label: t("Logbook:Time"), value: f.timestamp(entry.changeTime), tabular: true },
                  { label: t("Logbook:ChangeType"), value: t(changeTypeKey(entry.changeType)) },
                  { label: t("Logbook:EntityType"), value: entry.entityTypeFullName ?? "" },
                  { label: t("Logbook:EntityId"), value: <code className="font-mono text-label">{entry.entityId ?? ""}</code> },
                  { label: t("Logbook:User"), value: entry.userName ?? "" },
                ]}
              />
            </FormSection>

            <FormSection title={t("Logbook:PropertyChanges")}>
              <PropertyChangesList changes={entry.propertyChanges ?? []} />
            </FormSection>
          </>
        )}
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
      size="md"
      title={title}
      commands={commands}
      tabs={[generalTab]}
    />
  );
}
