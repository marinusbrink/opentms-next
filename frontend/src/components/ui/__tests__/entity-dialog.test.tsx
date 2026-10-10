import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Save } from "lucide-react";
import { describe, expect, it, vi } from "vitest";

import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";
import { Fingerprint } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));

// The command bar reads the toolbar feature flag; render the Office-365 strip.
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { features: { values: { "UI.CommonToolbar": "true" } } } }),
}));

class MockResizeObserver {
  private readonly cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) {
    this.cb = cb;
  }
  observe(el: Element) {
    this.cb([{ contentRect: { width: 9999 } as DOMRectReadOnly, target: el } as ResizeObserverEntry], this as unknown as ResizeObserver);
  }
  disconnect() {}
  unobserve() {}
}
vi.stubGlobal("ResizeObserver", MockResizeObserver);

function renderEditor(overrides: Partial<React.ComponentProps<typeof EntityDialog>> = {}) {
  const onOpenChange = vi.fn();
  const save = vi.fn();
  render(
    <EntityDialog
      open
      onOpenChange={onOpenChange}
      variant="editor"
      size="sm"
      title="Vehicle DAF 12"
      commands={[{ id: "save", labelKey: "Save", icon: Save, isPrimary: true, onClick: save }]}
      {...overrides}
    >
      <Input id="name" aria-label="Name" />
    </EntityDialog>,
  );
  return { onOpenChange, save };
}

function headerCloseButton() {
  const header = document.body.querySelector('[data-slot="entity-dialog-header"]') as HTMLElement;
  return within(header).getByRole("button", { name: "Close" });
}

/** The Close command the dialog injects into the command strip (text, not aria-label). */
function closeCommand() {
  return Array.from(document.body.querySelectorAll("button")).find(
    (b) => b.textContent?.trim() === "Close" && !b.getAttribute("aria-label"),
  )!;
}

describe("EntityDialog editor (UI-DLG-004/005/008/010, UI-ACT-002, UI-KEY-003)", () => {
  it("has a brand header with the title and a Close X, and the commands under it with Close after Save", () => {
    const { container } = { container: document.body };
    renderEditor();
    const header = container.querySelector('[data-slot="entity-dialog-header"]') as HTMLElement;
    expect(header.className).toContain("bg-brand");
    expect(header).toHaveTextContent("Vehicle DAF 12");
    expect(within(header).getByRole("button", { name: "Close" })).toBeInTheDocument();

    const labels = Array.from(container.querySelectorAll("button"))
      .map((b) => b.textContent?.trim())
      .filter((text) => text === "Save" || text === "Close");
    expect(labels.indexOf("Save")).toBeLessThan(labels.lastIndexOf("Close"));
    expect(container.querySelector('[data-slot="entity-dialog-footer"]')).toBeNull();
  });

  it("focuses the first editable field on open", async () => {
    renderEditor();
    await waitFor(() => expect(document.activeElement).toBe(screen.getByLabelText("Name")));
  });

  it("closes immediately when the form is clean (via the Close command in the strip)", () => {
    const { onOpenChange } = renderEditor({ isDirty: false });
    fireEvent.click(closeCommand());
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("asks before discarding a dirty form, and only closes after Discard (via the X)", async () => {
    const { onOpenChange } = renderEditor({ isDirty: true });
    fireEvent.click(headerCloseButton());
    expect(onOpenChange).not.toHaveBeenCalled();
    expect(await screen.findByText("UnsavedChanges")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Discard" }));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("ignores close requests and disables the commands while busy", () => {
    const { onOpenChange, save } = renderEditor({ isBusy: true });
    fireEvent.click(headerCloseButton());
    expect(onOpenChange).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("Save").closest("button")!);
    expect(save).not.toHaveBeenCalled();
  });
});

describe("EntityDialog action (UI-DLG-005)", () => {
  it("has a plain header and a right-aligned footer, and closes without a guard", () => {
    const onOpenChange = vi.fn();
    render(
      <EntityDialog
        open
        onOpenChange={onOpenChange}
        variant="action"
        size="sm"
        title="Merge relations"
        footer={
          <>
            <Button variant="outline">Cancel</Button>
            <Button variant="destructive">Merge</Button>
          </>
        }
      >
        <p>Body</p>
      </EntityDialog>,
    );
    const header = document.body.querySelector('[data-slot="entity-dialog-header"]')!;
    expect(header.className).not.toContain("bg-brand");
    const footer = document.body.querySelector('[data-slot="entity-dialog-footer"]')!;
    expect(footer.className).toContain("justify-end");
    expect(footer.lastElementChild).toHaveTextContent("Merge");

    fireEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});

describe("EntityDialog tabs (UI-FORM-007/008)", () => {
  const tabs: EntityDialogTab[] = [
    { value: "general", label: "General", content: <Input id="g" aria-label="General field" /> },
    { value: "capabilities", label: "Capabilities", content: <p>Capabilities body</p>, hasError: true },
    { value: "identification", label: "RecordIdentification", icon: Fingerprint, content: <p>Identification body</p> },
  ];

  function renderTabbed(props: Partial<React.ComponentProps<typeof EntityDialog>> = {}) {
    const onActiveTabChange = vi.fn();
    render(
      <EntityDialog
        open
        onOpenChange={vi.fn()}
        variant="editor"
        size="md"
        title="Vehicle DAF 12"
        commands={[{ id: "save", labelKey: "Save", icon: Save, isPrimary: true, onClick: vi.fn() }]}
        tabs={tabs}
        onActiveTabChange={onActiveTabChange}
        {...props}
      />,
    );
    return { onActiveTabChange };
  }

  it("renders the strip in the ribbon row with General first and the icon tab last, named by its label", () => {
    renderTabbed();
    const strip = screen.getByRole("tablist");
    const tabButtons = screen.getAllByRole("tab");
    expect(tabButtons[0]).toHaveTextContent("General");
    expect(tabButtons.at(-1)).toHaveAttribute("aria-label", "RecordIdentification");
    expect(strip.closest('[data-slot="entity-dialog-ribbon"]')).not.toBeNull();
  });

  it("shows the first tab's content on open and switches panels on click", () => {
    const { onActiveTabChange } = renderTabbed();
    expect(screen.getByLabelText("General field")).toBeVisible();
    expect(screen.getByText("Identification body")).not.toBeVisible();

    fireEvent.click(screen.getByRole("tab", { name: "RecordIdentification" }));
    expect(onActiveTabChange).toHaveBeenCalledWith("identification");
    expect(screen.getByText("Identification body")).toBeVisible();
  });

  it("follows a controlled activeTab and marks a tab with errors", () => {
    renderTabbed({ activeTab: "capabilities" });
    expect(screen.getByText("Capabilities body")).toBeVisible();
    const errorTab = screen.getByRole("tab", { name: /Capabilities/ });
    expect(errorTab.className).toContain("text-destructive");
    expect(errorTab.querySelector("svg")).not.toBeNull();
  });
});
