import { useCallback, useState, type ReactNode } from "react";
import { X } from "lucide-react";

import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { EditorTabStrip, Ribbon, withCloseCommand, type EntityDialogTab, type RibbonCommand } from "@/components/ui/entity-dialog";
import { Tabs } from "@/components/ui/tabs";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* A record shown as a page (design 61, Assumption 1: the transport order, the template and the trip are routes under
 * their list, reached through useOpenRecord; DEVIATION(UI-DLG-007)/(UI-PAGE-003) approved in design 61). Everything
 * else of the editor rules holds and is rendered by this one component, the page twin of EntityDialog's `editor`:
 * the brand header bar with the title, the state badges and the close X (UI-DLG-004, UI-DLG-012); the ribbon of
 * commands in captioned groups with Close added to the primary's group (UI-DLG-005, UI-ACT-002); the end-aligned tab
 * strip at the right of the ribbon row (UI-FORM-008) with the error mark (UI-FORM-007); every tab panel mounted in
 * one grid cell so switching tabs never moves anything (UI-DLG-015) and each panel scrolls its own content
 * (UI-DLG-006); the dirty guard on Close and the X (UI-DLG-008); every command disabled while busy (UI-DLG-010). The
 * page root is `flex h-full flex-col` and never scrolls (UI-PAGE-001). */

export interface RecordPageProps {
  /** Already translated: "Transport order TO-2026-00012" or t("NewTransportOrder") (UI-DLG-004). */
  title: string;
  badges?: ReactNode;
  commands: RibbonCommand[];
  tabs: EntityDialogTab[];
  activeTab?: string;
  onActiveTabChange?: (value: string) => void;
  /** Guards Close and the X (UI-DLG-008). */
  isDirty?: boolean;
  /** Blocks closing and disables every command (UI-DLG-010). */
  isBusy?: boolean;
  /** Back to the list, after the dirty guard. */
  onClose: () => void;
  className?: string;
}

export function RecordPage({ title, badges, commands, tabs, activeTab, onActiveTabChange, isDirty = false, isBusy = false, onClose, className }: RecordPageProps) {
  const { t } = useL();
  const [discardOpen, setDiscardOpen] = useState(false);
  const [uncontrolledTab, setUncontrolledTab] = useState<string | undefined>(tabs[0]?.value);
  const currentTab = activeTab ?? uncontrolledTab ?? tabs[0]?.value;

  const requestClose = useCallback(() => {
    if (isBusy) return;
    if (isDirty) {
      setDiscardOpen(true);
      return;
    }
    onClose();
  }, [isBusy, isDirty, onClose]);

  const ribbonCommands = withCloseCommand(commands, requestClose).map((command) => ({ ...command, disabled: command.disabled || isBusy }));

  return (
    <div className={cn("flex h-full flex-col", className)} data-slot="record-page">
      <Tabs
        value={currentTab}
        onValueChange={(value) => {
          setUncontrolledTab(String(value));
          onActiveTabChange?.(String(value));
        }}
        className="contents gap-0"
      >
        <div data-slot="record-page-header" className="flex h-dialog-header shrink-0 items-center gap-2 bg-brand px-4 text-brand-foreground">
          <h1 className="truncate text-base leading-none font-semibold text-brand-foreground">{title}</h1>
          {badges}
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={t("Close")}
            onClick={requestClose}
            className="ml-auto text-brand-foreground hover:bg-brand-hover hover:text-brand-foreground"
          >
            <X className="size-5" />
          </Button>
        </div>
        <div data-slot="record-page-ribbon" className="flex shrink-0 items-end gap-2 border-b bg-background px-3 pt-2">
          <Ribbon commands={ribbonCommands} t={t} />
          <EditorTabStrip tabs={tabs} t={t} />
        </div>
        {/* grid-rows-[minmax(0,1fr)]: a layout function, not a size (the one shrinkable row the panels share). */}
        <div data-slot="record-page-body" className="grid min-h-0 flex-1 grid-rows-[minmax(0,1fr)] overflow-hidden bg-surface-page">
          {tabs.map((tab) => {
            const active = tab.value === currentTab;
            return (
              <div
                key={tab.value}
                role="tabpanel"
                aria-label={tab.label}
                aria-hidden={!active || undefined}
                inert={!active || undefined}
                data-slot="record-page-panel"
                data-state={active ? "active" : "inactive"}
                style={active ? undefined : { visibility: "hidden", opacity: 0 }}
                className={cn("col-start-1 row-start-1 min-h-0 overflow-y-auto px-6 py-5 outline-none", !active && "pointer-events-none")}
              >
                {tab.content}
              </div>
            );
          })}
        </div>
      </Tabs>
      <ConfirmDialog
        open={discardOpen}
        onOpenChange={setDiscardOpen}
        title={t("UnsavedChanges")}
        description={t("DiscardChangesQuestion")}
        confirmLabel={t("Discard")}
        destructive
        onConfirm={() => {
          setDiscardOpen(false);
          onClose();
        }}
      />
    </div>
  );
}
