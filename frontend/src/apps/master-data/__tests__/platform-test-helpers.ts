import { act, fireEvent, screen, within } from "@testing-library/react";
import { expect } from "vitest";

/* Shared DOM helpers for the Platform master-data editor tests (design 54). The editors render inside the real
 * EntityDialog, which keeps every tab panel mounted (UI-DLG-015), so field lookups are scoped to one panel: the
 * e-mail template's language tabs carry the same labels. Not a test file: vitest's include pattern matches *.test.*
 * only. */

export const ok = (data: unknown) => ({ data, error: undefined, response: new Response() });

export const editorDialog = () => document.querySelectorAll<HTMLElement>("[role=dialog]")[0];

const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

/** A tab panel of the editor, by its tab's label (the label is the localization key under the test's t()). */
export function panel(tabLabel: string): HTMLElement {
  const found = editorDialog().querySelector<HTMLElement>(`[role=tabpanel][aria-label="${tabLabel}"]`);
  if (!found) throw new Error(`no tab panel ${tabLabel}`);
  return found;
}

/** The input, select or textarea labelled `label` in the panel of `tabLabel`. */
export const fieldIn = (tabLabel: string, label: string) =>
  within(panel(tabLabel)).getByLabelText(new RegExp(`^${escape(label)}(?![A-Za-z.])`), { selector: "input, select, textarea" }) as HTMLInputElement;

export const queryFieldIn = (tabLabel: string, label: string) =>
  within(panel(tabLabel)).queryByLabelText(new RegExp(`^${escape(label)}(?![A-Za-z.])`), { selector: "input, select, textarea" }) as HTMLInputElement | null;

export const typeIn = (tabLabel: string, label: string, value: string) => fireEvent.change(fieldIn(tabLabel, label), { target: { value } });

export const tab = (name: string) => fireEvent.click(screen.getByRole("tab", { name }));

/** A checkbox of the ACTIVE panel (inactive panels are aria-hidden, so the role query skips them). */
export const checkbox = (name: string) => within(editorDialog()).getByRole("checkbox", { name });

export const ribbonButton = (label: string) =>
  Array.from(editorDialog().querySelectorAll<HTMLElement>("[data-slot=ribbon-button]")).find((b) => b.textContent?.includes(label)) ?? null;

export const submitEditor = () =>
  act(async () => {
    fireEvent.submit(editorDialog().querySelector("form")!);
    await Promise.resolve();
  });

/** Every enabled form control of the dialog body (tab panels only; the ribbon and the tab list are not controls). */
export function enabledControls(): HTMLElement[] {
  return Array.from(editorDialog().querySelectorAll<HTMLElement>("[role=tabpanel] input, [role=tabpanel] select, [role=tabpanel] textarea")).filter(
    (el) => !(el as HTMLInputElement).disabled && (el as HTMLInputElement).type !== "hidden" && el.getAttribute("aria-hidden") !== "true",
  );
}

export function expectNoSave() {
  expect(ribbonButton("Save")).toBeNull();
  expect(ribbonButton("Delete")).toBeNull();
}
