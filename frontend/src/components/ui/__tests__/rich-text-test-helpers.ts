import { act } from "@testing-library/react";
import type { Editor } from "@tiptap/react";

/* Shared helpers for the tests that run the real RichTextEditor (Tiptap 3 / ProseMirror) in jsdom. Not a test file:
 * vitest's include pattern matches *.test.* only.
 *
 * jsdom has no layout: ProseMirror measures the selection (Range.getClientRects / getBoundingClientRect) when it
 * scrolls it into view, which every `focus()` command does. The stubs return empty rectangles; nothing else changes.
 * Tiptap keeps its instance on the editable element (`dom.editor`, there "for tests"), so a test can drive the editor
 * the way typing does: a ProseMirror transaction that inserts text at the selection. */

const EMPTY_RECT = { x: 0, y: 0, top: 0, left: 0, right: 0, bottom: 0, width: 0, height: 0, toJSON: () => ({}) } as DOMRect;

export function installProseMirrorDomStubs(): void {
  Range.prototype.getClientRects = function getClientRects() {
    return Object.assign([], { item: () => null }) as unknown as DOMRectList;
  };
  Range.prototype.getBoundingClientRect = () => EMPTY_RECT;
  if (!document.elementFromPoint) document.elementFromPoint = () => null;
}

/** The Tiptap editor behind a rendered contenteditable. */
export const editorOf = (element: HTMLElement): Editor => {
  const editor = (element as unknown as { editor?: Editor }).editor;
  if (!editor) throw new Error("not a Tiptap editor element");
  return editor;
};

/** Types `text` at the current selection (replacing it), as the browser's text input does. */
export const typeVisually = (element: HTMLElement, text: string) =>
  act(() => {
    const editor = editorOf(element);
    editor.view.dispatch(editor.state.tr.insertText(text));
  });

/** Moves the cursor to the end of the document, then types `text`. */
export const typeAtEnd = (element: HTMLElement, text: string) => {
  act(() => {
    editorOf(element).commands.focus("end");
  });
  typeVisually(element, text);
};

/** Selects the document positions from..to (1 is the start of the first paragraph's text). */
export const selectRange = (element: HTMLElement, from: number, to: number = from) =>
  act(() => {
    editorOf(element).commands.setTextSelection({ from, to });
  });

export const selectAll = (element: HTMLElement) =>
  act(() => {
    editorOf(element).commands.selectAll();
  });

/** The document position just inside the first occurrence of `text` (after its first character). */
export function positionIn(element: HTMLElement, text: string): number {
  let found = -1;
  editorOf(element).state.doc.descendants((node, pos) => {
    if (found < 0 && node.isText && node.text!.includes(text)) found = pos + node.text!.indexOf(text) + 1;
  });
  if (found < 0) throw new Error(`text "${text}" not in the document`);
  return found;
}

/** The document range covering the first occurrence of `text`. */
export function rangeOf(element: HTMLElement, text: string): [number, number] {
  const start = positionIn(element, text) - 1;
  return [start, start + text.length];
}
