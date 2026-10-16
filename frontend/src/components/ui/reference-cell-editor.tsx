import { useEffect, useId, useMemo, useRef, useState } from "react";
import type { CustomCellEditorProps } from "ag-grid-react";
import { Check, Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { ReferenceOption } from "@/components/ui/reference-combobox";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The LinesGrid cell editor for a column that picks a record from a list loaded once (the country of a
 * postal-code range, the vehicle type of a load factor): ReferenceCombobox's behaviour inside a grid cell,
 * with the "Add new …" row of UI-LOOK-004. Like the date cell editor it is a popup editor that renders its
 * list inline (no portal), so a click in the list never trips the grid's stopEditingWhenCellsLoseFocus.
 * Typing filters (the highlight starts on the first match), the arrow keys move the highlight, Enter or a click
 * picks; Tab commits the highlighted option and leaves the move to the next cell to the grid (UI-KEY-004);
 * Escape cancels (the grid). ArrowDown past the last option highlights "Add new …", and Enter there creates.
 * "Add new …" cancels the edit and hands the typed text and the row to `onCreateNew`; the caller opens the
 * entity's editor on top and writes the saved record's id into that row. The keys are handled by a native
 * listener on the input: it runs before AG Grid's keydown listener on the popup, so the value is committed before
 * the grid ends the edit on Tab or Enter. ag-grid-react is a type-only import. */

export interface ReferenceCellEditorParams<TRow> {
  options: ReferenceOption[];
  /** Shows "Add new …" with the typed text; omit when the user may not create. */
  onCreateNew?: (term: string, row: TRow) => void;
  /** The label of that row, already translated. */
  createLabel?: string;
}

type Props<TRow> = CustomCellEditorProps<TRow, string | null> & ReferenceCellEditorParams<TRow>;

const ITEM_CLASS = "relative flex w-full cursor-default items-center gap-2 rounded-md py-1 pr-8 pl-1.5 text-left text-sm outline-hidden select-none";

const filterOptions = (options: ReferenceOption[], term: string) => {
  const query = term.trim().toLowerCase();
  return query ? options.filter((o) => o.label.toLowerCase().includes(query)) : options;
};

export function ReferenceCellEditor<TRow>({ value, onValueChange, stopEditing, api, data, eventKey, options, onCreateNew, createLabel }: Props<TRow>) {
  const { t } = useL();
  const listId = useId();
  const createId = `${listId}-create`;
  const inputRef = useRef<HTMLInputElement>(null);
  /* A printable key that started the edit is the first typed character. */
  const [term, setTerm] = useState(eventKey && eventKey.length === 1 ? eventKey : "");
  const filtered = useMemo(() => filterOptions(options, term), [options, term]);
  /* The highlight indexes the filtered list: the cell's value when the edit starts without a typed character,
   * the first match when it starts with one. */
  const [highlight, setHighlight] = useState(() => (term ? 0 : Math.max(0, filtered.findIndex((o) => o.value === value))));
  const canCreate = onCreateNew != null && data != null;
  /* "Add new …" is highlighted only by moving past the last option. */
  const [createHighlighted, setCreateHighlighted] = useState(false);
  const createActive = canCreate && createHighlighted;
  const active = createActive ? undefined : filtered[Math.min(highlight, filtered.length - 1)];

  const pick = (option: ReferenceOption | undefined) => {
    if (!option) return;
    onValueChange(option.value);
    /* End the edit once the grid has taken the value. */
    setTimeout(() => stopEditing(), 0);
  };

  const create = () => {
    if (!onCreateNew || !data) return;
    const typed = term.trim();
    /* Cancel the edit (the cell keeps its value); the saved record is written into the row afterwards. */
    api.stopEditing(true);
    onCreateNew(typed, data);
  };

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      event.stopPropagation();
      const last = filtered.length - 1;
      const current = Math.min(highlight, last);
      if (event.key === "ArrowDown") {
        if (createActive) return;
        if (current >= last) {
          if (canCreate) setCreateHighlighted(true);
          return;
        }
        setHighlight(current + 1);
      } else if (createActive) {
        setCreateHighlighted(false);
        setHighlight(Math.max(0, last));
      } else if (last >= 0) {
        setHighlight(Math.max(0, current - 1));
      }
    } else if (event.key === "Enter") {
      event.preventDefault();
      event.stopPropagation();
      if (createActive) create();
      else pick(active);
    } else if (event.key === "Tab") {
      /* Commit the highlighted option; the event goes on to the grid, which moves to the next cell and ends the
       * edit with this value (UI-KEY-004). On "Add new …" or without a match Tab just moves on. */
      if (active) onValueChange(active.value);
    }
  };

  /* The listener reads the latest render's state through the ref. */
  const keyHandler = useRef(onKeyDown);
  keyHandler.current = onKeyDown;
  useEffect(() => {
    const input = inputRef.current;
    if (!input) return;
    const listener = (event: KeyboardEvent) => keyHandler.current(event);
    input.addEventListener("keydown", listener);
    return () => input.removeEventListener("keydown", listener);
  }, []);

  const activeDescendant = createActive ? createId : active ? `${listId}-${active.value}` : undefined;

  return (
    <div data-slot="reference-cell-editor" className="flex w-64 flex-col rounded-md border border-border bg-popover p-1 text-popover-foreground shadow-md">
      <Input
        ref={inputRef}
        autoFocus
        value={term}
        aria-label={t("Search")}
        aria-controls={listId}
        aria-activedescendant={activeDescendant}
        role="combobox"
        aria-expanded
        onChange={(e) => {
          setTerm(e.target.value);
          setHighlight(0);
          setCreateHighlighted(false);
        }}
        className="h-8"
      />
      <div id={listId} role="listbox" className="mt-1 max-h-60 overflow-auto">
        {filtered.length === 0 ? (
          <p className="px-2 py-1.5 text-sm text-muted-foreground">{t("MasterData:NoOptions")}</p>
        ) : (
          filtered.map((option) => (
            <div
              key={option.value}
              id={`${listId}-${option.value}`}
              role="option"
              aria-selected={option.value === value}
              onMouseDown={(e) => e.preventDefault()}
              onMouseEnter={() => {
                setHighlight(filtered.indexOf(option));
                setCreateHighlighted(false);
              }}
              onClick={() => pick(option)}
              className={cn(ITEM_CLASS, option === active && "bg-accent text-accent-foreground")}
            >
              <span className="truncate">{option.label}</span>
              {option.value === value && (
                <span className="pointer-events-none absolute right-2 flex size-4 items-center justify-center">
                  <Check className="size-4" />
                </span>
              )}
            </div>
          ))
        )}
      </div>
      {canCreate && (
        <div className="mt-1 border-t border-border pt-1">
          <Button
            id={createId}
            type="button"
            variant="ghost"
            size="sm"
            data-highlighted={createActive ? "" : undefined}
            className={cn("w-full justify-start", createActive && "bg-accent text-accent-foreground")}
            onClick={create}
          >
            <Plus aria-hidden="true" /> {createLabel ?? t("AddNew")}
          </Button>
        </div>
      )}
    </div>
  );
}

/** Column settings for a LinesGrid column that picks from `options`: a popup editor under the cell. */
export function referenceCellEditorColDef<TRow>(params: ReferenceCellEditorParams<TRow>) {
  return {
    cellEditor: ReferenceCellEditor<TRow>,
    cellEditorPopup: true,
    cellEditorPopupPosition: "under" as const,
    cellEditorParams: params,
  };
}
