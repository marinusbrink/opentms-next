import { Suspense } from "react";
import type { IDockviewPanelProps } from "dockview-react";

import { useAppFeatures } from "@/app/app-features";
import type { PlanboardPanelParams } from "@/apps/planboard/layout";
import { isPlanboardPaneType, PLANBOARD_PANES } from "@/apps/planboard/panes/registry";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { Skeleton } from "@/components/ui/skeleton";

/* One dockview panel: the pane of its type, lazy, or FeatureUnavailable when the tenant lacks the pane's feature (a
 * stored layout may still hold it; design 70, "App, routes, features"). */
export function PlanboardPanel({ params }: IDockviewPanelProps<PlanboardPanelParams>) {
  const features = useAppFeatures();
  if (!features.isLoaded) return <Skeleton className="h-full w-full" />;
  const pane = isPlanboardPaneType(params?.type) ? PLANBOARD_PANES[params.type] : null;
  if (!pane || !features.isFeatureEnabled(pane.feature)) return <FeatureUnavailable />;
  const Pane = pane.component;
  return (
    <div className="h-full min-h-0 overflow-hidden bg-background">
      <Suspense fallback={<Skeleton className="h-full w-full" />}>
        <Pane instanceId={params.instanceId} type={pane.type} initial={params.initial} />
      </Suspense>
    </div>
  );
}
