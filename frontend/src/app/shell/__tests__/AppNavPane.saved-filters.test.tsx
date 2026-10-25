import { fireEvent, render, screen, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The saved filters of a list in the nav pane (UI-PAGE-004; design 61: the Transport app's Orders and Trips). A
 * filter is a child entry that links to its list's path with a search, is active only when the location carries that
 * search, and makes its list active with it. Real apps.config and feature checker. Risk class: High (the pane is the
 * shell; a filter that marks the wrong entry active misleads every list). */

let pathname = "/transport/orders";
let search: Record<string, unknown> | undefined = { filter: "today" };
vi.mock("@tanstack/react-router", () => ({
  useLocation: () => ({ pathname, search }),
  Link: ({ to, search: linkSearch, children, className, "aria-label": ariaLabel, "aria-current": ariaCurrent }: {
    to: string; search?: Record<string, string>; children?: ReactNode; className?: string; "aria-label"?: string; "aria-current"?: "page";
  }) => (
    <a href={linkSearch ? `${to}?${new URLSearchParams(linkSearch).toString()}` : to} aria-label={ariaLabel} aria-current={ariaCurrent} className={className}>
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

import { APPS } from "@/app/apps.config";
import { AppNavPane } from "@/app/shell/AppNavPane";

const transport = APPS.find((a) => a.id === "transport")!;
const ALL_ON = {
  "Apps.Transport": "true",
  "Apps.Transport.Orders": "true",
  "Apps.Transport.Orders.Templates": "true",
  "Apps.Transport.Trips": "true",
  "Apps.Transport.Documents": "true",
};

const nav = () => screen.getByRole("navigation");
const links = () => within(nav()).getAllByRole("link");
const activeLinks = () => Array.from(nav().querySelectorAll('a[aria-current="page"]'));

beforeEach(() => {
  values = { ...ALL_ON };
  pathname = "/transport/orders";
  search = { filter: "today" };
});

describe("AppNavPane – saved filters (UI-PAGE-004)", () => {
  it("shows Orders open with its six filters linking to the list with ?filter=, and the other views", () => {
    render(<AppNavPane app={transport} collapsed={false} onToggleCollapsed={vi.fn()} />);
    expect(within(nav()).getByRole("button", { name: /App:Transport\.Orders/ })).toBeInTheDocument();
    const hrefs = links().map((a) => a.getAttribute("href"));
    expect(hrefs).toEqual([
      "/transport/orders?filter=today",
      "/transport/orders?filter=tomorrow",
      "/transport/orders?filter=this-week",
      "/transport/orders?filter=open",
      "/transport/orders?filter=drafts",
      "/transport/orders?filter=all",
      "/transport/templates",
      "/transport/documents",
    ]);
    /* Trips is a closed category: its filters show once it is opened. */
    fireEvent.click(within(nav()).getByRole("button", { name: /App:Transport\.Trips/ }));
    expect(links().map((a) => a.getAttribute("href"))).toContain("/transport/trips?filter=open");
  });

  it("only the filter in the location is active, and its list category is marked", () => {
    render(<AppNavPane app={transport} collapsed={false} onToggleCollapsed={vi.fn()} />);
    expect(activeLinks().map((a) => a.getAttribute("href"))).toEqual(["/transport/orders?filter=today"]);
    expect(within(nav()).getByRole("button", { name: /App:Transport\.Orders/ }).className).toContain("font-medium");
  });

  it("another filter in the location moves the active entry", () => {
    search = { filter: "drafts" };
    render(<AppNavPane app={transport} collapsed={false} onToggleCollapsed={vi.fn()} />);
    expect(activeLinks().map((a) => a.getAttribute("href"))).toEqual(["/transport/orders?filter=drafts"]);
  });

  it("a record page keeps the list's filter active (the filters travel with the record, useOpenRecord)", () => {
    pathname = "/transport/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6";
    search = { filter: "open" };
    render(<AppNavPane app={transport} collapsed={false} onToggleCollapsed={vi.fn()} />);
    expect(activeLinks().map((a) => a.getAttribute("href"))).toEqual(["/transport/orders?filter=open"]);
  });

  it("without a search no filter is active, while the list category still is (the index redirects to the first filter)", () => {
    search = undefined;
    render(<AppNavPane app={transport} collapsed={false} onToggleCollapsed={vi.fn()} />);
    expect(activeLinks()).toEqual([]);
    expect(within(nav()).getByRole("button", { name: /App:Transport\.Orders/ }).className).toContain("font-medium");
  });

  it("a filter of another list with the same value is not active", () => {
    pathname = "/transport/trips";
    search = { filter: "today" };
    render(<AppNavPane app={transport} collapsed={false} onToggleCollapsed={vi.fn()} />);
    expect(activeLinks().map((a) => a.getAttribute("href"))).toEqual(["/transport/trips?filter=today"]);
  });

  it("Orders off hides Orders with its filters and Templates (the chain); Trips and Documents stay", () => {
    values = { ...ALL_ON, "Apps.Transport.Orders": "false" };
    pathname = "/transport/trips";
    render(<AppNavPane app={transport} collapsed={false} onToggleCollapsed={vi.fn()} />);
    expect(within(nav()).queryByRole("button", { name: /App:Transport\.Orders/ })).toBeNull();
    expect(within(nav()).queryByRole("link", { name: /App:Transport\.Templates/ })).toBeNull();
    expect(within(nav()).getByRole("button", { name: /App:Transport\.Trips/ })).toBeInTheDocument();
    expect(within(nav()).getByRole("link", { name: /App:Transport\.Documents/ })).toBeInTheDocument();
  });

  it("the icon rail shows the four top-level views; a filter is reached by expanding", () => {
    const onToggle = vi.fn();
    render(<AppNavPane app={transport} collapsed onToggleCollapsed={onToggle} />);
    expect(within(nav()).getAllByRole("listitem")).toHaveLength(4);
    fireEvent.click(within(nav()).getByRole("button", { name: "App:Transport.Orders" }));
    expect(onToggle).toHaveBeenCalled();
  });
});
