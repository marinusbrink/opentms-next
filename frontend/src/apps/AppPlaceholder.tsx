import type { ReactNode } from "react";

import type { AppDefinition } from "@/app/apps.config";
import { useAppFeatures } from "@/app/app-features";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

export interface ViewPlaceholderProps {
  /** The view permission (UI-PAGE-005): without it the PermissionDenied panel replaces the content area. */
  permission: string;
  /** The node's feature (UI-PERM-006); off renders FeatureUnavailable. The shell gates the route too. */
  feature?: string;
  /** A record page's header line (a list page has no title, UI-PAGE-002). */
  heading?: string;
  /** A list's saved filter from the URL (UI-PAGE-004), exposed as `data-filter` until the grid consumes it. */
  filter?: string;
  /** The page's own content, once the screen is built; the stub text until then. */
  children?: ReactNode;
}

/* The skeleton of a view whose screen is still to be built (design 61: the Transport app's views and pages, the new
 * Master data lists): the page states that are the same for every screen (UI-PAGE-005), with the stub text in the
 * content slot. The screen builder replaces the children; the gates stay. */
export function ViewPlaceholder({ permission, feature, heading, filter, children }: ViewPlaceholderProps) {
  const { t } = useL();
  const features = useAppFeatures();
  const canView = useIsGranted(permission);

  if (feature != null && features.isLoaded && !features.isFeatureEnabled(feature)) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;

  return (
    <div className="flex h-full flex-col" data-slot="view-placeholder" data-filter={filter}>
      {heading != null && (
        <div className="flex h-command-bar shrink-0 items-center border-b px-6">
          <h1 className="truncate text-title font-semibold">{heading}</h1>
        </div>
      )}
      <div className="min-h-0 flex-1 px-6 pb-6">
        {children ?? (
          <div className="flex h-64 flex-col items-center justify-center text-center">
            <p className="text-sm font-medium text-muted-foreground">{t("Shell:EmptyAppTitle")}</p>
            <p className="mt-1 text-sm text-muted-foreground">{t("Shell:EmptyAppText")}</p>
          </div>
        )}
      </div>
    </div>
  );
}

/* Empty authenticated page for a stubbed app. Real features arrive per approved
 * design through the department pipeline — never by growing this placeholder. */
export function AppPlaceholder({ app }: { app: AppDefinition }) {
  const { t } = useL();

  return (
    <div className="p-6">
      <div className="mx-auto mt-10 flex max-w-md flex-col items-center text-center">
        <span className={`flex size-16 items-center justify-center rounded-2xl text-white ${app.tileClass}`}>
          <app.icon className="size-8" />
        </span>
        <h1 className="mt-6 text-xl font-semibold">{t(app.nameKey)}</h1>
        <p className="mt-2 text-sm font-medium text-muted-foreground">{t("Shell:EmptyAppTitle")}</p>
        <p className="mt-1 text-sm text-muted-foreground">{t("Shell:EmptyAppText")}</p>
        <p className="mt-6 text-xs text-muted-foreground">
          {t("Shell:ReadsFrom")}:{" "}
          {app.domains.map((domain) => (
            <span key={domain} className="mx-1 rounded bg-neutral-200 px-1.5 py-0.5 font-mono">
              {domain}
            </span>
          ))}
        </p>
      </div>
    </div>
  );
}
