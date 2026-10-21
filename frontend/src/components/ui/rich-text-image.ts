import { mergeAttributes } from "@tiptap/react";
import Image from "@tiptap/extension-image";

/* The RichTextEditor's image node (GAP-08). Tiptap's Image extension, with two things an e-mail body needs:
 *
 * - Inline (`inline: true`): the default bodies end with `<p><img src="{{ company.logo_url }}" alt="Logo"
 *   width="200"></p>`, and an inline image round-trips as exactly that. A block image would leave the paragraph and be
 *   re-emitted as a bare `<img>`, which an e-mail client lays out differently.
 * - A guarded `src`: only `http:`, `https:`, scheme-less (relative) URLs and Scriban expressions (`{{ … }}`) reach the
 *   editor's live DOM. An image with any other scheme (`javascript:`, `data:`, `vbscript:`, …) is dropped at parse time
 *   and never rendered with its source, whichever way it got into the document. Event-handler attributes (`onerror`)
 *   are not attributes of the node, so the schema drops them.
 *
 * `width` and `height` are the extension's own attributes: read from the attribute, written back as attributes (never
 * as a style), so `width="200"` survives unchanged. The `src` is stored and serialised as given: ProseMirror sets it as
 * an attribute and never resolves or encodes it, so a Scriban expression stays `{{ company.logo_url }}`. */

/* Browsers strip ASCII control characters and whitespace from a URL before reading its scheme; so does this check. */
const stripControls = (value: string): string =>
  Array.from(value)
    .filter((character) => {
      const code = character.charCodeAt(0);
      return code > 0x20 && code !== 0x7f;
    })
    .join("");
const SCHEME = /^[a-z][a-z0-9+.-]*:/i;

/** Is `src` a source the editor may render? http(s), a relative URL, or a Scriban expression. */
export function isAllowedImageSrc(src: string | null | undefined): boolean {
  if (!src) return false;
  const value = stripControls(src);
  if (!value) return false;
  if (value.startsWith("{{")) return true;
  const scheme = SCHEME.exec(value);
  if (!scheme) return true;
  const name = scheme[0].slice(0, -1).toLowerCase();
  return name === "http" || name === "https";
}

export const RichTextImage = Image.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      src: {
        default: null,
        parseHTML: (element: HTMLElement) => {
          const src = element.getAttribute("src");
          return isAllowedImageSrc(src) ? src : null;
        },
      },
      width: {
        default: null,
        parseHTML: (element: HTMLElement) => element.getAttribute("width"),
      },
      height: {
        default: null,
        parseHTML: (element: HTMLElement) => element.getAttribute("height"),
      },
    };
  },

  parseHTML() {
    return [
      {
        tag: this.options.allowBase64 ? "img[src]" : 'img[src]:not([src^="data:"])',
        getAttrs: (element) => (isAllowedImageSrc((element as HTMLElement).getAttribute("src")) ? null : false),
      },
    ];
  },

  renderHTML({ HTMLAttributes }) {
    const attributes = mergeAttributes(this.options.HTMLAttributes, HTMLAttributes);
    if (!isAllowedImageSrc(attributes.src as string | null)) delete attributes.src;
    return ["img", attributes];
  },
}).configure({ inline: true, allowBase64: false });
