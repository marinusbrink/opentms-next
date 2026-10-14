import { useState } from "react";
import { UserRoundCog } from "lucide-react";

import { Button } from "@/components/ui/button";
import { switchSession } from "@/auth/session-switch";
import { useReturnFromImpersonation } from "@/domains/platform/impersonation";
import { useApplicationConfiguration } from "@/lib/abp/queries";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* While a host user is logged in as a tenant user ("Log in as", docs/designs/tenants-and-editions.md), a strip under
 * the app bar says so and offers the way back. The facts come from ABP's impersonator claims, which the application
 * configuration returns; nothing shows in a normal session. */
export function ImpersonationBanner() {
  const { t } = useL();
  const { data } = useApplicationConfiguration();
  const back = useReturnFromImpersonation();
  const [error, setError] = useState<string | null>(null);

  const user = data?.currentUser;
  if (!user?.impersonatorUserId) return null;

  const returnToImpersonator = async () => {
    setError(null);
    try {
      const url = await back.mutateAsync();
      const tenant = user.impersonatorTenantId
        ? { id: user.impersonatorTenantId, name: user.impersonatorTenantName ?? "" }
        : undefined;
      switchSession(url, tenant);
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  const message = t("Impersonation:Banner")
    .replace("{0}", user.userName ?? "")
    .replace("{1}", data?.currentTenant?.name ?? "")
    .replace("{2}", user.impersonatorUserName ?? "");

  return (
    <div role="status" className="flex items-center gap-3 border-b bg-tone-warning-bg px-4 py-1.5 text-sm text-tone-warning">
      <UserRoundCog className="size-4 shrink-0" aria-hidden />
      <span className="min-w-0 flex-1 truncate">{error ?? message}</span>
      <Button size="sm" variant="outline" onClick={() => void returnToImpersonator()} disabled={back.isPending}>
        {user.impersonatorTenantId ? t("Impersonation:BackToTenant") : t("Impersonation:BackToHost")}
      </Button>
    </div>
  );
}
