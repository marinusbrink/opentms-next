import { useCallback, useEffect, useMemo, useState } from "react";
import { useLocation, useNavigate } from "@tanstack/react-router";
import { DockviewReact, type DockviewApi, type DockviewTheme } from "dockview-react";
import "dockview-react/dist/styles/dockview.css";
import "@/styles/planboard-dockview.css";

import { useAppFeatures } from "@/app/app-features";
import {
  applyLayoutPlan,
  defaultLayoutPlan,
  PANE_COMPONENT,
  parseStoredLayout,
  retitlePanels,
  toStoredLayout,
  type PlanboardPanelParams,
} from "@/apps/planboard/layout";
import { PlanboardPanel } from "@/apps/planboard/PlanboardPanel";
import { PlanboardToolbar } from "@/apps/planboard/PlanboardToolbar";
import {
  isPlanboardPaneType,
  newPaneInstanceId,
  PANE_SEARCH_KEY,
  PLANBOARD_PANE_TYPES,
  PLANBOARD_PANES,
  type PlanboardPaneType,
} from "@/apps/planboard/panes/registry";
import { paneSettingsKey, PLANBOARD_LAYOUT_KEY, usePlanboardSettings } from "@/apps/planboard/settings-store";
import { PlanboardTripPeekHost } from "@/apps/planboard/trip-peek";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Planboard workspace (design 70, "Workspace", PO decision D14): a dockview layout of panes under the toolbar. The
 * layout loads once (the stored one, or the default when there is none or it is corrupt or outdated) and every change
 * is saved debounced. A nav node links here with `?pane=<type>`: the workspace opens or focuses that pane and drops the
 * parameter again, so the next click on the same node works too. */

/** dockview's theme hook: the variables live in src/styles/planboard-dockview.css, mapped to the theme tokens. */
const OPENTMS_DOCKVIEW_THEME: DockviewTheme = { name: "opentms", className: "dockview-theme-opentms", colorScheme: "light" };

const components = { [PANE_COMPONENT]: PlanboardPanel };

function EmptyWorkspace() {
  const { t } = useL();
  return (
    <div role="status" className="flex h-full items-center justify-center p-6 text-sm text-muted-foreground">
      {t("Planboard:EmptyWorkspace")}
    </div>
  );
}

export function PlanboardWorkspace() {
  const { t } = useL();
  const features = useAppFeatures();
  const { store, saver } = usePlanboardSettings();
  const location = useLocation();
  const navigate = useNavigate();
  const [api, setApi] = useState<DockviewApi | null>(null);
  const [ready, setReady] = useState(false);

  const enabledKey = features.isLoaded ? PLANBOARD_PANE_TYPES.filter((type) => features.isFeatureEnabled(PLANBOARD_PANES[type].feature)).join(",") : "";
  const enabledTypes = useMemo(() => (enabledKey ? (enabledKey.split(",") as PlanboardPaneType[]) : []), [enabledKey]);
  const titleOf = useCallback((type: PlanboardPaneType) => t(PLANBOARD_PANES[type].titleKey), [t]);

  /* Load the layout once dockview and the feature values are there. */
  useEffect(() => {
    if (!api || !features.isLoaded || ready) return;
    let cancelled = false;
    store
      .load(PLANBOARD_LAYOUT_KEY)
      .catch(() => null)
      .then((stored) => {
        if (cancelled) return;
        const layout = parseStoredLayout(stored);
        let restored = false;
        if (layout) {
          try {
            api.fromJSON(layout);
            retitlePanels(api, titleOf);
            restored = api.panels.length > 0;
          } catch {
            api.clear();
          }
        }
        if (!restored) applyLayoutPlan(api, defaultLayoutPlan((type) => enabledTypes.includes(type)), titleOf);
        setReady(true);
      });
    return () => {
      cancelled = true;
    };
  }, [api, features.isLoaded, ready, store, enabledTypes, titleOf]);

  /* Save every layout change (debounced 1 s); a closed Scheduler instance takes its settings with it. */
  useEffect(() => {
    if (!api || !ready) return;
    const layoutChanged = api.onDidLayoutChange(() => saver.schedule(PLANBOARD_LAYOUT_KEY, toStoredLayout(api.toJSON())));
    const panelRemoved = api.onDidRemovePanel((panel) => {
      const type = (panel.params as Partial<PlanboardPanelParams> | undefined)?.type;
      if (isPlanboardPaneType(type) && PLANBOARD_PANES[type].multiple) {
        saver.cancel(paneSettingsKey(panel.id));
        void store.remove(paneSettingsKey(panel.id)).catch(() => undefined);
      }
    });
    return () => {
      layoutChanged.dispose();
      panelRemoved.dispose();
    };
  }, [api, ready, saver, store]);

  /* The tab titles follow the user's language. */
  useEffect(() => {
    if (api && ready) retitlePanels(api, titleOf);
  }, [api, ready, titleOf]);

  const addPanel = useCallback(
    (type: PlanboardPaneType) => {
      if (!api) return;
      const id = newPaneInstanceId(type, api.panels.map((p) => p.id));
      const params: PlanboardPanelParams = { type, instanceId: id, ...(type === "scheduler" ? { initial: { mode: "tractor" } } : {}) };
      api.addPanel({
        id,
        component: PANE_COMPONENT,
        title: titleOf(type),
        params,
        ...(api.activeGroup ? { position: { referenceGroup: api.activeGroup, direction: "within" as const } } : {}),
      });
    },
    [api, titleOf],
  );

  /** The Add pane menu: a new Scheduler every time; another pane is focused when it is already open. */
  const addPane = useCallback(
    (type: PlanboardPaneType) => {
      if (!api) return;
      const existing = PLANBOARD_PANES[type].multiple ? undefined : api.getPanel(type);
      if (existing) existing.api.setActive();
      else addPanel(type);
    },
    [api, addPanel],
  );

  /** A nav node: focus the pane (the first Scheduler) when it is open, else open it. */
  const openOrFocusPane = useCallback(
    (type: PlanboardPaneType) => {
      if (!api) return;
      const existing = api.panels.find((p) => (p.params as Partial<PlanboardPanelParams> | undefined)?.type === type);
      if (existing) existing.api.setActive();
      else addPanel(type);
    },
    [api, addPanel],
  );

  const resetLayout = useCallback(() => {
    if (!api) return;
    api.clear();
    applyLayoutPlan(api, defaultLayoutPlan((type) => enabledTypes.includes(type)), titleOf);
  }, [api, enabledTypes, titleOf]);

  const requestedPane = (location.search as Record<string, unknown> | undefined)?.[PANE_SEARCH_KEY];
  useEffect(() => {
    if (requestedPane == null || !ready) return;
    if (isPlanboardPaneType(requestedPane) && enabledTypes.includes(requestedPane)) openOrFocusPane(requestedPane);
    void navigate({ to: "/planboard", search: {}, replace: true });
  }, [requestedPane, ready, enabledTypes, openOrFocusPane, navigate]);

  return (
    <div className="flex h-full min-h-0 flex-col">
      <PlanboardToolbar enabledTypes={enabledTypes} onAddPane={addPane} onResetLayout={resetLayout} ready={ready} />
      {/* isolate: the dockview sashes (z 99) and the scheduler component's notices stay below dialogs and sheets. */}
      <div className="relative isolate min-h-0 flex-1">
        <DockviewReact
          theme={OPENTMS_DOCKVIEW_THEME}
          components={components}
          watermarkComponent={EmptyWorkspace}
          onReady={(event) => setApi(event.api)}
        />
        <PlanboardTripPeekHost />
      </div>
    </div>
  );
}
