/**
 * Pure logic tests for the retry state machine extracted from opentms-grid.tsx.
 *
 * Finding #1 (High) — confirmed bug: the toast for repeated manual-retry failures
 * is designed to appear after the user retries two or more times. However, the
 * counter (manualRetryCountRef) is reset to 0 at the very start of every getRows
 * call (line 355), and retryServerSideLoads() always triggers a new getRows call.
 * This means the counter is always 0 when the toast-check runs (line 402), so the
 * toast never fires regardless of how many times the user retries.
 *
 * No React rendering or AG Grid is required to prove this — the bug lives entirely
 * in the interaction between handleManualRetry and getRows.
 */

import { describe, expect, it } from "vitest";

describe("OpenTmsGrid retry state machine", () => {
  /**
   * Replicates the exact state tracked by manualRetryCountRef and showErrorToast
   * from opentms-grid.tsx, and simulates the interplay between handleManualRetry
   * and the getRows datasource closure.
   */
  function buildRetryStateMachine() {
    // Mirrors: const manualRetryCountRef = useRef(0);
    let manualRetryCount = 0;
    // Mirrors: const [showErrorToast, setShowErrorToast] = useState(false);
    let showErrorToast = false;

    // Mirrors: datasource.getRows — simplified to the failure path at retryIndex=1.
    // The real function also does async work; we collapse that here because the
    // bug manifests before any await.
    function getRows() {
      // Line 355: manualRetryCountRef.current = 0  ← THE BUG
      manualRetryCount = 0;

      // Simulate: auto-retry already attempted (retryIndex 0 already fired);
      // we are now in the retryIndex=1 branch — the manual-retry failure path.
      const retryIndex = 1;
      if (retryIndex > 0) {
        // Line 402-404: if (manualRetryCountRef.current >= 2) setShowErrorToast(true)
        if (manualRetryCount >= 2) {
          showErrorToast = true;
        }
      }
    }

    // Mirrors: handleManualRetry — increments the counter then triggers getRows
    // via retryServerSideLoads(), which AG Grid resolves synchronously in tests.
    function handleManualRetry() {
      // Line 335: manualRetryCountRef.current += 1
      manualRetryCount += 1;
      // Line 336: gridApiRef.current?.retryServerSideLoads()
      // retryServerSideLoads triggers getRows synchronously in this simulation.
      getRows();
    }

    return { getRows, handleManualRetry, getToastState: () => showErrorToast };
  }

  it("passes: toast state is initially false", () => {
    const { getToastState } = buildRetryStateMachine();
    expect(getToastState()).toBe(false);
  });

  it("passes: manual retry count resets to 0 inside getRows (documenting the bug mechanism)", () => {
    let manualRetryCount = 0;
    function getRows() {
      manualRetryCount = 0; // Line 355 — resets the counter
    }
    manualRetryCount = 5; // Simulate pre-existing increments
    getRows();
    expect(manualRetryCount).toBe(0); // Confirms the reset happens
  });

  /**
   * BUG PROOF — this test FAILS with the current code.
   *
   * The design intent (docs/designs/6-platform-grid-component.md §3.3):
   *   "After two or more consecutive manual retries, a toast should appear to
   *    inform the user that repeated failures have occurred."
   *
   * The bug: manualRetryCountRef.current is reset to 0 in getRows (line 355)
   * before the check at line 402, so the condition `>= 2` can never be reached.
   *
   * Fix: move `manualRetryCountRef.current = 0` to the SUCCESS path only,
   * or use a separate variable incremented in handleManualRetry and cleared on success.
   */
  it("BUG(Finding#1): toast fires after 2 or more consecutive manual retries — FAILS because manualRetryCountRef is reset in getRows", () => {
    const { getRows, handleManualRetry, getToastState } = buildRetryStateMachine();

    // Initial load fails (auto-retry also fails) → failed state
    getRows();

    // User clicks retry three times
    handleManualRetry(); // retry 1: count → 1, then getRows resets to 0
    handleManualRetry(); // retry 2: count → 1, then getRows resets to 0
    handleManualRetry(); // retry 3: count → 1, then getRows resets to 0

    // This assertion FAILS: toast should be shown after 2+ retries, but the bug
    // prevents the counter from ever reaching 2 before being reset.
    expect(getToastState()).toBe(true);
  });
});
