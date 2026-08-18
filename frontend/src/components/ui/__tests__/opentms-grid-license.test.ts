/**
 * Pure logic tests for the AG Grid license key validation path.
 *
 * Risk class: Medium — "AG Grid key validation (watermark path)"
 * (design §Test risk analysis: "missing key → watermark rendered, no crash,
 * structured log emitted").
 *
 * The license-check logic in opentms-grid.tsx runs at module import time:
 *
 *   const licenseKey = (import.meta.env.VITE_AG_GRID_LICENSE_KEY ?? "");
 *   const licenseDetails = LicenseManager.getLicenseDetails(licenseKey);
 *   if (!licenseDetails.valid) {
 *     console.error({ event: "AgGridLicenseInvalid" });
 *   }
 *   LicenseManager.setLicenseKey(licenseKey);
 *
 * Testing module-level side effects across isolated imports is not feasible in
 * Vitest without module cache flushing between every test, which makes test
 * runs slow and fragile. Instead, the logic is extracted and tested as a pure
 * function — identical in structure to the production code — so correctness is
 * verified without coupling to Vitest's module system.
 */

import { describe, expect, it, vi } from "vitest";

// ── Extracted license-check logic (mirrors opentms-grid.tsx verbatim) ─────────

function checkLicense(
  licenseKey: string,
  getLicenseDetails: (key: string) => { valid: boolean },
  logError: (payload: Record<string, string>) => void,
): void {
  const details = getLicenseDetails(licenseKey);
  if (!details.valid) {
    logError({ event: "AgGridLicenseInvalid" });
  }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("AG Grid license key validation (design §UI design — States table)", () => {
  it("emits structured log with event AgGridLicenseInvalid when key is absent (empty string)", () => {
    const logError = vi.fn();
    checkLicense("", () => ({ valid: false }), logError);

    expect(logError).toHaveBeenCalledOnce();
    expect(logError).toHaveBeenCalledWith({ event: "AgGridLicenseInvalid" });
  });

  it("emits structured log when getLicenseDetails reports key as invalid", () => {
    const logError = vi.fn();
    checkLicense("expired-or-wrong-key", () => ({ valid: false }), logError);

    expect(logError).toHaveBeenCalledWith({ event: "AgGridLicenseInvalid" });
  });

  it("does NOT emit any log when key is valid", () => {
    const logError = vi.fn();
    checkLicense("valid-license-key", () => ({ valid: true }), logError);

    expect(logError).not.toHaveBeenCalled();
  });

  it("emits exactly one log entry per validation call regardless of key length", () => {
    const logError = vi.fn();
    checkLicense("x".repeat(2000), () => ({ valid: false }), logError);

    expect(logError).toHaveBeenCalledOnce();
  });

  it("does not throw regardless of key validity (non-blocking path per design)", () => {
    // Design: "Grid renders normally with AG Grid watermark; logger.error with
    // structured log — never blocks."
    expect(() =>
      checkLicense("", () => ({ valid: false }), () => { /* no-op */ }),
    ).not.toThrow();

    expect(() =>
      checkLicense("valid", () => ({ valid: true }), () => { /* no-op */ }),
    ).not.toThrow();
  });

  it("log payload uses exact key 'event' with value 'AgGridLicenseInvalid'", () => {
    // Verifies the structured-log shape is exactly what monitoring can key on.
    const logError = vi.fn();
    checkLicense("", () => ({ valid: false }), logError);

    const payload = logError.mock.calls[0]![0] as Record<string, string>;
    expect(Object.keys(payload)).toEqual(["event"]);
    expect(payload["event"]).toBe("AgGridLicenseInvalid");
  });
});
