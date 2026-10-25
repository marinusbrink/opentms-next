import { fireEvent, render, screen } from "@testing-library/react";
import { StrictMode } from "react";
import { beforeAll, describe, expect, it, vi } from "vitest";

/* HtmlBodyField (design 54, the e-mail template's HTML body): the RichTextEditor, lazily loaded (Tiptap is its own
 * chunk), in a FormField. Covered: the skeleton while the chunk loads, then the editor; the label, required, error and
 * maxLength wiring; the guard against silent layout loss (preserveUnsupportedMarkup) is on; read-only reaches the
 * editor; and Scriban typed in the visual view reaches onChange un-escaped inside {{ }} only (transformOutput =
 * unescapeScribanRegions). Risk class: Medium (UI component). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

import { HtmlBodyField, type HtmlBodyFieldProps } from "@/apps/master-data/components/HtmlBodyField";
import { installProseMirrorDomStubs, typeAtEnd } from "@/components/ui/__tests__/rich-text-test-helpers";

beforeAll(installProseMirrorDomStubs);

const ID = "htmlBody";

async function renderField(props: Partial<HtmlBodyFieldProps> = {}) {
  const onChange = vi.fn();
  const onBlur = vi.fn();
  render(<HtmlBodyField id={ID} label="Body" value="<p>Hello</p>" readOnly={false} onChange={onChange} onBlur={onBlur} {...props} />);
  return { onChange, onBlur };
}

const editor = () => screen.findByRole("textbox", { name: /^Body/ });

describe("HtmlBodyField", () => {
  /* First in the file on purpose: React.lazy resolves the editor chunk once per module, so only the first render
   * shows the fallback (vi.resetModules would load a second copy of React/Tiptap). It is also the regression test of
   * 2026-10-19: on this first, suspended mount (in StrictMode, as the app runs in development) the editor's effects
   * touched the instance useEditor had already destroyed ("Cannot read properties of null (reading 'cached')") and the
   * field disappeared; verified to fail without the isDestroyed guards in rich-text-editor.tsx. */
  it("shows a skeleton under the label while the editor chunk loads, then the editor; a changed and a lossy value still work (StrictMode)", async () => {
    const consoleError = vi.spyOn(console, "error");
    try {
      const onChange = vi.fn();
      const field = (value: string) => (
        <StrictMode>
          <HtmlBodyField id={ID} label="Body" value={value} readOnly={false} required onChange={onChange} />
        </StrictMode>
      );
      const { rerender } = render(field("<p>Hello</p>"));
      expect(document.querySelector("[data-slot=skeleton]")).not.toBeNull();
      expect(screen.getByText("Body")).toBeInTheDocument();
      expect(screen.queryByRole("toolbar")).toBeNull();

      const box = await editor();
      expect(box).toHaveAttribute("contenteditable", "true");
      expect(box).toHaveTextContent("Hello");
      expect(document.querySelector("[data-slot=skeleton]")).toBeNull();
      expect(screen.getByRole("toolbar", { name: "RichText:Toolbar" })).toBeInTheDocument();

      rerender(field("<p>Second {{ name }}</p>"));
      expect(await editor()).toHaveTextContent("Second {{ name }}");
      rerender(field('<div class="x">Third</div>'));
      expect(await screen.findByRole("status")).toHaveTextContent("RichText:UnsupportedMarkupNotice");
      expect(document.getElementById(ID)).toHaveValue('<div class="x">Third</div>');
      expect(onChange).not.toHaveBeenCalled();
      expect(consoleError.mock.calls.filter((call) => call.some((arg) => /cached|error occurred/i.test(String(arg instanceof Error ? arg.message : arg))))).toEqual([]);
    } finally {
      consoleError.mockRestore();
    }
  });

  it("is named by its label and carries required, the error and its description", async () => {
    await renderField({ required: true, error: "Platform:Required" });
    const box = await editor();
    expect(box).toHaveAttribute("id", ID);
    expect(box).toHaveAttribute("aria-labelledby", `${ID}-label`);
    expect(box).toHaveAttribute("aria-required", "true");
    expect(box).toHaveAttribute("aria-invalid", "true");
    expect(box).toHaveAttribute("aria-describedby", `${ID}-error`);
    expect(document.getElementById(`${ID}-error`)).toHaveTextContent("Platform:Required");
    expect(document.getElementById(`${ID}-label`)).toHaveTextContent("Body *");
  });

  it("a visually typed {{ if a > b }} reaches onChange with a raw >; the text outside {{ }} stays escaped", async () => {
    const { onChange } = await renderField({ value: "<p>Dear {{ customer.name }}</p>" });
    const box = await editor();
    typeAtEnd(box, ", x > y {{ if a > b && c }}overdue{{ end }}");
    expect(onChange).toHaveBeenLastCalledWith("<p>Dear {{ customer.name }}, x &gt; y {{ if a > b && c }}overdue{{ end }}</p>");
  });

  it("opens a body the visual view cannot keep in the source view, with the warning", async () => {
    const table = "<table><tbody>{{ for line in lines }}<tr><td>{{ line.text }}</td></tr>{{ end }}</tbody></table>";
    const { onChange } = await renderField({ value: table });
    expect(await screen.findByRole("status")).toHaveTextContent("RichText:UnsupportedMarkupNotice");
    const source = document.getElementById(ID)!;
    expect(source.tagName).toBe("TEXTAREA");
    expect(source).toHaveValue(table);
    expect(source).toHaveAttribute("aria-labelledby", `${ID}-label`);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("passes read-only and maxLength to the editor", async () => {
    await renderField({ readOnly: true, maxLength: 200000 });
    const box = await editor();
    expect(box).toHaveAttribute("contenteditable", "false");
    expect(screen.getByRole("button", { name: "RichText:Bold" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "RichText:SourceView" }));
    expect(document.getElementById(ID)).toHaveAttribute("maxlength", "200000");
    expect(document.getElementById(ID)).toHaveAttribute("readonly");
  });

  it("calls onBlur when focus leaves the field", async () => {
    const { onBlur } = await renderField();
    const box = await editor();
    fireEvent.focusOut(box, { relatedTarget: document.body });
    expect(onBlur).toHaveBeenCalledTimes(1);
  });
});

