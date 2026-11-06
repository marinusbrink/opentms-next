import { useId, useRef, useState, type ReactNode } from "react";
import { Pencil } from "lucide-react";

import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { FormField } from "@/components/ui/form-field";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* Editing a list cell in a popup (design 74 §6): the value with a pencil, like the Planboard's InlineTextCell; the pencil
 * opens a popover over the grid with whatever edits the value: a record picker, a date, a window. The popover is portalled
 * out of the grid, so AG Grid sees none of its keys and clicks. The cell knows nothing of the save: the popup calls `close`
 * after it, and the patched row brings the new value. */

const DIALOG_BODY = '[data-slot="entity-dialog-body"]';

export interface PopupEditCellProps<TRow> {
  /** What the cell shows. */
  text: ReactNode;
  data: TRow | undefined;
  /** False: the cell only shows the value (no permission, or a business state that forbids the edit). */
  canEdit: (row: TRow) => boolean;
  renderPopup: (row: TRow, close: () => void) => ReactNode;
  /** The popover's width, when the default is too small (tokens only). */
  popupClassName?: string;
}

export function PopupEditCell<TRow>({ text, data, canEdit, renderPopup, popupClassName }: PopupEditCellProps<TRow>) {
  const { t } = useL();
  const [open, setOpen] = useState(false);
  const dialogsWhenOpened = useRef(0);
  const editable = data != null && canEdit(data);
  const label = t("Edit");

  return (
    <span className="group flex h-full items-center gap-1">
      <span className="min-w-0 flex-1 truncate">{text}</span>
      {editable && (
        <Popover
          open={open}
          onOpenChange={(next, details) => {
            if (next) dialogsWhenOpened.current = document.querySelectorAll(DIALOG_BODY).length;
            /* A picker's "Add new …" opens an editor over the popover (UI-LOOK-004), which lives inside it: working in that
             * editor counts as outside for the popover, and closing it would take the editor along. */
            const leaving = details.reason === "outside-press" || details.reason === "focus-out";
            if (!next && leaving && document.querySelectorAll(DIALOG_BODY).length > dialogsWhenOpened.current) return;
            setOpen(next);
          }}
        >
          <Tooltip>
            <TooltipTrigger
              render={
                <PopoverTrigger
                  render={
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-xs"
                      aria-label={label}
                      className="opacity-0 group-hover:opacity-100 focus-visible:opacity-100 data-popup-open:opacity-100"
                      onClick={(event) => event.stopPropagation()}
                      onDoubleClick={(event) => event.stopPropagation()}
                    >
                      <Pencil aria-hidden="true" />
                    </Button>
                  }
                />
              }
            />
            <TooltipContent>{label}</TooltipContent>
          </Tooltip>
          <PopoverContent align="start" className={cn("w-80", popupClassName)} data-slot="popup-edit-cell">
            {open && renderPopup(data, () => setOpen(false))}
          </PopoverContent>
        </Popover>
      )}
    </span>
  );
}

export interface InlineDateCellProps<TRow> {
  /** `YYYY-MM-DD` or null. */
  value: string | null | undefined;
  data: TRow | undefined;
  /** The field's label in the popup (already translated). */
  label: string;
  canEdit: (row: TRow) => boolean;
  /** Without a value the save is not offered. */
  required?: boolean;
  /** Saves the date; rejects after showing the failure (the popup then stays open). */
  onCommit: (row: TRow, value: string | null) => Promise<void>;
}

/** A date-only cell (UI-FMT-004) edited with the library date picker; picking a day or leaving a typed one saves it. */
export function InlineDateCell<TRow>({ value, data, label, canEdit, required, onCommit }: InlineDateCellProps<TRow>) {
  const f = useFormat();
  return (
    <PopupEditCell
      text={<span className="tabular-nums">{f.date(value)}</span>}
      data={data}
      canEdit={canEdit}
      renderPopup={(row, close) => <DateEditor value={value ?? null} label={label} required={required} onCommit={(next) => onCommit(row, next)} close={close} />}
    />
  );
}

function DateEditor({ value, label, required, onCommit, close }: { value: string | null; label: string; required?: boolean; onCommit: (value: string | null) => Promise<void>; close: () => void }) {
  const id = useId();
  const [draft, setDraft] = useState(value);
  const [busy, setBusy] = useState(false);

  const commit = async (next: string | null) => {
    setDraft(next);
    if (busy || next === value || (required && !next)) return;
    setBusy(true);
    try {
      await onCommit(next);
      close();
    } catch {
      /* The caller showed the failure; the popup stays for another try. */
      setDraft(value);
    } finally {
      setBusy(false);
    }
  };

  return (
    <FormField label={label} htmlFor={id} required={required} floating="always">
      <DatePicker id={id} value={draft} onChange={(next) => void commit(next)} required={required} disabled={busy} autoFocus />
    </FormField>
  );
}
