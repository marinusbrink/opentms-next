import { useEffect } from "react";
import { useLocation, useNavigate } from "@tanstack/react-router";
import { APPS } from "@/app/apps.config";
import { useAppFeatures } from "@/app/app-features";
import { UsersView } from "@/apps/admin/users/UsersView";
import { RolesView } from "@/apps/admin/roles/RolesView";
import { OrganizationUnitsView } from "@/apps/admin/organization-units/OrganizationUnitsView";
import { FunctionalLogbookView } from "@/apps/admin/logbook/FunctionalLogbookView";
import { TechnicalLogbookView } from "@/apps/admin/logbook/TechnicalLogbookView";
import { MaintenanceView } from "@/apps/admin/maintenance/MaintenanceView";
import { EmailSettingsView } from "@/apps/admin/email/EmailSettingsView";

const adminApp = APPS.find((app) => app.id === "admin")!;

export function AdministrationApp() {
  const location = useLocation();
  const navigate = useNavigate();
  const features = useAppFeatures();

  // Bare /admin and bare /admin/logbook go to the first view that is on ("App features", tenants-and-editions.md)
  const pathname = location.pathname.replace(/\/+$/, "");
  const redirect =
    pathname === "/admin"
      ? features.firstEnabledPath(adminApp)
      : pathname === "/admin/logbook"
        ? features.firstEnabledPath(adminApp, "/admin/logbook")
        : undefined;
  useEffect(() => {
    if (redirect) {
      void navigate({ to: redirect, replace: true });
    }
  }, [redirect, navigate]);

  if (location.pathname.startsWith("/admin/roles")) {
    return <RolesView />;
  }
  if (location.pathname.startsWith("/admin/organization-units")) {
    return <OrganizationUnitsView />;
  }
  if (location.pathname.startsWith("/admin/email")) {
    return <EmailSettingsView />;
  }
  if (location.pathname.startsWith("/admin/maintenance")) {
    return <MaintenanceView />;
  }
  if (location.pathname.startsWith("/admin/logbook/technical")) {
    return <TechnicalLogbookView />;
  }
  if (location.pathname.startsWith("/admin/logbook")) {
    return <FunctionalLogbookView />;
  }
  return <UsersView />;
}
