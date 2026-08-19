import { useEffect, useState } from "react";
import { Outlet, useLocation } from "@tanstack/react-router";
import { useAuth } from "react-oidc-context";
import { findAppByPath } from "@/app/apps.config";
import { WaffleLauncher } from "@/app/shell/WaffleLauncher";
import { UserTenantMenu } from "@/app/shell/UserTenantMenu";
import { AppNavPane } from "@/app/shell/AppNavPane";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { FullScreenSpinner } from "@/app/shell/FullScreenSpinner";

/* The authenticated Office-style shell: slim top app bar with the waffle launcher
 * top-left, the current app's name in the bar, and the user/tenant menu on the
 * right. Every app view renders inside this layout. */
export function AppShell() {
  const auth = useAuth();
  const { t } = useL();
  const location = useLocation();
  const activeApp = findAppByPath(location.pathname);
  const [navCollapsed, setNavCollapsed] = useState(() => window.innerWidth < 768);

  useEffect(() => {
    if (!auth.isLoading && !auth.isAuthenticated && !auth.activeNavigator) {
      void auth.signinRedirect({ state: { returnTo: location.pathname } });
    }
  }, [auth, location.pathname]);

  if (!auth.isAuthenticated) {
    return <FullScreenSpinner />;
  }

  const hasNav = activeApp?.views != null && activeApp.views.length > 0;

  return (
    <div className="flex min-h-screen flex-col bg-neutral-100">
      <header className="flex h-12 items-center bg-brand text-white shadow-sm">
        <WaffleLauncher />
        <div className="flex items-baseline gap-3 pl-1">
          <span className="text-sm font-bold tracking-wide">{t("AppName")}</span>
          {activeApp && (
            <>
              <span aria-hidden className="text-white/40">
                |
              </span>
              <span className="text-sm">{t(activeApp.nameKey)}</span>
            </>
          )}
        </div>
        <div className="ml-auto flex items-center">
          <UserTenantMenu />
        </div>
      </header>
      {hasNav ? (
        <div className="flex flex-1">
          <AppNavPane
            app={activeApp}
            collapsed={navCollapsed}
            onToggleCollapsed={() => setNavCollapsed((v) => !v)}
          />
          <main className="min-w-0 flex-1 p-6">
            <Outlet />
          </main>
        </div>
      ) : (
        <main className="flex-1 p-6">
          <Outlet />
        </main>
      )}
    </div>
  );
}
