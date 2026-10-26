import { joinDateTime } from "@/apps/transport/orders/order-form";
import { parseTimeOfDay } from "@/domains/orders/reference-data";
import type { LegUpdateDto } from "@/domains/planning-execution/legs";

/* A leg end's planned time window in the leg dialog (design 59 build note 14: `originWindow` / `destinationWindow
 * { from, till }`): two time-of-day texts on the end's planned date, as the other time fields of the Transport app
 * (HH:mm, the browser's zone). The window is sent only when the user changed it or moved the end's date (the window
 * then moves with it); both texts emptied clear a window (the whole day); an end without a change sends nothing. */

export type LegTimeWindowInput = NonNullable<LegUpdateDto["originWindow"]>;

export interface WindowTexts {
  from: string;
  till: string;
}

/** The validation key of a window: an unreadable time, or a window that ends before it starts. */
export function windowError(texts: WindowTexts): string | undefined {
  if (texts.from.trim() && !parseTimeOfDay(texts.from)) return "Orders:InvalidTime";
  if (texts.till.trim() && !parseTimeOfDay(texts.till)) return "Orders:InvalidTime";
  const from = parseTimeOfDay(texts.from);
  const till = parseTimeOfDay(texts.till);
  if (from && till && from > till) return "PlanningExecution:WindowEndsBeforeStart";
  return undefined;
}

/**
 * The window to send for one end, or undefined (unchanged). `initial` are the texts the dialog opened with,
 * `initialDate` / `date` the end's planned date before and now.
 */
export function windowInput(initial: WindowTexts, texts: WindowTexts, initialDate: string | null, date: string | null): LegTimeWindowInput | undefined {
  const same = initial.from.trim() === texts.from.trim() && initial.till.trim() === texts.till.trim();
  const empty = !texts.from.trim() && !texts.till.trim();
  if (same && (date === initialDate || empty)) return undefined;
  if (empty) return { from: null, till: null };
  const day = date ?? initialDate;
  return {
    from: texts.from.trim() ? joinDateTime(day, texts.from) : null,
    till: texts.till.trim() ? joinDateTime(day, texts.till) : null,
  };
}
