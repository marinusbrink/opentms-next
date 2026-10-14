import { useMemo, useState } from "react";

import { PermissionDenied } from "@/components/ui/permission-denied";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { BackgroundJobsTab } from "@/apps/admin/maintenance/BackgroundJobsTab";
import { CachesTab } from "@/apps/admin/maintenance/CachesTab";
import { DatabasesTab } from "@/apps/admin/maintenance/DatabasesTab";
import { HealthTab } from "@/apps/admin/maintenance/HealthTab";
import { OutboxTab } from "@/apps/admin/maintenance/OutboxTab";
import { useIsGranted, useIsHostSide } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";


export type MaintenanceTabId = "caches" | "databases" | "outbox" | "health" | "background-jobs";

/* The Maintenance view of the Administration app (docs/designs/maintenance-view.md): five tabs,
 * each a page of its own with a command bar (UI-PAGE-006 for tabbed settings pages). A tab the
 * user may not see is not rendered (UI-PERM-002); the host-only tabs are never granted to a
 * tenant. On the tenant side the whole view sits behind the Apps.Administration.Maintenance feature
 * (constitution rule 4), which the shell checks; the host has no tenant to carry a feature value and
 * is gated by its host-side permissions instead. */
export function MaintenanceView() {
  const { t } = useL();
  const isHost = useIsHostSide();
  const canView = useIsGranted("Platform.Administration.Maintenance");
  const canCaches = useIsGranted("Platform.Administration.Maintenance.Caches");
  const canDatabases = useIsGranted("Platform.Administration.Maintenance.Databases");
  const canOutbox = useIsGranted("Platform.Administration.Maintenance.Outbox");
  const canHealth = useIsGranted("Platform.Administration.Maintenance.Health");
  const canJobs = useIsGranted("Platform.Administration.Maintenance.BackgroundJobs");

  const tabs = useMemo(
    () =>
      [
        { id: "caches" as const, labelKey: "Maintenance:Caches", visible: isHost && canCaches, content: <CachesTab /> },
        { id: "databases" as const, labelKey: "Maintenance:Databases", visible: isHost && canDatabases, content: <DatabasesTab /> },
        { id: "outbox" as const, labelKey: "Maintenance:Outbox", visible: canOutbox, content: <OutboxTab /> },
        { id: "health" as const, labelKey: "Maintenance:Health", visible: isHost && canHealth, content: <HealthTab /> },
        { id: "background-jobs" as const, labelKey: "Maintenance:BackgroundJobs", visible: isHost && canJobs, content: <BackgroundJobsTab /> },
      ].filter((tab) => tab.visible),
    [isHost, canCaches, canDatabases, canOutbox, canHealth, canJobs],
  );

  const [chosen, setChosen] = useState<MaintenanceTabId | undefined>();
  const active = tabs.some((tab) => tab.id === chosen) ? chosen! : tabs[0]?.id;

  /* Its feature (Apps.Administration.Maintenance, tenant side only) is checked by the shell. */
  if (!canView || tabs.length === 0) {
    return <PermissionDenied />;
  }

  return (
    <Tabs
      value={active}
      onValueChange={(value) => setChosen(value as MaintenanceTabId)}
      className="flex h-full flex-col gap-0"
    >
      <TabsList variant="line" className="mx-6 mt-2 w-fit" aria-label={t("Administration:Maintenance")}>
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
