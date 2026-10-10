import { useCallback, useRef, useState, type ReactNode } from "react";
import { X } from "lucide-react";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* Every dialog except confirmations (UI-DLG-001 to -010).
 *
 * - `editor`: a record window. Brand header bar with the title, optional state badges and
 *   the close X; the editor's commands as an AppCommandBar under the header; no footer.
 *   The **Close** command is added by this component, right after the primary command,
 *   so the dirty guard is always the same: clicking the backdrop never closes an editor;
 *   Esc, the X and Close ask "Discard changes?" when `isDirty`.
 * - `action`: one operation with parameters. Plain header, a body, a right-aligned footer
 *   `[Cancel] [Primary]` passed in by the feature. Closes on Esc and backdrop unless busy.
 *
 * `size` is a content tier (UI-DLG-003); features never pass a width. While `isBusy`, close
 * requests are ignored and every command is disabled (UI-DLG-010). */

export type EntityDialogVariant = "editor" | "action";
export type EntityDialogSize = "sm" | "md" | "lg" | "full";

export interface EntityDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  variant: EntityDialogVariant;
  size: EntityDialogSize;
  title: string;
  badges?: ReactNode;
  commands?: AppCommandBarCommand[];
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

  const editorCommands: AppCommandBarCommand[] | undefined =
    variant === "editor"
      ? withCloseCommand(commands ?? [], t, requestClose).map((command) => ({
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
                className="flex h-dialog-header shrink-0 items-center gap-2 bg-brand px-3 text-brand-foreground"
              >
                <DialogTitle className="truncate text-title leading-none font-semibold text-brand-foreground">
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
                  <X />
                </Button>
              </div>
              {editorCommands && editorCommands.length > 0 && (
                <AppCommandBar commands={editorCommands} className="shrink-0" />
              )}
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

          <div ref={bodyRef} data-slot="entity-dialog-body" className="min-h-0 flex-1 overflow-auto p-4">
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
function withCloseCommand(
  commands: AppCommandBarCommand[],
  t: (key: string) => string,
  onClose: () => void,
): AppCommandBarCommand[] {
  const closeCommand: AppCommandBarCommand = {
    id: CLOSE_COMMAND_ID,
    labelKey: "Close",
    tooltipKey: "Close",
    icon: X,
    onClick: onClose,
  };
  void t;
  const primaries = commands.filter((c) => c.isPrimary);
  const others = commands.filter((c) => !c.isPrimary && c.id !== CLOSE_COMMAND_ID);
  return [...primaries, closeCommand, ...others];
}
