import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

import { useApplicationConfiguration } from "@/lib/abp/queries";

/* The Maintenance view and its tabs. Risk class: Critical — a tab that renders without its
 * permission or on the wrong side exposes host-wide figures and actions; the tenant side must
 * stay behind the feature flag (constitution rule 4). Medium — the command bars and states. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: vi.fn(() => ({ data: undefined })),
}));

const toastSuccess = vi.fn();
const toastError = vi.fn();
vi.mock("sonner", () => ({
  toast: { success: (...args: unknown[]) => toastSuccess(...args), error: (...args: unknown[]) => toastError(...args) },
}));

const clearMutateAsync = vi.fn();
const dispatchMutate = vi.fn();
const requeueMutateAsync = vi.fn();
const deleteMutateAsync = vi.fn();
const fetchers = { databases: vi.fn(), outbox: vi.fn(), jobs: vi.fn() };
const jobsFetcherArgs: unknown[] = [];
let clearOptions: { onSuccess?: (r: unknown) => void; onError?: (e: unknown) => void } | undefined;

const cacheStatus = {
  data: { providerName: "InMemory", isPerInstance: true, entryCount: 12, keyPrefix: null, instanceName: "box-1" },
  isLoading: false,
  isError: false,
  error: undefined,
  refetch: vi.fn(),
};
const healthReport = {
  data: {
    status: "Degraded",
    totalDurationMilliseconds: 12,
    entries: [
      { name: "db", status: "Healthy", description: "ok", durationMilliseconds: 10, tags: ["ready"], error: null },
      { name: "queue", status: "Degraded", description: null, durationMilliseconds: 2, tags: [], error: "slow" },
    ],
    process: {
      applicationName: "OpenTms.HttpApi.Host",
      version: "1.0",
      environment: "Development",
      machineName: "box-1",
      startedAt: "2026-10-11T08:00:00Z",
      uptimeSeconds: 3600,
      runtimeVersion: ".NET 10",
      workingSetBytes: 1024,
      managedMemoryBytes: 512,
      processorCount: 8,
      outboxDispatchEnabled: true,
    },
  },
  isLoading: false,
  isError: false,
  error: undefined,
  refetch: vi.fn(),
};
const jobsOverview = {
  data: {
    isAvailable: true,
    enqueued: 1,
    scheduled: 2,
    processing: 0,
    succeeded: 40,
    failed: 3,
    deleted: 0,
    recurring: 1,
    servers: [{ name: "srv", workerCount: 20, queues: ["default"], startedAt: null, heartbeatAt: null }],
    queues: [{ name: "default", length: 1, fetched: 0 }],
  },
  isLoading: false,
  isError: false,
  error: undefined,
  refetch: vi.fn(),
};

vi.mock("@/domains/platform/administration-maintenance", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/domains/platform/administration-maintenance")>();
  return {
    ...actual,
    useCacheStatus: () => cacheStatus,
    useClearCaches: (options: typeof clearOptions) => {
      clearOptions = options;
      return { mutateAsync: clearMutateAsync, isPending: false };
    },
    useDatabasesGridFetcher: () => fetchers.databases,
    useDatabasesSummary: () => ({
      data: { databaseCount: 2, tenantDatabaseCount: 1, sharedTenantCount: 1, totalSizeBytes: 2048, unreachableCount: 0, hostBlobStorage: { blobCount: 3, contentBytes: 1024, tableSizeBytes: 4096 } },
      refetch: vi.fn(),
    }),
    useDatabaseDetail: () => ({ data: undefined, isLoading: true, isError: false }),
    useOutboxOverview: () => ({
      data: { dispatchEnabled: true, sweepPeriod: "00:05:00", batchSize: 100, instanceName: "box-1" },
    }),
    useOutboxGridFetcher: () => fetchers.outbox,
    useDispatchOutbox: () => ({ mutate: dispatchMutate, isPending: false }),
    useHealthReport: () => healthReport,
    useBackgroundJobsOverview: () => jobsOverview,
    useBackgroundJobsGridFetcher: (state: unknown) => {
      jobsFetcherArgs.push(state);
      return fetchers.jobs;
    },
    useRequeueBackgroundJob: () => ({ mutateAsync: requeueMutateAsync, isPending: false }),
    useDeleteBackgroundJob: () => ({ mutateAsync: deleteMutateAsync, isPending: false }),
  };
});

type MockGridProps = {
  gridId: string;
  filters?: React.ReactNode;
  onOpen?: (row: unknown) => void;
  rowActions?: (rows: unknown[]) => { id: string; onClick: () => void; destructive?: boolean }[];
};
const gridProps: MockGridProps[] = [];
vi.mock("@/components/ui/opentms-grid-lazy", () => ({
  LazyOpenTmsGrid: (props: MockGridProps) => {
    gridProps.push(props);
    return (
      <div data-testid="mock-grid" data-grid-id={props.gridId}>
        {props.filters}
      </div>
    );
  },
}));

vi.mock("@/apps/admin/components/DatabaseDetailDialog", () => ({
  DatabaseDetailDialog: ({ databaseKey }: { databaseKey: string }) => <div data-testid="database-dialog">{databaseKey}</div>,
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

function setConfig(options: { host: boolean; policies: string[]; features?: Record<string, string> }) {
  mockAppConfig.mockReturnValue({
    data: {
      currentTenant: options.host ? { id: null, name: null, isAvailable: false } : { id: "t-1", name: "demo", isAvailable: true },
      auth: { grantedPolicies: Object.fromEntries(options.policies.map((p) => [p, true])) },
      features: { values: options.features ?? {} },
    },
  } as unknown as ReturnType<typeof useApplicationConfiguration>);
}

const ALL_POLICIES = [
  "Platform.Administration.Maintenance",
  "Platform.Administration.Maintenance.Caches",
  "Platform.Administration.Maintenance.Caches.Clear",
  "Platform.Administration.Maintenance.Databases",
  "Platform.Administration.Maintenance.Outbox",
  "Platform.Administration.Maintenance.Outbox.Dispatch",
  "Platform.Administration.Maintenance.Health",
  "Platform.Administration.Maintenance.BackgroundJobs",
  "Platform.Administration.Maintenance.BackgroundJobs.Manage",
];

import { MaintenanceView } from "@/apps/admin/maintenance/MaintenanceView";

beforeEach(() => {
  gridProps.length = 0;
  jobsFetcherArgs.length = 0;
  clearMutateAsync.mockReset();
  dispatchMutate.mockReset();
  toastSuccess.mockReset();
  toastError.mockReset();
  clearOptions = undefined;
});

describe("MaintenanceView – who sees which tabs (critical)", () => {
  it("shows the permission-denied panel without the view permission (UI-PAGE-005)", () => {
    setConfig({ host: true, policies: ["Platform.Administration.Maintenance.Caches"] });
    render(<MaintenanceView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    expect(screen.queryByRole("tab")).not.toBeInTheDocument();
  });

  it("shows the permission-denied panel while the configuration is still unknown", () => {
    mockAppConfig.mockReturnValue({ data: undefined } as unknown as ReturnType<typeof useApplicationConfiguration>);
    render(<MaintenanceView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
  });

  it("renders every tab for a host user with every permission, caches first", () => {
    setConfig({ host: true, policies: ALL_POLICIES });
    render(<MaintenanceView />);
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual([
      "Maintenance:Caches",
      "Maintenance:Databases",
      "Maintenance:Outbox",
      "Maintenance:Health",
      "Maintenance:BackgroundJobs",
    ]);
  });

  it("hides tabs the user may not see instead of disabling them (UI-PERM-002)", () => {
    setConfig({ host: true, policies: ["Platform.Administration.Maintenance", "Platform.Administration.Maintenance.Health"] });
    render(<MaintenanceView />);
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Maintenance:Health"]);
    expect(screen.queryByRole("tab", { name: "Maintenance:Caches" })).not.toBeInTheDocument();
  });

  it("on the tenant side shows only the outbox tab, and only with the feature enabled (constitution rule 4)", () => {
    setConfig({ host: false, policies: ALL_POLICIES });
    const { unmount } = render(<MaintenanceView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    unmount();

    setConfig({ host: false, policies: ALL_POLICIES, features: { "Platform.Maintenance": "true" } });
    render(<MaintenanceView />);
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Maintenance:Outbox"]);
  });

  it("switches tabs on click and mounts one tab at a time", async () => {
    setConfig({ host: true, policies: ALL_POLICIES });
    render(<MaintenanceView />);
    expect(screen.getByText("Maintenance:ProviderInMemory")).toBeInTheDocument();
    expect(screen.queryByTestId("mock-grid")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("tab", { name: "Maintenance:Databases" }));
    const grid = await screen.findByTestId("mock-grid");
    expect(grid.dataset.gridId).toBe("platform.maintenance.databases");
    expect(screen.queryByText("Maintenance:ProviderInMemory")).not.toBeInTheDocument();
  });
});

describe("CachesTab", () => {
  it("shows the status, the per-instance notice and the clear command only with the clear permission", () => {
    setConfig({ host: true, policies: ["Platform.Administration.Maintenance", "Platform.Administration.Maintenance.Caches"] });
    const { unmount } = render(<MaintenanceView />);
    expect(screen.getByText("Maintenance:ProviderInMemory")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("Maintenance:PerInstanceNotice");
    expect(screen.queryByRole("button", { name: /Maintenance:ClearCaches/ })).not.toBeInTheDocument();
    unmount();

    setConfig({ host: true, policies: ALL_POLICIES });
    render(<MaintenanceView />);
    expect(screen.getByRole("button", { name: /Maintenance:ClearCaches/ })).toBeInTheDocument();
  });

  it("clears after confirmation, then confirms with a toast naming the count (UI-FB-002)", async () => {
    setConfig({ host: true, policies: ALL_POLICIES });
    clearMutateAsync.mockImplementation(async () => {
      clearOptions?.onSuccess?.({ clearedEntryCount: 12, instanceName: "box-1" });
    });
    render(<MaintenanceView />);

    fireEvent.click(screen.getByRole("button", { name: /Maintenance:ClearCaches/ }));
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog).toHaveTextContent("Maintenance:ClearCachesQuestion");
    expect(clearMutateAsync).not.toHaveBeenCalled();

    const buttons = dialog.querySelectorAll("button");
    fireEvent.click(buttons[buttons.length - 1]);
    await waitFor(() => expect(clearMutateAsync).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(toastSuccess).toHaveBeenCalledWith("Maintenance:CachesCleared"));
  });

  it("reports a failed clear with a toast (UI-FB-004)", () => {
    setConfig({ host: true, policies: ALL_POLICIES });
    render(<MaintenanceView />);
    clearOptions?.onError?.({ error: { message: "nope" } });
    expect(toastError).toHaveBeenCalledWith("nope");
  });
});

describe("DatabasesTab", () => {
  it("shows the summary cards and opens the detail dialog from the grid", async () => {
    setConfig({ host: true, policies: ALL_POLICIES });
    render(<MaintenanceView />);
    fireEvent.click(screen.getByRole("tab", { name: "Maintenance:Databases" }));
    await screen.findByTestId("mock-grid");

    expect(screen.getByText("Maintenance:BlobStorageHost")).toBeInTheDocument();
    expect(screen.getByText("Maintenance:TotalSize")).toBeInTheDocument();

    const props = gridProps.at(-1)!;
    expect(props.rowActions!([{ key: "host" }]).map((a) => a.id)).toEqual(["open"]);
    props.onOpen!({ key: "host" });
    expect(await screen.findByTestId("database-dialog")).toHaveTextContent("host");
  });
});

describe("OutboxTab", () => {
  it("offers 'dispatch now' per row only with the dispatch permission and never for a shared-tier row", async () => {
    setConfig({ host: true, policies: ALL_POLICIES });
    render(<MaintenanceView />);
    fireEvent.click(screen.getByRole("tab", { name: "Maintenance:Outbox" }));
    await screen.findByTestId("mock-grid");

    const props = gridProps.at(-1)!;
    const actions = props.rowActions!([{ key: "t-1", tenantId: "t-1", isSharedTier: false }]);
    expect(actions.map((a) => a.id)).toEqual(["dispatch"]);
    actions[0].onClick();
    expect(dispatchMutate).toHaveBeenCalledWith("t-1");

    expect(props.rowActions!([{ key: "host", tenantId: null, isSharedTier: false }])).toHaveLength(1);
    expect(props.rowActions!([{ key: "s-1", tenantId: "s-1", isSharedTier: true }])).toHaveLength(0);
    expect(props.rowActions!([{ key: "a" }, { key: "b" }])).toHaveLength(0);
  });

  it("has no dispatch action without the dispatch permission", async () => {
    setConfig({ host: true, policies: ["Platform.Administration.Maintenance", "Platform.Administration.Maintenance.Outbox"] });
    render(<MaintenanceView />);
    await screen.findByTestId("mock-grid");
    expect(gridProps.at(-1)!.rowActions!([{ key: "host", tenantId: null }])).toHaveLength(0);
  });
});

describe("HealthTab", () => {
  it("shows the overall status, every check and the process facts", async () => {
    setConfig({ host: true, policies: ["Platform.Administration.Maintenance", "Platform.Administration.Maintenance.Health"] });
    render(<MaintenanceView />);
    expect(screen.getAllByText("Maintenance:StatusDegraded")).toHaveLength(2); // the overall card and the degraded check
    expect(screen.getByText("Maintenance:StatusHealthy")).toBeInTheDocument();
    expect(screen.getByText("db")).toBeInTheDocument();
    expect(screen.getByText("slow")).toBeInTheDocument();
    expect(screen.getByText("OpenTms.HttpApi.Host")).toBeInTheDocument();
    expect(screen.getByText("Maintenance:DispatchOn")).toBeInTheDocument();
  });
});

describe("BackgroundJobsTab", () => {
  it("starts on failed jobs, switches state through the cards and limits actions by state and permission", async () => {
    setConfig({ host: true, policies: ALL_POLICIES });
    render(<MaintenanceView />);
    fireEvent.click(screen.getByRole("tab", { name: "Maintenance:BackgroundJobs" }));
    await screen.findByTestId("mock-grid");
    expect(jobsFetcherArgs.at(-1)).toBe("Failed");

    fireEvent.click(screen.getByRole("button", { name: /Maintenance:State.Succeeded/ }));
    await waitFor(() => expect(jobsFetcherArgs.at(-1)).toBe("Succeeded"));

    const props = gridProps.at(-1)!;
    expect(props.rowActions!([{ id: "1", state: "Failed" }]).map((a) => a.id)).toEqual(["requeue", "delete"]);
    expect(props.rowActions!([{ id: "2", state: "Succeeded" }])).toHaveLength(0);
    expect(props.rowActions!([{ id: "3", state: "Recurring" }])).toHaveLength(0);
    expect(props.rowActions!([{ id: "4", state: "Enqueued" }]).map((a) => a.id)).toEqual(["delete"]);
  });

  it("has no job actions without the manage permission", async () => {
    setConfig({ host: true, policies: ["Platform.Administration.Maintenance", "Platform.Administration.Maintenance.BackgroundJobs"] });
    render(<MaintenanceView />);
    await screen.findByTestId("mock-grid");
    expect(gridProps.at(-1)!.rowActions!([{ id: "1", state: "Failed" }])).toHaveLength(0);
  });
});
