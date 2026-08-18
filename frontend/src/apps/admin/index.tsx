import { useEffect } from "react";
import { useLocation, useNavigate, Link } from "@tanstack/react-router";
import { UsersView } from "@/apps/admin/users/UsersView";
import { RolesView } from "@/apps/admin/roles/RolesView";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { Separator } from "@/components/ui/separator";
import { cn } from "@/lib/utils";

export function AdministrationApp() {
  const { t } = useL();
  const location = useLocation();
  const navigate = useNavigate();

  const activeTab = location.pathname.startsWith("/admin/roles") ? "roles" : "users";

  // Redirect bare /admin → /admin/users
  useEffect(() => {
    if (location.pathname === "/admin" || location.pathname === "/admin/") {
      void navigate({ to: "/admin/users", replace: true });
    }
  }, [location.pathname, navigate]);

  return (
    <div className="flex h-[calc(100vh-5rem)] flex-col gap-0">
      {/* Tab navigation */}
      <nav className="flex items-end gap-0 border-b">
        <Link
          to="/admin/users"
          className={cn(
            "border-b-2 px-4 py-2 text-sm font-medium transition-colors",
            activeTab === "users"
              ? "border-primary text-primary"
              : "border-transparent text-muted-foreground hover:text-foreground",
          )}
        >
          {t("Administration:Users")}
        </Link>
        <Link
          to="/admin/roles"
          className={cn(
            "border-b-2 px-4 py-2 text-sm font-medium transition-colors",
            activeTab === "roles"
              ? "border-primary text-primary"
              : "border-transparent text-muted-foreground hover:text-foreground",
          )}
        >
          {t("Administration:Roles")}
        </Link>
        <Separator className="mb-0" />
      </nav>

      {/* View */}
      <div className="min-h-0 flex-1 pt-4">
        {activeTab === "users" ? <UsersView /> : <RolesView />}
      </div>
    </div>
  );
}
