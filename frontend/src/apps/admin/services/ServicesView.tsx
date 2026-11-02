import { useMemo, useState } from "react";

import { PermissionDenied } from "@/components/ui/permission-denied";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { EmailSettingsView } from "@/apps/admin/email/EmailSettingsView";
import { ServiceSettingsView } from "@/apps/admin/services/ServiceSettingsView";
import { useIsGranted, useIsHostSide } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

export type ServicesTabId = "email" | "documents" | "routing";

/* Administration → Services (docs/designs/maintenance-view.md, "Services"; PO 2026-11-02): the host's settings of the
 * external services, a tab per service, each a settings page of its own with a command bar.
 * DEVIATION(UI-PAGE-006): a tab per service with its own Save and test instead of one Save over all tabs (each service
 * saves and tests on its own, like the Maintenance tabs), approved in docs/designs/maintenance-view.md, "Services". E-mail is design 64's page; Documents and Routing are the service settings. A tab the user may not
 * see is not rendered (UI-PERM-002); every tab is host only. */
export function ServicesView() {
  const { t } = useL();
  const isHost = useIsHostSide();
  const canEmail = useIsGranted("Platform.Administration.EmailSettings");
  const canServices = useIsGranted("Integrations.ServiceSettings");

  const tabs = useMemo(
    () =>
      [
        { id: "email" as const, labelKey: "Services:Tab.Email", visible: canEmail, content: <EmailSettingsView /> },
        { id: "documents" as const, labelKey: "Services:Tab.Documents", visible: canServices, content: <ServiceSettingsView service="documents" /> },
        { id: "routing" as const, labelKey: "Services:Tab.Routing", visible: canServices, content: <ServiceSettingsView service="routing" /> },
      ].filter((tab) => tab.visible),
    [canEmail, canServices],
  );

  const [chosen, setChosen] = useState<ServicesTabId | undefined>();
  const active = tabs.some((tab) => tab.id === chosen) ? chosen! : tabs[0]?.id;

  if (!isHost || tabs.length === 0) {
    return <PermissionDenied />;
  }

  return (
    <Tabs value={active} onValueChange={(value) => setChosen(value as ServicesTabId)} className="flex h-full flex-col gap-0">
      <TabsList variant="line" className="mx-6 mt-2 w-fit" aria-label={t("Administration:ServiceSettings")}>
        {tabs.map((tab) => (
          <TabsTrigger key={tab.id} value={tab.id} className="px-3">
            {t(tab.labelKey)}
          </TabsTrigger>
        ))}
      </TabsList>
      {tabs.map((tab) => (
        <TabsContent key={tab.id} value={tab.id} className="flex min-h-0 flex-1 flex-col">
          {tab.content}
        </TabsContent>
      ))}
    </Tabs>
  );
}
