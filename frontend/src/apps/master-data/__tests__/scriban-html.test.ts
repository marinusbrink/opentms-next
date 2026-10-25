import { describe, expect, it } from "vitest";

import { unescapeScribanRegions } from "@/apps/master-data/components/scriban-html";

/* Scriban typed in the visual editor (design 54, the e-mail template body; PO 2026-10-18). The editor serialises text
 * HTML-escaped, so `{{ if a > b }}` would reach Scriban as `{{ if a &gt; b }}` and fail to parse on the server.
 * unescapeScribanRegions decodes the entities the serialiser writes, only inside `{{ … }}`, in one pass; a
 * non-breaking space (entity or character) becomes a plain space there. Text outside the regions and unbalanced
 * braces stay untouched. Risk class: Medium (it rewrites the stored body; the server re-parses every Scriban field on
 * save, so a wrong decode surfaces as TemplateSyntaxError, not as a silent change). */

const NBSP = " ";

describe("unescapeScribanRegions — inside {{ }}", () => {
  it.each([
    ["&gt;", "{{ if a &gt; b }}", "{{ if a > b }}"],
    ["&lt;", "{{ if a &lt; b }}", "{{ if a < b }}"],
    ["&amp;", "{{ a &amp;&amp; b }}", "{{ a && b }}"],
    ["&quot;", "{{ x == &quot;yes&quot; }}", '{{ x == "yes" }}'],
    ["&#39;", "{{ x == &#39;yes&#39; }}", "{{ x == 'yes' }}"],
    ["&#x27;", "{{ x == &#x27;yes&#x27; }}", "{{ x == 'yes' }}"],
    ["upper-case entity names", "{{ a &GT; b &LT; c &AMP; d &QUOT;e&QUOT; &#X27;f&#X27; }}", "{{ a > b < c & d \"e\" 'f' }}"],
    ["a placeholder without entities", "<p>Dear {{ customer.name }},</p>", "<p>Dear {{ customer.name }},</p>"],
    ["a region spanning lines", "{{ if a &gt; b\n   &amp;&amp; c }}", "{{ if a > b\n   && c }}"],
    ["entities it does not know stay", "{{ a &euro; &#62; &copy; }}", "{{ a &euro; &#62; &copy; }}"],
  ])("%s", (_name, input, expected) => {
    expect(unescapeScribanRegions(input)).toBe(expected);
  });
});

describe("unescapeScribanRegions — non-breaking spaces", () => {
  it.each([
    ["&nbsp;", "{{&nbsp;customer.name&nbsp;}}", "{{ customer.name }}"],
    ["&NBSP; (upper case)", "{{&NBSP;x }}", "{{ x }}"],
    ["&#160;", "{{&#160;x&#160;}}", "{{ x }}"],
    ["the U+00A0 character", `{{${NBSP}if a${NBSP}&gt; b }}`, "{{ if a > b }}"],
    ["outside a region they stay", `<p>Total:&nbsp;{{ total }}${NBSP}EUR&#160;</p>`, `<p>Total:&nbsp;{{ total }}${NBSP}EUR&#160;</p>`],
  ])("%s", (_name, input, expected) => {
    expect(unescapeScribanRegions(input)).toBe(expected);
  });
});

describe("unescapeScribanRegions — only the regions, once", () => {
  it("decodes only inside the regions: the text around them stays escaped", () => {
    expect(unescapeScribanRegions("<p>x &gt; y {{ if a &gt; b }}yes &amp; no{{ end }} z &lt; w</p>")).toBe("<p>x &gt; y {{ if a > b }}yes &amp; no{{ end }} z &lt; w</p>");
  });

  it("decodes every region of a body, across paragraphs", () => {
    expect(unescapeScribanRegions("<p>{{ if a &gt; b }}</p><p>a &gt; b</p><p>{{ end }}{{ c &lt; d }}</p><p>{{ &quot;e&quot; }}</p>")).toBe(
      '<p>{{ if a > b }}</p><p>a &gt; b</p><p>{{ end }}{{ c < d }}</p><p>{{ "e" }}</p>',
    );
  });

  it.each([
    ["&amp;gt; becomes &gt;, not >", "{{ s == &quot;&amp;gt;&quot; }}", '{{ s == "&gt;" }}'],
    ["&amp;amp; becomes &amp;", "{{ &amp;amp; }}", "{{ &amp; }}"],
    ["&amp;nbsp; becomes &nbsp;, not a space", "{{ &amp;nbsp; }}", "{{ &nbsp; }}"],
  ])("one pass: %s", (_name, input, expected) => {
    expect(unescapeScribanRegions(input)).toBe(expected);
  });

  it("is idempotent on its own output when no entity is left to decode", () => {
    const once = unescapeScribanRegions("<p>{{ if a &gt; b }}x &gt; y{{ end }}</p>");
    expect(unescapeScribanRegions(once)).toBe(once);
  });
});

describe("unescapeScribanRegions — unbalanced braces stay untouched", () => {
  it.each([
    ["an opening without a closing", "<p>{{ if a &gt; b</p>"],
    ["a closing without an opening", "<p>a &gt; b }}</p>"],
    ["single braces", "<p>{ a &gt; b }</p>"],
    ["no braces at all", "<p>a &gt; b &amp; c&nbsp;d</p>"],
    ["an empty string", ""],
  ])("%s", (_name, input) => {
    expect(unescapeScribanRegions(input)).toBe(input);
  });

  it("a closed region followed by an unclosed one: only the closed one is decoded", () => {
    expect(unescapeScribanRegions("{{ a &gt; b }} &gt; {{ c &gt; d")).toBe("{{ a > b }} &gt; {{ c &gt; d");
  });

  it("a region ends at the first }}", () => {
    expect(unescapeScribanRegions("{{ a &gt; b }} &gt; }}")).toBe("{{ a > b }} &gt; }}");
  });
});
