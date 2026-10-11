import { useL } from "@/lib/i18n/LocalizationProvider";
import type { LogbookPropertyChange } from "@/domains/platform/administration-logbook";

/* The changed properties of one entity change: a small, fixed, read-only list inside a dialog
 * (the UI-GRID-001 exception), laid out as a three-column grid. */
export function PropertyChangesList({ changes }: { changes: LogbookPropertyChange[] }) {
  const { t } = useL();

  if (changes.length === 0) {
    return <p className="text-sm text-muted-foreground">{t("Logbook:NoPropertyChanges")}</p>;
  }

  return (
    <div className="grid grid-cols-[minmax(8rem,auto)_1fr_1fr] gap-x-4 gap-y-2 text-sm" role="table" aria-label={t("Logbook:PropertyChanges")}>
      <div className="contents" role="row">
        <span className="text-label font-medium text-muted-foreground" role="columnheader">{t("Logbook:Property")}</span>
        <span className="text-label font-medium text-muted-foreground" role="columnheader">{t("Logbook:OriginalValue")}</span>
        <span className="text-label font-medium text-muted-foreground" role="columnheader">{t("Logbook:NewValue")}</span>
      </div>
      {changes.map((change) => (
        <div key={change.id} className="contents" role="row">
          <span className="font-medium" role="cell">{change.propertyName}</span>
          <span className="break-all text-muted-foreground" role="cell">{change.originalValue ?? ""}</span>
          <span className="break-all" role="cell">{change.newValue ?? ""}</span>
        </div>
      ))}
    </div>
  );
}
