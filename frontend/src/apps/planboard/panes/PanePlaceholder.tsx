import { PLANBOARD_PANES, type PlanboardPaneProps } from "@/apps/planboard/panes/registry";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The stand-in content of a pane until its design-70 implementation lands: the pane's title on an empty surface. */
export function PanePlaceholder({ type }: PlanboardPaneProps) {
  const { t } = useL();
  const pane = PLANBOARD_PANES[type];
  const Icon = pane.icon;
  return (
    <div role="status" className="flex h-full items-center justify-center gap-2 bg-background p-4 text-sm text-muted-foreground">
      <Icon size={16} aria-hidden />
      <span>{t(pane.titleKey)}</span>
    </div>
  );
}
