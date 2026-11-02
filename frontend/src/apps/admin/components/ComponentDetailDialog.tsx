import { Alert, AlertDescription } from "@/components/ui/alert";
import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";
import { FormSection } from "@/components/ui/form-section";
import { LogbookDetailRows } from "@/apps/admin/components/LogbookDetailRows";
import { healthComponentTexts, healthStatusKey, type HealthCheckEntry } from "@/domains/platform/administration-maintenance";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

interface ComponentDetailDialogProps {
  entry: HealthCheckEntry;
  open: boolean;
  onClose: () => void;
}

/* One component of the Health tab, read-only (UI-DLG-002 editor for viewing a record; the dialog adds Close). What a
 * system administrator needs to act: the finding with what to do, what depends on the component, the settings that
 * decide it (names only, never values) with their environment-variable form, and the technical result of the check. */
export function ComponentDetailDialog({ entry, open, onClose }: ComponentDetailDialogProps) {
  const { t } = useL();
  const f = useFormat();
  const texts = healthComponentTexts(entry, t);
  const settings = entry.settings ?? [];
  const tone = cn(entry.status === "Unhealthy" && "text-destructive", entry.status === "Degraded" && "text-tone-warning");
  const area = [t(`Maintenance:Area.${entry.area || "Core"}`), entry.isOptional ? t("Maintenance:Optional") : null]
    .filter(Boolean)
    .join(" · ");

  const generalTab: EntityDialogTab = {
    value: "general",
    label: t("General"),
    content: (
      <div className="grid grid-cols-1 gap-4">
        <FormSection title={t("Maintenance:Finding")}>
          <LogbookDetailRows
            rows={[
              { label: t("Maintenance:Status"), value: <span className={tone}>{t(healthStatusKey(entry.status))}</span> },
              { label: t("Maintenance:Area"), value: area },
              ...(entry.target ? [{ label: t("Maintenance:Target"), value: entry.target }] : []),
            ]}
          />
          <p className="mt-3 text-sm">{texts.finding}</p>
          {entry.error && (
            <Alert variant="destructive" className="mt-3">
              <AlertDescription className="break-words">{entry.error}</AlertDescription>
            </Alert>
          )}
        </FormSection>

        {texts.impact && (
          <FormSection title={t("Maintenance:Impact")}>
            <p className="text-sm">{texts.impact}</p>
          </FormSection>
        )}

        {settings.length > 0 && (
          <FormSection title={t("Maintenance:Settings")}>
            <ul className="grid gap-1 text-sm">
              {settings.map((setting) => (
                <li key={setting} className="flex flex-wrap gap-x-3">
                  <code className="font-mono">{setting}</code>
                  {setting.includes(":") && <code className="font-mono text-muted-foreground">{setting.replaceAll(":", "__")}</code>}
                </li>
              ))}
            </ul>
            <p className="mt-2 text-label text-muted-foreground">{t("Maintenance:SettingsHelp")}</p>
          </FormSection>
        )}

        <FormSection title={t("Maintenance:TechnicalDetail")}>
          <LogbookDetailRows
            rows={[
              { label: t("Maintenance:Check"), value: entry.name ?? "" },
              ...(entry.description ? [{ label: t("Maintenance:Description"), value: entry.description }] : []),
              { label: t("Maintenance:Duration"), value: f.number(entry.durationMilliseconds), tabular: true },
              { label: t("Maintenance:Tags"), value: (entry.tags ?? []).join(", ") },
            ]}
          />
        </FormSection>
      </div>
    ),
  };

  return (
    <EntityDialog
      variant="editor"
      size="md"
      title={`${t("Maintenance:Component")} ${texts.title}`}
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      tabs={[generalTab]}
    />
  );
}
