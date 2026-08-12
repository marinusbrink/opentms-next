import { useState } from "react";
import { useAuth } from "react-oidc-context";
import { Building2, LogOut } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useApplicationConfiguration, findTenantByName } from "@/lib/abp/queries";
import { getStoredTenant, setStoredTenant } from "@/auth/tenant";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* User + tenant menu, right side of the app bar. The tenant shown here comes from
 * the backend's application-configuration response — i.e. it proves what tenant
 * ABP actually resolved, not what the client merely stored. */
export function UserTenantMenu() {
  const { t } = useL();
  const auth = useAuth();
  const { data: config } = useApplicationConfiguration();
  const [tenantInput, setTenantInput] = useState(getStoredTenant()?.name ?? "");
  const [tenantError, setTenantError] = useState(false);

  const userName =
    (auth.user?.profile.preferred_username as string | undefined) ??
    (auth.user?.profile.name as string | undefined) ??
    "";
  const resolvedTenantName = config?.currentTenant?.name ?? t("Shell:TenantHost");
  const initials = userName.slice(0, 2).toUpperCase() || "?";

  async function applyTenant() {
    const name = tenantInput.trim();
    setTenantError(false);

    let tenant: { id: string; name: string } | undefined;
    if (name.length > 0) {
      try {
        const result = await findTenantByName(name);
        if (!result.success || !result.tenantId) {
          setTenantError(true);
          return;
        }
        tenant = { id: result.tenantId, name: result.name ?? name };
      } catch {
        setTenantError(true);
        return;
      }
    }

    setStoredTenant(tenant);
    /* End the auth-server session too, not just the local one: an SSO cookie from
     * the previous tenant would silently win over the new __tenant context and the
     * next token would still belong to the old tenant. After the sign-out redirect
     * the user signs in again, now in the newly selected tenant's context. */
    await auth.signoutRedirect();
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button
            variant="ghost"
            className="h-12 gap-2 rounded-none px-3 text-white hover:bg-white/15 hover:text-white"
          >
            <span className="hidden text-xs sm:inline">{resolvedTenantName}</span>
            <Avatar className="size-7">
              <AvatarFallback className="bg-white/20 text-xs text-white">{initials}</AvatarFallback>
            </Avatar>
          </Button>
        }
      />
      <DropdownMenuContent align="end" className="w-72">
        <div className="px-2 py-1.5">
          <p className="text-xs text-muted-foreground">{t("Shell:SignedInAs")}</p>
          <p className="truncate text-sm font-medium">{userName}</p>
          <p className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
            <Building2 className="size-3" />
            {t("Shell:Tenant")}: {resolvedTenantName}
          </p>
        </div>
        <DropdownMenuSeparator />
        <div className="px-2 py-2" onKeyDown={(e) => e.stopPropagation()}>
          <Label htmlFor="tenant-switch" className="text-xs">
            {t("Shell:SwitchTenant")}
          </Label>
          <div className="mt-1 flex gap-2">
            <Input
              id="tenant-switch"
              value={tenantInput}
              placeholder={t("Shell:TenantName")}
              aria-invalid={tenantError}
              onChange={(e) => {
                setTenantInput(e.target.value);
                setTenantError(false);
              }}
              onKeyDown={(e) => {
                if (e.key === "Enter") void applyTenant();
              }}
              className="h-8"
            />
            <Button size="sm" className="h-8" onClick={() => void applyTenant()}>
              {t("Shell:Apply")}
            </Button>
          </div>
          <p className={`mt-1 text-xs ${tenantError ? "text-destructive" : "text-muted-foreground"}`}>
            {tenantError ? t("Shell:TenantName") : t("Shell:TenantSwitchHint")}
          </p>
        </div>
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={() => void auth.signoutRedirect()}>
          <LogOut className="size-4" />
          {t("Shell:SignOut")}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
