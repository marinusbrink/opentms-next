import { useCallback, useEffect, useRef, useState } from "react";
import { GripVertical, Pencil, TriangleAlert } from "lucide-react";

import { setDragPayload, type PlanboardDragPayload } from "@/apps/planboard/dnd";
import { warningKey } from "@/apps/planboard/panes/lists/grid-helpers";
import { Button } from "@/components/ui/button";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import type { TripWarningDto } from "@/domains/planning-execution/trips";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* Grid support of the Planboard's list panes (design 70 "Panes"): the panes embed normal OpenTmsGrids (UI-BSP-002) and add
 * the visual pieces the board needs around them: the drag handle, inline text editing, the warnings
 * cell and the lateness badge. The non-visual helpers live in grid-helpers.ts. */

/** The drag handle of a row (utility column): drags the payload `payloadOf` builds for the row. */
export function DragHandleCell<TRow>({ data, payloadOf, labelKey }: { data: TRow | undefined; payloadOf: (row: TRow) => PlanboardDragPayload | null; labelKey: string }) {
  const { t } = useL();
  if (!data) return null;
  return (
    <span
      draggable
      role="button"
      aria-label={t(labelKey)}
      title={t(labelKey)}
      className="inline-flex h-full cursor-grab items-center text-muted-foreground active:cursor-grabbing"
      onDragStart={(event) => {
        const payload = payloadOf(data);
        if (!payload) {
          event.preventDefault();
          return;
        }
        setDragPayload(event.dataTransfer, payload);
      }}
    >
      <GripVertical className="size-4" aria-hidden="true" />
    </span>
  );
}

export interface InlineTextCellProps<TRow> {
  value: string | null | undefined;
  data: TRow | undefined;
  /** False: the cell only shows the value (no permission, or a business state that forbids the edit). */
  canEdit: (row: TRow) => boolean;
  maxLength: number;
  /** Saves the value; rejects after showing the failure (the cell then shows the old value again). */
  onCommit: (row: TRow, value: string | null) => Promise<void>;
}

/**
 * Inline text editing inside a list cell: the pencil (or F2 on the focused button) opens an input, Enter or leaving it
 * saves, Escape cancels. The new value shows at once (optimistic) and stays until the row is patched; a failure restores
 * the old one. The input stops its key and double-click events natively, so the grid neither opens the record on Enter
 * nor starts its own editing.
 */
export function InlineTextCell<TRow>({ value, data, canEdit, maxLength, onCommit }: InlineTextCellProps<TRow>) {
  const { t } = useL();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState("");
  const [pending, setPending] = useState<string | null | undefined>(undefined);
  const committing = useRef(false);
  /* True while the input is open: a blur after Enter, Escape or unmount does nothing. */
  const open = useRef(false);

  /* The patched row ends the optimistic value. */
  useEffect(() => setPending(undefined), [value]);

  const shown = pending !== undefined ? pending : value;
  const editable = data != null && canEdit(data);

  const close = () => {
    open.current = false;
    setEditing(false);
  };

  const commit = async (next: string) => {
    if (!open.current || committing.current || !data) return;
    close();
    const normalized = next.trim() === "" ? null : next.trim();
    if ((normalized ?? "") === (value ?? "")) return;
    committing.current = true;
    setPending(normalized);
    try {
      await onCommit(data, normalized);
    } catch {
      setPending(undefined);
    } finally {
      committing.current = false;
    }
  };

  const commitRef = useRef(commit);
  commitRef.current = commit;

  const inputRef = useCallback((input: HTMLInputElement | null) => {
    if (!input) return;
    input.focus();
    input.select();
    const stop = (event: Event) => event.stopPropagation();
    input.addEventListener("keydown", (event) => {
      event.stopPropagation();
      if (event.key === "Enter") {
        event.preventDefault();
        void commitRef.current(input.value);
      } else if (event.key === "Escape") {
        event.preventDefault();
        open.current = false;
        setEditing(false);
      }
    });
    input.addEventListener("dblclick", stop);
    input.addEventListener("mousedown", stop);
  }, []);

  if (editing) {
    return (
      <input
        ref={inputRef}
        aria-label={t("Edit")}
        defaultValue={draft}
        maxLength={maxLength}
        onBlur={(event) => void commitRef.current(event.target.value)}
        className="h-7 w-full rounded-md border border-input bg-background px-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
      />
    );
  }

  return (
    <span className="group flex h-full items-center gap-1">
      <span className="min-w-0 flex-1 truncate">{shown ?? ""}</span>
      {editable && (
        <Tooltip>
          <TooltipTrigger
            render={
              <Button
                type="button"
                variant="ghost"
                size="icon-xs"
                aria-label={t("Edit")}
                className="opacity-0 group-hover:opacity-100 focus-visible:opacity-100"
                onClick={(event) => {
                  event.stopPropagation();
                  setDraft(shown ?? "");
                  open.current = true;
                  setEditing(true);
                }}
                onDoubleClick={(event) => event.stopPropagation()}
              >
                <Pencil aria-hidden="true" />
              </Button>
            }
          />
          <TooltipContent>{t("Edit")}</TooltipContent>
        </Tooltip>
      )}
    </span>
  );
}

/** One warning icon with the count and the localized warnings in its tooltip; nothing for an empty list. */
export function TripWarningsCell({ value }: { value: TripWarningDto[] | null | undefined }) {
  const { t } = useL();
  if (!value || value.length === 0) return null;
  const label = t("PlanningExecution:Warnings");
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <span role="img" aria-label={`${label} (${value.length})`} className="inline-flex items-center gap-1 text-tone-warning">
            <TriangleAlert className="size-4" aria-hidden="true" />
            <span className="text-label tabular-nums">{value.length}</span>
          </span>
        }
      />
      <TooltipContent>
        <ul className="list-disc pl-4">
          {value.map((warning, index) => (
            <li key={`${warning.code}-${warning.legId ?? ""}-${warning.stopId ?? ""}-${index}`}>{t(warningKey(warning.code))}</li>
          ))}
        </ul>
      </TooltipContent>
    </Tooltip>
  );
}

/** The lateness badge of a stop: "Late 25 min" in the danger tone, "On time" in the success tone, nothing when unknown. */
export function LatenessBadge({ minutes, known }: { minutes: number | null; known: boolean }) {
  const { t } = useL();
  if (!known) return null;
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 text-label font-medium",
        minutes == null ? "bg-tone-success-bg text-tone-success" : "bg-tone-danger-bg text-tone-danger",
      )}
    >
      {minutes == null ? t("Planboard:Lists.OnTime") : t("Planboard:Lists.LateMinutes").replace("{0}", String(minutes))}
    </span>
  );
}

