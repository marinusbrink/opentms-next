import { useL } from "@/lib/i18n/LocalizationProvider";

/* The state of a route whose app or navigation feature is off for this tenant (docs/designs/tenants-and-editions.md,
 * "App features"). Same shape as PermissionDenied (UI-PAGE-005), its own message: a permission can be granted by the
 * tenant's administrator, a feature only by the tenant's edition. */
export function FeatureUnavailable() {
  const { t } = useL();
  return (
    <div
      role="status"
      className="m-6 flex h-64 items-center justify-center rounded-lg border bg-background p-8 text-sm text-muted-foreground"
    >
      {t("FeatureUnavailable")}
    </div>
  );
}
