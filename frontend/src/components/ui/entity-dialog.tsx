import { useCallback, useRef, useState, type ReactNode } from "react";
import { X } from "lucide-react";

import type { AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* Every dialog except confirmations (UI-DLG-001 to -010).
 *
 * - `editor`: a record window (D-05, D-13 A as decided 2026-10-10: the old ribbon, modernised).
 *   Brand header bar with the title, optional state badges and the close X; under it the
 *   ribbon: icon-over-label buttons in captioned groups; no footer. The **Close** command is
 *   added by this component, right after the primary command and in the primary's group, so
 *   the dirty guard is always the same: clicking the backdrop never closes an editor; Esc,
 *   the X and Close ask "Discard changes?" when `isDirty`.
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

export interface EntityDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  variant: EntityDialogVariant;
  size: EntityDialogSize;
  title: string;
  badges?: ReactNode;
  commands?: RibbonCommand[];
  /** Editor only: the tab strip rendered at the right end of the ribbon row (UI-FORM-008). */
  tabs?: ReactNode;
  footer?: ReactNode;
  isDirty?: boolean;
  isBusy?: boolean;
  children: ReactNode;
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
  footer,
  isDirty = false,
  isBusy = false,
  children,
}: EntityDialogProps) {
  const { t } = useL();
  const [discardOpen, setDiscardOpen] = useState(false);
  const bodyRef = useRef<HTMLDivElement>(null);
  const footerRef = useRef<HTMLDivElement>(null);

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
   * the primary button when there are no inputs. */
  const initialFocus = useCallback(() => {
    const field = bodyRef.current?.querySelector<HTMLElement>(FOCUSABLE_FIELD);
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
              <div data-slot="entity-dialog-ribbon" className="flex shrink-0 items-end gap-2 border-b bg-background px-3 pt-2">
                {editorCommands && editorCommands.length > 0 && <Ribbon commands={editorCommands} t={t} />}
                {tabs && <div className="ml-auto flex items-end self-stretch">{tabs}</div>}
              </div>
            </>
          ) : (
            <div data-slot="entity-dialog-header" className="flex shrink-0 items-center gap-2 px-4 pt-4">
              <DialogTitle className="text-title leading-none font-semibold">{title}</DialogTitle>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label={t("Close")}
                onClick={requestClose}
                className="ml-auto"
              >
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
            )}
          >
            {children}
          </div>

          {variant === "action" && footer && (
            <div
              ref={footerRef}
              data-slot="entity-dialog-footer"
              className="flex shrink-0 justify-end gap-2 border-t p-4"
            >
              {footer}
            </div>
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
  return [
    ...primaries.map((c) => ({ ...c, groupKey: c.groupKey ?? editGroup })),
    closeCommand,
    ...others,
  ];
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
