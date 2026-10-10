import { renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { useIsFeatureEnabled, useIsGranted } from "@/lib/auth/permissions";
import { useApplicationConfiguration } from "@/lib/abp/queries";

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: vi.fn(),
}));

const mockConfig = vi.mocked(useApplicationConfiguration);

function withConfig(data: unknown) {
  mockConfig.mockReturnValue({ data } as unknown as ReturnType<typeof useApplicationConfiguration>);
}

describe("useIsGranted / useIsFeatureEnabled (UI-PERM-001/006)", () => {
  it("is true only for an explicitly granted policy", () => {
    withConfig({ auth: { grantedPolicies: { "Platform.Administration.Users": true, "X.Y": false } } });
    expect(renderHook(() => useIsGranted("Platform.Administration.Users")).result.current).toBe(true);
    expect(renderHook(() => useIsGranted("X.Y")).result.current).toBe(false);
    expect(renderHook(() => useIsGranted("Unknown")).result.current).toBe(false);
  });

  it("is false while the configuration has not loaded", () => {
    withConfig(undefined);
    expect(renderHook(() => useIsGranted("Platform.Administration.Users")).result.current).toBe(false);
    expect(renderHook(() => useIsFeatureEnabled("UI.CommonToolbar")).result.current).toBe(false);
  });

  it("reads feature flags as the string \"true\"", () => {
    withConfig({ features: { values: { "UI.CommonToolbar": "true", "Other": "false" } } });
    expect(renderHook(() => useIsFeatureEnabled("UI.CommonToolbar")).result.current).toBe(true);
    expect(renderHook(() => useIsFeatureEnabled("Other")).result.current).toBe(false);
  });
});
