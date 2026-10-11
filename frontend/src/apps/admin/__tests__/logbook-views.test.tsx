import { fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

import { useApplicationConfiguration } from "@/lib/abp/queries";

/* The two logbook list pages. Risk class: High — a page that renders without its permission
 * exposes audit data; the period filter decides which history is visible. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: vi.fn(() => ({ data: undefined })),
}));

const functionalFetcher = vi.fn();
const technicalFetcher = vi.fn();
const fetcherArgs = { functional: [] as unknown[], technical: [] as unknown[] };

vi.mock("@/domains/platform/administration-logbook", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/domains/platform/administration-logbook")>();
  return {
    ...actual,
    useFunctionalLogbookGridFetcher: (filter: unknown) => {
      fetcherArgs.functional.push(filter);
      return functionalFetcher;
    },
    useTechnicalLogbookGridFetcher: (filter: unknown) => {
      fetcherArgs.technical.push(filter);
      return technicalFetcher;
    },
  };
});

vi.mock("@/apps/admin/components/FunctionalLogbookEntryDialog", () => ({
  FunctionalLogbookEntryDialog: ({ entryId }: { entryId: string }) => <div data-testid="functional-dialog">{entryId}</div>,
}));

vi.mock("@/apps/admin/components/TechnicalLogbookEntryDialog", () => ({
  TechnicalLogbookEntryDialog: ({ entryId }: { entryId: string }) => <div data-testid="technical-dialog">{entryId}</div>,
}));

type MockGridProps = {
  gridId: string;
  filters?: ReactNode;
  onOpen?: (row: unknown) => void;
  rowActions?: (rows: unknown[]) => { id: string; onClick: () => void }[];
};
const gridProps: MockGridProps[] = [];
vi.mock("@/components/ui/opentms-grid", () => ({
  OpenTmsGrid: (props: MockGridProps) => {
    gridProps.push(props);
    return (
      <div data-testid="mock-grid" data-grid-id={props.gridId}>
        {props.filters}
      </div>
    );
  },
}));

class MockResizeObserver {
  private cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) {
    this.cb = cb;
  }
  observe(el: Element) {
    this.cb([{ contentRect: { width: 9999 } as DOMRectReadOnly, target: el } as ResizeObserverEntry], this as unknown as ResizeObserver);
  }
  disconnect() {}
  unobserve() {}
}

beforeAll(() => vi.stubGlobal("ResizeObserver", MockResizeObserver));
afterAll(() => vi.unstubAllGlobals());

const mockAppConfig = vi.mocked(useApplicationConfiguration);
function setGrants(...policies: string[]) {
  mockAppConfig.mockReturnValue({
    data: { auth: { grantedPolicies: Object.fromEntries(policies.map((p) => [p, true])) } },
  } as unknown as ReturnType<typeof useApplicationConfiguration>);
}

import { FunctionalLogbookView } from "@/apps/admin/logbook/FunctionalLogbookView";
import { TechnicalLogbookView } from "@/apps/admin/logbook/TechnicalLogbookView";

beforeEach(() => {
  gridProps.length = 0;
  fetcherArgs.functional.length = 0;
  fetcherArgs.technical.length = 0;
  window.localStorage.clear();
});

describe("FunctionalLogbookView", () => {
  it("shows the permission-denied panel without the functional logbook permission (UI-PAGE-005)", () => {
    setGrants("Platform.Administration.Logbook.Technical");
    render(<FunctionalLogbookView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    expect(screen.queryByTestId("mock-grid")).not.toBeInTheDocument();
  });

  it("renders the grid with the period and change-type filters in the toolbar slot (UI-GRID-005)", async () => {
    setGrants("Platform.Administration.Logbook.Functional");
    render(<FunctionalLogbookView />);
    const grid = await screen.findByTestId("mock-grid");
    expect(grid.dataset.gridId).toBe("platform.administration.functionalLogbook");
    expect(screen.getByRole("button", { name: "Logbook:Period" })).toHaveTextContent("Logbook:Period.Last6Months");
    expect(screen.getByRole("button", { name: "Logbook:ChangeType" })).toHaveTextContent("Logbook:AllChangeTypes");
    expect(fetcherArgs.functional.at(-1)).toEqual({ period: "last6Months", changeType: undefined });
  });

  it("starts from the period the user chose last time", async () => {
    window.localStorage.setItem("opentms.logbook.period.platform.administration.functionalLogbook", "last7Days");
    setGrants("Platform.Administration.Logbook.Functional");
    render(<FunctionalLogbookView />);
    await screen.findByTestId("mock-grid");
    expect(fetcherArgs.functional.at(-1)).toEqual({ period: "last7Days", changeType: undefined });
  });

  it("opens the entry dialog on row open and offers 'open request' only with the technical permission", async () => {
    setGrants("Platform.Administration.Logbook.Functional");
    render(<FunctionalLogbookView />);
    await screen.findByTestId("mock-grid");

    const row = { id: "c-1", auditLogId: "a-1" };
    const actions = gridProps.at(-1)!.rowActions!([row]);
    expect(actions.map((a) => a.id)).toEqual(["open"]);

    gridProps.at(-1)!.onOpen!(row);
    expect(await screen.findByTestId("functional-dialog")).toHaveTextContent("c-1");
  });

  it("opens the request dialog from the row menu when the technical logbook is allowed", async () => {
    setGrants("Platform.Administration.Logbook.Functional", "Platform.Administration.Logbook.Technical");
    render(<FunctionalLogbookView />);
    await screen.findByTestId("mock-grid");

    const actions = gridProps.at(-1)!.rowActions!([{ id: "c-1", auditLogId: "a-1" }]);
    expect(actions.map((a) => a.id)).toEqual(["open", "open-request"]);
    actions[1].onClick();
    expect(await screen.findByTestId("technical-dialog")).toHaveTextContent("a-1");
  });
});

describe("TechnicalLogbookView", () => {
  it("shows the permission-denied panel without the technical logbook permission", () => {
    setGrants("Platform.Administration.Logbook.Functional");
    render(<TechnicalLogbookView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
  });

  it("renders the grid with the period and errors-only filters and passes them to the fetcher", async () => {
    setGrants("Platform.Administration.Logbook.Technical");
    render(<TechnicalLogbookView />);
    const grid = await screen.findByTestId("mock-grid");
    expect(grid.dataset.gridId).toBe("platform.administration.technicalLogbook");
    expect(fetcherArgs.technical.at(-1)).toEqual({ period: "last6Months", errorsOnly: false });

    fireEvent.click(screen.getByRole("checkbox", { name: "Logbook:ErrorsOnly" }));
    expect(fetcherArgs.technical.at(-1)).toEqual({ period: "last6Months", errorsOnly: true });
  });

  it("opens the request dialog on row open", async () => {
    setGrants("Platform.Administration.Logbook.Technical");
    render(<TechnicalLogbookView />);
    await screen.findByTestId("mock-grid");
    gridProps.at(-1)!.onOpen!({ id: "a-9" });
    expect(await screen.findByTestId("technical-dialog")).toHaveTextContent("a-9");
  });
});
