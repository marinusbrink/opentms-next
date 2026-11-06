/// <reference types="node" />
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ComponentType, ReactNode } from "react";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* Design 74 "UI design" and PO decision 6, the screen Integrations -> AI connector: the status of each connection comes from the
 * server and the screen derives nothing; Switch off, Accept and Withdraw go through ConfirmDialog and change nothing until the
 * server has answered; Accept and Withdraw only with Manage; not accepted shows the status only; a user with Use sees what the
 * server answers, and the screen asks the same for everyone.
 *
 * The domain hooks are real, the generated client is a path dispatcher, the texts are the backend's own nl and en resource.
 * Risk class: Medium (UI flows). The server enforces the permissions, the feature, own versus all rows and the tenant (backend
 * AiConnector tests); on this screen the negative check is that the request names no user and no tenant. */

const { captured, i18n, location } = vi.hoisted(() => ({
  captured: { grid: null as Record<string, unknown> | null },
  i18n: { texts: {} as Record<string, string>, culture: "en" },
  location: { pathname: "/integrations/ai-connector" },
}));

vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => vi.fn(),
  useLocation: () => ({ pathname: location.pathname, search: {} }),
  Link: ({ to, children }: { to: string; children?: ReactNode }) => <a href={to}>{children}</a>,
}));
vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => i18n.texts[k] ?? k, culture: i18n.culture, isLoading: false }) }));

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: features }, localization: { languages: [] } } }),
}));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/opentms-grid-lazy", async () => {
  const { MockOpenTmsGrid } = await import("@/apps/transport/__tests__/transport-test-mocks");
  const Grid = MockOpenTmsGrid as unknown as ComponentType<Record<string, unknown>>;
  return {
    LazyOpenTmsGrid: (props: Record<string, unknown>) => {
      captured.grid = props;
      return <Grid {...props} />;
    },
  };
});
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { toast } from "sonner";

import { createAppFeatureChecker } from "@/app/app-features";
import { APPS } from "@/app/apps.config";
import { IntegrationsApp } from "@/apps/integrations";
import { AiConnectorView } from "@/apps/integrations/views/AiConnectorView";
import { GET, POST, calls, lastCall, resetClient, routes, type ClientCall } from "@/apps/transport/__tests__/test-setup-mocks";
import { GRID_REQUEST, barButton, fail, gridRows, ok, stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";
import type { GridRowAction, GridSelectionDto } from "@/components/ui/opentms-grid";
import { fetchAiConnections, type AiConnectionRow } from "@/domains/integrations/ai-connector";

const USE = "Integrations.AiConnector.Use";
const MANAGE = "Integrations.AiConnector.Manage";

const STATUS_PATH = "/api/integrations/ai-connector/status";
const ACCEPT_PATH = "/api/integrations/ai-connector/accept";
const WITHDRAW_PATH = "/api/integrations/ai-connector/withdraw";
const CONNECTIONS_PATH = "/api/integrations/ai-connector/connections";
const REVOKE_PATH = "/api/integrations/ai-connector/connections/{id}/revoke";

const URL = "https://api.example.test/mcp";
const ACCEPTED = { connectorUrl: URL, isAccepted: true, acceptedAt: "2026-11-05T09:30:00Z", acceptedByUserName: "Anna de Vries" };
const NOT_ACCEPTED = { connectorUrl: URL, isAccepted: false };

const row = (id: string, status: number, extra: Partial<AiConnectionRow> = {}) =>
  ({ id, userId: `u-${id}`, userName: "Anna de Vries", toolName: "Claude", connectedAt: "2026-11-01T08:00:00Z", status, ...extra }) as unknown as AiConnectionRow;
const LIVE = row("c-live", 1);
const NOT_WORKING = row("c-notworking", 2, { toolName: "ChatGPT" });
const EXPIRED = row("c-expired", 3, { toolName: "Gemini" });
const SWITCHED_OFF = row("c-off", 4, { revokedAt: "2026-11-02T08:00:00Z" });
const ALL_ROWS = [LIVE, NOT_WORKING, EXPIRED, SWITCHED_OFF];

const page = (rows: AiConnectionRow[]) => ({ rows, totalCount: rows.length, filteredCount: rows.length });

const REPO_ROOT = resolve((import.meta as unknown as { dirname: string }).dirname, "../../../../..");
const resource = (lang: string) =>
  (JSON.parse(readFileSync(resolve(REPO_ROOT, `backend/modules/integrations/OpenTms.Integrations.Domain.Shared/Localization/Integrations/${lang}.json`), "utf-8")) as { texts: Record<string, string> }).texts;
const EN = resource("en");
const NL = resource("nl");

const writeText = vi.fn().mockResolvedValue(undefined);

beforeAll(() => {
  stubGlobals();
  Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
});

beforeEach(() => {
  i18n.texts = EN;
  i18n.culture = "en";
  location.pathname = "/integrations/ai-connector";
  captured.grid = null;
  grants = { [USE]: true, [MANAGE]: true };
  features = { "Apps.Integrations": "true", "Apps.Integrations.AiConnector": "true" };
  gridRows.current = [];
  writeText.mockClear();
  vi.mocked(toast.success).mockClear();
  vi.mocked(toast.error).mockClear();
  resetClient();
  routes.GET[STATUS_PATH] = () => ACCEPTED;
  routes.GET[CONNECTIONS_PATH] = () => page(ALL_ROWS);
  routes.POST[ACCEPT_PATH] = () => ACCEPTED;
  routes.POST[WITHDRAW_PATH] = () => NOT_ACCEPTED;
  routes.POST[REVOKE_PATH] = () => undefined;
});

afterEach(() => {
  vi.unstubAllGlobals();
  stubGlobals();
});

function renderView(element: React.ReactElement = <AiConnectorView />) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}
const click = async (element: HTMLElement) =>
  act(async () => {
    fireEvent.click(element);
    await Promise.resolve();
  });

const rows = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=mock-grid-row]"));
const rowById = (id: string) => document.querySelector<HTMLElement>(`[data-slot=mock-grid-row][data-id=${id}]`)!;
const cell = (id: string, column: string) => rowById(id).querySelector<HTMLElement>(`[data-cell=${column}]`)!;
const rowAction = (id: string) => rowById(id).querySelector<HTMLElement>("[data-row-action=switch-off]");
const grid = () => captured.grid as unknown as { rowActions: (rows: AiConnectionRow[]) => GridRowAction[]; onSelectionChange?: (selection: GridSelectionDto) => void; hideSearch?: boolean; emptyMessage?: string; onOpen?: unknown; onExport?: unknown; filters?: unknown };
const acceptance = () => document.querySelector<HTMLElement>("[data-slot=ai-connector-acceptance]")!;
const dialog = () => screen.getByRole("alertdialog");
const dialogButton = (name: string) => within(dialog()).getByRole("button", { name });
const dialogGone = () => waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
const queryButton = (name: string) => screen.queryByRole("button", { name });
const skeleton = () => document.querySelector("[data-slot=skeleton]");
const revokedIds = () => calls(POST, REVOKE_PATH).map(([, init]) => init?.params?.path?.id);

/** Makes the next call of `mock` to `path` wait until the returned function is called with the answer. */
function holdNext(mock: typeof POST, path: string) {
  let release: (answer: unknown) => void = () => undefined;
  const held = new Promise<unknown>((done) => {
    release = done;
  });
  const passThrough = mock.getMockImplementation() as ClientCall;
  mock.mockImplementation(async (called, init) => (called === path ? held : passThrough(called, init)));
  return (answer: unknown) =>
    act(async () => {
      release(answer);
      await held;
    });
}

/** The bar's selection command reads as disabled, and a click on it opens nothing. */
async function expectCommandInert() {
  const command = barButton("Switch off");
  expect(command).toHaveAttribute("aria-disabled", "true");
  await click(command);
  expect(screen.queryByRole("alertdialog")).toBeNull();
}

async function renderLoaded() {
  renderView();
  await screen.findByTestId("mock-grid");
  await waitFor(() => expect(rows().length).toBeGreaterThan(0));
}

describe("the status of a connection (PO decision 6)", () => {
  it.each([
    ["en", 1, "c-live", "Live"],
    ["en", 2, "c-notworking", "Not working"],
    ["en", 3, "c-expired", "Expired"],
    ["en", 4, "c-off", "Switched off"],
    ["nl", 1, "c-live", "Live"],
    ["nl", 2, "c-notworking", "Werkt niet"],
    ["nl", 3, "c-expired", "Verlopen"],
    ["nl", 4, "c-off", "Uitgezet"],
  ])("%s: status %i renders its label", async (lang, _status, id, label) => {
    i18n.texts = lang === "nl" ? NL : EN;
    i18n.culture = lang;
    await renderLoaded();
    expect(cell(id, "status")).toHaveTextContent(new RegExp(`^${label}$`));
  });

  it("the screen derives nothing: the server's status wins over the row's dates", async () => {
    routes.GET[CONNECTIONS_PATH] = () =>
      page([
        row("c-a", 1, { revokedAt: "2026-11-02T08:00:00Z", lastUsedAt: "2020-01-01T00:00:00Z" }),
        row("c-b", 4, { revokedAt: null, lastUsedAt: new Date().toISOString() }),
      ]);
    await renderLoaded();
    expect(cell("c-a", "status")).toHaveTextContent("Live");
    expect(rowAction("c-a")).not.toBeNull();
    expect(cell("c-b", "status")).toHaveTextContent("Switched off");
    expect(rowAction("c-b")).toBeNull();
  });

  it("keeps the server's order of the rows and shows user, tool and the dates", async () => {
    await renderLoaded();
    expect(rows().map((r) => r.getAttribute("data-id"))).toEqual(["c-live", "c-notworking", "c-expired", "c-off"]);
    expect(cell("c-live", "userName")).toHaveTextContent("Anna de Vries");
    expect(cell("c-notworking", "toolName")).toHaveTextContent("ChatGPT");
    expect(cell("c-live", "connectedAt").textContent).toMatch(/2026/);
    expect(cell("c-off", "revokedAt").textContent).toMatch(/2026/);
    expect(cell("c-live", "revokedAt").textContent).toBe("");
    expect(cell("c-live", "lastUsedAt").textContent).toBe("");
  });

  it("does not let the grid sort on the status, the user or the tool: the server sorts those", async () => {
    await renderLoaded();
    const sortable = (column: string) => document.querySelector(`[data-column=${column}]`)!.getAttribute("data-sortable");
    expect(["status", "userName", "toolName"].map(sortable)).toEqual(["false", "false", "false"]);
  });
});

describe("Switch off", () => {
  it("is on every row that is not switched off, and absent on a SwitchedOff row", async () => {
    await renderLoaded();
    expect(rowAction("c-live")).not.toBeNull();
    expect(rowAction("c-notworking")).not.toBeNull();
    expect(rowAction("c-expired")).not.toBeNull();
    expect(rowAction("c-off")).toBeNull();
    expect(grid().rowActions([SWITCHED_OFF])).toEqual([]);
  });

  it("asks first, names the tool and the user, and is destructive; nothing is called before confirming", async () => {
    await renderLoaded();
    await click(rowAction("c-notworking")!);

    expect(dialog()).toHaveTextContent("Switch off AI connection");
    expect(dialog()).toHaveTextContent("Switch off ChatGPT of Anna de Vries?");
    expect(dialogButton("Switch off").className).toContain("destructive");
    expect(POST).not.toHaveBeenCalled();
  });

  it("Cancel closes the dialog and calls nothing", async () => {
    await renderLoaded();
    await click(rowAction("c-live")!);
    await click(dialogButton("Cancel"));
    await dialogGone();
    expect(POST).not.toHaveBeenCalled();
    expect(cell("c-live", "status")).toHaveTextContent("Live");
  });

  it("calls revoke only after confirming, and changes nothing until the server has answered", async () => {
    await renderLoaded();
    const answer = holdNext(POST, REVOKE_PATH);
    const fetchesBefore = calls(GET, CONNECTIONS_PATH).length;

    await click(rowAction("c-live")!);
    expect(revokedIds()).toEqual([]);
    await click(dialogButton("Switch off"));

    /* The call is out and unanswered: the row still reads Live, its action is still there, nothing was refetched or toasted. */
    await waitFor(() => expect(revokedIds()).toEqual(["c-live"]));
    expect(dialog()).toHaveTextContent("Switching off");
    expect(cell("c-live", "status")).toHaveTextContent("Live");
    expect(rowAction("c-live")).not.toBeNull();
    expect(calls(GET, CONNECTIONS_PATH)).toHaveLength(fetchesBefore);
    expect(toast.success).not.toHaveBeenCalled();

    /* The server has switched it off: the grid fetches again and shows what the server now says. */
    routes.GET[CONNECTIONS_PATH] = () => page([row("c-live", 4, { revokedAt: "2026-11-06T08:00:00Z" }), NOT_WORKING]);
    await answer(ok(undefined));
    await dialogGone();
    expect(calls(GET, CONNECTIONS_PATH)).toHaveLength(fetchesBefore + 1);
    await waitFor(() => expect(cell("c-live", "status")).toHaveTextContent("Switched off"));
    expect(rowAction("c-live")).toBeNull();
    expect(toast.success).toHaveBeenCalledWith("1 AI connection switched off.");
    expect(revokedIds()).toEqual(["c-live"]);
  });

  it("a row keeps its status when the server refuses; the refusal shows in the dialog, which stays open", async () => {
    routes.POST[REVOKE_PATH] = () => fail("Connection could not be switched off", 403);
    await renderLoaded();
    await click(rowAction("c-live")!);
    await click(dialogButton("Switch off"));

    await waitFor(() => expect(dialog()).toHaveTextContent("Connection could not be switched off"));
    expect(cell("c-live", "status")).toHaveTextContent("Live");
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("the right-click action on several rows leaves out the switched-off ones", async () => {
    await renderLoaded();
    const [action] = grid().rowActions([LIVE, SWITCHED_OFF, EXPIRED]);
    expect(action.multi).toBe(true);
    expect(action.destructive).toBe(true);

    await act(async () => action.onClick!());
    expect(dialog()).toHaveTextContent("Switch off 2 connections?");
    expect(POST).not.toHaveBeenCalled();
    await click(dialogButton("Switch off"));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("2 AI connections switched off."));
    expect(revokedIds()).toEqual(["c-live", "c-expired"]);
  });

  it("the selection command is disabled without a selection, asks first, and revokes the selected ones", async () => {
    await renderLoaded();
    await expectCommandInert();

    await act(async () => grid().onSelectionChange!({ mode: "Explicit", explicitIds: ["c-live", "c-notworking"], filterRequest: null, excludedIds: [] }));
    await click(barButton("Switch off 2"));
    expect(dialog()).toHaveTextContent("Switch off 2 connections?");
    expect(POST).not.toHaveBeenCalled();

    await click(dialogButton("Switch off"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("2 AI connections switched off."));
    expect(revokedIds()).toEqual(["c-live", "c-notworking"]);
    /* The selection is cleared, so the command cannot act on the same rows again. */
    await dialogGone();
    await expectCommandInert();
  });

  it("a failure halfway a selection stops there, shows the refusal, and refreshes the grid with what did happen", async () => {
    await renderLoaded();
    const fetchesBefore = calls(GET, CONNECTIONS_PATH).length;
    routes.POST[REVOKE_PATH] = (init) => (init?.params?.path?.id === "c-notworking" ? fail("Refused", 403) : undefined);
    await act(async () => grid().onSelectionChange!({ mode: "Explicit", explicitIds: ["c-live", "c-notworking", "c-expired"], filterRequest: null, excludedIds: [] }));
    await click(barButton("Switch off 3"));
    await click(dialogButton("Switch off"));

    await waitFor(() => expect(dialog()).toHaveTextContent("Refused"));
    expect(revokedIds()).toEqual(["c-live", "c-notworking"]);
    expect(toast.success).not.toHaveBeenCalled();
    await waitFor(() => expect(calls(GET, CONNECTIONS_PATH)).toHaveLength(fetchesBefore + 1));
  });

  it("a filter-based selection, from the grid's select all, offers no command: this list switches off row by row", async () => {
    await renderLoaded();
    await act(async () => grid().onSelectionChange!({ mode: "FilterBased", explicitIds: [], filterRequest: GRID_REQUEST, excludedIds: [] }));
    await expectCommandInert();
  });
});

describe("Accept and Withdraw only with Manage", () => {
  it("with Manage and accepted: Withdraw, no Accept", async () => {
    await renderLoaded();
    expect(queryButton("Withdraw")).not.toBeNull();
    expect(queryButton("Accept")).toBeNull();
  });

  it("with Use only and accepted: neither, and Switch off stays", async () => {
    grants = { [USE]: true };
    await renderLoaded();
    expect(queryButton("Withdraw")).toBeNull();
    expect(queryButton("Accept")).toBeNull();
    expect(rowAction("c-live")).not.toBeNull();
  });

  it("with Use only and not accepted: neither", async () => {
    grants = { [USE]: true };
    routes.GET[STATUS_PATH] = () => NOT_ACCEPTED;
    renderView();
    await waitFor(() => expect(acceptance()).toBeTruthy());
    expect(queryButton("Withdraw")).toBeNull();
    expect(queryButton("Accept")).toBeNull();
  });

  it("Manage without Use sees the whole screen", async () => {
    grants = { [MANAGE]: true };
    await renderLoaded();
    expect(queryButton("Withdraw")).not.toBeNull();
    expect(rowAction("c-live")).not.toBeNull();
  });
});

describe("Withdraw", () => {
  it.each([
    ["en", EN, "Withdraw", /all connections stop/i],
    ["nl", NL, NL["AiConnector:Withdraw"], /alle koppelingen stoppen/i],
  ])("%s: the dialog says that all connections stop, and nothing is called before confirming", async (lang, texts, label, sentence) => {
    i18n.texts = texts;
    i18n.culture = lang;
    await renderLoaded();
    await click(screen.getByRole("button", { name: label }));
    expect(dialog().textContent).toMatch(sentence);
    expect(dialogButton(label).className).toContain("destructive");
    expect(POST).not.toHaveBeenCalled();
  });

  it("changes nothing until the server has answered, then shows the server's state without the grid", async () => {
    await renderLoaded();
    const answer = holdNext(POST, WITHDRAW_PATH);
    await click(screen.getByRole("button", { name: "Withdraw" }));
    await click(dialogButton("Withdraw"));

    await waitFor(() => expect(calls(POST, WITHDRAW_PATH)).toHaveLength(1));
    expect(acceptance()).toHaveTextContent("Data sharing accepted");
    expect(screen.queryByTestId("mock-grid")).not.toBeNull();
    expect(toast.success).not.toHaveBeenCalled();

    await answer(ok(NOT_ACCEPTED));
    await dialogGone();
    expect(acceptance()).toHaveTextContent("Data sharing not accepted");
    expect(screen.queryByTestId("mock-grid")).toBeNull();
    expect(queryButton("Accept")).not.toBeNull();
    expect(toast.success).toHaveBeenCalledWith(EN["AiConnector:Withdrawn"]);
  });

  it("a refusal shows in the dialog and the screen stays accepted", async () => {
    routes.POST[WITHDRAW_PATH] = () => fail("No", 403);
    await renderLoaded();
    await click(screen.getByRole("button", { name: "Withdraw" }));
    await click(dialogButton("Withdraw"));
    await waitFor(() => expect(dialog()).toHaveTextContent("No"));
    expect(acceptance()).toHaveTextContent("Data sharing accepted");
    expect(screen.queryByTestId("mock-grid")).not.toBeNull();
  });
});

describe("not accepted", () => {
  beforeEach(() => {
    routes.GET[STATUS_PATH] = () => NOT_ACCEPTED;
  });

  async function renderNotAccepted() {
    renderView();
    await waitFor(() => expect(acceptance()).toBeTruthy());
  }

  it("with Manage: status only, the note for an administrator, and Accept", async () => {
    await renderNotAccepted();
    expect(acceptance()).toHaveTextContent("Data sharing not accepted");
    expect(acceptance()).toHaveTextContent(EN["AiConnector:Status.NotAcceptedManage"]);
    expect(queryButton("Accept")).not.toBeNull();
    expect(screen.queryByTestId("mock-grid")).toBeNull();
    expect(calls(GET, CONNECTIONS_PATH)).toHaveLength(0);
    expect(queryButton("Switch off")).toBeNull();
    expect(document.getElementById("ai-connector-url")).toHaveValue(URL);
  });

  it("without Manage: status only, the note to ask an administrator, and no Accept", async () => {
    grants = { [USE]: true };
    await renderNotAccepted();
    expect(acceptance()).toHaveTextContent(EN["AiConnector:Status.NotAcceptedUser"]);
    expect(queryButton("Accept")).toBeNull();
    expect(screen.queryByTestId("mock-grid")).toBeNull();
    expect(calls(GET, CONNECTIONS_PATH)).toHaveLength(0);
    expect(queryButton("Switch off")).toBeNull();
  });

  it("Accept lists which data leaves, to which kind of vendor and which agreement applies; nothing is called before confirming", async () => {
    await renderNotAccepted();
    await click(screen.getByRole("button", { name: "Accept" }));
    for (const key of ["Accept.Title", "Accept.Description", "Accept.DataHeading", "Connect.DataOrders", "Connect.DataTrips", "Connect.DataRelations", "Accept.DataMoney", "Accept.DataMetrics", "Accept.DataLeft", "Accept.VendorHeading", "Accept.Vendor", "Accept.AgreementHeading", "Accept.Agreement"]) {
      expect(dialog(), key).toHaveTextContent(EN[`AiConnector:${key}`]);
    }
    expect(POST).not.toHaveBeenCalled();

    await click(dialogButton("Cancel"));
    await dialogGone();
    expect(POST).not.toHaveBeenCalled();
  });

  it("Accept changes nothing until the server has answered, then shows the server's state and the grid", async () => {
    await renderNotAccepted();
    const answer = holdNext(POST, ACCEPT_PATH);
    await click(screen.getByRole("button", { name: "Accept" }));
    await click(dialogButton("Accept"));

    await waitFor(() => expect(calls(POST, ACCEPT_PATH)).toHaveLength(1));
    expect(acceptance()).toHaveTextContent("Data sharing not accepted");
    expect(screen.queryByTestId("mock-grid")).toBeNull();

    await answer(ok(ACCEPTED));
    await dialogGone();
    expect(acceptance()).toHaveTextContent("Data sharing accepted");
    expect(await screen.findByTestId("mock-grid")).toBeTruthy();
    expect(queryButton("Withdraw")).not.toBeNull();
    expect(toast.success).toHaveBeenCalledWith(EN["AiConnector:Accepted"]);
  });

  it("a refused Accept shows in the dialog and the screen stays not accepted", async () => {
    routes.POST[ACCEPT_PATH] = () => fail("No", 403);
    await renderNotAccepted();
    await click(screen.getByRole("button", { name: "Accept" }));
    await click(dialogButton("Accept"));
    await waitFor(() => expect(dialog()).toHaveTextContent("No"));
    expect(acceptance()).toHaveTextContent("Data sharing not accepted");
    expect(screen.queryByTestId("mock-grid")).toBeNull();
  });
});

describe("the acceptance when accepted", () => {
  it("says by whom and when", async () => {
    await renderLoaded();
    expect(acceptance().textContent).toMatch(/Accepted by Anna de Vries on .*2026/);
    expect(acceptance().textContent).not.toMatch(/\{\d\}/);
  });

  it("says only when, after the accepting user's id was cleared (design 74, Art. 17)", async () => {
    routes.GET[STATUS_PATH] = () => ({ ...ACCEPTED, acceptedByUserName: null });
    await renderLoaded();
    expect(acceptance().textContent).toMatch(/Accepted on .*2026/);
    expect(acceptance().textContent).not.toContain("Accepted by");
  });
});

describe("the states", () => {
  it("loading: a skeleton and no grid until the status has arrived, then the status and the grid", async () => {
    const answer = holdNext(GET, STATUS_PATH);
    renderView();
    expect(skeleton()).not.toBeNull();
    expect(acceptance()).toBeNull();
    expect(screen.queryByTestId("mock-grid")).toBeNull();
    expect(calls(GET, CONNECTIONS_PATH)).toHaveLength(0);

    await answer(ok(ACCEPTED));
    await waitFor(() => expect(acceptance()).toBeTruthy());
    expect(skeleton()).toBeNull();
    expect(await screen.findByTestId("mock-grid")).toBeTruthy();
  });

  it("empty: the grid's text names the connector URL, there is no row and nothing to switch off", async () => {
    routes.GET[CONNECTIONS_PATH] = () => page([]);
    renderView();
    await screen.findByTestId("mock-grid");
    expect(grid().emptyMessage).toBe(`No connections yet. Add ${URL} as a connector in your AI tool.`);
    expect(rows()).toEqual([]);
    await expectCommandInert();
  });

  it("error: the server's message and Try again; retrying asks again and then shows the status", async () => {
    routes.GET[STATUS_PATH] = () => fail("The connector status could not be read", 500);
    renderView();
    const retry = await screen.findByRole("button", { name: "Try again" });
    expect(screen.getByText("The connector status could not be read")).toBeInTheDocument();
    expect(screen.queryByTestId("mock-grid")).toBeNull();
    expect(skeleton()).toBeNull();
    expect(calls(GET, STATUS_PATH)).toHaveLength(1);

    routes.GET[STATUS_PATH] = () => ACCEPTED;
    await click(retry);
    await waitFor(() => expect(acceptance()).toBeTruthy());
    expect(calls(GET, STATUS_PATH)).toHaveLength(2);
    expect(screen.queryByRole("button", { name: "Try again" })).toBeNull();
    expect(await screen.findByTestId("mock-grid")).toBeTruthy();
  });

  it("error without a message: the localized fallback, never a raw object", async () => {
    GET.mockImplementation(async () => ({ data: undefined, error: {}, response: new Response(null, { status: 500 }) }));
    i18n.texts = { ...EN, UnexpectedError: "Something went wrong" };
    renderView();
    expect(await screen.findByText("Something went wrong")).toBeInTheDocument();
  });

  it("a failing list reaches the grid as a rejection, which is what shows the grid's own retry", async () => {
    routes.GET[CONNECTIONS_PATH] = () => fail("The list could not be read", 500);
    await expect(fetchAiConnections(GRID_REQUEST)).rejects.toEqual({ error: { message: "The list could not be read" } });
  });

  it("permission denied: without Use and Manage the standard state, and nothing is fetched", () => {
    grants = {};
    renderView();
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    expect(document.querySelector("[data-slot=ai-connector-view]")).toBeNull();
    expect(GET).not.toHaveBeenCalled();
  });

  it("another module's permission gives no access", () => {
    grants = { "Integrations.Mailboxes.Manage": true, "Integrations.ApiClients": true };
    renderView();
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    expect(GET).not.toHaveBeenCalled();
  });
});

describe("the connector URL", () => {
  it("is shown read-only and goes to the clipboard with the copy button", async () => {
    await renderLoaded();
    const input = document.getElementById("ai-connector-url") as HTMLInputElement;
    expect(input.value).toBe(URL);
    expect(input.readOnly).toBe(true);

    await click(screen.getByRole("button", { name: "Copy Connector URL" }));
    expect(writeText).toHaveBeenCalledTimes(1);
    expect(writeText).toHaveBeenCalledWith(URL);
  });

  it("is copyable before the data sharing is accepted", async () => {
    routes.GET[STATUS_PATH] = () => NOT_ACCEPTED;
    renderView();
    await click(await screen.findByRole("button", { name: "Copy Connector URL" }));
    expect(writeText).toHaveBeenCalledWith(URL);
  });

  it("offers no copy button without a URL", async () => {
    routes.GET[STATUS_PATH] = () => ({ ...NOT_ACCEPTED, connectorUrl: null });
    renderView();
    await waitFor(() => expect(acceptance()).toBeTruthy());
    expect(queryButton("Copy Connector URL")).toBeNull();
  });
});

describe("the grid of the connections", () => {
  it("has no search box: the endpoint does not search", async () => {
    await renderLoaded();
    expect(grid().hideSearch).toBe(true);
    expect(grid().filters).toBeUndefined();
    expect(screen.queryByRole("textbox", { name: "Grid:Search" })).toBeNull();
    /* The only text box of the screen is the connector URL. */
    expect(screen.getAllByRole("textbox").map((box) => box.id)).toEqual(["ai-connector-url"]);
  });

  it("opens no record and exports nothing: design 74 names row actions only", async () => {
    await renderLoaded();
    expect(grid().onOpen).toBeUndefined();
    expect(grid().onExport).toBeUndefined();
  });

  it("sends the grid's page and sort to the server", async () => {
    await fetchAiConnections({ ...GRID_REQUEST, startRow: 100, endRow: 200, sortModels: [{ colId: "connectedAt", sort: "desc" }] });
    expect(lastCall(GET, CONNECTIONS_PATH)!.params!.query).toEqual({ StartRow: 100, EndRow: 200, SortBy: "connectedAt", SortDir: "desc" });
  });
});

describe("own rows come from the server's answer", () => {
  const OWN = row("c-own", 1, { userName: "Bram (me)" });
  const OTHER = row("c-other", 1, { userName: "Someone else", toolName: "ChatGPT" });
  const PAGING_ONLY = { StartRow: 0, EndRow: 50, SortBy: undefined, SortDir: undefined };

  it("with Use only: the rows the server answers, asked for without a user or a tenant in the request", async () => {
    grants = { [USE]: true };
    routes.GET[CONNECTIONS_PATH] = () => page([OWN]);
    await renderLoaded();
    expect(rows().map((r) => r.getAttribute("data-id"))).toEqual(["c-own"]);
    expect(screen.queryByText("Someone else")).toBeNull();
    expect(lastCall(GET, CONNECTIONS_PATH)!.params!.query).toEqual(PAGING_ONLY);
    expect(lastCall(GET, CONNECTIONS_PATH)!.params!.path).toBeUndefined();
  });

  it("with Manage: the same request, and every row the server answers", async () => {
    routes.GET[CONNECTIONS_PATH] = () => page([OWN, OTHER]);
    await renderLoaded();
    expect(rows().map((r) => r.getAttribute("data-id"))).toEqual(["c-own", "c-other"]);
    expect(lastCall(GET, CONNECTIONS_PATH)!.params!.query).toEqual(PAGING_ONLY);
  });

  it("the screen does not filter or hide on its own: with Use, a row the server answers shows and can be switched off", async () => {
    grants = { [USE]: true };
    routes.GET[CONNECTIONS_PATH] = () => page([OTHER]);
    await renderLoaded();
    expect(screen.getByText("Someone else")).toBeInTheDocument();
    await click(rowAction("c-other")!);
    await click(dialogButton("Switch off"));
    await waitFor(() => expect(revokedIds()).toEqual(["c-other"]));
    /* The server decides: with Use on another user's row it refuses, and the refusal is what the dialog shows (backend tests). */
  });

  it("a revoke the server refuses for lack of permission leaves the row as it was", async () => {
    grants = { [USE]: true };
    routes.GET[CONNECTIONS_PATH] = () => page([OTHER]);
    routes.POST[REVOKE_PATH] = () => fail("Not your connection", 403);
    await renderLoaded();
    await click(rowAction("c-other")!);
    await click(dialogButton("Switch off"));
    await waitFor(() => expect(dialog()).toHaveTextContent("Not your connection"));
    expect(cell("c-other", "status")).toHaveTextContent("Live");
  });
});

describe("the route and the feature (design 74, rule 4)", () => {
  const integrations = APPS.find((app) => app.id === "integrations")!;
  const PATH = "/integrations/ai-connector";

  it("the Integrations app routes the path to this screen", async () => {
    renderView(<IntegrationsApp />);
    await waitFor(() => expect(document.querySelector("[data-slot=ai-connector-view]")).not.toBeNull());
  });

  it("the node is off by default, needs its own feature and the app's", () => {
    const tenant = (values: Record<string, string>) => createAppFeatureChecker(values, false);
    expect(tenant({ "Apps.Integrations": "true", "Apps.Integrations.AiConnector": "true" }).isPathEnabled(integrations, PATH)).toBe(true);
    expect(tenant({ "Apps.Integrations": "true" }).isPathEnabled(integrations, PATH)).toBe(false);
    expect(tenant({ "Apps.Integrations": "true", "Apps.Integrations.AiConnector": "false" }).isPathEnabled(integrations, PATH)).toBe(false);
    expect(tenant({ "Apps.Integrations": "false", "Apps.Integrations.AiConnector": "true" }).isPathEnabled(integrations, PATH)).toBe(false);
  });

  it("switching the node off leaves the group Order intake alone, and the other way round", () => {
    const intake = { "Apps.Integrations.Intake": "true", "Apps.Integrations.Intake.Mailboxes": "true" };
    const nodeOff = createAppFeatureChecker({ "Apps.Integrations": "true", ...intake }, false);
    expect(nodeOff.visibleViews(integrations).map((view) => view.path)).toEqual(["/integrations/intake"]);
    const intakeOff = createAppFeatureChecker({ "Apps.Integrations": "true", "Apps.Integrations.AiConnector": "true" }, false);
    expect(intakeOff.visibleViews(integrations).map((view) => view.path)).toEqual([PATH]);
    expect(intakeOff.firstEnabledPath(integrations)).toBe(PATH);
  });
});
