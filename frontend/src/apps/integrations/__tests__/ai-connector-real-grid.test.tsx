import { act, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ComponentType } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The AI connector screen with the real OpenTmsGrid and its toolbar; only AG Grid's own table is a stand-in, because it does not
 * render in jsdom. It proves what the screen's other tests take from the grid's props: the toolbar has no search box, and the
 * no-rows text is the screen's. Risk class: Medium (UI flows). */

vi.mock("ag-grid-react", () => ({
  AgGridReact: ({ noRowsOverlayComponent, noRowsOverlayComponentParams }: { noRowsOverlayComponent: ComponentType<Record<string, unknown>>; noRowsOverlayComponentParams: Record<string, unknown> }) => {
    const Overlay = noRowsOverlayComponent;
    return (
      <div data-testid="ag-grid">
        <Overlay {...noRowsOverlayComponentParams} />
      </div>
    );
  },
}));
vi.mock("@tanstack/react-router", () => ({ useNavigate: () => vi.fn(), useLocation: () => ({ pathname: "/integrations/ai-connector", search: {} }) }));
vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k === "AiConnector:NoConnections" ? "No connections yet. Add {0}." : k), culture: "en", isLoading: false }),
}));
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: { "Integrations.AiConnector.Use": true } }, features: { values: {} }, localization: { languages: [] } } }),
}));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { AiConnectorView } from "@/apps/integrations/views/AiConnectorView";
import { resetClient, routes } from "@/apps/transport/__tests__/test-setup-mocks";
import { stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";

const URL = "https://api.example.test/mcp";

beforeAll(stubGlobals);
beforeEach(() => {
  resetClient();
  routes.GET["/api/integrations/ai-connector/status"] = () => ({ connectorUrl: URL, isAccepted: true, acceptedAt: "2026-11-05T09:30:00Z" });
});

async function renderView() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <AiConnectorView />
    </QueryClientProvider>,
  );
  await act(async () => {
    await Promise.resolve();
  });
  await screen.findByTestId("ag-grid", undefined, { timeout: 10000 });
}

describe("AiConnectorView with the real OpenTmsGrid", () => {
  it("has a toolbar without a search box", async () => {
    await renderView();
    await waitFor(() => expect(screen.getByRole("button", { name: "Grid:Reset" })).toBeInTheDocument());
    expect(screen.queryByPlaceholderText("Grid:Search")).toBeNull();
    expect(screen.queryByRole("textbox", { name: "Grid:Search" })).toBeNull();
  });

  it("shows the screen's no-rows text with the connector URL", async () => {
    await renderView();
    expect(await screen.findByText(`No connections yet. Add ${URL}.`)).toBeInTheDocument();
  });
});
