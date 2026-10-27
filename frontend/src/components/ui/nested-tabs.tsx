import { useState, type ReactNode } from "react";
import { CircleAlert } from "lucide-react";

import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* A tab group inside a tab (UI-FORM-008 "Nested tabs": start-aligned, text only, at most one level deep), e.g. the
 * order's Freight items / Additional services / Remarks for the planner under General, or the Container /
 * Measurements tabs inside a freight item's details (design 61 "Order page, legacy tab structure"). A tab whose
 * content holds an invalid field is marked like a main tab (UI-FORM-007: destructive title, alert icon, tooltip
 * "TabHasErrors"). Every panel stays mounted (`keepMounted`): what the user typed in a hidden tab (the stop strip of
 * the freight item being entered) is still there when they come back; a hidden panel is out of the tab order. The
 * strip wears the editor strip's look, compact: uppercase, brand underline. Never put a NestedTabs inside another. */

export interface NestedTab {
  value: string;
  /** Already translated. */
  label: string;
  content: ReactNode;
  /** UI-FORM-007: the tab's content holds an invalid field. */
  hasError?: boolean;
}

export interface NestedTabsProps {
  tabs: NestedTab[];
  /** Controlled active tab (an editor that opens the tab of the first error, UI-FORM-006); omit for the first tab. */
  value?: string;
  onValueChange?: (value: string) => void;
  className?: string;
}

const LIST = "h-8 w-full justify-start gap-0 rounded-none border-b p-0";
const TRIGGER =
  "h-8 flex-none rounded-none border-0 px-3 text-label font-semibold tracking-wide text-muted-foreground uppercase after:bottom-0 after:bg-brand hover:text-foreground data-active:text-foreground";

export function NestedTabs({ tabs, value, onValueChange, className }: NestedTabsProps) {
  const { t } = useL();
  const [uncontrolled, setUncontrolled] = useState<string | undefined>(tabs[0]?.value);
  const current = tabs.some((tab) => tab.value === (value ?? uncontrolled)) ? (value ?? uncontrolled) : tabs[0]?.value;
  return (
    <Tabs
      value={current}
      onValueChange={(next) => {
        setUncontrolled(String(next));
        onValueChange?.(String(next));
      }}
      className={cn("gap-3", className)}
      data-slot="nested-tabs"
    >
      <TooltipProvider>
        <TabsList variant="line" className={LIST}>
          {tabs.map((tab) => {
            const trigger = (
              <TabsTrigger value={tab.value} className={cn(TRIGGER, tab.hasError && "text-destructive data-active:text-destructive")} data-has-error={tab.hasError ? "true" : undefined}>
                {tab.hasError && <CircleAlert className="size-4" aria-hidden="true" />}
                {tab.label}
              </TabsTrigger>
            );
            if (!tab.hasError) return <span key={tab.value} className="contents">{trigger}</span>;
            return (
              <Tooltip key={tab.value}>
                <TooltipTrigger render={trigger} />
                <TooltipContent>{`${tab.label}: ${t("TabHasErrors")}`}</TooltipContent>
              </Tooltip>
            );
          })}
        </TabsList>
      </TooltipProvider>
      {tabs.map((tab) => (
        <TabsContent key={tab.value} value={tab.value} keepMounted data-slot="nested-tab-panel" aria-label={tab.label}>
          {tab.content}
        </TabsContent>
      ))}
    </Tabs>
  );
}
