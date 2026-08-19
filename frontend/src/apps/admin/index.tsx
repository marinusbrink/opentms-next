import { useEffect } from "react";
import { useLocation, useNavigate } from "@tanstack/react-router";
import { UsersView } from "@/apps/admin/users/UsersView";
import { RolesView } from "@/apps/admin/roles/RolesView";

export function AdministrationApp() {
  const location = useLocation();
  const navigate = useNavigate();

  // Redirect bare /admin → /admin/users
  useEffect(() => {
    if (location.pathname === "/admin" || location.pathname === "/admin/") {
      void navigate({ to: "/admin/users", replace: true });
    }
  }, [location.pathname, navigate]);

  if (location.pathname.startsWith("/admin/roles")) {
    return <RolesView />;
  }
  return <UsersView />;
}
