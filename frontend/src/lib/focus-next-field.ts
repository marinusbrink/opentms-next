/* Moving focus on after a keystroke that changed the form (UI-KEY-004): a lookup that commits its match on Tab enables the
 * fields that depend on it (a stop's address and contact once its relation is chosen) only after React has rendered
 * the commit, so the browser's own Tab move would skip them. The lookup prevents that move, renders the commit and
 * then moves focus here: to the next field in the Tab order of the innermost dialog (else the page), skipping disabled,
 * inert, hidden and tabindex="-1" elements. Shift+Tab is never routed here (fields before the lookup do not depend on it). */

const CANDIDATES = "input, select, textarea, button, a[href], [tabindex]";

const isTabbable = (el: HTMLElement) => {
  if (el.tabIndex < 0) return false;
  if ((el as HTMLInputElement).disabled) return false;
  if (el instanceof HTMLInputElement && el.type === "hidden") return false;
  /* aria-hidden is no Tab-order fact (an open list marks the rest of the page aria-hidden while it closes). */
  if (el.closest("[inert], [hidden]")) return false;
  /* jsdom has no layout: every element would count as invisible. */
  if (typeof el.checkVisibility === "function" && !navigator.userAgent.includes("jsdom")) return el.checkVisibility({ visibilityProperty: true });
  return true;
};

/** Focuses the field after `from` in the Tab order; returns false when there is none. */
export function focusNextField(from: HTMLElement): boolean {
  const root = from.closest<HTMLElement>("[role='dialog'], [role='alertdialog']") ?? from.ownerDocument.body;
  const fields = Array.from(root.querySelectorAll<HTMLElement>(CANDIDATES)).filter((el) => el === from || isTabbable(el));
  const next = fields[fields.indexOf(from) + 1];
  if (!next) return false;
  next.focus();
  return true;
}
