import { useL } from "@/lib/i18n/LocalizationProvider";

/* The state of a record page whose record does not exist (design 61, useOpenRecord: deleted meanwhile, a link from
 * another tenant, a mistyped id). Same shape as PermissionDenied (UI-PAGE-005) and FeatureUnavailable. */
export function RecordNotFound() {
  const { t } = useL();
  return (
    <div
      role="status"
      className="m-6 flex h-64 items-center justify-center rounded-lg border bg-background p-8 text-sm text-muted-foreground"
    >
      {t("RecordNotFound")}
    </div>
  );
}
