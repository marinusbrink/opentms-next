import { useEffect } from "react";
import { useLocation, useNavigate } from "@tanstack/react-router";

import { EditionsView } from "@/apps/tenants/EditionsView";
import { TenantsView } from "@/apps/tenants/TenantsView";
import { useIsGranted } from "@/lib/auth/permissions";

/* The Tenants app (host only, docs/designs/tenants-and-editions.md): one app with two views in the left pane,
 * Tenants and Editions (PO 2026-10-14). Routes by pathname like the Administration app; the bare app path goes
 * to the first view the user may see. */
const TENANTS_PATH = "/tenants/list";
const EDITIONS_PATH = "/tenants/editions";

export function TenantsApp() {
  const location = useLocation();
  const navigate = useNavigate();
  const canTenants = useIsGranted("Platform.Tenants");
  const canEditions = useIsGranted("Platform.Editions");
  const pathname = location.pathname.replace(/\/+$/, "");
  const redirect = pathname === "/tenants" ? (canTenants ? TENANTS_PATH : canEditions ? EDITIONS_PATH : undefined) : undefined;

  useEffect(() => {
    if (redirect) {
      void navigate({ to: redirect, replace: true });
    }
  }, [redirect, navigate]);

  if (pathname.startsWith(EDITIONS_PATH)) return <EditionsView />;
  return <TenantsView />;
}
