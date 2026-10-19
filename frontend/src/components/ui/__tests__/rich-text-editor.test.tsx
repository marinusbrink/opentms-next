import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { StrictMode, Suspense, lazy, useState } from "react";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";

/* RichTextEditor (component map GAP-08, PO 2026-10-18: Tiptap), the HTML body control of the e-mail template
 * (design 49). The real editor runs in jsdom (Tiptap 3 / ProseMirror; the layout stubs are in rich-text-test-helpers);
 * a "typed" edit is a ProseMirror text-input transaction. Covered: the toolbar (labels, tooltips, aria-pressed), HTML
 * out (bold, the empty document as ""), a value from outside (no onChange, no undo step), own edits not resetting the
 * editor, the source view (raw HTML, emitted as typed), the unsupported-markup guard (lossy values open in the source
 * view with a role=status warning, the visual view stays reachable, the value is kept until an edit there),
 * transformOutput (visual output only), maxLength, read-only / disabled, onBlur for the whole control, and the link
 * popover. Risk class: Medium (UI component), except "untrusted HTML" below: Critical (template HTML authored by one
 * user is rendered in another user's session in the app's origin; script or event handlers reaching the live DOM
 * would be a session / tenant-data leak). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

import { RichTextEditor, type RichTextEditorProps } from "@/components/ui/rich-text-editor";
import { editorOf, installProseMirrorDomStubs, positionIn, rangeOf, selectAll, selectRange, typeAtEnd, typeVisually } from "./rich-text-test-helpers";

beforeAll(installProseMirrorDomStubs);

afterEach(() => {
  delete (window as unknown as { __pwned?: unknown }).__pwned;
});

const ID = "body";
const TOOLBAR_LABELS = [
  "RichText:Bold",
  "RichText:Italic",
  "RichText:Underline",
  "RichText:Strikethrough",
  "RichText:Paragraph",
  "RichText:Heading1",
  "RichText:Heading2",
  "RichText:Heading3",
  "RichText:BulletList",
  "RichText:OrderedList",
  "RichText:Link",
  "RichText:AlignLeft",
  "RichText:AlignCenter",
  "RichText:AlignRight",
  "RichText:Undo",
  "RichText:Redo",
  "RichText:SourceView",
];
const FORMATTING_LABELS = TOOLBAR_LABELS.filter((label) => label !== "RichText:SourceView");
const TABLE = "<table><tbody>{{ for line in lines }}<tr><td>{{ line.text }}</td></tr>{{ end }}</tbody></table>";

/** The editable element: the contenteditable in the visual view, the textarea in the source view (both carry the id). */
const control = () => document.getElementById(ID) as HTMLElement;
const isVisual = () => control().getAttribute("contenteditable") != null;
const toolbar = () => screen.getByRole("toolbar", { name: "RichText:Toolbar" });
const tool = (label: string) => within(toolbar()).getByRole("button", { name: label });
const warning = () => screen.queryByRole("status");
const lastEmitted = (spy: ReturnType<typeof vi.fn>) => spy.mock.calls.at(-1)?.[0] as string | undefined;
const openTooltips = () => Array.from(document.querySelectorAll("[data-slot=tooltip-content][data-open]")).map((el) => el.textContent);

function renderEditor(props: Partial<RichTextEditorProps> = {}) {
  const onChange = vi.fn();
  const utils = render(<RichTextEditor id={ID} value="<p>Hello world</p>" onChange={onChange} {...props} />);
  return { onChange, ...utils };
}

/** A parent that keeps the value, as a form does. */
function Controlled({ initial, onChange, ...rest }: Partial<RichTextEditorProps> & { initial: string }) {
  const [value, setValue] = useState(initial);
  return (
    <RichTextEditor
      id={ID}
      value={value}
      onChange={(next) => {
        onChange?.(next);
        setValue(next);
      }}
      {...rest}
    />
  );
}

describe("RichTextEditor — toolbar", () => {
  it("is a toolbar named Formatting that controls the editable; every button is type=button with its label as aria-label and tooltip", async () => {
    renderEditor();
    expect(toolbar()).toHaveAttribute("aria-controls", ID);
    const buttons = within(toolbar()).getAllByRole("button");
    expect(buttons.map((b) => b.getAttribute("aria-label"))).toEqual(TOOLBAR_LABELS);
    /* A toolbar button inside the editor's <form> must never submit it. */
    expect(buttons.every((b) => b.getAttribute("type") === "button")).toBe(true);
    for (const label of TOOLBAR_LABELS) {
      const button = tool(label);
      if ((button as HTMLButtonElement).disabled) continue;
      act(() => button.focus());
      await waitFor(() => expect(openTooltips()).toContain(label));
      act(() => button.blur());
    }
  });

  it("aria-pressed follows the selection (heading, paragraph, alignment, bold, link, list); Undo and Redo are not toggles", () => {
    renderEditor({
      value: '<h2>Title</h2><p style="text-align: center"><strong>Bold</strong> plain <a href="https://example.com">link</a></p><ul><li><p>Item</p></li></ul>',
    });
    const pressed = () => Object.fromEntries(FORMATTING_LABELS.map((label) => [label.replace("RichText:", ""), tool(label).getAttribute("aria-pressed")]));

    selectRange(control(), positionIn(control(), "Title"));
    expect(pressed()).toEqual(expect.objectContaining({ Heading2: "true", Heading1: "false", Heading3: "false", Paragraph: "false", Bold: "false", BulletList: "false" }));

    selectRange(control(), positionIn(control(), "Bold"));
    expect(pressed()).toEqual(expect.objectContaining({ Bold: "true", Paragraph: "true", Heading2: "false", AlignCenter: "true", AlignLeft: "false", AlignRight: "false", Link: "false" }));

    selectRange(control(), positionIn(control(), "link"));
    expect(pressed()).toEqual(expect.objectContaining({ Link: "true", Bold: "false" }));

    selectRange(control(), positionIn(control(), "Item"));
    expect(pressed()).toEqual(expect.objectContaining({ BulletList: "true", OrderedList: "false" }));

    expect(tool("RichText:Undo")).not.toHaveAttribute("aria-pressed");
    expect(tool("RichText:Redo")).not.toHaveAttribute("aria-pressed");
    expect(tool("RichText:SourceView")).toHaveAttribute("aria-pressed", "false");
  });
});

describe("RichTextEditor — HTML out, value in", () => {
  it("Bold wraps the selection in <strong> and emits the HTML; Bold again removes it", () => {
    const { onChange } = renderEditor();
    selectRange(control(), ...rangeOf(control(), "Hello"));
    fireEvent.click(tool("RichText:Bold"));
    expect(lastEmitted(onChange)).toBe("<p><strong>Hello</strong> world</p>");
    expect(tool("RichText:Bold")).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(tool("RichText:Bold"));
    expect(lastEmitted(onChange)).toBe("<p>Hello world</p>");
    expect(tool("RichText:Bold")).toHaveAttribute("aria-pressed", "false");
  });

  it('an emptied document is emitted as "" (not <p></p>), so a required check on the value works', () => {
    const { onChange } = renderEditor();
    selectAll(control());
    act(() => {
      editorOf(control()).commands.deleteSelection();
    });
    expect(lastEmitted(onChange)).toBe("");
  });

  it("a value from outside replaces the content without onChange and without an undo step", () => {
    const { onChange, rerender } = renderEditor();
    rerender(<RichTextEditor id={ID} value="<p>Second record</p>" onChange={onChange} />);
    expect(control()).toHaveTextContent("Second record");
    expect(control()).not.toHaveTextContent("Hello");
    expect(tool("RichText:Undo")).toBeDisabled();
    rerender(<RichTextEditor id={ID} value="" onChange={onChange} />);
    expect(control().textContent).toBe("");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("own edits do not reset the editor: the parent passing the emitted value back keeps the content and the cursor", () => {
    const onChange = vi.fn();
    render(<Controlled initial="<p>Hello world</p>" onChange={onChange} />);
    selectRange(control(), rangeOf(control(), "Hello")[1]);
    typeVisually(control(), ",");
    typeVisually(control(), " there");
    expect(lastEmitted(onChange)).toBe("<p>Hello, there world</p>");
    expect(control()).toHaveTextContent("Hello, there world");
    /* One undo step per edit: the round trip through the parent added none. */
    expect(tool("RichText:Undo")).toBeEnabled();
  });
});

describe("RichTextEditor — source view", () => {
  it("shows a textarea with the raw HTML; its edits are emitted as typed (no transform) and formatting is off there", () => {
    const onChange = vi.fn();
    const transformOutput = vi.fn((html: string) => html.toUpperCase());
    render(<Controlled initial="<p>Hi {{ name }}</p>" onChange={onChange} transformOutput={transformOutput} aria-labelledby="the-label" />);
    fireEvent.click(tool("RichText:SourceView"));
    expect(tool("RichText:SourceView")).toHaveAttribute("aria-pressed", "true");
    expect(control().tagName).toBe("TEXTAREA");
    expect(control()).toHaveValue("<p>Hi {{ name }}</p>");
    expect(control()).toHaveAttribute("aria-labelledby", "the-label");
    for (const label of FORMATTING_LABELS) expect(tool(label)).toBeDisabled();

    const typed = "<p>Hi &gt; {{ if a > b }}</p>  \n";
    fireEvent.change(control(), { target: { value: typed } });
    expect(onChange).toHaveBeenLastCalledWith(typed);
    expect(transformOutput).not.toHaveBeenCalled();

    /* Back to the visual view: the typed source is shown parsed; the toggle itself emits nothing. */
    fireEvent.click(tool("RichText:SourceView"));
    expect(isVisual()).toBe(true);
    expect(control()).toHaveTextContent("Hi > {{ if a > b }}");
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(warning()).toBeNull();
  });

  it("a lossy initial value opens in the source view with the warning; the visual view stays reachable and the value is kept until an edit there", () => {
    const onChange = vi.fn();
    render(<Controlled initial={TABLE} onChange={onChange} />);
    expect(warning()).toHaveTextContent("RichText:UnsupportedMarkupNotice");
    expect(control().tagName).toBe("TEXTAREA");
    expect(control()).toHaveValue(TABLE);
    expect(tool("RichText:SourceView")).toHaveAttribute("aria-pressed", "true");
    expect(tool("RichText:Bold")).toBeDisabled();

    fireEvent.click(tool("RichText:SourceView"));
    expect(isVisual()).toBe(true);
    expect(warning()).not.toBeNull();
    expect(onChange).not.toHaveBeenCalled();

    /* An edit in the visual view makes its content the value: the warning no longer applies. */
    typeAtEnd(control(), "!");
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(warning()).toBeNull();
  });

  it("switching back from the source view checks the typed HTML: a lossy one shows the warning", () => {
    const onChange = vi.fn();
    render(<Controlled initial="<p>Hi</p>" onChange={onChange} />);
    expect(warning()).toBeNull();
    fireEvent.click(tool("RichText:SourceView"));
    fireEvent.change(control(), { target: { value: '<div class="box">Hi</div>' } });
    fireEvent.click(tool("RichText:SourceView"));
    expect(isVisual()).toBe(true);
    expect(warning()).not.toBeNull();
    expect(onChange.mock.calls).toEqual([['<div class="box">Hi</div>']]);
  });

  it("a lossy value from outside switches to the source view with the warning, without onChange", () => {
    const { onChange, rerender } = renderEditor();
    expect(isVisual()).toBe(true);
    rerender(<RichTextEditor id={ID} value={TABLE} onChange={onChange} />);
    expect(control().tagName).toBe("TEXTAREA");
    expect(warning()).not.toBeNull();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("preserveUnsupportedMarkup={false} opens a lossy value in the visual view without a warning", () => {
    renderEditor({ value: TABLE, preserveUnsupportedMarkup: false });
    expect(isVisual()).toBe(true);
    expect(warning()).toBeNull();
  });
});

describe("RichTextEditor — transformOutput", () => {
  it("rewrites what the visual view emits; the source view emits the textarea's text untouched", () => {
    const onChange = vi.fn();
    const transformOutput = vi.fn((html: string) => html.replace(/&gt;/g, ">"));
    render(<Controlled initial="<p>a</p>" onChange={onChange} transformOutput={transformOutput} />);
    typeAtEnd(control(), " {{ x > y }}");
    expect(transformOutput).toHaveBeenLastCalledWith("<p>a {{ x &gt; y }}</p>");
    expect(lastEmitted(onChange)).toBe("<p>a {{ x > y }}</p>");
    /* The transformed value counts as the own edit: the editor is not reset to it. */
    expect(control()).toHaveTextContent("a {{ x > y }}");

    transformOutput.mockClear();
    fireEvent.click(tool("RichText:SourceView"));
    fireEvent.change(control(), { target: { value: "<p>&gt;</p>" } });
    expect(lastEmitted(onChange)).toBe("<p>&gt;</p>");
    expect(transformOutput).not.toHaveBeenCalled();
  });
});

describe("RichTextEditor — maxLength", () => {
  it("refuses a visual edit that would make the HTML longer than maxLength, passes a shorter one; the textarea has the same limit", () => {
    const { onChange, rerender } = renderEditor({ value: "<p>Hello</p>", maxLength: 15 });
    typeAtEnd(control(), " world");
    expect(onChange).not.toHaveBeenCalled();
    expect(control().textContent).toBe("Hello");

    typeAtEnd(control(), "!");
    expect(onChange).toHaveBeenLastCalledWith("<p>Hello!</p>");

    /* A value from outside is never refused. */
    rerender(<RichTextEditor id={ID} value="<p>A text much longer than fifteen</p>" onChange={onChange} maxLength={15} />);
    expect(control()).toHaveTextContent("A text much longer than fifteen");

    fireEvent.click(tool("RichText:SourceView"));
    expect(control()).toHaveAttribute("maxlength", "15");
  });
});

describe("RichTextEditor — read-only and disabled", () => {
  it("read-only: not editable and aria-readonly, every formatting button disabled; the source view opens, read-only", () => {
    renderEditor({ readOnly: true });
    expect(control()).toHaveAttribute("contenteditable", "false");
    expect(control()).toHaveAttribute("aria-readonly", "true");
    expect(editorOf(control()).isEditable).toBe(false);
    for (const label of FORMATTING_LABELS) expect(tool(label)).toBeDisabled();
    expect(tool("RichText:SourceView")).toBeEnabled();
    fireEvent.click(tool("RichText:SourceView"));
    expect(control().tagName).toBe("TEXTAREA");
    expect(control()).toHaveAttribute("readonly");
    expect(control()).toHaveValue("<p>Hello world</p>");
  });

  it("a read-only editor that becomes editable (record loaded with Update rights) accepts edits", () => {
    const { onChange, rerender } = renderEditor({ readOnly: true });
    rerender(<RichTextEditor id={ID} value="<p>Hello world</p>" onChange={onChange} readOnly={false} />);
    expect(control()).toHaveAttribute("contenteditable", "true");
    expect(tool("RichText:Bold")).toBeEnabled();
  });

  it("disabled: every button, the source view included, is disabled", () => {
    renderEditor({ disabled: true });
    for (const label of TOOLBAR_LABELS) expect(tool(label)).toBeDisabled();
    expect(control()).toHaveAttribute("contenteditable", "false");
  });
});

describe("RichTextEditor — onBlur", () => {
  it("fires when focus leaves the whole control, not when it moves to the control's own toolbar", () => {
    const onBlur = vi.fn();
    renderEditor({ onBlur });
    const outside = document.createElement("button");
    document.body.append(outside);
    try {
      fireEvent.focusOut(control(), { relatedTarget: tool("RichText:Bold") });
      expect(onBlur).not.toHaveBeenCalled();
      fireEvent.focusOut(control(), { relatedTarget: outside });
      expect(onBlur).toHaveBeenCalledTimes(1);
      fireEvent.focusOut(tool("RichText:Bold"), { relatedTarget: null });
      expect(onBlur).toHaveBeenCalledTimes(2);
    } finally {
      outside.remove();
    }
  });
});

describe("RichTextEditor — link popover", () => {
  const urlInput = () => screen.findByLabelText("RichText:LinkUrl");

  it("sets a link on the selection, shows the current address when reopened, and removes it", async () => {
    const onChange = vi.fn();
    render(<Controlled initial="<p>Track here</p>" onChange={onChange} />);
    selectRange(control(), ...rangeOf(control(), "Track"));
    fireEvent.click(tool("RichText:Link"));
    const input = await urlInput();
    expect(input).toHaveValue("");
    expect(screen.queryByRole("button", { name: "RichText:RemoveLink" })).toBeNull();
    fireEvent.change(input, { target: { value: " {{ order.track_url }} " } });
    fireEvent.click(screen.getByRole("button", { name: "RichText:ApplyLink" }));
    expect(lastEmitted(onChange)).toBe('<p><a href="{{ order.track_url }}">Track</a> here</p>');
    await waitFor(() => expect(screen.queryByLabelText("RichText:LinkUrl")).toBeNull());

    selectRange(control(), positionIn(control(), "Track"));
    expect(tool("RichText:Link")).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(tool("RichText:Link"));
    expect(await urlInput()).toHaveValue("{{ order.track_url }}");
    fireEvent.click(screen.getByRole("button", { name: "RichText:RemoveLink" }));
    expect(lastEmitted(onChange)).toBe("<p>Track here</p>");
  });

  it("with an empty selection, Enter in the address field inserts the address as linked text", async () => {
    const onChange = vi.fn();
    render(<Controlled initial="<p>See</p>" onChange={onChange} />);
    act(() => {
      editorOf(control()).commands.focus("end");
    });
    fireEvent.click(tool("RichText:Link"));
    const input = await urlInput();
    fireEvent.change(input, { target: { value: "https://example.com/track" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(lastEmitted(onChange)).toBe('<p>See<a href="https://example.com/track">https://example.com/track</a></p>');
  });

  it("Critical: a javascript: address never becomes a link target, on a selection or inserted", async () => {
    const onChange = vi.fn();
    render(<Controlled initial="<p>Track here</p>" onChange={onChange} />);
    selectRange(control(), ...rangeOf(control(), "Track"));
    fireEvent.click(tool("RichText:Link"));
    fireEvent.change(await urlInput(), { target: { value: "javascript:window.__pwned=1" } });
    fireEvent.click(screen.getByRole("button", { name: "RichText:ApplyLink" }));

    act(() => {
      editorOf(control()).commands.focus("end");
    });
    fireEvent.click(tool("RichText:Link"));
    const input = await urlInput();
    fireEvent.change(input, { target: { value: "javascript:window.__pwned=2" } });
    fireEvent.keyDown(input, { key: "Enter" });

    for (const [html] of onChange.mock.calls as [string][]) expect(html).not.toMatch(/href="\s*javascript:/i);
    for (const anchor of Array.from(document.querySelectorAll("a"))) expect(anchor.getAttribute("href") ?? "").not.toMatch(/^\s*javascript:/i);
  });
});

describe("RichTextEditor — untrusted HTML (Critical)", () => {
  const HOSTILE =
    '<p>Hi</p><script>window.__pwned = 1</script><img src="x" onerror="window.__pwned = 2"><p onclick="window.__pwned = 3"><a href="javascript:window.__pwned = 4">click</a></p>';

  const expectNothingLive = () => {
    expect(document.querySelectorAll("script")).toHaveLength(0);
    expect(document.querySelectorAll("img")).toHaveLength(0);
    expect(document.querySelectorAll("[onerror], [onclick]")).toHaveLength(0);
    for (const anchor of Array.from(document.querySelectorAll("a"))) expect(anchor.getAttribute("href") ?? "").not.toMatch(/^\s*javascript:/i);
    expect((window as unknown as { __pwned?: unknown }).__pwned).toBeUndefined();
  };

  it("script, event handlers, images and javascript: links in a value never reach the live document in the visual view", () => {
    renderEditor({ value: HOSTILE, preserveUnsupportedMarkup: false });
    expect(control()).toHaveTextContent("Hi");
    expectNothingLive();
  });

  it("the same value opens as text in the source view (with the warning), and switching to the visual view still renders none of it", () => {
    renderEditor({ value: HOSTILE });
    expect(warning()).not.toBeNull();
    expect(control()).toHaveValue(HOSTILE);
    expectNothingLive();
    fireEvent.click(tool("RichText:SourceView"));
    expect(isVisual()).toBe(true);
    expectNothingLive();
  });
});

describe("RichTextEditor — regression: React unmounts and reconnects the editor (StrictMode, Suspense)", () => {
  /* Found 2026-10-19: with immediatelyRender, useEditor makes its instance during render and destroys it 1 ms later
   * unless the component's effects have mounted by then; it makes a new one when they do. When React commits later
   * than that (a Suspense boundary revealing a lazy editor is throttled; StrictMode's simulated unmount / reconnect),
   * the effects of the committed render called getHTML() / setContent() on the destroyed instance and threw "Cannot
   * read properties of null (reading 'cached')", which unmounted the whole dialog. Each test mounts the editor through
   * a FRESH React.lazy inside Suspense and StrictMode, as HtmlBodyField does on the first open of an e-mail template,
   * and checks that the content, the value-from-outside path and the lossy check still work. Verified to fail without
   * the isDestroyed guards in rich-text-editor.tsx. */
  const freshLazyEditor = () => lazy(async () => ({ default: RichTextEditor }));
  const crashes = (spy: { mock: { calls: unknown[][] } }) =>
    spy.mock.calls.filter((call) => call.some((arg) => /cached|error occurred/i.test(String(arg instanceof Error ? arg.message : arg))));

  it("a lazily mounted editor shows its initial value, takes a changed value from outside and opens a lossy one in the source view, without an error", async () => {
    const consoleError = vi.spyOn(console, "error");
    try {
      const LazyEditor = freshLazyEditor();
      const onChange = vi.fn();
      const tree = (value: string) => (
        <StrictMode>
          <Suspense fallback={<p>loading</p>}>
            <LazyEditor id={ID} value={value} onChange={onChange} />
          </Suspense>
        </StrictMode>
      );
      const { rerender } = render(tree("<p>First record</p>"));
      expect(screen.getByText("loading")).toBeInTheDocument();
      await waitFor(() => expect(control()).toHaveTextContent("First record"));
      expect(isVisual()).toBe(true);
      expect(warning()).toBeNull();

      rerender(tree("<p>Second record</p>"));
      await waitFor(() => expect(control()).toHaveTextContent("Second record"));

      rerender(tree(TABLE));
      await waitFor(() => expect(control().tagName).toBe("TEXTAREA"));
      expect(warning()).not.toBeNull();
      expect(onChange).not.toHaveBeenCalled();
      expect(crashes(consoleError)).toEqual([]);
    } finally {
      consoleError.mockRestore();
    }
  });

  it("a lazily mounted editor with a lossy initial value opens in the source view with the warning; visual edits still emit", async () => {
    const consoleError = vi.spyOn(console, "error");
    try {
      const LazyEditor = freshLazyEditor();
      const onChange = vi.fn();
      function Parent() {
        const [value, setValue] = useState(TABLE);
        return (
          <LazyEditor
            id={ID}
            value={value}
            onChange={(next) => {
              onChange(next);
              setValue(next);
            }}
          />
        );
      }
      render(
        <StrictMode>
          <Suspense fallback={<p>loading</p>}>
            <Parent />
          </Suspense>
        </StrictMode>,
      );
      await waitFor(() => expect(warning()).not.toBeNull());
      expect(control().tagName).toBe("TEXTAREA");
      expect(control()).toHaveValue(TABLE);
      fireEvent.click(tool("RichText:SourceView"));
      expect(isVisual()).toBe(true);
      typeAtEnd(control(), "!");
      expect(onChange).toHaveBeenCalledTimes(1);
      expect(warning()).toBeNull();
      expect(crashes(consoleError)).toEqual([]);
    } finally {
      consoleError.mockRestore();
    }
  });

  it("a lazily mounted read-only editor becomes editable when the prop changes", async () => {
    const LazyEditor = freshLazyEditor();
    const tree = (readOnly: boolean) => (
      <StrictMode>
        <Suspense fallback={<p>loading</p>}>
          <LazyEditor id={ID} value="<p>Hello</p>" onChange={vi.fn()} readOnly={readOnly} />
        </Suspense>
      </StrictMode>
    );
    const { rerender } = render(tree(true));
    await waitFor(() => expect(control()).toHaveAttribute("contenteditable", "false"));
    rerender(tree(false));
    expect(control()).toHaveAttribute("contenteditable", "true");
    expect(tool("RichText:Bold")).toBeEnabled();
  });
});
