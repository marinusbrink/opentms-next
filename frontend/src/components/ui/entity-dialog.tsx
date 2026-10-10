import { useCallback, useRef, useState, type ReactNode } from "react";
import { CircleAlert, X, type LucideIcon } from "lucide-react";

import type { AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* Every dialog except confirmations (UI-DLG-001 to -010).
 *
 * - `editor`: a record window (D-05, D-13 A as decided 2026-10-10: the old ribbon, modernised).
 *   Brand header bar with the title, optional state badges and the close X; under it the
 *   ribbon: icon-over-label buttons in captioned groups, and at the right end of that row the
 *   tab strip (UI-FORM-008: General first, the icon tabs last); no footer. The **Close** command
 *   is added by this component, right after the primary command and in the primary's group, so
 *   the dirty guard is always the same: clicking the backdrop never closes an editor; Esc, the X
 *   and Close ask "Discard changes?" when `isDirty`.
 * - `action`: one operation with parameters. Plain header, a body, a right-aligned footer
 *   `[Cancel] [Primary]` passed in by the feature. Closes on Esc and backdrop unless busy.
 *
 * `size` is a content tier (UI-DLG-003); features never pass a width. While `isBusy`, close
 * requests are ignored and every command is disabled (UI-DLG-010). */

export type EntityDialogVariant = "editor" | "action";
export type EntityDialogSize = "sm" | "md" | "lg" | "full";

/** A ribbon command: an AppCommandBar command with the group caption it belongs to (UI-ACT-002). */
export interface RibbonCommand extends AppCommandBarCommand {
  /** Localization key of the group caption under the button(s); defaults to "Edit" for the primary group. */
  groupKey?: string;
}

/** One tab of an editor (UI-FORM-008). Text tabs have a label; the standard tabs at the end have an icon. */
export interface EntityDialogTab {
  value: string;
  label: string;
  /** Icon-only tab (Attachments, Remarks, Record identification): the label becomes the tooltip. */
  icon?: LucideIcon;
  content: ReactNode;
  /** UI-FORM-007: a tab whose content has an invalid field is marked. */
  hasError?: boolean;
}

export interface EntityDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  variant: EntityDialogVariant;
  size: EntityDialogSize;
  title: string;
  badges?: ReactNode;
  commands?: RibbonCommand[];
  /** Editor only: the tabs, in order (General first, the icon tabs last). The strip sits at the right end of the ribbon row; the panels fill the body. */
  tabs?: EntityDialogTab[];
  /** Controlled active tab; omit to let the dialog manage it (first tab on open). */
  activeTab?: string;
  onActiveTabChange?: (value: string) => void;
  footer?: ReactNode;
  isDirty?: boolean;
  isBusy?: boolean;
  /** The body when there are no tabs. */
  children?: ReactNode;
}

const SIZE_CLASSES: Record<EntityDialogSize, string> = {
  sm: "sm:max-w-dialog-sm",
  md: "sm:max-w-dialog-md",
  lg: "sm:max-w-dialog-lg",
  full: "h-[96vh] w-[98vw] max-w-none sm:max-w-none",
};

const FOCUSABLE_FIELD =
  'input:not([type="hidden"]):not([disabled]), select:not([disabled]), textarea:not([disabled]), [contenteditable="true"]';

export const CLOSE_COMMAND_ID = "close";

export function EntityDialog({
  open,
  onOpenChange,
  variant,
  size,
  title,
  badges,
  commands,
  tabs,
  activeTab,
  onActiveTabChange,
  footer,
  isDirty = false,
  isBusy = false,
  children,
}: EntityDialogProps) {
  const { t } = useL();
  const [discardOpen, setDiscardOpen] = useState(false);
  const [uncontrolledTab, setUncontrolledTab] = useState<string | undefined>(tabs?.[0]?.value);
  const bodyRef = useRef<HTMLDivElement>(null);
  const footerRef = useRef<HTMLDivElement>(null);

  const hasTabs = variant === "editor" && tabs != null && tabs.length > 0;
  const currentTab = activeTab ?? uncontrolledTab ?? tabs?.[0]?.value;
  const changeTab = (value: string) => {
    setUncontrolledTab(value);
    onActiveTabChange?.(value);
  };

  const requestClose = useCallback(() => {
    if (isBusy) return;
    if (variant === "editor" && isDirty) {
      setDiscardOpen(true);
      return;
    }
    onOpenChange(false);
  }, [isBusy, isDirty, onOpenChange, variant]);

  const handleOpenChange = useCallback(
    (next: boolean, details: { reason: string; cancel: () => void }) => {
      if (next) {
        onOpenChange(true);
        return;
      }
      /* UI-DLG-008: clicking the backdrop never closes an editor. */
      if (variant === "editor" && details.reason === "outside-press") {
        details.cancel();
        return;
      }
      requestClose();
    },
    [onOpenChange, requestClose, variant],
  );

  /* UI-KEY-003: editors focus the first editable field; action dialogs the first input, or
   * the primary button when there are no inputs. Hidden tab panels are skipped. */
  const initialFocus = useCallback(() => {
    const fields = bodyRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE_FIELD);
    const field = fields ? Array.from(fields).find((el) => el.closest("[hidden]") == null) : undefined;
    if (field) return field;
    if (variant === "action") {
      const buttons = footerRef.current?.querySelectorAll<HTMLElement>("button:not([disabled])");
      const primary = buttons && buttons.length > 0 ? buttons[buttons.length - 1] : null;
      if (primary) return primary;
    }
    return true;
  }, [variant]);

  const editorCommands: RibbonCommand[] | undefined =
    variant === "editor"
      ? withCloseCommand(commands ?? [], requestClose).map((command) => ({
          ...command,
          disabled: command.disabled || isBusy,
        }))
      : undefined;

  const content = (
    <>
      {variant === "editor" ? (
        <>
          <div
            data-slot="entity-dialog-header"
            className="flex h-dialog-header shrink-0 items-center gap-2 bg-brand px-4 text-brand-foreground"
          >
            <DialogTitle className="truncate text-base leading-none font-semibold text-brand-foreground">
              {title}
            </DialogTitle>
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
          <div
            data-slot="entity-dialog-ribbon"
            className="flex shrink-0 items-end gap-2 border-b bg-background px-3 pt-2"
          >
            {editorCommands && editorCommands.length > 0 && <Ribbon commands={editorCommands} t={t} />}
            {hasTabs && <EditorTabStrip tabs={tabs!} t={t} />}
          </div>
        </>
      ) : (
        <div data-slot="entity-dialog-header" className="flex shrink-0 items-center gap-2 px-4 pt-4">
          <DialogTitle className="text-title leading-none font-semibold">{title}</DialogTitle>
          <Button variant="ghost" size="icon-sm" aria-label={t("Close")} onClick={requestClose} className="ml-auto">
            <X />
          </Button>
        </div>
      )}

      <div
        ref={bodyRef}
        data-slot="entity-dialog-body"
        className={cn(
          "min-h-0 flex-1 overflow-auto p-4",
          variant === "editor" && "bg-surface-page px-6 py-5",
          hasTabs && "grid content-start",
        )}
      >
        {hasTabs
          ? tabs!.map((tab) => (
              /* UI-DLG-015: switching tabs never changes the dialog's height. Every panel stays
               * mounted in the same grid cell; an inactive one keeps its space but is invisible
               * (and out of the tab order and the accessibility tree), so the body is as tall as
               * the tallest tab. */
              <TabsContent
                key={tab.value}
                value={tab.value}
                keepMounted
                className="col-start-1 row-start-1 outline-none [&[hidden]]:pointer-events-none [&[hidden]]:invisible [&[hidden]]:block"
              >
                {tab.content}
              </TabsContent>
            ))
          : children}
      </div>

      {variant === "action" && footer && (
        <div ref={footerRef} data-slot="entity-dialog-footer" className="flex shrink-0 justify-end gap-2 border-t p-4">
          {footer}
        </div>
      )}
    </>
  );

  return (
    <>
      <Dialog open={open} onOpenChange={handleOpenChange}>
        <DialogContent
          showCloseButton={false}
          initialFocus={initialFocus}
          data-variant={variant}
          data-size={size}
          className={cn(
            "flex max-h-[92vh] flex-col gap-0 overflow-hidden rounded-lg p-0 shadow-xl",
            SIZE_CLASSES[size],
          )}
        >
          {hasTabs ? (
            /* The tab strip lives in the ribbon row and the panels in the body: one Tabs root
             * around both, rendered as `contents` so it does not disturb the dialog's layout. */
            <Tabs value={currentTab} onValueChange={(value) => changeTab(String(value))} className="contents gap-0">
              {content}
            </Tabs>
          ) : (
            content
          )}
        </DialogContent>
      </Dialog>

      {variant === "editor" && (
        <ConfirmDialog
          open={discardOpen}
          onOpenChange={setDiscardOpen}
          title={t("UnsavedChanges")}
          description={t("DiscardChangesQuestion")}
          confirmLabel={t("Discard")}
          destructive
          onConfirm={() => {
            setDiscardOpen(false);
            onOpenChange(false);
          }}
        />
      )}
    </>
  );
}

/** UI-ACT-002: Save (primary), then Close, then the rest. Close is never written by a feature. */
function withCloseCommand(commands: RibbonCommand[], onClose: () => void): RibbonCommand[] {
  const primaries = commands.filter((c) => c.isPrimary);
  const editGroup = primaries[0]?.groupKey ?? "Edit";
  const closeCommand: RibbonCommand = {
    id: CLOSE_COMMAND_ID,
    labelKey: "Close",
    tooltipKey: "Close",
    icon: X,
    groupKey: editGroup,
    onClick: onClose,
  };
  const others = commands.filter((c) => !c.isPrimary && c.id !== CLOSE_COMMAND_ID);
  return [...primaries.map((c) => ({ ...c, groupKey: c.groupKey ?? editGroup })), closeCommand, ...others];
}

/* The ribbon (D-13 A, modernised): icon over label, buttons in groups with a caption under
 * them, a divider between groups. Icons in the brand colour, destructive ones in red. */
function Ribbon({ commands, t }: { commands: RibbonCommand[]; t: (key: string) => string }) {
  const groups: { key: string; commands: RibbonCommand[] }[] = [];
  for (const command of commands) {
    const key = command.groupKey ?? "";
    const last = groups[groups.length - 1];
    if (last && last.key === key) {
      last.commands.push(command);
    } else {
      groups.push({ key, commands: [command] });
    }
  }

  return (
    <div className="flex items-stretch gap-2" role="toolbar" aria-label={t("Edit")}>
      {groups.map((group, index) => (
        <div key={`${group.key}-${index}`} className="flex items-stretch gap-2">
          {index > 0 && <div className="my-2 w-px self-stretch bg-border" aria-hidden="true" />}
          <div className="flex flex-col items-center gap-1 pb-1.5">
            <div className="flex gap-1.5">
              {group.commands.map((command) => (
                <RibbonButton key={command.id} command={command} t={t} />
              ))}
            </div>
            {group.key && (
              <span className="text-label font-medium tracking-wider text-muted-foreground uppercase">
                {t(group.key)}
              </span>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}

function RibbonButton({ command, t }: { command: RibbonCommand; t: (key: string) => string }) {
  const Icon = command.icon;
  const isDisabled = command.disabled === true;
  const isDestructive = command.variant === "destructive";
  return (
    <button
      type="button"
      title={t(isDisabled && command.disabledReasonKey ? command.disabledReasonKey : (command.tooltipKey ?? command.labelKey))}
      aria-disabled={isDisabled || undefined}
      data-slot="ribbon-button"
      onClick={() => {
        if (!isDisabled) command.onClick();
      }}
      className={cn(
        "flex h-[4.25rem] min-w-[4.75rem] flex-col items-center justify-center gap-1 rounded-lg border border-border bg-background px-2.5 text-sm text-foreground transition-colors",
        "hover:border-brand hover:bg-brand-subtle focus-visible:border-brand focus-visible:ring-2 focus-visible:ring-brand/40 focus-visible:outline-none",
        command.isPrimary && "border-brand/50 font-medium",
        isDestructive && "hover:border-destructive hover:bg-destructive/10",
        isDisabled && "cursor-default opacity-50 hover:border-border hover:bg-background",
      )}
    >
      {Icon && <Icon className={cn("size-7", isDestructive ? "text-destructive" : "text-brand")} aria-hidden="true" />}
      <span className="max-w-[7rem] truncate">{t(command.labelKey)}</span>
    </button>
  );
}

/* UI-FORM-008: end-aligned, General first, the icon-only standard tabs last with tooltips;
 * UI-FORM-007: a tab with errors is red with an alert icon. Old-app look: uppercase, brand ink. */
function EditorTabStrip({ tabs, t }: { tabs: EntityDialogTab[]; t: (key: string) => string }) {
  const triggerClass =
    "h-10 flex-none rounded-none border-0 px-4 text-sm font-semibold tracking-wide text-muted-foreground uppercase after:bottom-0 after:bg-brand hover:text-foreground data-active:text-foreground";
  return (
    <TooltipProvider>
      <TabsList variant="line" className="ml-auto h-auto gap-0 self-end rounded-none p-0">
        {tabs.map((tab) => {
          const Icon = tab.icon;
          const trigger = (
            <TabsTrigger
              value={tab.value}
              aria-label={Icon ? tab.label : undefined}
              className={cn(triggerClass, Icon && "px-3", tab.hasError && "text-destructive data-active:text-destructive")}
            >
              {tab.hasError && <CircleAlert className="size-4" aria-hidden="true" />}
              {Icon ? <Icon className="size-5" aria-hidden="true" /> : tab.label}
            </TabsTrigger>
          );
          const tooltip = tab.hasError ? `${tab.label}: ${t("TabHasErrors")}` : Icon ? tab.label : null;
          if (!tooltip) {
            return <span key={tab.value} className="contents">{trigger}</span>;
          }
          return (
            <Tooltip key={tab.value}>
              <TooltipTrigger render={trigger} />
              <TooltipContent>{tooltip}</TooltipContent>
            </Tooltip>
          );
        })}
      </TabsList>
    </TooltipProvider>
  );
}
