/* Can the RichTextEditor's visual view hold an HTML value without loss? (GAP-08; PO 2026-10-18: e-mail templates are
 * often table / inline-style HTML, and silently losing layout is not acceptable.)
 *
 * The check compares a normalised signature of the original HTML with one of the editor's own output after parsing it.
 * The signature lists elements (tag + sorted attributes, styles normalised), comments and text (whitespace collapsed),
 * in document order. It ignores only what the editor normalises without losing anything:
 *  - tag aliases the editor writes differently (b → strong, i → em, strike / del → s);
 *  - a <p> without attributes (the editor wraps loose text and list-item content in paragraphs);
 *  - whitespace-only text and whitespace differences inside text.
 * Everything else that differs (a table, a div, a class, a style the editor does not know, a comment such as an
 * Outlook conditional comment, a <style> in the head) makes the value "lossy". */

const ALIASES: Record<string, string> = { b: "strong", i: "em", strike: "s", del: "s" };

const collapse = (text: string): string => text.replace(/[\s ]+/g, " ").trim();

function normaliseStyle(style: string): string {
  return style
    .split(";")
    .map((declaration) => {
      const colon = declaration.indexOf(":");
      if (colon < 0) return collapse(declaration).toLowerCase();
      return `${collapse(declaration.slice(0, colon)).toLowerCase()}:${collapse(declaration.slice(colon + 1))}`;
    })
    .filter((declaration) => declaration.length > 0)
    .sort()
    .join(";");
}

function attributesOf(element: Element): string {
  const parts: string[] = [];
  for (const attribute of Array.from(element.attributes)) {
    const value = attribute.name === "style" ? normaliseStyle(attribute.value) : collapse(attribute.value);
    if (attribute.name === "style" && !value) continue;
    parts.push(`${attribute.name}=${value}`);
  }
  return parts.sort().join(" ");
}

function walk(node: Node, out: string[]): void {
  for (const child of Array.from(node.childNodes)) {
    if (child.nodeType === 3) {
      const text = collapse(child.textContent ?? "");
      if (text) out.push(`#${text}`);
    } else if (child.nodeType === 8) {
      out.push(`!${collapse(child.textContent ?? "")}`);
    } else if (child.nodeType === 1) {
      const element = child as Element;
      const raw = element.tagName.toLowerCase();
      const tag = ALIASES[raw] ?? raw;
      const attributes = attributesOf(element);
      const ignored = tag === "p" && !attributes;
      if (!ignored) out.push(`<${tag}${attributes ? ` ${attributes}` : ""}>`);
      walk(element, out);
      if (!ignored) out.push(`</${tag}>`);
    }
  }
}

/** The normalised signature of an HTML document or fragment (see the file comment). */
export function htmlSignature(html: string): string[] {
  const doc = new DOMParser().parseFromString(html, "text/html");
  const out: string[] = [];
  const htmlAttributes = attributesOf(doc.documentElement);
  if (htmlAttributes) out.push(`<html ${htmlAttributes}>`);
  walk(doc.head, out);
  const bodyAttributes = attributesOf(doc.body);
  if (bodyAttributes) out.push(`<body ${bodyAttributes}>`);
  /* Comments before <html> or between head and body end up outside both. */
  for (const child of Array.from(doc.childNodes)) {
    if (child.nodeType === 8) out.push(`!${collapse(child.textContent ?? "")}`);
  }
  walk(doc.body, out);
  return out;
}

/** True when `roundTripped` (the editor's HTML after parsing `original`) keeps everything `original` has. */
export function isLosslessRoundTrip(original: string, roundTripped: string): boolean {
  if (!original.trim()) return true;
  const a = htmlSignature(original);
  const b = htmlSignature(roundTripped);
  return a.length === b.length && a.every((token, index) => token === b[index]);
}
