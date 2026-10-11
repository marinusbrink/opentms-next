import { useEffect } from "react";
import { useLocation, useNavigate } from "@tanstack/react-router";
import { UsersView } from "@/apps/admin/users/UsersView";
import { RolesView } from "@/apps/admin/roles/RolesView";
import { OrganizationUnitsView } from "@/apps/admin/organization-units/OrganizationUnitsView";
import { FunctionalLogbookView } from "@/apps/admin/logbook/FunctionalLogbookView";
import { TechnicalLogbookView } from "@/apps/admin/logbook/TechnicalLogbookView";

export function AdministrationApp() {
  const location = useLocation();
  const navigate = useNavigate();

  // Redirect bare /admin → /admin/users, and bare /admin/logbook → the functional logbook
  useEffect(() => {
    if (location.pathname === "/admin" || location.pathname === "/admin/") {
      void navigate({ to: "/admin/users", replace: true });
    } else if (location.pathname === "/admin/logbook" || location.pathname === "/admin/logbook/") {
      void navigate({ to: "/admin/logbook/functional", replace: true });
    }
  }, [location.pathname, navigate]);

  if (location.pathname.startsWith("/admin/roles")) {
    return <RolesView />;
  }
  if (location.pathname.startsWith("/admin/organization-units")) {
    return <OrganizationUnitsView />;
  }
  if (location.pathname.startsWith("/admin/logbook/technical")) {
    return <TechnicalLogbookView />;
  }
  if (location.pathname.startsWith("/admin/logbook")) {
    return <FunctionalLogbookView />;
  }
  return <UsersView />;
}
