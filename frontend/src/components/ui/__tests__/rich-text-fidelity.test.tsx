import { render, screen } from "@testing-library/react";
import { beforeAll, describe, expect, it, vi } from "vitest";

/* Can the RichTextEditor's visual view hold an HTML value without loss? (GAP-08, PO 2026-10-18: e-mail templates are
 * often table / inline-style HTML, and silently losing layout is not acceptable.) Two parts:
 *  1. isLosslessRoundTrip / htmlSignature as a pure function: what the signature ignores (tag aliases, a bare <p>,
 *     whitespace, attribute and style-declaration order) and what it never ignores (text, elements, attributes,
 *     comments, head content, html/body attributes).
 *  2. The cases the builder checked, through the REAL editor: the value goes into a RichTextEditor, the editor's own
 *     HTML comes out, and the verdict must match; with preserveUnsupportedMarkup on, a lossy value must open in the
 *     source view with the warning and a lossless one in the visual view without it.
 * Risk class: Medium (UI component); a wrong "lossless" verdict silently rewrites a tenant's mail layout on the first
 * visual edit, so the lossy side of the table is the part that matters. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

import { RichTextEditor } from "@/components/ui/rich-text-editor";
import { htmlSignature, isLosslessRoundTrip } from "@/components/ui/rich-text-fidelity";
import { editorOf, installProseMirrorDomStubs } from "./rich-text-test-helpers";

beforeAll(installProseMirrorDomStubs);

describe("isLosslessRoundTrip — what the signature ignores", () => {
  it.each([
    ["an empty original", "", "<p>anything</p>"],
    ["a whitespace-only original", "  \n ", ""],
    ["identical HTML", "<p>Hi</p>", "<p>Hi</p>"],
    ["b → strong", "<p><b>x</b></p>", "<p><strong>x</strong></p>"],
    ["i → em", "<p><i>x</i></p>", "<p><em>x</em></p>"],
    ["strike → s", "<p><strike>x</strike></p>", "<p><s>x</s></p>"],
    ["del → s", "<p><del>x</del></p>", "<p><s>x</s></p>"],
    ["loose text wrapped in a bare <p>", "Hello {{ name }}", "<p>Hello {{ name }}</p>"],
    ["list-item content wrapped in a bare <p>", "<ul><li>One</li></ul>", "<ul><li><p>One</p></li></ul>"],
    ["whitespace inside text", "<p>a   \n b</p>", "<p>a b</p>"],
    ["a non-breaking space against a space", "<p>a&nbsp;b</p>", "<p>a b</p>"],
    ["whitespace-only text between blocks", "<p>a</p>\n\n<p>b</p>", "<p>a</p><p>b</p>"],
    ["attribute order", '<a href="x" target="_blank">l</a>', '<a target="_blank" href="x">l</a>'],
    ["style declaration order, spacing and the trailing semicolon", '<p style="color: red;text-align:center">x</p>', '<p style="text-align: center; color: red;">x</p>'],
    ["style property case", '<p style="TEXT-ALIGN: center">x</p>', '<p style="text-align: center">x</p>'],
    ["an empty style attribute", '<p style="">x</p>', "<p>x</p>"],
  ])("%s → lossless", (_name, original, roundTripped) => {
    expect(isLosslessRoundTrip(original, roundTripped)).toBe(true);
  });
});

describe("isLosslessRoundTrip — what the signature never ignores", () => {
  it.each([
    ["changed text", "<p>Hello</p>", "<p>Hallo</p>"],
    ["a dropped element", "<p><u>x</u></p>", "<p>x</p>"],
    ["a dropped wrapper (div)", "<div>x</div>", "<p>x</p>"],
    ["a <p> with attributes (not bare)", '<p class="lead">x</p>', "<p>x</p>"],
    ["a dropped attribute", '<a href="x" title="t">l</a>', '<a href="x">l</a>'],
    ["a changed attribute value", '<a href="x">l</a>', '<a href="y">l</a>'],
    ["a dropped style declaration", '<p style="text-align: center; color: red">x</p>', '<p style="text-align: center">x</p>'],
    ["a changed style value", '<p style="color: Red">x</p>', '<p style="color: red">x</p>'],
    ["a dropped comment", "<!-- keep --><p>x</p>", "<p>x</p>"],
    ["a dropped <style> in the head", "<html><head><style>p{color:red}</style></head><body><p>x</p></body></html>", "<p>x</p>"],
    ["a dropped body attribute", '<html><body bgcolor="#ffffff"><p>x</p></body></html>', "<p>x</p>"],
    ["a dropped html attribute", '<html lang="nl"><body><p>x</p></body></html>', "<p>x</p>"],
    ["reordered text", "<p>a</p><p>b</p>", "<p>b</p><p>a</p>"],
  ])("%s → lossy", (_name, original, roundTripped) => {
    expect(isLosslessRoundTrip(original, roundTripped)).toBe(false);
  });

  it("the signature lists elements, comments and text in document order", () => {
    expect(htmlSignature('<!-- c --><p>Hi <b>there</b></p><a href="x">l</a>')).toEqual(["!c", "#Hi", "<strong>", "#there", "</strong>", '<a href=x>', "#l", "</a>"]);
  });
});

/* The builder's cases (PO 2026-10-18): the left column is what a tenant's template may contain. */
const LOSSLESS: [string, string][] = [
  ["plain text with a Scriban placeholder", "Dear {{ customer.name }},"],
  ["bold (b)", "<p><b>Bold</b> text</p>"],
  ["italic (i)", "<p><i>Italic</i> text</p>"],
  ["underline (u)", "<p><u>Underlined</u> text</p>"],
  ["strike", "<p><strike>Struck</strike> text</p>"],
  ["del", "<p><del>Deleted</del> text</p>"],
  ["bullet and numbered lists", "<ul><li>One</li><li>Two</li></ul><ol><li>First</li><li>Second</li></ol>"],
  ["headings 1-3", "<h1>Title</h1><h2>Section</h2><h3>Sub</h3><p>Body</p>"],
  ["text-align", '<p style="text-align: center">Centered</p><p style="text-align: right">Right</p>'],
  ["a link with a Scriban href, target and rel", '<p><a href="{{ order.track_url }}" target="_blank" rel="noopener noreferrer">Track your order</a></p>'],
  ["br", "<p>Line one<br>Line two</p>"],
  ["hr", "<p>Above</p><hr><p>Below</p>"],
  ["blockquote", "<blockquote><p>Quoted</p></blockquote>"],
  ["&nbsp;", "<p>Total:&nbsp;{{ invoice.total }}</p>"],
  ["{{ if }} … {{ end }} around paragraphs", "{{ if invoice.overdue }}<p>Your invoice is overdue.</p>{{ end }}<p>Regards</p>"],
];

const LOSSY: [string, string][] = [
  ["a table with {{ for }} rows", "<table><tbody>{{ for line in lines }}<tr><td>{{ line.text }}</td></tr>{{ end }}</tbody></table>"],
  ["a div", "<div>Block</div>"],
  ["a span with a style", '<p><span style="color: #c00">Red</span> text</p>'],
  ["a class", '<p class="lead">Intro</p>'],
  ["a font element", '<p><font color="red">Old</font></p>'],
  ["an image", '<p><img src="https://example.com/logo.png" alt="Logo"></p>'],
  ["an Outlook conditional comment", "<!--[if mso]><p>Outlook only</p><![endif]--><p>Everyone</p>"],
  ["a full document with a style block and bgcolor", '<html><head><style>p { color: #333; }</style></head><body bgcolor="#ffffff"><p>Hello</p></body></html>'],
];

const CASES = [...LOSSLESS.map(([name, html]) => [name, html, "lossless"] as const), ...LOSSY.map(([name, html]) => [name, html, "lossy"] as const)];

describe("the builder's cases through the real editor", () => {
  it("has the cases", () => {
    expect(LOSSLESS).toHaveLength(15);
    expect(LOSSY).toHaveLength(8);
  });

  it.each(CASES)("%s → %s", (_name, html, verdict) => {
    /* What the editor makes of it (the visual view, no guard). */
    const first = render(<RichTextEditor id="probe" value={html} onChange={vi.fn()} preserveUnsupportedMarkup={false} />);
    const editorHtml = editorOf(document.getElementById("probe")!).getHTML();
    first.unmount();
    expect(isLosslessRoundTrip(html, editorHtml), `editor made: ${editorHtml}`).toBe(verdict === "lossless");

    /* What the user sees with the guard on (the default). */
    const onChange = vi.fn();
    render(<RichTextEditor id="guarded" value={html} onChange={onChange} />);
    const control = document.getElementById("guarded")!;
    if (verdict === "lossy") {
      expect(screen.getByRole("status")).toHaveTextContent("RichText:UnsupportedMarkupNotice");
      expect(control.tagName).toBe("TEXTAREA");
      expect(control).toHaveValue(html);
    } else {
      expect(screen.queryByRole("status")).toBeNull();
      expect(control).toHaveAttribute("contenteditable", "true");
    }
    expect(onChange).not.toHaveBeenCalled();
  });
});
