import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";

/* Legs outside a trip's window (design 68: `LegOutsideTripWindow` unless allowOutsideWindow; D10 warn, never block):
 * `run` returns the command's value on success, opens the ConfirmDialog with the server's text on the refusal,
 * retries with allowOutsideWindow = true on confirm, resolves `cancelled` on cancel, rethrows any other failure, and
 * rejects (dialog closed) when the retry fails. Risk class: Medium (the dialog decides whether a plan breaks a window). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

import { isCancelledOutcome, isLegOutsideTripWindow, LEG_OUTSIDE_TRIP_WINDOW, useOutsideWindowConfirm, type OutsideWindowResult } from "@/apps/planboard/outside-window";

const refusal = { error: { code: LEG_OUTSIDE_TRIP_WINDOW, message: "Leg L-1 falls outside the trip's window 08:00–12:00." } };

function Harness({ attempt, onResult }: { attempt: (allow: boolean) => Promise<string>; onResult: (r: OutsideWindowResult<string> | { error: unknown }) => void }) {
  const { run, dialog } = useOutsideWindowConfirm();
  const [busy, setBusy] = useState(false);
  return (
    <>
      <button
        disabled={busy}
        onClick={() => {
          setBusy(true);
          run(attempt).then(onResult, (error: unknown) => onResult({ error }));
        }}
      >
        go
      </button>
      {dialog}
    </>
  );
}

describe("isLegOutsideTripWindow / isCancelledOutcome", () => {
  it("recognises the server's refusal code only", () => {
    expect(isLegOutsideTripWindow(refusal)).toBe(true);
    expect(isLegOutsideTripWindow({ error: { code: "OpenTms.PlanningExecution:TripNotPlanned" } })).toBe(false);
    expect(isLegOutsideTripWindow(new Error(LEG_OUTSIDE_TRIP_WINDOW))).toBe(false);
    expect(isLegOutsideTripWindow(null)).toBe(false);
  });

  it("recognises a cancelled outcome", () => {
    expect(isCancelledOutcome({ cancelled: true })).toBe(true);
    expect(isCancelledOutcome({ cancelled: false, value: 1 })).toBe(false);
    expect(isCancelledOutcome(undefined)).toBe(false);
    expect(isCancelledOutcome("cancelled")).toBe(false);
  });
});

describe("useOutsideWindowConfirm", () => {
  it("returns the value without a dialog when the first attempt succeeds", async () => {
    const attempt = vi.fn(async () => "ok");
    const onResult = vi.fn();
    render(<Harness attempt={attempt} onResult={onResult} />);
    await act(async () => void fireEvent.click(screen.getByText("go")));
    await waitFor(() => expect(onResult).toHaveBeenCalledWith({ cancelled: false, value: "ok" }));
    expect(attempt.mock.calls).toEqual([[false]]);
    expect(screen.queryByRole("alertdialog")).toBeNull();
  });

  it("asks with the server's text on the refusal and retries with allowOutsideWindow on confirm", async () => {
    const attempt = vi.fn(async (allow: boolean) => {
      if (!allow) throw refusal;
      return "assigned";
    });
    const onResult = vi.fn();
    render(<Harness attempt={attempt} onResult={onResult} />);
    await act(async () => void fireEvent.click(screen.getByText("go")));

    const dialog = await screen.findByRole("alertdialog");
    expect(dialog).toHaveTextContent("Planboard:OutsideWindow.Title");
    expect(dialog).toHaveTextContent("Leg L-1 falls outside the trip's window 08:00–12:00.");
    await act(async () => void fireEvent.click(screen.getByRole("button", { name: "Planboard:OutsideWindow.Confirm" })));

    await waitFor(() => expect(onResult).toHaveBeenCalledWith({ cancelled: false, value: "assigned" }));
    expect(attempt.mock.calls).toEqual([[false], [true]]);
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
  });

  it("resolves cancelled without a retry when the user keeps the legs out", async () => {
    const attempt = vi.fn(async () => {
      throw refusal;
    });
    const onResult = vi.fn();
    render(<Harness attempt={attempt} onResult={onResult} />);
    await act(async () => void fireEvent.click(screen.getByText("go")));
    await screen.findByRole("alertdialog");
    await act(async () => void fireEvent.click(screen.getByRole("button", { name: "Cancel" })));
    await waitFor(() => expect(onResult).toHaveBeenCalledWith({ cancelled: true }));
    expect(attempt).toHaveBeenCalledTimes(1);
  });

  it("rethrows any other failure without a dialog", async () => {
    const other = { error: { code: "OpenTms.PlanningExecution:ConcurrencyConflict", message: "Changed by someone else" } };
    const onResult = vi.fn();
    render(<Harness attempt={async () => Promise.reject(other)} onResult={onResult} />);
    await act(async () => void fireEvent.click(screen.getByText("go")));
    await waitFor(() => expect(onResult).toHaveBeenCalledWith({ error: other }));
    expect(screen.queryByRole("alertdialog")).toBeNull();
  });

  it("closes the dialog and rejects when the retry fails", async () => {
    const retryError = { error: { code: "OpenTms.PlanningExecution:TripCompleted", message: "Completed" } };
    const attempt = vi.fn(async (allow: boolean) => {
      throw allow ? retryError : refusal;
    });
    const onResult = vi.fn();
    render(<Harness attempt={attempt} onResult={onResult} />);
    await act(async () => void fireEvent.click(screen.getByText("go")));
    await screen.findByRole("alertdialog");
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Planboard:OutsideWindow.Confirm" }));
    });
    await waitFor(() => expect(onResult).toHaveBeenCalledWith({ error: retryError }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
  });
});
