import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { DatePicker } from "@/components/ui/date-picker";
import { dateInputPattern, formatDateInput, maskDateInput, parseDateInput, toIsoDate } from "@/lib/format";

/* The date picker (UI-FMT-002/003/004, component-map GAP-03 option (a)).
 * Risk class: Medium — a timezone shift here moves every qualification and planning date by a day. */

const state = vi.hoisted(() => ({ culture: "nl" }));

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: state.culture, isLoading: false }),
}));

describe("date input helpers", () => {
  it("derives the typing pattern from the formatting locale", () => {
    expect(dateInputPattern("nl-NL")).toBe("DD-MM-YYYY");
    expect(dateInputPattern("en-GB")).toBe("DD/MM/YYYY");
    expect(dateInputPattern("de-DE")).toBe("DD.MM.YYYY");
  });

  it("masks digits into the pattern", () => {
    expect(maskDateInput("29032026", "nl-NL")).toBe("29-03-2026");
    expect(maskDateInput("2903", "en-GB")).toBe("29/03");
    expect(maskDateInput("29-", "nl-NL")).toBe("29-");
    expect(maskDateInput("29-03-2026x", "nl-NL")).toBe("29-03-2026");
  });

  it("parses a valid typed date to YYYY-MM-DD and rejects invalid or incomplete ones", () => {
    expect(parseDateInput("29-03-2026", "nl-NL")).toBe("2026-03-29");
    expect(parseDateInput("9-3-2026", "nl-NL")).toBe("2026-03-09");
    expect(parseDateInput("29/03/2026", "en-GB")).toBe("2026-03-29");
    expect(parseDateInput("31-02-2026", "nl-NL")).toBeNull();
    expect(parseDateInput("29-03-26", "nl-NL")).toBeNull();
    expect(parseDateInput("29-03", "nl-NL")).toBeNull();
    expect(parseDateInput("", "nl-NL")).toBeNull();
  });

  it("formats YYYY-MM-DD for the input per locale and never shifts by the timezone", () => {
    expect(formatDateInput("2026-03-29", "nl-NL")).toBe("29-03-2026");
    expect(formatDateInput("2026-03-29", "en-GB")).toBe("29/03/2026");
    expect(formatDateInput(null, "nl-NL")).toBe("");
    expect(formatDateInput("2026-03-29T10:00:00Z", "nl-NL")).toBe("");
    expect(toIsoDate(new Date(2026, 2, 29))).toBe("2026-03-29");
  });
});

describe("DatePicker", () => {
  beforeEach(() => {
    state.culture = "nl";
  });

  it("shows the value in the nl pattern and commits a typed date as YYYY-MM-DD on blur", () => {
    const onChange = vi.fn();
    const { rerender } = render(<DatePicker id="d" value="2026-03-29" onChange={onChange} />);
    const input = screen.getByRole("textbox");
    expect(input).toHaveValue("29-03-2026");
    expect(input).toHaveAttribute("placeholder", "DD-MM-YYYY");

    fireEvent.change(input, { target: { value: "15042026" } });
    expect(input).toHaveValue("15-04-2026");
    expect(onChange).not.toHaveBeenCalled();
    fireEvent.blur(input);
    expect(onChange).toHaveBeenCalledWith("2026-04-15");
    expect(input).not.toHaveAttribute("aria-invalid");

    rerender(<DatePicker id="d" value="2026-04-15" onChange={onChange} />);
    expect(input).toHaveValue("15-04-2026");
  });

  it("gives null and aria-invalid for an invalid or incomplete typed date", () => {
    const onChange = vi.fn();
    render(<DatePicker id="d" value="2026-03-29" onChange={onChange} />);
    const input = screen.getByRole("textbox");
    fireEvent.change(input, { target: { value: "31-02-2026" } });
    fireEvent.blur(input);
    expect(onChange).toHaveBeenCalledWith(null);
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveValue("31-02-2026");

    fireEvent.change(input, { target: { value: "" } });
    fireEvent.blur(input);
    expect(input).not.toHaveAttribute("aria-invalid");
  });

  it("uses the en-GB pattern for en", () => {
    state.culture = "en";
    const onChange = vi.fn();
    render(<DatePicker id="d" value="2026-03-29" onChange={onChange} />);
    const input = screen.getByRole("textbox");
    expect(input).toHaveValue("29/03/2026");
    expect(input).toHaveAttribute("placeholder", "DD/MM/YYYY");
    fireEvent.change(input, { target: { value: "01/04/2026" } });
    fireEvent.blur(input);
    expect(onChange).toHaveBeenCalledWith("2026-04-01");
  });

  it("writes the picked day without a timezone shift (29 March 2026 is the DST switch)", async () => {
    const onChange = vi.fn();
    render(<DatePicker id="d" value="2026-03-28" onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: "PickDate" }));
    const grid = await screen.findByRole("grid");
    expect(grid.querySelectorAll("thead th")[0]).toHaveTextContent(/^ma/i);
    fireEvent.click(screen.getByRole("button", { name: /29 maart 2026/i }));
    expect(onChange).toHaveBeenCalledWith("2026-03-29");
  });

  it("clears when optional and never shows the clear when required or disabled", () => {
    const onChange = vi.fn();
    const { rerender } = render(<DatePicker id="d" value="2026-03-29" onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: "Clear" }));
    expect(onChange).toHaveBeenCalledWith(null);

    rerender(<DatePicker id="d" value="2026-03-29" onChange={onChange} required />);
    expect(screen.queryByRole("button", { name: "Clear" })).toBeNull();
    rerender(<DatePicker id="d" value="2026-03-29" onChange={onChange} disabled />);
    expect(screen.queryByRole("button", { name: "Clear" })).toBeNull();
    expect(screen.getByRole("textbox")).toBeDisabled();
  });

  it("the calendar button is no Tab stop: keyboard entry goes from the date to the next field (design 61, legacy tab structure 9)", () => {
    render(
      <>
        <DatePicker id="d" value="2026-03-29" onChange={() => {}} required />
        <input aria-label="Next field" />
      </>,
    );
    const calendar = screen.getByRole("button", { name: "PickDate" });
    expect(calendar).toHaveAttribute("tabindex", "-1");
    /* What the browser's Tab order holds after the date: only elements without a negative tabindex. */
    const tabbable = Array.from(document.querySelectorAll<HTMLElement>("input, button")).filter((el) => el.tabIndex >= 0 && !el.hasAttribute("disabled"));
    expect(tabbable.map((el) => el.getAttribute("aria-label") ?? el.id)).toEqual(["d", "Next field"]);
    /* The mouse still opens it. */
    fireEvent.click(calendar);
    expect(calendar).toHaveAttribute("aria-expanded", "true");
  });

  it("forwards the FormField's aria attributes to the input", () => {
    render(<DatePicker id="d" value={null} onChange={() => {}} aria-invalid aria-describedby="d-error" aria-required />);
    const input = screen.getByRole("textbox");
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveAttribute("aria-describedby", "d-error");
    expect(input).toHaveAttribute("aria-required", "true");
  });
});
