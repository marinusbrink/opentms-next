import { useEffect, useState } from "react";
import { Outlet, useLocation } from "@tanstack/react-router";
import { useAuth } from "react-oidc-context";
import { findAppByPath } from "@/app/apps.config";
import { useAppFeatures } from "@/app/app-features";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { WaffleLauncher } from "@/app/shell/WaffleLauncher";
import { UserMenu } from "@/app/shell/UserMenu";
import { AppNavPane } from "@/app/shell/AppNavPane";
import { ImpersonationBanner } from "@/app/shell/ImpersonationBanner";
import { takeRefusedSwitch, takeSwitchReturnTo } from "@/auth/session-switch";
import { toast } from "sonner";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { FullScreenSpinner } from "@/app/shell/FullScreenSpinner";
import { Toaster } from "@/components/ui/sonner";

/* The authenticated Office-style shell: slim top app bar with the waffle launcher
 * top-left, the current app's name in the bar, and the user/tenant menu on the
 * right. Every app view renders inside this layout. */
export function AppShell() {
  const auth = useAuth();
  const { t } = useL();
  const location = useLocation();
  const activeApp = findAppByPath(location.pathname);
  const features = useAppFeatures();
  /* A route whose app or node feature is off shows why, instead of the screen (direct links, old bookmarks). */
  const unavailable = activeApp != null && features.isLoaded && !features.isPathEnabled(activeApp, location.pathname);
  const content = unavailable ? <FeatureUnavailable /> : <Outlet />;
  const [navCollapsed, setNavCollapsed] = useState(() => window.innerWidth < 768);

  useEffect(() => {
    if (auth.isAuthenticated && takeRefusedSwitch()) {
      toast.error(t("Impersonation:Refused"));
    }
  }, [auth.isAuthenticated, t]);

  useEffect(() => {
    if (!auth.isLoading && !auth.isAuthenticated && !auth.activeNavigator) {
      /* After an account switch the user lands on the screen they switched from, not on the switch's landing route. */
      void auth.signinRedirect({ state: { returnTo: takeSwitchReturnTo() ?? location.pathname } });
    }
  }, [auth, location.pathname]);

  if (!auth.isAuthenticated) {
    return <FullScreenSpinner />;
  }

  const hasNav = activeApp?.views != null && activeApp.views.length > 0 && features.visibleViews(activeApp).length > 0;

  return (
    /* Pinned to the viewport: the nav pane and the view scroll on their own, never the page (a long nav tree used to
     * put the page's scrollbar at the window edge). */
    <div className="flex h-dvh flex-col overflow-hidden bg-surface-page">
      <header className="flex h-12 items-center bg-brand text-brand-foreground shadow-sm">
        <WaffleLauncher />
        <div className="flex items-baseline gap-3 pl-1">
          <span className="text-sm font-bold tracking-wide">{t("AppName")}</span>
          {activeApp && (
            <>
              <span aria-hidden className="text-brand-foreground/40">
                |
              </span>
              <span className="text-sm">{t(activeApp.nameKey)}</span>
            </>
          )}
        </div>
        <div className="ml-auto flex items-center">
          <UserMenu />
        </div>
      </header>
      <ImpersonationBanner />
      {hasNav ? (
        <div className="flex min-h-0 flex-1">
          <AppNavPane
            app={activeApp}
            collapsed={navCollapsed}
            onToggleCollapsed={() => setNavCollapsed((v) => !v)}
          />
          <main className="min-h-0 min-w-0 flex-1 overflow-y-auto">
            {content}
          </main>
        </div>
      ) : (
        /* Same row wrapper as above: a stretched flex item has a definite height, so a full-width
         * view's h-full (a grid filling the page) resolves. */
        <div className="flex min-h-0 flex-1">
          <main className="min-h-0 min-w-0 flex-1 overflow-y-auto">
            {content}
          </main>
        </div>
      )}
      <Toaster />
    </div>
  );
}
