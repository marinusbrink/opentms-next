import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* Editing a list cell in a popup (design 74 §6): the pencil opens a popover over the grid; `close` closes it; a picker's "Add new"
 * editor inside it keeps it open (UI-LOOK-004). InlineDateCell saves on the date. Risk class: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockDatePicker }));

import { stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";
import { InlineDateCell, PopupEditCell } from "@/components/ui/grid-edit-cells";

interface Row {
  id: string;
}
const ROW: Row = { id: "r1" };

const pencil = () => screen.queryByRole("button", { name: "Edit" });
const popup = () => document.querySelector("[data-slot=popup-edit-cell]");
const openPopup = async () => {
  await act(async () => void fireEvent.click(pencil()!));
  await waitFor(() => expect(popup()).not.toBeNull());
};
/** A press outside the popover, as Base UI listens for it. */
const pressOutside = async () => {
  await act(async () => {
    fireEvent.pointerDown(document.body);
    fireEvent.mouseDown(document.body);
    fireEvent.pointerUp(document.body);
    fireEvent.mouseUp(document.body);
    fireEvent.click(document.body);
  });
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 20));
  });
};

beforeAll(stubGlobals);

describe("PopupEditCell", () => {
  const cell = (canEdit: (row: Row) => boolean = () => true, data: Row | undefined = ROW, onRender = vi.fn()) => (
    <div>
      <PopupEditCell<Row>
        text="Value"
        data={data}
        canEdit={canEdit}
        renderPopup={(row, close) => {
          onRender(row);
          return (
            <button type="button" onClick={close}>
              done
            </button>
          );
        }}
      />
    </div>
  );

  it("shows the value and the pencil", () => {
    render(cell());
    expect(screen.getByText("Value")).toBeInTheDocument();
    expect(pencil()).not.toBeNull();
    expect(popup()).toBeNull();
  });

  it("only shows the value without the right, per row", () => {
    const canEdit = vi.fn(() => false);
    render(cell(canEdit));
    expect(screen.getByText("Value")).toBeInTheDocument();
    expect(pencil()).toBeNull();
    expect(canEdit).toHaveBeenCalledWith(ROW);
  });

  it("only shows the value without a row (a group row)", () => {
    const canEdit = vi.fn(() => true);
    render(cell(canEdit, null as unknown as undefined));
    expect(pencil()).toBeNull();
    expect(canEdit).not.toHaveBeenCalled();
  });

  it("renders the popup only when opened, with the row", async () => {
    const onRender = vi.fn();
    render(cell(undefined, undefined, onRender));
    expect(onRender).not.toHaveBeenCalled();
    await openPopup();
    expect(onRender).toHaveBeenCalledWith(ROW);
  });

  it("`close` closes it", async () => {
    render(cell());
    await openPopup();
    fireEvent.click(screen.getByRole("button", { name: "done" }));
    await waitFor(() => expect(popup()).toBeNull());
  });

  it("the pencil's click does not reach the grid: a row click would select, a double click would open", async () => {
    const onClick = vi.fn();
    const onDoubleClick = vi.fn();
    render(
      <div onClick={onClick} onDoubleClick={onDoubleClick}>
        {cell()}
      </div>,
    );
    fireEvent.doubleClick(pencil()!);
    fireEvent.click(pencil()!);
    expect(onClick).not.toHaveBeenCalled();
    expect(onDoubleClick).not.toHaveBeenCalled();
  });

  it("a press outside closes it", async () => {
    render(cell());
    await openPopup();
    await pressOutside();
    await waitFor(() => expect(popup()).toBeNull());
  });

  describe("with a create-in-place editor over it (UI-LOOK-004)", () => {
    const editorBody = () => {
      const body = document.createElement("div");
      body.setAttribute("data-slot", "entity-dialog-body");
      document.body.appendChild(body);
      return body;
    };

    it("a press outside the popover, in the editor, keeps it open", async () => {
      render(cell());
      await openPopup();
      const body = editorBody();
      await pressOutside();
      expect(popup()).not.toBeNull();
      body.remove();
    });

    it("once the editor is gone, a press outside closes it again", async () => {
      render(cell());
      await openPopup();
      const body = editorBody();
      await pressOutside();
      body.remove();
      await pressOutside();
      await waitFor(() => expect(popup()).toBeNull());
    });

    it("`close` still closes it: the save after the editor", async () => {
      render(cell());
      await openPopup();
      const body = editorBody();
      fireEvent.click(screen.getByRole("button", { name: "done" }));
      await waitFor(() => expect(popup()).toBeNull());
      body.remove();
    });

    it("a cell opened inside an editor counts only editors opened after it", async () => {
      const outer = editorBody();
      render(cell());
      await openPopup();
      await pressOutside();
      await waitFor(() => expect(popup()).toBeNull());
      outer.remove();
    });
  });
});

describe("InlineDateCell", () => {
  const onCommit = vi.fn(async () => undefined);
  beforeEach(() => {
    onCommit.mockReset().mockImplementation(async () => undefined);
  });

  const show = (props: Partial<Parameters<typeof InlineDateCell<Row>>[0]> = {}) => render(<InlineDateCell<Row> value="2026-11-10" data={ROW} label="Date" canEdit={() => true} onCommit={onCommit} {...props} />);
  const picker = () => popup()!.querySelector("input") as HTMLInputElement;
  const pick = async (value: string) => {
    await act(async () => void fireEvent.change(picker(), { target: { value } }));
  };

  it("shows the formatted date; no pencil without the right", () => {
    show({ canEdit: () => false });
    expect(screen.getByText(/2026|10/)).toBeInTheDocument();
    expect(pencil()).toBeNull();
  });

  it("picking a day saves it and closes", async () => {
    show();
    await openPopup();
    expect(picker().value).toBe("2026-11-10");
    await pick("2026-11-12");
    expect(onCommit).toHaveBeenCalledWith(ROW, "2026-11-12");
    await waitFor(() => expect(popup()).toBeNull());
  });

  it("the day it already had saves nothing", async () => {
    /* Emptying a required date saves nothing and keeps the draft empty; the old day back is then no change. */
    show({ required: true });
    await openPopup();
    await pick("");
    await pick("2026-11-10");
    expect(onCommit).not.toHaveBeenCalled();
    expect(popup()).not.toBeNull();
  });

  it("a required date cannot be emptied: no save, the popup stays", async () => {
    show({ required: true });
    await openPopup();
    await pick("");
    expect(onCommit).not.toHaveBeenCalled();
    expect(popup()).not.toBeNull();
  });

  it("an optional date can: null goes to the save", async () => {
    show();
    await openPopup();
    await pick("");
    expect(onCommit).toHaveBeenCalledWith(ROW, null);
  });

  it("a failure keeps the popup for another try, with the old date back", async () => {
    onCommit.mockImplementationOnce(async () => {
      throw new Error("shown by the caller");
    });
    show();
    await openPopup();
    await pick("2026-11-12");
    expect(onCommit).toHaveBeenCalledTimes(1);
    expect(popup()).not.toBeNull();
    expect(picker().value).toBe("2026-11-10");
    await pick("2026-11-13");
    expect(onCommit).toHaveBeenLastCalledWith(ROW, "2026-11-13");
    await waitFor(() => expect(popup()).toBeNull());
  });

  it("the picker is disabled while saving", async () => {
    let release = () => {};
    onCommit.mockImplementationOnce(() => new Promise<undefined>((resolve) => (release = () => resolve(undefined))));
    show();
    await openPopup();
    await pick("2026-11-12");
    expect(picker()).toBeDisabled();
    await act(async () => release());
    await waitFor(() => expect(popup()).toBeNull());
  });
});
