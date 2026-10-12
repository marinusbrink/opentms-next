import { useEffect, useId, useMemo, useRef, useState } from "react";
import type { CustomCellEditorProps } from "ag-grid-react";
import { CalendarDays, X } from "lucide-react";
import { de, enGB, es, fr, it, nl, pt, ru } from "react-day-picker/locale";

import { Calendar } from "@/components/ui/calendar";
import { InputGroup, InputGroupAddon, InputGroupButton, InputGroupInput } from "@/components/ui/input-group";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { dateInputPattern, formatDateInput, formattingLocale, maskDateInput, parseDateInput, toDate, toIsoDate } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The library date picker (component map GAP-03, option (a); UI-FMT-002/003/004): a masked Input
 * typed in the culture's date format with the shadcn Calendar (react-day-picker) in a Popover as a
 * convenience. Typing is validated on blur; the value travels as `YYYY-MM-DD` without a time or a
 * timezone shift. Optional pickers clear with an X (UI-LOOK-006); required ones replace the value. */

export interface DatePickerProps {
  id: string;
  /** `YYYY-MM-DD` or null. */
  value: string | null;
  onChange: (value: string | null) => void;
  disabled?: boolean;
  required?: boolean;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
  "aria-required"?: boolean;
  /** Only a format example is allowed (UI-FORM-003); defaults to the locale's pattern. */
  placeholder?: string;
  className?: string;
  autoFocus?: boolean;
  /** Hand over a complete typed date at once instead of on blur/Enter (grid cell editors). */
  commitWhileTyping?: boolean;
  /** "popover" (default) opens the calendar from a button; "inline" renders it under the input (grid cell editors). */
  calendar?: "popover" | "inline";
  /** Called after a day was picked in the calendar (after onChange); a grid cell editor ends the edit on it. */
  onPicked?: (isoDate: string) => void;
}

const DAY_PICKER_LOCALES = { "nl-NL": nl, "en-GB": enGB, "de-DE": de, "fr-FR": fr, "es-ES": es, "it-IT": it, "pt-PT": pt, "ru-RU": ru } as const;

export function DatePicker({
  id,
  value,
  onChange,
  disabled = false,
  required = false,
  "aria-invalid": ariaInvalid,
  "aria-describedby": ariaDescribedBy,
  "aria-required": ariaRequired,
  placeholder,
  className,
  autoFocus,
  commitWhileTyping = false,
  calendar = "popover",
  onPicked,
}: DatePickerProps) {
  const { t, culture } = useL();
  const locale = formattingLocale(culture);
  const [text, setText] = useState(() => formatDateInput(value, locale));
  const [invalid, setInvalid] = useState(false);
  const [open, setOpen] = useState(false);
  const textRef = useRef(text);
  textRef.current = text;

  /* Follow the value from outside; keep the user's (possibly invalid) text while it still means the same. */
  useEffect(() => {
    if (parseDateInput(textRef.current, locale) !== value) {
      setText(formatDateInput(value, locale));
      setInvalid(false);
    }
  }, [value, locale]);

  const selected = useMemo(() => toDate(value) ?? undefined, [value]);
  const canClear = !required && !disabled && (value != null || text.length > 0);

  const commit = () => {
    const trimmed = text.trim();
    if (trimmed.length === 0) {
      setInvalid(false);
      if (value !== null) onChange(null);
      return;
    }
    const parsed = parseDateInput(trimmed, locale);
    setInvalid(parsed === null);
    if (parsed !== null) setText(formatDateInput(parsed, locale));
    if (parsed !== value) onChange(parsed);
  };

  const pick = (date: Date | undefined) => {
    if (!date) return;
    const iso = toIsoDate(date);
    setText(formatDateInput(iso, locale));
    setInvalid(false);
    setOpen(false);
    if (iso !== value) onChange(iso);
    onPicked?.(iso);
  };

  const clear = () => {
    setText("");
    setInvalid(false);
    if (value !== null) onChange(null);
  };

  const calendarView = (
    <Calendar
      mode="single"
      required={false}
      selected={selected}
      defaultMonth={selected}
      onSelect={pick}
      weekStartsOn={1}
      locale={DAY_PICKER_LOCALES[locale as keyof typeof DAY_PICKER_LOCALES]}
    />
  );

  return (
    <Popover open={calendar === "popover" && open} onOpenChange={setOpen}>
      <InputGroup data-slot="date-picker" className={cn("w-auto", className)}>
        <InputGroupInput
          id={id}
          type="text"
          inputMode="numeric"
          autoComplete="off"
          autoFocus={autoFocus}
          value={text}
          placeholder={placeholder ?? dateInputPattern(locale)}
          disabled={disabled}
          aria-invalid={ariaInvalid || invalid || undefined}
          aria-describedby={ariaDescribedBy}
          aria-required={ariaRequired}
          className="tabular-nums"
          onChange={(e) => {
            const masked = maskDateInput(e.target.value, locale);
            setText(masked);
            setInvalid(false);
            if (commitWhileTyping) {
              /* A grid cell editor: the grid commits on Enter before this input sees the key, so a
               * complete date is handed over as soon as it is typed (an incomplete one stays null). */
              const parsed = masked.trim().length === 0 ? null : parseDateInput(masked, locale);
              if (parsed !== value && (parsed !== null || masked.trim().length === 0)) onChange(parsed);
            }
          }}
          onBlur={commit}
          onKeyDown={(e) => {
            if (e.key === "Enter") commit();
          }}
        />
        <InputGroupAddon align="inline-end">
          {canClear && (
            <InputGroupButton size="icon-xs" aria-label={t("Clear")} onClick={clear}>
              <X aria-hidden="true" />
            </InputGroupButton>
          )}
          {calendar === "popover" && (
            <Tooltip>
              <TooltipTrigger
                render={<PopoverTrigger render={<InputGroupButton size="icon-sm" aria-label={t("PickDate")} disabled={disabled} />} />}
              >
                <CalendarDays aria-hidden="true" />
              </TooltipTrigger>
              <TooltipContent>{t("PickDate")}</TooltipContent>
            </Tooltip>
          )}
        </InputGroupAddon>
      </InputGroup>
      {calendar === "inline" ? (
        <div data-slot="date-picker-calendar">{calendarView}</div>
      ) : (
        <PopoverContent align="end" className="w-auto p-0">
          {calendarView}
        </PopoverContent>
      )}
    </Popover>
  );
}

/* The AG Grid cell editor for a LinesGrid date column (design 43: qualification dates). AG Grid 33
 * creates React editors reactively: the grid reads `value` through `onValueChange`, so no imperative
 * getValue handle is needed and ag-grid-react stays a type-only import here. It is a popup editor
 * (DATE_CELL_EDITOR_COL_DEF) that renders the calendar inline under the masked input, so the grid's
 * `stopEditingWhenCellsLoseFocus` never fires on a calendar click; a complete typed date is handed
 * over at once. */
export function DatePickerCellEditor({ value, onValueChange, stopEditing }: CustomCellEditorProps<unknown, string | null>) {
  const id = useId();
  return (
    <div data-slot="date-picker-cell-editor" className="rounded-md border border-border bg-popover p-2 text-popover-foreground shadow-md">
      <DatePicker
        id={id}
        value={value ?? null}
        onChange={onValueChange}
        /* A picked day is final: end the edit once the grid has taken the value. */
        onPicked={() => setTimeout(() => stopEditing(), 0)}
        autoFocus
        commitWhileTyping
        calendar="inline"
      />
    </div>
  );
}

/** Column settings for a LinesGrid date column: the editor is a popup under the cell, so the inline calendar stays inside the grid. */
export const DATE_CELL_EDITOR_COL_DEF = { cellEditor: DatePickerCellEditor, cellEditorPopup: true, cellEditorPopupPosition: "under" } as const;
