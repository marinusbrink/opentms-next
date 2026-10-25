import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";

import { TaskList, Wizard, type WizardStep, type WizardTask } from "@/components/ui/wizard";

/* The Wizard (UI-WIZ-001 to -005, GAP-04 built in design 61): one action dialog, a numbered linear step header, the
 * fixed footer with Cancel left and Previous / Next or the verb right, Next disabled on an invalid step, the TaskList
 * with per-item status and a progress bar once the run starts, and a single Close that waits for the run to finish.
 * Risk class: Medium (UI flow shared by every batch operation of the Transport run). */

/* The keys themselves, except the two texts with placeholders, which the wizard fills in. */
const TEXTS: Record<string, string> = { "Wizard:StepOf": "Step {0} of {1}", "Wizard:Progress": "{0} of {1} done" };
vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => TEXTS[key] ?? key, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { features: { values: {} } } }),
}));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}
vi.stubGlobal("ResizeObserver", MockResizeObserver);

const steps = (overrides: Partial<Record<string, Partial<WizardStep>>> = {}): WizardStep[] => [
  { id: "intro", title: "Intro", content: <p>Explains what happens</p>, ...overrides.intro },
  { id: "select", title: "Select", content: <input aria-label="Reference" />, ...overrides.select },
  { id: "confirm", title: "Confirm", content: <p>Ready</p>, ...overrides.confirm },
];

const dialog = () => screen.getByRole("dialog");
const footer = () => document.body.querySelector('[data-slot="entity-dialog-footer"]') as HTMLElement;
const button = (name: string) => within(footer()).getByRole("button", { name });
const queryButton = (name: string) => within(footer()).queryByRole("button", { name });
const stepItems = () => Array.from(dialog().querySelectorAll<HTMLElement>('ol[aria-label="Wizard:Steps"] li'));

function renderWizard(props: Partial<React.ComponentProps<typeof Wizard>> = {}) {
  const onOpenChange = vi.fn();
  const onFinish = vi.fn();
  render(<Wizard open onOpenChange={onOpenChange} title="Generate invoices" steps={steps()} finishLabel="Generate" onFinish={onFinish} {...props} />);
  return { onOpenChange, onFinish };
}

describe("Wizard – dialog and footer (UI-WIZ-001, UI-WIZ-002)", () => {
  it("is one full action dialog with the title, Cancel at the left and Next at the right; no Previous on the first step", () => {
    renderWizard();
    const content = document.body.querySelector('[data-slot="dialog-content"]')!;
    expect(content).toHaveAttribute("data-variant", "action");
    expect(content).toHaveAttribute("data-size", "full");
    expect(dialog()).toHaveTextContent("Generate invoices");

    const buttons = within(footer()).getAllByRole("button").map((b) => b.textContent);
    expect(buttons).toEqual(["Cancel", "Wizard:Next"]);
    expect(button("Cancel").className).toContain("mr-auto");
    expect(footer().lastElementChild).toHaveTextContent("Wizard:Next");
  });

  it("Cancel closes; the header X closes a wizard on its first step without asking", () => {
    const { onOpenChange } = renderWizard();
    fireEvent.click(button("Cancel"));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("past the first step the wizard has input: the X asks before discarding (UI-WIZ-001, UI-DLG-008)", async () => {
    const { onOpenChange } = renderWizard();
    fireEvent.click(button("Wizard:Next"));
    const header = document.body.querySelector('[data-slot="entity-dialog-header"]') as HTMLElement;
    fireEvent.click(within(header).getByRole("button", { name: "Close" }));
    expect(onOpenChange).not.toHaveBeenCalled();
    expect(await screen.findByText("UnsavedChanges")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Discard" }));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("shows Previous from the second step and the verb instead of Next on the last step", () => {
    renderWizard();
    fireEvent.click(button("Wizard:Next"));
    expect(button("Wizard:Previous")).toBeInTheDocument();
    expect(screen.getByLabelText("Reference")).toBeInTheDocument();
    fireEvent.click(button("Wizard:Next"));
    expect(queryButton("Wizard:Next")).toBeNull();
    expect(footer().lastElementChild).toHaveTextContent("Generate");
    fireEvent.click(button("Wizard:Previous"));
    expect(screen.getByLabelText("Reference")).toBeInTheDocument();
  });

  it("disables Next while the current step is invalid and enables it when the step becomes valid", () => {
    function Harness() {
      const [valid, setValid] = useState(false);
      return (
        <Wizard
          open
          onOpenChange={() => undefined}
          title="Archive trips"
          steps={steps({ intro: { isValid: valid, content: <button type="button" onClick={() => setValid(true)}>make valid</button> } })}
          finishLabel="Archive"
          onFinish={() => undefined}
        />
      );
    }
    render(<Harness />);
    expect(button("Wizard:Next")).toBeDisabled();
    fireEvent.click(screen.getByText("make valid"));
    expect(button("Wizard:Next")).toBeEnabled();
  });

  it("the verb starts the operation once, shows the busy label meanwhile and recovers when it fails", async () => {
    let reject: (reason?: unknown) => void = () => undefined;
    const onFinish = vi.fn(() => new Promise<void>((_, r) => (reject = r)));
    renderWizard({ onFinish, busyLabel: "Generating" });
    fireEvent.click(button("Wizard:Next"));
    fireEvent.click(button("Wizard:Next"));
    fireEvent.click(button("Generate"));
    expect(onFinish).toHaveBeenCalledTimes(1);
    expect(button("Generating")).toBeDisabled();
    fireEvent.click(button("Generating"));
    expect(onFinish).toHaveBeenCalledTimes(1);
    reject(new Error("refused"));
    await waitFor(() => expect(button("Generate")).toBeEnabled());
  });
});

describe("Wizard – step header (UI-WIZ-003)", () => {
  it("numbers the steps; the current one is brand, completed ones show a check, future ones are muted", () => {
    renderWizard();
    const [intro, select, confirm] = stepItems();
    expect(intro).toHaveAttribute("data-state", "current");
    expect(intro).toHaveAttribute("aria-current", "step");
    expect(intro.querySelector("span")!.className).toContain("bg-brand");
    expect(intro).toHaveTextContent("1");
    expect(select).toHaveAttribute("data-state", "future");
    expect(confirm).toHaveAttribute("data-state", "future");

    fireEvent.click(button("Wizard:Next"));
    const [done, now] = stepItems();
    expect(done).toHaveAttribute("data-state", "completed");
    expect(done.querySelector("svg")).not.toBeNull();
    expect(now).toHaveAttribute("data-state", "current");
  });

  it("a completed step can be revisited; clicking a future step does nothing", () => {
    renderWizard();
    fireEvent.click(within(stepItems()[2]).getByRole("button"));
    expect(stepItems()[0]).toHaveAttribute("data-state", "current");

    fireEvent.click(button("Wizard:Next"));
    fireEvent.click(button("Wizard:Next"));
    expect(stepItems()[2]).toHaveAttribute("data-state", "current");
    fireEvent.click(within(stepItems()[0]).getByRole("button"));
    expect(stepItems()[0]).toHaveAttribute("data-state", "current");
    expect(screen.getByText("Explains what happens")).toBeInTheDocument();
  });

  it("the step label reads Step n of m", () => {
    renderWizard();
    expect(within(stepItems()[1]).getByRole("button")).toHaveAttribute("aria-label", "Step 2 of 3: Select");
  });
});

describe("Wizard – keyboard (UI-KEY-001, UI-ACT-005)", () => {
  it("Enter in the step form advances a valid step", () => {
    renderWizard();
    fireEvent.submit(dialog().querySelector("form")!);
    expect(stepItems()[1]).toHaveAttribute("data-state", "current");
  });

  it("Enter never starts a destructive operation; the destructive verb is red and still works by click", async () => {
    const { onFinish } = renderWizard({ destructive: true, finishLabel: "Delete" });
    fireEvent.click(button("Wizard:Next"));
    fireEvent.click(button("Wizard:Next"));
    fireEvent.submit(dialog().querySelector("form")!);
    expect(onFinish).not.toHaveBeenCalled();
    expect(button("Delete").className).toContain("destructive");
    fireEvent.click(button("Delete"));
    expect(onFinish).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(button("Delete")).toBeEnabled());
  });

  it("Enter on a non-destructive last step starts the operation", async () => {
    const { onFinish } = renderWizard();
    fireEvent.click(button("Wizard:Next"));
    fireEvent.click(button("Wizard:Next"));
    fireEvent.submit(dialog().querySelector("form")!);
    expect(onFinish).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(button("Generate")).toBeEnabled());
  });

  it("a synchronous failure of the operation re-enables the verb", async () => {
    const onFinish = vi.fn(() => {
      throw new Error("refused");
    });
    renderWizard({ onFinish });
    fireEvent.click(button("Wizard:Next"));
    fireEvent.click(button("Wizard:Next"));
    fireEvent.click(button("Generate"));
    expect(onFinish).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(button("Generate")).toBeEnabled());
  });
});

describe("Wizard – the run (UI-WIZ-004)", () => {
  const tasks: WizardTask[] = [
    { id: "a", label: "TO-1001", status: "done" },
    { id: "b", label: "TO-1002", status: "failed", reason: "Already invoiced" },
    { id: "c", label: "TO-1003", status: "running" },
    { id: "d", label: "TO-1004", status: "waiting" },
  ];

  it("shows the TaskList with one row per item, each status and the failure reason, and the progress bar", () => {
    renderWizard({ tasks });
    const list = dialog().querySelector('[data-slot="task-list"]')!;
    const rows = Array.from(list.querySelectorAll("li"));
    expect(rows.map((r) => r.getAttribute("data-status"))).toEqual(["done", "failed", "running", "waiting"]);
    expect(rows[1]).toHaveTextContent("TO-1002");
    expect(rows[1]).toHaveTextContent("Already invoiced");
    expect(rows[2]).toHaveTextContent("Wizard:Task.Running");
    const bar = screen.getByRole("progressbar");
    expect(bar).toHaveAttribute("aria-valuenow", "50");
    expect(screen.getByText("2 of 4 done")).toBeInTheDocument();
    expect(dialog().querySelector("form")).toBeNull();
  });

  it("the footer is a single Close, disabled until every item has finished, and the dialog cannot be closed meanwhile", () => {
    const { onOpenChange } = renderWizard({ tasks });
    expect(within(footer()).getAllByRole("button").map((b) => b.textContent)).toEqual(["Close"]);
    expect(button("Close")).toBeDisabled();
    const header = document.body.querySelector('[data-slot="entity-dialog-header"]') as HTMLElement;
    fireEvent.click(within(header).getByRole("button", { name: "Close" }));
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it("Close is enabled once every item is done or failed, and closes without asking", () => {
    const finished = tasks.map((task) => (task.status === "done" || task.status === "failed" ? task : { ...task, status: "done" as const }));
    const { onOpenChange } = renderWizard({ tasks: finished });
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "100");
    expect(button("Close")).toBeEnabled();
    fireEvent.click(button("Close"));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("isFinished overrides the item rule", () => {
    renderWizard({ tasks, isFinished: true });
    expect(button("Close")).toBeEnabled();
  });

  it("every step reads completed while the run shows", () => {
    renderWizard({ tasks });
    expect(stepItems().map((item) => item.getAttribute("data-state"))).toEqual(["completed", "completed", "completed"]);
    stepItems().forEach((item) => expect(within(item).getByRole("button")).toBeDisabled());
  });

  it("TaskList stands alone with an empty run at 0%", () => {
    render(<TaskList tasks={[]} />);
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "0");
    expect(screen.getByText("0 of 0 done")).toBeInTheDocument();
  });
});

describe("Wizard – controlled step", () => {
  it("follows the step prop and reports changes", () => {
    const onStepChange = vi.fn();
    renderWizard({ step: 1, onStepChange });
    expect(stepItems()[1]).toHaveAttribute("data-state", "current");
    fireEvent.click(button("Wizard:Next"));
    expect(onStepChange).toHaveBeenCalledWith(2);
    fireEvent.click(button("Wizard:Previous"));
    expect(onStepChange).toHaveBeenCalledWith(0);
  });
});
