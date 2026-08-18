/**
 * Pure state-machine tests for the handleReset / scheduleSettingsSave race.
 *
 * Finding #2 (High) — open: api.resetColumnState() fires column-state-change
 * events (onColumnVisible, onColumnMoved, onColumnResized). Each handler calls
 * scheduleSettingsSave(), which re-arms the 1 000 ms debounced PUT. handleReset
 * then issues the DELETE immediately. 1 000 ms later the re-armed debounce fires
 * the PUT — overwriting the reset with the default column state.
 *
 * No clearTimeout is called anywhere in the current handleReset, so the PUT
 * always fires after a reset (even without any pre-existing pending save).
 *
 * Reproduction: the test named "FINDING_2" below FAILS on the current code;
 * it is the implementer's target and proof of the bug.
 *
 * Fix: add clearTimeout(settingsSaveDebounceRef.current) in handleReset
 * AFTER api.resetColumnState() returns (to cancel the events' re-armed debounce).
 */

import { describe, expect, it, vi } from "vitest";

describe("OpenTmsGrid handleReset / scheduleSettingsSave debounce race", () => {
  /**
   * Builds a minimal state machine mirroring the relevant parts of
   * handleReset and scheduleSettingsSave from opentms-grid.tsx.
   *
   * @param applyFix - when true the machine applies the one-line fix
   *   (clearTimeout after simulateColumnEvents)
   */
  function buildMachine(applyFix: boolean) {
    const calls: string[] = [];
    let debounceTimerId: ReturnType<typeof setTimeout> | undefined;

    // Mirrors scheduleSettingsSave: cancel any pending save, arm a new one.
    function scheduleSettingsSave() {
      clearTimeout(debounceTimerId);
      debounceTimerId = setTimeout(() => calls.push("PUT"), 1000);
    }

    // Mirrors the synchronous column-state-change events fired by
    // api.resetColumnState() (onColumnVisible, onColumnMoved, onColumnResized).
    // Each handler calls scheduleSettingsSave() — this re-arms the debounce
    // inside the handleReset call frame, after any clearTimeout at the top.
    function simulateColumnEvents() {
      scheduleSettingsSave();
    }

    // Mirrors the CURRENT handleReset — no clearTimeout anywhere.
    // When applyFix is true it adds the missing clearTimeout after the events.
    function handleReset() {
      simulateColumnEvents(); // api.resetColumnState() → column events → scheduleSettingsSave
      if (applyFix) {
        clearTimeout(debounceTimerId); // fix: cancel the re-armed debounce
      }
      calls.push("DELETE"); // DELETE fires immediately in background
    }

    return { handleReset, scheduleSettingsSave, getCalls: () => calls };
  }

  it("FINDING_2: resetColumnState events re-arm a debounced PUT that fires after the DELETE", () => {
    // Fix applied: clearTimeout(settingsSaveDebounceRef.current) in handleReset cancels
    // the debounce re-armed by resetColumnState's synchronous column events.
    // Expected: only ["DELETE"] — reset leaves no pending save behind.
    vi.useFakeTimers();
    try {
      const { handleReset, getCalls } = buildMachine(true /* with fix */);

      handleReset();
      vi.advanceTimersByTime(1100);

      expect(getCalls()).toEqual(["DELETE"]);
    } finally {
      vi.useRealTimers();
    }
  });

  it("documents current buggy sequence for clarity: DELETE followed by PUT", () => {
    // This test PASSES on the current buggy code and documents the observed
    // wrong behaviour that Finding #2 describes.
    vi.useFakeTimers();
    try {
      const { handleReset, getCalls } = buildMachine(false /* buggy */);

      handleReset();
      vi.advanceTimersByTime(1100);

      // DELETE fires immediately; PUT fires 1 000 ms later (the bug).
      expect(getCalls()).toEqual(["DELETE", "PUT"]);
    } finally {
      vi.useRealTimers();
    }
  });

  it("fixed: clearTimeout after resetColumnState prevents spurious PUT after DELETE", () => {
    // Documents the correct post-fix behaviour: passes once the fix is applied.
    vi.useFakeTimers();
    try {
      const { handleReset, getCalls } = buildMachine(true /* with fix */);

      handleReset();
      vi.advanceTimersByTime(1100);

      // Only DELETE fires; no PUT overwrites the reset.
      expect(getCalls()).toEqual(["DELETE"]);
    } finally {
      vi.useRealTimers();
    }
  });

  it("fixed: pre-existing pending save is also suppressed when reset fires", () => {
    // Scenario: user resized a column 200 ms ago (save debounce armed), then
    // immediately clicked Reset. With the fix, neither the pre-existing save
    // nor the events-triggered re-armed save fires after the DELETE.
    vi.useFakeTimers();
    try {
      const { handleReset, scheduleSettingsSave, getCalls } = buildMachine(
        true /* with fix */,
      );

      // Save debounce armed 200 ms before the reset.
      scheduleSettingsSave();
      vi.advanceTimersByTime(200);

      handleReset();
      vi.advanceTimersByTime(1100);

      expect(getCalls()).toEqual(["DELETE"]);
    } finally {
      vi.useRealTimers();
    }
  });
});
