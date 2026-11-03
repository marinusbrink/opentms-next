import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* Design 72 "UI design", Integrations → Order intake (PO 2026-11-03; formerly in the Master data app): Mailboxes (connect flow: the display name, the
 * request, the authorize URL the browser goes to; the OAuth return ?connected= opening the editor at the folder step and
 * ?connectError= showing the localized refusal; row actions Reconnect / Pause / Resume / Delete only with
 * Integrations.Mailboxes.Manage), API clients (the one-time secret dialog with client id, secret, token URL, scope and
 * curl; regenerate; gating by Integrations.ApiClients.Manage) and Import profiles (the read-only rules table, Activate /
 * Retire gating by Integrations.ImportProfiles.Manage). Every screen is behind the tenant feature
 * Integrations.OrderIntake (UI-PERM-006). The domain hooks are real; the generated client is a path dispatcher. Risk
 * class: Medium (UI flows); the server enforces permissions, features and tenant isolation (backend tests). */

const navigateMock = vi.fn();
let pathname = "/integrations/mailboxes";
let search: Record<string, unknown> = {};
vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => navigateMock,
  useLocation: () => ({ pathname, search }),
  Link: ({ to, children, className }: { to: string; children?: ReactNode; className?: string }) => (
    <a href={to} className={className}>
      {children}
    </a>
  ),
}));

/* Identity translator, except a few texts that prove a localized message is shown (not the raw code). */
const TEXTS: Record<string, string> = { "Mailboxes:ConnectError.access_denied": "Access was refused at the provider" };
vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => TEXTS[k] ?? k, culture: "en", isLoading: false }) }));

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
let configLoaded = true;
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: configLoaded ? { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: features }, localization: { languages: [] } } : undefined,
  }),
}));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/opentms-grid-lazy", async () => ({ LazyOpenTmsGrid: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockOpenTmsGrid }));
vi.mock("@/components/ui/multi-select", async () => ({ MultiSelect: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockMultiSelect }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockReferenceCombobox }));
vi.mock("@/components/ui/entity-lookup", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/components/ui/entity-lookup")>()),
  EntityLookup: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockEntityLookup,
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

/* The Mailbox and API client editors: stand-ins recording their props (the real Mailbox editor where a test asks for it). */
const { editorProps, useRealMailboxEditor } = vi.hoisted(() => ({
  editorProps: { mailbox: null as Record<string, unknown> | null, apiClient: null as Record<string, unknown> | null },
  useRealMailboxEditor: { current: false },
}));
vi.mock("@/apps/integrations/editors/MailboxEditor", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/apps/integrations/editors/MailboxEditor")>();
  return {
    MailboxEditor: (props: Parameters<typeof real.MailboxEditor>[0]) => {
      if (useRealMailboxEditor.current) return real.MailboxEditor(props);
      editorProps.mailbox = props as unknown as Record<string, unknown>;
      return <div data-testid="mailbox-editor" data-id={props.id} data-readonly={String(props.readOnly)} />;
    },
  };
});
vi.mock("@/apps/integrations/editors/ApiClientEditor", () => ({
  ApiClientEditor: (props: { mode: string; id?: string; readOnly: boolean; onSecret: (secret: Record<string, string>, name: string) => void }) => {
    editorProps.apiClient = props as unknown as Record<string, unknown>;
    return (
      <div data-testid="api-client-editor" data-mode={props.mode} data-id={props.id ?? ""}>
        <button type="button" onClick={() => props.onSecret(NEW_SECRET, "Acme ERP")}>
          editor-created
        </button>
      </div>
    );
  },
}));

import { toast } from "sonner";

import { ApiClientsView } from "@/apps/integrations/views/ApiClientsView";
import { ImportProfilesView } from "@/apps/integrations/views/ImportProfilesView";
import { MailboxesView } from "@/apps/integrations/views/MailboxesView";
import { ImportProfileEditor } from "@/apps/integrations/editors/ImportProfileEditor";
import { DELETE, GET, POST, calls, lastCall, resetClient, routes } from "@/apps/transport/__tests__/test-setup-mocks";
import { barButton, fail, gridRows, panel, ribbonButton, stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";

const NEW_SECRET = { id: "ac-1", clientId: "otms_ABCDEFGHIJKLMNOPQRSTUVWX", clientSecret: "s3cr3t-0123456789-abcdefghij-klmnopqrst", tokenUrl: "https://api.example.test/connect/token", scope: "OpenTms" };

const INTAKE_FEATURES = {
  "Integrations.OrderIntake": "true",
  "Apps.Integrations": "true",
  "Apps.Integrations.Intake": "true",
  "Apps.Integrations.Intake.Received": "true",
};

const assign = vi.fn();
const writeText = vi.fn().mockResolvedValue(undefined);

beforeAll(() => {
  stubGlobals();
  Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
});

beforeEach(() => {
  navigateMock.mockReset();
  assign.mockReset();
  writeText.mockClear();
  vi.stubGlobal("location", { ...window.location, assign });
  vi.mocked(toast.success).mockClear();
  vi.mocked(toast.error).mockClear();
  pathname = "/integrations/mailboxes";
  search = {};
  grants = {};
  features = { ...INTAKE_FEATURES };
  configLoaded = true;
  gridRows.current = [];
  editorProps.mailbox = null;
  editorProps.apiClient = null;
  useRealMailboxEditor.current = false;
  resetClient();
});

afterEach(() => {
  vi.unstubAllGlobals();
  stubGlobals();
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}

const rows = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=mock-grid-row]"));
const rowActions = (index = 0) => Array.from(rows()[index].querySelectorAll<HTMLElement>("[data-row-action]")).map((b) => b.getAttribute("data-row-action"));
const rowAction = (index: number, id: string) => rows()[index].querySelector<HTMLElement>(`[data-row-action=${id}]`)!;
const click = async (element: HTMLElement) =>
  act(async () => {
    fireEvent.click(element);
    await Promise.resolve();
  });
const queryBarButton = (label: string) => {
  try {
    return barButton(label);
  } catch {
    return null;
  }
};
const confirmDialog = () => screen.getByRole("alertdialog");

/* ── Mailboxes ───────────────────────────────────────────────────────────────────────────────────────────────── */

const MAILBOX_ROWS = [
  { id: "mb-active", displayName: "Orders inbox", provider: 1, emailAddress: "orders@acme.test", folderName: "Orders", status: 3, lastSyncAt: "2026-11-03T08:00:00Z" },
  { id: "mb-paused", displayName: "Paused inbox", provider: 2, emailAddress: "paused@acme.test", folderName: "Inbox/Orders", status: 4 },
  { id: "mb-reconnect", displayName: "Revoked inbox", provider: 1, emailAddress: "revoked@acme.test", folderName: "Orders", status: 5, lastErrorCode: "invalid_grant" },
];

describe("MailboxesView (design 72)", () => {
  const VIEW = { "Integrations.Mailboxes": true };
  const MANAGE = { ...VIEW, "Integrations.Mailboxes.Manage": true };

  beforeEach(() => {
    routes.GET["/api/integrations/mailbox-connection"] = () => ({ rows: MAILBOX_ROWS, totalCount: 3, filteredCount: 3 });
  });

  it("is not rendered when the tenant feature Integrations.OrderIntake is off, and fetches nothing", () => {
    grants = MANAGE;
    features = { ...INTAKE_FEATURES, "Integrations.OrderIntake": "false" };
    renderWith(<MailboxesView />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
    expect(calls(GET, "/api/integrations/mailbox-connection")).toHaveLength(0);
  });

  it("needs Integrations.Mailboxes", () => {
    renderWith(<MailboxesView />);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    expect(calls(GET, "/api/integrations/mailbox-connection")).toHaveLength(0);
  });

  it("view only: the grid, Open as the only row action, no Connect commands", async () => {
    grants = VIEW;
    renderWith(<MailboxesView />);
    await waitFor(() => expect(rows()).toHaveLength(3));
    expect(document.querySelector("[data-grid-id='integrations.mailboxes']")).not.toBeNull();
    for (const index of [0, 1, 2]) expect(rowActions(index)).toEqual(["open"]);
    expect(queryBarButton("Integrations:ConnectGmail")).toBeNull();
    expect(queryBarButton("Integrations:ConnectMicrosoft365")).toBeNull();
    await click(rowAction(0, "open"));
    expect(screen.getByTestId("mailbox-editor")).toHaveAttribute("data-readonly", "true");
  });

  it("with Manage: Pause on an active mailbox, Resume on a paused one, Reconnect on one that needs it; Delete on every row", async () => {
    grants = MANAGE;
    renderWith(<MailboxesView />);
    await waitFor(() => expect(rows()).toHaveLength(3));
    expect(rowActions(0)).toEqual(["edit", "pause", "delete"]);
    expect(rowActions(1)).toEqual(["edit", "resume", "delete"]);
    expect(rowActions(2)).toEqual(["edit", "reconnect", "delete"]);
  });

  it("Pause and Resume call their endpoint and say so", async () => {
    grants = MANAGE;
    renderWith(<MailboxesView />);
    await waitFor(() => expect(rows()).toHaveLength(3));
    await click(rowAction(0, "pause"));
    await waitFor(() => expect(lastCall(POST, "/api/integrations/mailbox-connection/{id}/pause")?.params?.path).toEqual({ id: "mb-active" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Integrations:MailboxPaused"));
    await click(rowAction(1, "resume"));
    await waitFor(() => expect(lastCall(POST, "/api/integrations/mailbox-connection/{id}/resume")?.params?.path).toEqual({ id: "mb-paused" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Integrations:MailboxResumed"));
  });

  it("Reconnect sends the browser to the provider's authorize URL", async () => {
    grants = MANAGE;
    routes.POST["/api/integrations/mailbox-connection/{id}/reconnect"] = () => ({ authorizeUrl: "https://accounts.google.test/o/oauth2/auth?state=xyz" });
    renderWith(<MailboxesView />);
    await waitFor(() => expect(rows()).toHaveLength(3));
    await click(rowAction(2, "reconnect"));
    await waitFor(() => expect(assign).toHaveBeenCalledWith("https://accounts.google.test/o/oauth2/auth?state=xyz"));
    expect(lastCall(POST, "/api/integrations/mailbox-connection/{id}/reconnect")?.params?.path).toEqual({ id: "mb-reconnect" });
  });

  it("Delete asks first (nothing is revoked at the provider) and deletes on confirm", async () => {
    grants = MANAGE;
    renderWith(<MailboxesView />);
    await waitFor(() => expect(rows()).toHaveLength(3));
    await click(rowAction(0, "delete"));
    expect(calls(DELETE, "/api/integrations/mailbox-connection/{id}")).toHaveLength(0);
    await click(within(confirmDialog()).getByRole("button", { name: "Delete" }));
    await waitFor(() => expect(lastCall(DELETE, "/api/integrations/mailbox-connection/{id}")?.params?.path).toEqual({ id: "mb-active" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Integrations:MailboxDeleted"));
  });

  it("Connect Gmail asks the display name, posts the connection and goes to the authorize URL", async () => {
    grants = MANAGE;
    routes.POST["/api/integrations/mailbox-connection"] = () => ({ id: "mb-new", authorizeUrl: "https://accounts.google.test/consent?state=abc" });
    renderWith(<MailboxesView />);
    await waitFor(() => expect(rows()).toHaveLength(3));
    await click(barButton("Integrations:ConnectGmail"));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText("Integrations:ConnectGmail")).toBeInTheDocument();

    /* Without a name nothing is posted. */
    await click(within(dialog).getByRole("button", { name: "Integrations:Connect" }));
    expect(calls(POST, "/api/integrations/mailbox-connection")).toHaveLength(0);
    expect(within(dialog).getByText("MasterData:Required")).toBeInTheDocument();

    fireEvent.change(dialog.querySelector("#connect-mailbox-name")!, { target: { value: "  Orders NL  " } });
    await click(within(dialog).getByRole("button", { name: "Integrations:Connect" }));
    await waitFor(() => expect(assign).toHaveBeenCalledWith("https://accounts.google.test/consent?state=abc"));
    expect(lastCall(POST, "/api/integrations/mailbox-connection")?.body).toEqual({ provider: 1, displayName: "Orders NL" });
  });

  it("Connect Microsoft 365 posts provider 2; a refusal stays in the dialog and nothing navigates", async () => {
    grants = MANAGE;
    routes.POST["/api/integrations/mailbox-connection"] = () => fail("Microsoft 365 is not set up on this platform");
    renderWith(<MailboxesView />);
    await waitFor(() => expect(rows()).toHaveLength(3));
    await click(barButton("Integrations:ConnectMicrosoft365"));
    const dialog = screen.getByRole("dialog");
    fireEvent.change(dialog.querySelector("#connect-mailbox-name")!, { target: { value: "Planning" } });
    await click(within(dialog).getByRole("button", { name: "Integrations:Connect" }));
    expect(await within(dialog).findByText("Microsoft 365 is not set up on this platform")).toBeInTheDocument();
    expect(lastCall(POST, "/api/integrations/mailbox-connection")?.body).toEqual({ provider: 2, displayName: "Planning" });
    expect(assign).not.toHaveBeenCalled();
  });

  it("?connected={id} (the OAuth return) opens the editor on that connection and removes the parameter", async () => {
    grants = MANAGE;
    search = { connected: "mb-new", keep: "1" };
    renderWith(<MailboxesView />);
    await waitFor(() => expect(screen.getByTestId("mailbox-editor")).toHaveAttribute("data-id", "mb-new"));
    expect(screen.getByTestId("mailbox-editor")).toHaveAttribute("data-readonly", "false");
    await waitFor(() => expect(navigateMock).toHaveBeenCalled());
    const call = navigateMock.mock.calls[0][0] as { to: string; replace: boolean; search: (previous: Record<string, unknown>) => Record<string, unknown> };
    expect(call.to).toBe("/integrations/mailboxes");
    expect(call.replace).toBe(true);
    expect(call.search({ connected: "mb-new", connectError: "x", keep: "1" })).toEqual({ keep: "1" });
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("?connectError={code} shows the localized refusal; an unknown code shows the generic provider error", async () => {
    grants = MANAGE;
    search = { connectError: "access_denied" };
    const { unmount } = renderWith(<MailboxesView />);
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Access was refused at the provider"));
    expect(screen.queryByTestId("mailbox-editor")).toBeNull();
    unmount();

    vi.mocked(toast.error).mockClear();
    search = { connectError: "something_new" };
    renderWith(<MailboxesView />);
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Mailboxes:ConnectError.provider_error"));
  });

  it("does not act on the OAuth return while the feature is off", () => {
    grants = MANAGE;
    features = { ...INTAKE_FEATURES, "Integrations.OrderIntake": "false" };
    search = { connected: "mb-new" };
    renderWith(<MailboxesView />);
    expect(screen.queryByTestId("mailbox-editor")).toBeNull();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("the editor opened from the return of a connection waiting for its folder says to choose it and offers the provider's folders", async () => {
    grants = MANAGE;
    useRealMailboxEditor.current = true;
    search = { connected: "mb-new" };
    routes.GET["/api/integrations/mailbox-connection/{id}"] = () => ({ id: "mb-new", displayName: "Orders NL", provider: 1, emailAddress: "orders@acme.test", status: 2, autoConfirm: false, concurrencyStamp: "c1" });
    routes.GET["/api/integrations/mailbox-connection/{id}/folders"] = () => [
      { id: "INBOX", name: "INBOX" },
      { id: "Label_7", name: "Orders" },
    ];
    renderWith(<MailboxesView />);
    expect(await screen.findByText("Integrations:ChooseFolderNotice")).toBeInTheDocument();
    await waitFor(() => expect(calls(GET, "/api/integrations/mailbox-connection/{id}/folders")).toHaveLength(1));
    const folder = (await screen.findByRole("option", { name: "Orders" })).closest("select")!;
    expect(Array.from(folder.querySelectorAll("option")).map((o) => o.textContent)).toEqual(["—", "INBOX", "Orders"]);
  });
});

/* ── API clients ─────────────────────────────────────────────────────────────────────────────────────────────── */

const API_CLIENT_ROWS = [
  { id: "ac-1", name: "Acme ERP", clientId: "otms_ABCDEFGHIJKLMNOPQRSTUVWX", customerName: "Acme", isActive: true },
  { id: "ac-2", name: "Old link", clientId: "otms_ZYXWVUTSRQPONMLKJIHGFEDC", customerName: null, isActive: false },
];

describe("ApiClientsView and the one-time secret dialog (design 72)", () => {
  const VIEW = { "Integrations.ApiClients": true };
  const MANAGE = { ...VIEW, "Integrations.ApiClients.Manage": true };

  beforeEach(() => {
    pathname = "/integrations/api-clients";
    routes.GET["/api/integrations/api-client"] = () => ({ rows: API_CLIENT_ROWS, totalCount: 2, filteredCount: 2 });
  });

  it("is not rendered with the feature off; needs Integrations.ApiClients", () => {
    grants = MANAGE;
    features = { ...INTAKE_FEATURES, "Integrations.OrderIntake": "false" };
    const { unmount } = renderWith(<ApiClientsView />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
    unmount();
    features = { ...INTAKE_FEATURES };
    grants = {};
    renderWith(<ApiClientsView />);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    expect(calls(GET, "/api/integrations/api-client")).toHaveLength(0);
  });

  it("view only: Open only, no New, no secret actions", async () => {
    grants = VIEW;
    renderWith(<ApiClientsView />);
    await waitFor(() => expect(rows()).toHaveLength(2));
    expect(rowActions(0)).toEqual(["open"]);
    expect(rowActions(1)).toEqual(["open"]);
    expect(queryBarButton("Integrations:NewApiClient")).toBeNull();
  });

  it("with Manage: Regenerate, Deactivate for an active client and Activate for an inactive one, Delete", async () => {
    grants = MANAGE;
    renderWith(<ApiClientsView />);
    await waitFor(() => expect(rows()).toHaveLength(2));
    expect(rowActions(0)).toEqual(["edit", "regenerate", "deactivate", "delete"]);
    expect(rowActions(1)).toEqual(["edit", "regenerate", "activate", "delete"]);
    await click(rowAction(0, "deactivate"));
    await waitFor(() => expect(lastCall(POST, "/api/integrations/api-client/{id}/deactivate")?.params?.path).toEqual({ id: "ac-1" }));
    await click(rowAction(1, "activate"));
    await waitFor(() => expect(lastCall(POST, "/api/integrations/api-client/{id}/activate")?.params?.path).toEqual({ id: "ac-2" }));
  });

  it("a create shows the one-time dialog: client id, secret, token URL, scope and the curl example, each copyable", async () => {
    grants = MANAGE;
    renderWith(<ApiClientsView />);
    await waitFor(() => expect(rows()).toHaveLength(2));
    await click(barButton("Integrations:NewApiClient"));
    expect(screen.getByTestId("api-client-editor")).toHaveAttribute("data-mode", "create");
    await click(screen.getByRole("button", { name: "editor-created" }));

    const dialog = document.querySelector<HTMLElement>("[data-slot=api-client-secret]")!;
    expect(dialog).not.toBeNull();
    expect(within(dialog).getByText("Integrations:SecretShownOnce")).toBeInTheDocument();
    const value = (id: string) => (dialog.querySelector(`#${id}`) as HTMLInputElement).value;
    expect(value("api-client-secret-client-id")).toBe(NEW_SECRET.clientId);
    expect(value("api-client-secret-secret")).toBe(NEW_SECRET.clientSecret);
    expect(value("api-client-secret-token-url")).toBe(NEW_SECRET.tokenUrl);
    expect(value("api-client-secret-scope")).toBe("OpenTms");
    const curl = dialog.querySelector("pre")!.textContent!;
    expect(curl).toContain(`curl -X POST "${NEW_SECRET.tokenUrl}"`);
    expect(curl).toContain("grant_type=client_credentials");
    expect(curl).toContain(`client_id=${NEW_SECRET.clientId}`);
    expect(curl).toContain(`client_secret=${NEW_SECRET.clientSecret}`);
    expect(curl).toContain("scope=OpenTms");
    /* The identity translator leaves the {0} placeholder: four field copy buttons and the curl copy button. */
    const copyButtons = within(dialog).getAllByRole("button", { name: /Integrations:CopyValue/ });
    expect(copyButtons).toHaveLength(5);
    await click(copyButtons[1]);
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(NEW_SECRET.clientSecret));

    /* Closing forgets the secret: it is never shown again. */
    await click(screen.getAllByRole("button", { name: "Close" }).find((b) => b.textContent === "Close")!);
    await waitFor(() => expect(document.querySelector("[data-slot=api-client-secret]")).toBeNull());
  });

  it("Regenerate secret asks first, posts, and shows the new secret once", async () => {
    grants = MANAGE;
    routes.POST["/api/integrations/api-client/{id}/regenerate-secret"] = () => ({ ...NEW_SECRET, clientSecret: "regenerated-secret-value-0000000000000000" });
    renderWith(<ApiClientsView />);
    await waitFor(() => expect(rows()).toHaveLength(2));
    await click(rowAction(0, "regenerate"));
    expect(calls(POST, "/api/integrations/api-client/{id}/regenerate-secret")).toHaveLength(0);
    await click(within(confirmDialog()).getByRole("button", { name: "Integrations:Regenerate" }));
    await waitFor(() => expect(lastCall(POST, "/api/integrations/api-client/{id}/regenerate-secret")?.params?.path).toEqual({ id: "ac-1" }));
    const dialog = await waitFor(() => document.querySelector<HTMLElement>("[data-slot=api-client-secret]")!);
    expect((dialog.querySelector("#api-client-secret-secret") as HTMLInputElement).value).toBe("regenerated-secret-value-0000000000000000");
  });
});

/* ── Import profiles ─────────────────────────────────────────────────────────────────────────────────────────── */

const PROFILE_ROWS = [
  { id: "p-proposed", name: "Acme xlsx", senderKey: "@acme.test", format: 5, status: 1, version: 1, confirmedCount: 1, correctedCount: 0 },
  { id: "p-active", name: "Beta pdf", senderKey: null, format: 3, status: 2, version: 3, confirmedCount: 7, correctedCount: 0 },
  { id: "p-retired", name: "Gamma csv", senderKey: "orders@gamma.test", format: 6, status: 3, version: 2, confirmedCount: 0, correctedCount: 2 },
];

const PROFILE = {
  id: "p-active",
  name: "Beta pdf",
  senderKey: "@beta.test",
  format: 3,
  status: 2,
  version: 3,
  origin: 1,
  modelId: "claude-opus-5-5",
  confirmedCount: 7,
  correctedCount: 0,
  lastUsedAt: "2026-11-02T10:00:00Z",
  sourceDocumentId: "doc-1",
  sourceInboundMessageId: "msg-1",
  concurrencyStamp: "c1",
  creationTime: "2026-10-30T10:00:00Z",
  signature: ["Ordernummer", "Laadadres"],
  rules: {
    orders: { scope: { kind: 2, value: null, groupBy: ["Ordernr"] } },
    lines: { scope: { kind: 2, value: null } },
    stops: [
      { role: 1, scope: { kind: 1, value: null } },
      { role: 2, scope: { kind: 1, value: null } },
    ],
    sheet: "Orders",
    headerRow: 1,
    fields: [
      { target: "order.customerReference", source: { kind: 2, value: "B7" }, transforms: [{ kind: 1 }] },
      { target: "lines[0].stops[0].date", source: { kind: 4, value: "Laaddatum" }, transforms: [{ kind: 2, format: "dd-MM-yyyy", culture: "nl-NL" }] },
      { target: "lines[0].transportUnitName", source: { kind: 5, value: "(\\w+)-pallet", group: 1 }, transforms: [{ kind: 5, map: { "EUR-pallet": "Europallet" } }] },
    ],
  },
};

describe("ImportProfilesView and the read-only rules (design 72)", () => {
  const VIEW = { "Integrations.ImportProfiles": true };
  const MANAGE = { ...VIEW, "Integrations.ImportProfiles.Manage": true };

  beforeEach(() => {
    pathname = "/integrations/import-profiles";
    routes.GET["/api/integrations/import-profile"] = () => ({ rows: PROFILE_ROWS, totalCount: 3, filteredCount: 3 });
    routes.GET["/api/integrations/import-profile/{id}"] = () => PROFILE;
  });

  it("is not rendered with the feature off; needs Integrations.ImportProfiles; no New command (profiles are learned)", async () => {
    grants = MANAGE;
    features = { ...INTAKE_FEATURES, "Integrations.OrderIntake": "false" };
    const { unmount } = renderWith(<ImportProfilesView />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
    unmount();
    features = { ...INTAKE_FEATURES };
    renderWith(<ImportProfilesView />);
    await waitFor(() => expect(rows()).toHaveLength(3));
    expect(document.querySelector("[data-grid-id='integrations.importProfiles']")).not.toBeNull();
    expect(screen.queryByText(/New/)).toBeNull();
  });

  it("Activate and Retire only with Manage, per status: not Activate when active, not Retire when retired", async () => {
    grants = VIEW;
    const { unmount } = renderWith(<ImportProfilesView />);
    await waitFor(() => expect(rows()).toHaveLength(3));
    for (const index of [0, 1, 2]) expect(rowActions(index)).toEqual(["open"]);
    unmount();

    grants = MANAGE;
    renderWith(<ImportProfilesView />);
    await waitFor(() => expect(rows()).toHaveLength(3));
    expect(rowActions(0)).toEqual(["edit", "activate", "retire", "delete"]);
    expect(rowActions(1)).toEqual(["edit", "retire", "delete"]);
    expect(rowActions(2)).toEqual(["edit", "activate", "delete"]);
    await click(rowAction(0, "activate"));
    await waitFor(() => expect(lastCall(POST, "/api/integrations/import-profile/{id}/activate")?.params?.path).toEqual({ id: "p-proposed" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Integrations:ImportProfileActivated"));
    await click(rowAction(1, "retire"));
    await waitFor(() => expect(lastCall(POST, "/api/integrations/import-profile/{id}/retire")?.params?.path).toEqual({ id: "p-active" }));
  });

  it("the status filter sends the chosen statuses", async () => {
    grants = VIEW;
    renderWith(<ImportProfilesView />);
    await waitFor(() => expect(calls(GET, "/api/integrations/import-profile")).toHaveLength(1));
    fireEvent.click(screen.getByRole("checkbox", { name: "Integrations:ImportProfileStatus.Active" }));
    await waitFor(() => expect(lastCall(GET, "/api/integrations/import-profile")?.params?.query?.Status).toEqual([2]));
  });

  it("?open={id} (a link from the trace) opens that profile and removes the parameter", async () => {
    grants = VIEW;
    search = { open: "p-active" };
    renderWith(<ImportProfilesView />);
    await waitFor(() => expect(lastCall(GET, "/api/integrations/import-profile/{id}")?.params?.path).toEqual({ id: "p-active" }));
    const call = navigateMock.mock.calls[0][0] as { replace: boolean; search: (previous: Record<string, unknown>) => Record<string, unknown> };
    expect(call.replace).toBe(true);
    expect(call.search({ open: "p-active", a: "b" })).toEqual({ a: "b" });
  });

  it("the editor shows the rules as field ← source → transforms, the structure and the signature", async () => {
    grants = MANAGE;
    renderWith(<ImportProfileEditor id="p-active" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    await waitFor(() => expect(screen.getByDisplayValue("Beta pdf")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("tab", { name: /Integrations:Rules/ }));
    const rules = panel("Integrations:Rules");
    await waitFor(() => expect(within(rules).getAllByRole("row")).toHaveLength(4));
    const cells = within(rules)
      .getAllByRole("row")
      .slice(1)
      .map((row) => within(row).getAllByRole("cell").map((cell) => cell.textContent));
    expect(cells).toEqual([
      ["order.customerReference", "← cell B7", "→ trim"],
      ["lines[0].stops[0].date", "← label Laaddatum", "→ date dd-MM-yyyy nl-NL"],
      ["lines[0].transportUnitName", "← regex (\\w+)-pallet (Integrations:RuleGroup)", "→ map EUR-pallet → Europallet"],
    ]);
    expect(rules.textContent).toContain("rowGroup [Ordernr]");
    expect(rules.textContent).toContain("Orders:IntakeStopRole.Pickup");
    expect(rules.textContent).toContain("Orders:IntakeStopRole.Delivery");
    expect(within(rules).getByText("Ordernummer")).toBeInTheDocument();
    expect(within(rules).getByText("Laadadres")).toBeInTheDocument();
    /* Read-only: the rules have no inputs. */
    expect(rules.querySelectorAll("input, select, textarea")).toHaveLength(0);
  });

  it("the editor's Activate / Retire ribbon follows the status and the Manage permission", async () => {
    grants = MANAGE;
    const { unmount } = renderWith(<ImportProfileEditor id="p-active" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    await waitFor(() => expect(ribbonButton("Integrations:Retire")).not.toBeNull());
    expect(ribbonButton("Integrations:Activate")).toBeNull();
    await click(ribbonButton("Integrations:Retire")!);
    await waitFor(() => expect(lastCall(POST, "/api/integrations/import-profile/{id}/retire")?.params?.path).toEqual({ id: "p-active" }));
    unmount();

    grants = VIEW;
    renderWith(<ImportProfileEditor id="p-active" readOnly open onClose={() => {}} onSaved={() => {}} />);
    await waitFor(() => expect(panel("Integrations:Rules")).toBeTruthy());
    await waitFor(() => expect(screen.getByDisplayValue("Beta pdf")).toBeInTheDocument());
    expect(ribbonButton("Integrations:Retire")).toBeNull();
    expect(ribbonButton("Integrations:Activate")).toBeNull();
  });

  it("links to the trace of the document it was learned from only when the user may open the trace", async () => {
    grants = { ...VIEW, "Integrations.InboundMessages": true };
    const { unmount } = renderWith(<ImportProfileEditor id="p-active" readOnly open onClose={() => {}} onSaved={() => {}} />);
    const link = await screen.findByRole("link", { name: "Integrations:ViewTrace" });
    expect(link).toHaveAttribute("href", "/integrations/received/msg-1");
    unmount();

    grants = VIEW;
    renderWith(<ImportProfileEditor id="p-active" readOnly open onClose={() => {}} onSaved={() => {}} />);
    expect(await screen.findByText("Integrations:TraceUnavailable")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Integrations:ViewTrace" })).toBeNull();
  });
});
