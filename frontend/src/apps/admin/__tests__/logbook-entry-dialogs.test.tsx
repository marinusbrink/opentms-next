import { render, screen } from "@testing-library/react";
import { beforeAll, afterAll, describe, expect, it, vi } from "vitest";

/* The read-only logbook dialogs. Risk class: Medium — they show what was recorded; the
 * failure path (UI-FB-004) and the empty states are what a reviewer checks. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { features: { values: {} } } }),
}));

const functionalEntry = vi.fn();
const technicalEntry = vi.fn();
vi.mock("@/domains/platform/administration-logbook", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/domains/platform/administration-logbook")>();
  return {
    ...actual,
    useFunctionalLogbookEntry: () => functionalEntry(),
    useTechnicalLogbookEntry: () => technicalEntry(),
  };
});

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

import { FunctionalLogbookEntryDialog } from "@/apps/admin/components/FunctionalLogbookEntryDialog";
import { TechnicalLogbookEntryDialog } from "@/apps/admin/components/TechnicalLogbookEntryDialog";

const stamp = new Date(2026, 9, 9, 14, 30, 15).toISOString();

describe("FunctionalLogbookEntryDialog", () => {
  it("shows the change, its property values and the 'open request' command", () => {
    functionalEntry.mockReturnValue({
      data: {
        id: "c-1",
        auditLogId: "a-1",
        changeTime: stamp,
        changeType: "Updated",
        entityTypeFullName: "OpenTms.Orders.Order",
        entityTypeName: "Order",
        entityId: "o-1",
        userName: "alice",
        propertyChanges: [{ id: "p-1", propertyName: "Status", originalValue: "\"New\"", newValue: "\"Planned\"" }],
      },
      isLoading: false,
      isError: false,
    });
    const onOpenRequest = vi.fn();
    render(<FunctionalLogbookEntryDialog entryId="c-1" open onClose={() => {}} onOpenRequest={onOpenRequest} />);

    expect(screen.getByText("Administration:FunctionalLogbookEntry 2026-10-09 14:30:15")).toBeInTheDocument();
    expect(screen.getByText("Logbook:ChangeType.Updated")).toBeInTheDocument();
    expect(screen.getByText("OpenTms.Orders.Order")).toBeInTheDocument();
    expect(screen.getByText("alice")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Logbook:PropertyChanges" })).toHaveTextContent("Status");
    expect(screen.getByText("\"Planned\"")).toBeInTheDocument();

    const openRequest = screen.getAllByRole("button").find((b) => b.textContent?.includes("Logbook:OpenRequest"))!;
    openRequest.click();
    expect(onOpenRequest).toHaveBeenCalledWith("a-1");
    expect(screen.queryByText("Save")).not.toBeInTheDocument();
  });

  it("shows the empty state when no property values were recorded", () => {
    functionalEntry.mockReturnValue({
      data: { id: "c-1", auditLogId: "a-1", changeTime: stamp, changeType: "Deleted", entityTypeFullName: "X.Y", entityTypeName: "Y", propertyChanges: [] },
      isLoading: false,
      isError: false,
    });
    render(<FunctionalLogbookEntryDialog entryId="c-1" open onClose={() => {}} />);
    expect(screen.getByText("Logbook:NoPropertyChanges")).toBeInTheDocument();
    expect(screen.queryByText("Logbook:OpenRequest")).not.toBeInTheDocument();
  });

  it("shows the API error inside the dialog (UI-FB-004) and a skeleton while loading", () => {
    functionalEntry.mockReturnValue({ data: undefined, isLoading: false, isError: true, error: { error: { message: "Not found" } } });
    const { unmount } = render(<FunctionalLogbookEntryDialog entryId="c-1" open onClose={() => {}} />);
    expect(screen.getByText("Not found")).toBeInTheDocument();
    unmount();

    functionalEntry.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    render(<FunctionalLogbookEntryDialog entryId="c-1" open onClose={() => {}} />);
    expect(document.body.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0);
  });
});

describe("TechnicalLogbookEntryDialog", () => {
  it("shows the request, its actions and its entity changes on three tabs", () => {
    technicalEntry.mockReturnValue({
      data: {
        id: "a-1",
        executionTime: stamp,
        executionDuration: 1234,
        httpMethod: "POST",
        url: "/api/orders",
        httpStatusCode: 500,
        userName: "bob",
        applicationName: "OpenTms",
        correlationId: "corr",
        clientIpAddress: "10.0.0.1",
        exceptions: "System.InvalidOperationException: boom",
        actions: [{ id: "x-1", serviceName: "OrderAppService", methodName: "CreateAsync", parameters: "{}", executionTime: stamp, executionDuration: 10 }],
        entityChanges: [{ id: "c-1", changeType: "Created", entityTypeName: "Order", entityId: "o-1", propertyChanges: [] }],
      },
      isLoading: false,
      isError: false,
    });
    render(<TechnicalLogbookEntryDialog entryId="a-1" open onClose={() => {}} />);

    expect(screen.getByText("Administration:TechnicalLogbookEntry 2026-10-09 14:30:15")).toBeInTheDocument();
    expect(screen.getByText("/api/orders")).toBeInTheDocument();
    expect(screen.getByText("500")).toBeInTheDocument();
    expect(screen.getByText("1,234")).toBeInTheDocument();
    expect(screen.getByText("System.InvalidOperationException: boom")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Logbook:Actions" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Logbook:EntityChanges" })).toBeInTheDocument();
    expect(screen.getByText("OrderAppService.CreateAsync")).toBeInTheDocument();
    expect(screen.getByText("Logbook:ChangeType.Created · Order o-1")).toBeInTheDocument();
  });

  it("shows the empty states for a request without actions or changes", () => {
    technicalEntry.mockReturnValue({
      data: { id: "a-1", executionTime: stamp, executionDuration: 1, actions: [], entityChanges: [] },
      isLoading: false,
      isError: false,
    });
    render(<TechnicalLogbookEntryDialog entryId="a-1" open onClose={() => {}} />);
    expect(screen.getByText("Logbook:NoActions")).toBeInTheDocument();
    expect(screen.getByText("Logbook:NoEntityChanges")).toBeInTheDocument();
  });
});
