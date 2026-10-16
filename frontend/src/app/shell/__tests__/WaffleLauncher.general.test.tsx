import { fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The Master data tile with its domain groups General and Platform (PO decision 2026-10-16,
 * docs/designs/tenants-and-editions.md D15; design 49): with General and Platform off, the app has no node left, so
 * its tile is hidden even though Apps.MasterData itself is on; one domain group with a list on keeps it. Risk class: Critical (Platform change; feature gating). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));

vi.mock("@tanstack/react-router", () => ({
  Link: ({ to, children, onClick }: { to: string; children: ReactNode; onClick?: () => void }) => (
    <a href={to} onClick={onClick}>
      {children}
    </a>
  ),
}));

let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: { auth: { grantedPolicies: {} }, features: { values: features }, currentTenant: { id: "t-1" } },
  }),
}));

import { APPS, type AppView } from "@/app/apps.config";
import { WaffleLauncher } from "@/app/shell/WaffleLauncher";

const masterData = APPS.find((a) => a.id === "master-data")!;

function masterDataOn(): Record<string, string> {
  const values: Record<string, string> = { [masterData.feature!]: "true" };
  const walk = (views: readonly AppView[] | undefined) =>
    views?.forEach((view) => {
      if (view.feature) values[view.feature] = "true";
      walk(view.children);
    });
  walk(masterData.views);
  return values;
}

const openLauncher = () => fireEvent.click(screen.getByRole("button", { name: "Shell:AppLauncher" }));
const tileNames = () => screen.queryAllByRole("link").map((link) => link.textContent);

beforeEach(() => {
  features = masterDataOn();
});

describe("WaffleLauncher – the General domain group", () => {
  it("shows the Master data tile when General and a list below it are on", () => {
    render(<WaffleLauncher />);
    openLauncher();
    expect(tileNames()).toContain(masterData.nameKey);
  });

  it("hides the Master data tile when General and Platform are off", () => {
    features = { ...masterDataOn(), "Apps.MasterData.General": "false", "Apps.MasterData.Platform": "false" };
    render(<WaffleLauncher />);
    openLauncher();
    expect(tileNames()).not.toContain(masterData.nameKey);
  });

  it("keeps the Master data tile when General is off but Platform has a list on (design 49)", () => {
    features = { ...masterDataOn(), "Apps.MasterData.General": "false" };
    render(<WaffleLauncher />);
    openLauncher();
    expect(tileNames()).toContain(masterData.nameKey);
  });

  it("keeps the Master data tile when only one Platform list is on", () => {
    features = {
      "Apps.MasterData": "true",
      "Apps.MasterData.General": "false",
      "Apps.MasterData.Platform": "true",
      "Apps.MasterData.Platform.NumberSequences": "true",
    };
    render(<WaffleLauncher />);
    openLauncher();
    expect(tileNames()).toEqual([masterData.nameKey]);
  });

  it("hides the Master data tile when every list below General and Platform is off", () => {
    /* General's lists sit at depth 5 (Apps.MasterData.General.<Group>.<List>), Platform's at depth 4. */
    const isList = (k: string) => k.split(".").length === 5 || (k.startsWith("Apps.MasterData.Platform.") && k.split(".").length === 4);
    features = Object.fromEntries(Object.entries(masterDataOn()).map(([k, v]) => [k, isList(k) ? "false" : v]));
    expect(Object.values(features).filter((v) => v === "false")).toHaveLength(19);
    render(<WaffleLauncher />);
    openLauncher();
    expect(tileNames()).not.toContain(masterData.nameKey);
  });

  it("keeps the other apps' tiles when General and Platform are off", () => {
    features = {
      ...masterDataOn(),
      "Apps.MasterData.General": "false",
      "Apps.MasterData.Platform": "false",
      "Apps.Administration": "true",
      "Apps.Administration.Users": "true",
    };
    render(<WaffleLauncher />);
    openLauncher();
    expect(tileNames()).not.toContain(masterData.nameKey);
    expect(tileNames()).toContain("App:Administration");
  });
});
