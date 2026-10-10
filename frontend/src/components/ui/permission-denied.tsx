import { useL } from "@/lib/i18n/LocalizationProvider";

/* The permission-denied state of a page (UI-PAGE-005): replaces the whole content area. */
export function PermissionDenied() {
  const { t } = useL();
  return (
    <div
      role="status"
      className="flex h-64 items-center justify-center rounded-lg border bg-background p-8 text-sm text-muted-foreground"
    >
      {t("PermissionDenied")}
    </div>
  );
}
