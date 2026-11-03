import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { useState, type ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The Master data nav tree at three levels (PO decision 2026-10-16, docs/designs/tenants-and-editions.md D15):
 * app -> domain group (General) -> group -> list. Real apps.config and real feature checker. Covers the indentation
 * per level, expand/collapse per group, the auto-open of the path to the active list on navigation (the lists'
 * paths are not under their groups' paths), General off hiding the whole subtree, and the icon rail. Risk class:
 * Critical (Platform change; feature gating of every screen). */

let pathname = "/master-data/countries";
vi.mock("@tanstack/react-router", () => ({
  useLocation: () => ({ pathname }),
  Link: ({ to, children, className, "aria-label": ariaLabel, "aria-current": ariaCurrent }: {
    to: string; children?: ReactNode; className?: string; "aria-label"?: string; "aria-current"?: "page";
  }) => (
    <a href={to} aria-label={ariaLabel} aria-current={ariaCurrent} className={className}>
      {children}
    </a>
  ),
}));

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/components/ui/tooltip", () => ({
  TooltipProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  TooltipTrigger: ({ children, render: renderProp }: { children?: ReactNode; render?: React.ReactElement }) =>
    renderProp != null ? renderProp : <>{children}</>,
  TooltipContent: () => null,
}));

let values: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, features: { values }, auth: { grantedPolicies: {} } } }),
}));

import { APPS, type AppView } from "@/app/apps.config";
import { AppNavPane } from "@/app/shell/AppNavPane";

const masterData = APPS.find((a) => a.id === "master-data")!;

function allMasterDataOn(): Record<string, string> {
  const result: Record<string, string> = { [masterData.feature!]: "true" };
  const walk = (views: readonly AppView[] | undefined) =>
    views?.forEach((view) => {
      if (view.feature) result[view.feature] = "true";
      walk(view.children);
    });
  walk(masterData.views);
  return result;
}

const nav = () => screen.getByRole("navigation");
const groupButton = (key: string) => within(nav()).getByRole("button", { name: new RegExp(key.replace(/\./g, "\\.")) });
const queryGroupButton = (key: string) => within(nav()).queryByRole("button", { name: new RegExp(key.replace(/\./g, "\\.")) });
const listLink = (key: string) => within(nav()).getByRole("link", { name: new RegExp(key.replace(/\./g, "\\.")) });
const queryListLink = (key: string) => within(nav()).queryByRole("link", { name: new RegExp(key.replace(/\./g, "\\.")) });

/** How many indented sub-lists an element sits in: 0 for the top level, one more per level. */
function indentLevel(element: Element): number {
  let level = 0;
  for (let node = element.parentElement; node && node !== nav(); node = node.parentElement) {
    if (node.tagName === "UL" && node.className.split(/\s+/).some((c) => /^pl-\d/.test(c))) level++;
  }
  return level;
}

function PaneWithState({ initiallyCollapsed }: { initiallyCollapsed: boolean }) {
  const [collapsed, setCollapsed] = useState(initiallyCollapsed);
  return <AppNavPane app={masterData} collapsed={collapsed} onToggleCollapsed={() => setCollapsed((c) => !c)} />;
}

beforeEach(() => {
  pathname = "/master-data/countries";
  values = allMasterDataOn();
});

describe("AppNavPane – the Master data tree at three levels", () => {
  it("apps.config holds General first, then Orders, Planning & Execution, Financial and Platform, with General's four groups", () => {
    expect(masterData.views!.map((v) => v.feature)).toEqual([
      "Apps.MasterData.General",
      "Apps.MasterData.Orders",
      "Apps.MasterData.PlanningExecution",
      "Apps.MasterData.Financial",
      "Apps.MasterData.Platform",
    ]);
    expect(masterData.views![0].path).toBe("/master-data/general");
    expect(masterData.views![4].path).toBe("/master-data/platform");
    expect(masterData.views![0].children!.map((v) => v.feature)).toEqual([
      "Apps.MasterData.General.Relations",
      "Apps.MasterData.General.Transport",
      "Apps.MasterData.General.Fleet",
      "Apps.MasterData.General.Reference",
    ]);
  });

  it("indents each level one step further than the level above it", () => {
    render(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);

    expect(indentLevel(groupButton("MasterData:Nav.General"))).toBe(0);
    expect(indentLevel(groupButton("MasterData:Nav.Reference"))).toBe(1);
    expect(indentLevel(groupButton("MasterData:Nav.Fleet"))).toBe(1);
    expect(indentLevel(listLink("MasterData:Countries"))).toBe(2);
  });

  it("opens the path to the active list on first render, and only that path", () => {
    render(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);

    expect(listLink("MasterData:Countries")).toHaveAttribute("aria-current", "page");
    expect(listLink("MasterData:Currencies")).toBeInTheDocument();
    /* The other groups are shown but closed. */
    expect(groupButton("MasterData:Nav.Fleet")).toBeInTheDocument();
    expect(queryListLink("MasterData:Vehicles")).toBeNull();
    expect(queryListLink("MasterData:Products")).toBeNull();
  });

  it("expands and collapses each group on its own", () => {
    render(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);

    fireEvent.click(groupButton("MasterData:Nav.Fleet"));
    expect(listLink("MasterData:Vehicles")).toBeInTheDocument();
    expect(listLink("MasterData:Countries")).toBeInTheDocument();

    fireEvent.click(groupButton("MasterData:Nav.Reference"));
    expect(queryListLink("MasterData:Countries")).toBeNull();
    expect(listLink("MasterData:Vehicles")).toBeInTheDocument();

    fireEvent.click(groupButton("MasterData:Nav.Fleet"));
    expect(queryListLink("MasterData:Vehicles")).toBeNull();
    expect(groupButton("MasterData:Nav.Fleet")).toBeInTheDocument();
  });

  it("collapsing the domain group hides its groups and lists; opening it again shows them", () => {
    render(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);

    fireEvent.click(groupButton("MasterData:Nav.General"));
    expect(queryGroupButton("MasterData:Nav.Reference")).toBeNull();
    expect(queryListLink("MasterData:Countries")).toBeNull();

    fireEvent.click(groupButton("MasterData:Nav.General"));
    expect(groupButton("MasterData:Nav.Reference")).toBeInTheDocument();
    expect(groupButton("MasterData:Nav.Fleet")).toBeInTheDocument();
  });

  it("opens the path when navigation moves into a closed group whose lists are not under its path", () => {
    pathname = "/master-data";
    const { rerender } = render(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);
    expect(queryGroupButton("MasterData:Nav.Fleet")).toBeNull();

    /* /master-data/vehicles is not under /master-data/fleet nor /master-data/general. */
    pathname = "/master-data/vehicles";
    rerender(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);

    expect(listLink("MasterData:Vehicles")).toHaveAttribute("aria-current", "page");
    expect(queryListLink("MasterData:Countries")).toBeNull();
  });

  it("opens the newly active group when navigating from one group's list to another's", () => {
    const { rerender } = render(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);
    expect(queryListLink("MasterData:Products")).toBeNull();

    pathname = "/master-data/products";
    rerender(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);

    expect(listLink("MasterData:Products")).toHaveAttribute("aria-current", "page");
    expect(listLink("MasterData:Countries")).not.toHaveAttribute("aria-current");
  });

  it("re-opens a domain group the user closed when navigation moves back into it", () => {
    pathname = "/master-data";
    const { rerender } = render(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);
    fireEvent.click(groupButton("MasterData:Nav.General"));
    expect(queryGroupButton("MasterData:Nav.Reference")).toBeInTheDocument();
    fireEvent.click(groupButton("MasterData:Nav.General"));
    expect(queryGroupButton("MasterData:Nav.Reference")).toBeNull();

    pathname = "/master-data/geographical-regions";
    rerender(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);

    expect(listLink("MasterData:GeographicalRegions")).toHaveAttribute("aria-current", "page");
  });

  it("with General off hides the whole subtree even though its groups and lists are on", () => {
    /* The other domain groups off too, so only General's own subtree is under test. */
    values = { ...allMasterDataOn(), "Apps.MasterData.General": "false", "Apps.MasterData.Orders": "false", "Apps.MasterData.PlanningExecution": "false", "Apps.MasterData.Financial": "false", "Apps.MasterData.Platform": "false" };
    render(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);

    expect(screen.queryByText("MasterData:Nav.General")).toBeNull();
    expect(screen.queryByText("MasterData:Nav.Reference")).toBeNull();
    expect(screen.queryByText("MasterData:Countries")).toBeNull();
  });

  it("with Platform off hides the Platform group and its lists; General stays", () => {
    values = { ...allMasterDataOn(), "Apps.MasterData.Platform": "false" };
    pathname = "/master-data/countries";
    render(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);

    expect(queryGroupButton("Platform:Nav.Platform")).toBeNull();
    expect(queryListLink("Platform:EmailTemplates")).toBeNull();
    expect(groupButton("MasterData:Nav.General")).toBeInTheDocument();
  });

  it("shows the Platform lists one level below the Platform group and opens it for an active Platform list", () => {
    pathname = "/master-data/document-types";
    render(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);

    expect(indentLevel(groupButton("Platform:Nav.Platform"))).toBe(0);
    expect(listLink("Platform:DocumentTypes")).toHaveAttribute("aria-current", "page");
    expect(indentLevel(listLink("Platform:DocumentTypes"))).toBe(1);
    expect(listLink("Platform:EmailTemplates")).toBeInTheDocument();
    expect(listLink("Platform:ReportDefinitions")).toBeInTheDocument();
    /* General's groups are shown, closed. */
    expect(queryGroupButton("MasterData:Nav.Reference")).toBeNull();
  });

  it("with every domain group off shows nothing", () => {
    values = { ...allMasterDataOn(), "Apps.MasterData.General": "false", "Apps.MasterData.Orders": "false", "Apps.MasterData.PlanningExecution": "false", "Apps.MasterData.Financial": "false", "Apps.MasterData.Platform": "false" };
    render(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);

    expect(within(nav()).queryAllByRole("listitem")).toHaveLength(0);
  });

  it("hides General when all its groups are off", () => {
    values = {
      ...allMasterDataOn(),
      "Apps.MasterData.General.Relations": "false",
      "Apps.MasterData.General.Transport": "false",
      "Apps.MasterData.General.Fleet": "false",
      "Apps.MasterData.General.Reference": "false",
    };
    render(<AppNavPane app={masterData} collapsed={false} onToggleCollapsed={() => {}} />);

    expect(screen.queryByText("MasterData:Nav.General")).toBeNull();
  });
});

describe("AppNavPane – the icon rail with three levels", () => {
  it("shows only General, highlighted, when a list below it is active", () => {
    render(<AppNavPane app={masterData} collapsed={true} onToggleCollapsed={() => {}} />);

    const general = within(nav()).getByRole("button", { name: "MasterData:Nav.General" });
    expect(general.className).toContain("bg-brand");
    /* The rail shows the top level only. */
    expect(within(nav()).queryByRole("button", { name: "MasterData:Nav.Reference" })).toBeNull();
    expect(within(nav()).queryByRole("link", { name: "MasterData:Countries" })).toBeNull();
  });

  it("does not highlight General when the page is not below it", () => {
    pathname = "/master-data";
    render(<AppNavPane app={masterData} collapsed={true} onToggleCollapsed={() => {}} />);

    expect(within(nav()).getByRole("button", { name: "MasterData:Nav.General" }).className).not.toContain("bg-brand");
  });

  it("clicking General in the rail expands the pane with General open", () => {
    pathname = "/master-data";
    render(<PaneWithState initiallyCollapsed={true} />);
    expect(nav().className).toContain("w-14");

    act(() => {
      fireEvent.click(within(nav()).getByRole("button", { name: "MasterData:Nav.General" }));
    });

    expect(nav().className).toContain("w-[250px]");
    expect(groupButton("MasterData:Nav.Relations")).toBeInTheDocument();
    expect(groupButton("MasterData:Nav.Reference")).toBeInTheDocument();
  });

  it("clicking General in the rail on a list page expands the pane with the path to the list open", () => {
    render(<PaneWithState initiallyCollapsed={true} />);

    act(() => {
      fireEvent.click(within(nav()).getByRole("button", { name: "MasterData:Nav.General" }));
    });

    expect(nav().className).toContain("w-[250px]");
    expect(listLink("MasterData:Countries")).toHaveAttribute("aria-current", "page");
  });
});
