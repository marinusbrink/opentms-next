/* Scriban inside HTML typed in the visual editor (design 49, the e-mail template body; PO 2026-10-18).
 * The editor serialises text HTML-escaped, so `{{ if a > b }}` typed visually would reach Scriban as
 * `{{ if a &gt; b }}` and fail to parse. Inside every `{{ … }}` region the entities the serialiser writes are turned
 * back into plain characters; text outside the regions stays escaped. One pass, so `&amp;gt;` becomes `&gt;` and is not
 * decoded twice. A non-breaking space becomes a plain space (Scriban treats it as an unknown character). */

const ENTITIES: Record<string, string> = {
  "&gt;": ">",
  "&lt;": "<",
  "&amp;": "&",
  "&quot;": '"',
  "&#39;": "'",
  "&#x27;": "'",
  "&nbsp;": " ",
  "&#160;": " ",
};

const ENTITY = /&(?:gt|lt|amp|quot|nbsp|#39|#x27|#160);| /gi;
const REGION = /\{\{[\s\S]*?\}\}/g;

/** Decodes the HTML entities inside every `{{ … }}` region of `html`; the rest is returned unchanged. */
export function unescapeScribanRegions(html: string): string {
  if (!html.includes("{{")) return html;
  return html.replace(REGION, (region) => region.replace(ENTITY, (entity) => ENTITIES[entity.toLowerCase()] ?? " "));
}
