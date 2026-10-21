import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

/* The Tags panel of the e-mail template editor (design 49, "Tags and default content", PO 2026-10-20). Covered: the
 * groups and tags of `GET /api/platform/email-template/tags?purpose=` rendered with their labels and monospace paths
 * (children indented); the filter (label, path, description; a matching child keeps its collection; a filter opens
 * every group); the group and panel collapse, the panel's state remembered in localStorage and harmless when the
 * store throws; the loading skeleton, the error state with Try again, the two empty states; the tooltip with the
 * description (the insert label when there is none); a click reports the snippet (a collection its loop block, a
 * child its own snippet) and does not take the focus; the rows are native buttons of type button, so Enter activates
 * them and never submits the form around them. The domain hook is real; the generated client is the mock.
 * Risk class: Medium (UI component; the catalogue is static, server-owned content). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { EmailTemplateTagsPanel, type EmailTemplateTagsPanelProps } from "@/apps/master-data/components/EmailTemplateTagsPanel";
import { ok } from "./platform-test-helpers";
import { TAG_GROUPS, deferred } from "./email-template-test-fixtures";

type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown> } }) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;

const TAGS_PATH = "/api/platform/email-template/tags";
const STORAGE_KEY = "opentms.emailTemplate.tagsCollapsed";

let tagsResponse: (query: Record<string, unknown> | undefined) => unknown;

beforeEach(() => {
  window.localStorage.clear();
  tagsResponse = () => ok(TAG_GROUPS);
  GET.mockReset().mockImplementation(async (path, init) => (path === TAGS_PATH ? tagsResponse(init?.params?.query) : ok([])));
});

afterEach(() => {
  vi.restoreAllMocks();
});

function renderPanel(props: Partial<EmailTemplateTagsPanelProps> = {}) {
  const onInsert = vi.fn();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const element = (overrides: Partial<EmailTemplateTagsPanelProps>) => (
    <QueryClientProvider client={client}>
      <EmailTemplateTagsPanel purpose={1} onInsert={onInsert} {...props} {...overrides} />
    </QueryClientProvider>
  );
  const utils = render(element({}));
  return { onInsert, ...utils, rerenderWith: (overrides: Partial<EmailTemplateTagsPanelProps>) => utils.rerender(element(overrides)) };
}

const section = () => document.querySelector<HTMLElement>("[data-slot=email-template-tags]")!;
const tagsRequests = () => GET.mock.calls.filter(([path]) => path === TAGS_PATH);
/** The row of a tag, found by its monospace path. */
const row = (path: string) => (screen.getByText(path, { selector: "code" }).closest("button") as HTMLButtonElement | null)!;
const queryRow = (path: string) => screen.queryByText(path, { selector: "code" })?.closest("button") ?? null;
const groupButton = (label: string) => screen.getByRole("button", { name: new RegExp(`^(Collapse|Expand) ${label}$`) });
const panelToggle = () => within(section()).getAllByRole("button").find((b) => b.getAttribute("aria-controls") === "email-template-tags-list")!;
const filterBox = () => screen.getByRole("searchbox", { name: "Platform:FilterTags" });
const visiblePaths = () => Array.from(section().querySelectorAll("code")).map((c) => c.textContent);
const openTooltips = () => Array.from(document.querySelectorAll("[data-slot=tooltip-content][data-open]")).map((el) => el.textContent);

const loaded = () => waitFor(() => expect(queryRow("company.name")).not.toBeNull());

describe("EmailTemplateTagsPanel — groups and tags", () => {
  it("asks the catalogue for the purpose and renders each group with its label and count, each tag with its label and monospace path; children are indented", async () => {
    renderPanel();
    await loaded();
    expect(section()).toHaveAttribute("aria-labelledby", "email-template-tags-heading");
    expect(screen.getByRole("heading", { name: "Platform:Tags" })).toBeInTheDocument();
    expect(screen.getByText("Platform:TagsHint")).toBeInTheDocument();
    expect(tagsRequests()).toEqual([[TAGS_PATH, { params: { query: { purpose: 1 } } }]]);

    expect(groupButton("Own company")).toHaveTextContent("2");
    expect(groupButton("Order")).toHaveTextContent("2");
    /* The collection row plus its two children. */
    expect(groupButton("Freight")).toHaveTextContent("3");
    expect(visiblePaths()).toEqual(["company.name", "company.email", "order.number", "order.reference", "freight", "freight.product", "freight.quantity"]);

    const name = row("company.name");
    expect(name).toHaveTextContent("Name");
    expect(within(name).getByText("company.name", { selector: "code" })).toHaveClass("font-mono");
    expect(name).not.toHaveClass("pl-6");
    expect(row("freight.product")).toHaveClass("pl-6");
    expect(row("freight")).not.toHaveClass("pl-6");
  });

  it("a changed purpose fetches that purpose's catalogue", async () => {
    const { rerenderWith } = renderPanel();
    await loaded();
    tagsResponse = () => ok([{ group: "invoice", label: "Invoice", tags: [{ path: "invoice.number", kind: 1, label: "Number", snippet: "{{ invoice.number }}" }] }]);
    rerenderWith({ purpose: 3 });
    await waitFor(() => expect(queryRow("invoice.number")).not.toBeNull());
    expect(tagsRequests().at(-1)).toEqual([TAGS_PATH, { params: { query: { purpose: 3 } } }]);
    expect(queryRow("company.name")).toBeNull();
  });
});

describe("EmailTemplateTagsPanel — filter", () => {
  it("narrows the rows by label, by path and by description, drops groups without a match, and clearing it restores everything", async () => {
    renderPanel();
    await loaded();

    fireEvent.change(filterBox(), { target: { value: "refer" } });
    expect(visiblePaths()).toEqual(["order.reference"]);
    expect(screen.queryByRole("button", { name: /Own company$/ })).toBeNull();

    fireEvent.change(filterBox(), { target: { value: "COMPANY.EM" } });
    expect(visiblePaths()).toEqual(["company.email"]);

    fireEvent.change(filterBox(), { target: { value: "own company's" } });
    expect(visiblePaths()).toEqual(["company.name"]);

    fireEvent.change(filterBox(), { target: { value: "" } });
    expect(visiblePaths()).toHaveLength(7);
  });

  it("a child that matches keeps its collection with only the matching children; a matching collection keeps all its children", async () => {
    renderPanel();
    await loaded();
    fireEvent.change(filterBox(), { target: { value: "product" } });
    expect(visiblePaths()).toEqual(["freight", "freight.product"]);
    expect(groupButton("Freight")).toHaveTextContent("2");

    fireEvent.change(filterBox(), { target: { value: "freight lines" } });
    expect(visiblePaths()).toEqual(["freight", "freight.product", "freight.quantity"]);
  });

  it("a filter opens a collapsed group; clearing it leaves the group as the user left it", async () => {
    renderPanel();
    await loaded();
    fireEvent.click(groupButton("Order"));
    expect(groupButton("Order")).toHaveAttribute("aria-expanded", "false");
    expect(queryRow("order.number")).toBeNull();

    fireEvent.change(filterBox(), { target: { value: "number" } });
    expect(groupButton("Order")).toHaveAttribute("aria-expanded", "true");
    expect(visiblePaths()).toEqual(["order.number"]);

    fireEvent.change(filterBox(), { target: { value: "" } });
    expect(groupButton("Order")).toHaveAttribute("aria-expanded", "false");
    expect(queryRow("order.number")).toBeNull();
    expect(queryRow("company.name")).not.toBeNull();
  });

  it("no match shows the no-match text, not the no-tags text", async () => {
    renderPanel();
    await loaded();
    fireEvent.change(filterBox(), { target: { value: "zzz" } });
    expect(screen.getByText("Platform:NoTagsMatch")).toBeInTheDocument();
    expect(screen.queryByText("Platform:NoTags")).toBeNull();
    expect(section().querySelectorAll("code")).toHaveLength(0);
  });
});

describe("EmailTemplateTagsPanel — collapse", () => {
  it("a group collapses and expands with aria-expanded and its list; the toggle does not take the focus", async () => {
    renderPanel();
    await loaded();
    const button = groupButton("Own company");
    expect(button).toHaveAttribute("aria-expanded", "true");
    expect(button).toHaveAttribute("aria-controls", "email-template-tags-company");
    expect(document.getElementById("email-template-tags-company")).not.toBeNull();
    /* preventDefault on mousedown: the caret stays in the field the next tag goes into. */
    expect(fireEvent.mouseDown(button)).toBe(false);
    fireEvent.click(button);
    expect(button).toHaveAttribute("aria-expanded", "false");
    expect(document.getElementById("email-template-tags-company")).toBeNull();
    expect(queryRow("company.name")).toBeNull();
    expect(queryRow("order.number")).not.toBeNull();
    fireEvent.click(button);
    expect(queryRow("company.name")).not.toBeNull();
  });

  it("the panel collapses, remembers it in localStorage, and a panel that opens collapsed fetches nothing until expanded", async () => {
    const first = renderPanel();
    await loaded();
    expect(panelToggle()).toHaveAttribute("aria-expanded", "true");
    expect(panelToggle()).toHaveAttribute("aria-label", "Collapse");
    fireEvent.click(panelToggle());
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe("1");
    expect(panelToggle()).toHaveAttribute("aria-expanded", "false");
    expect(panelToggle()).toHaveAttribute("aria-label", "Expand");
    expect(document.getElementById("email-template-tags-list")).toBeNull();
    expect(screen.queryByRole("searchbox")).toBeNull();
    /* The heading stays, so the user can find the panel again. */
    expect(screen.getByRole("heading", { name: "Platform:Tags" })).toBeInTheDocument();
    first.unmount();

    GET.mockClear();
    renderPanel();
    expect(panelToggle()).toHaveAttribute("aria-expanded", "false");
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(tagsRequests()).toEqual([]);

    fireEvent.click(panelToggle());
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe("0");
    await loaded();
    expect(tagsRequests()).toHaveLength(1);
  });

  it("a localStorage that throws (private mode, a full store) breaks neither rendering nor the toggle", async () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("SecurityError");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("QuotaExceededError");
    });
    renderPanel();
    await loaded();
    expect(panelToggle()).toHaveAttribute("aria-expanded", "true");
    fireEvent.click(panelToggle());
    expect(panelToggle()).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(panelToggle());
    expect(panelToggle()).toHaveAttribute("aria-expanded", "true");
    expect(queryRow("company.name")).not.toBeNull();
  });
});

describe("EmailTemplateTagsPanel — loading, error, empty", () => {
  it("shows a busy skeleton while the catalogue loads, then the groups", async () => {
    const response = deferred<unknown>();
    tagsResponse = () => response.promise;
    renderPanel();
    const busy = section().querySelector("[aria-busy=true]")!;
    expect(busy).not.toBeNull();
    expect(busy.querySelectorAll("[data-slot=skeleton]").length).toBeGreaterThan(0);
    expect(screen.queryByText("Platform:NoTags")).toBeNull();
    /* The filter box is there while loading: the panel's shape does not jump. */
    expect(filterBox()).toBeInTheDocument();

    await act(async () => {
      response.resolve(ok(TAG_GROUPS));
    });
    await loaded();
    expect(section().querySelector("[aria-busy=true]")).toBeNull();
  });

  it("a failed request shows the API's message with Try again, which fetches again and shows the groups", async () => {
    tagsResponse = () => ({ data: undefined, error: { error: { message: "Catalogue unavailable" } }, response: new Response(null, { status: 500 }) });
    renderPanel();
    const alert = await within(section()).findByRole("alert");
    expect(alert).toHaveTextContent("Catalogue unavailable");
    expect(section().querySelectorAll("code")).toHaveLength(0);

    tagsResponse = () => ok(TAG_GROUPS);
    fireEvent.click(within(alert).getByRole("button", { name: "Platform:RetryTags" }));
    await loaded();
    expect(tagsRequests()).toHaveLength(2);
    expect(within(section()).queryByRole("alert")).toBeNull();
  });

  it("an error without a message shows the generic text", async () => {
    tagsResponse = () => ({ data: undefined, error: {}, response: new Response(null, { status: 500 }) });
    renderPanel();
    expect(await within(section()).findByRole("alert")).toHaveTextContent("UnexpectedError");
  });

  it("a purpose without tags shows the no-tags text", async () => {
    tagsResponse = () => ok([]);
    renderPanel();
    expect(await screen.findByText("Platform:NoTags")).toBeInTheDocument();
    expect(section().querySelectorAll("code")).toHaveLength(0);
  });
});

describe("EmailTemplateTagsPanel — tooltip", () => {
  it("a focused row shows its description; a tag without one shows the insert label", async () => {
    renderPanel();
    await loaded();
    act(() => row("order.number").focus());
    await waitFor(() => expect(openTooltips()).toContain("The order number"));
    act(() => row("order.number").blur());

    act(() => row("company.email").focus());
    await waitFor(() => expect(openTooltips()).toContain("Platform:InsertTag"));
    act(() => row("company.email").blur());

    act(() => row("freight.quantity").focus());
    await waitFor(() => expect(openTooltips()).toContain("Platform:InsertTag"));
  });
});

describe("EmailTemplateTagsPanel — insert", () => {
  it("a click reports the tag's snippet: a value its {{ path }}, a collection its loop block, a child its own snippet", async () => {
    const { onInsert } = renderPanel();
    await loaded();
    fireEvent.click(row("company.email"));
    expect(onInsert).toHaveBeenLastCalledWith("{{ company.email }}");
    fireEvent.click(row("freight"));
    expect(onInsert).toHaveBeenLastCalledWith("{{ for line in freight }}\n{{ line.product }}\n{{ line.quantity }}\n{{ end }}");
    fireEvent.click(row("freight.product"));
    expect(onInsert).toHaveBeenLastCalledWith("{{ line.product }}");
    expect(onInsert).toHaveBeenCalledTimes(3);
  });

  it("a row does not take the focus on mousedown: the caret stays in the field the tag goes into", async () => {
    const { onInsert } = renderPanel();
    await loaded();
    const input = document.createElement("input");
    document.body.append(input);
    try {
      act(() => input.focus());
      expect(fireEvent.mouseDown(row("company.name"))).toBe(false);
      fireEvent.click(row("company.name"));
      expect(document.activeElement).toBe(input);
      expect(onInsert).toHaveBeenCalledWith("{{ company.name }}");
    } finally {
      input.remove();
    }
  });

  it("every row is a native button of type button: Enter activates it and never submits a form around the panel", async () => {
    /* jsdom has no activation behaviour (Enter on a button is the browser's click), so what is proven here is the
     * element: a <button type="button"> that the panel renders inside the editor's <form>. */
    const onSubmit = vi.fn((e: React.FormEvent) => e.preventDefault());
    const onInsert = vi.fn();
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <form onSubmit={onSubmit}>
          <EmailTemplateTagsPanel purpose={1} onInsert={onInsert} />
          <button type="submit">Save</button>
        </form>
      </QueryClientProvider>,
    );
    await loaded();
    const rows = Array.from(section().querySelectorAll("code")).map((code) => code.closest("button")!);
    expect(rows).toHaveLength(7);
    for (const button of rows) {
      expect(button.tagName).toBe("BUTTON");
      expect(button).toHaveAttribute("type", "button");
    }
    for (const button of within(section()).getAllByRole("button")) expect(button).toHaveAttribute("type", "button");

    act(() => row("order.number").focus());
    expect(document.activeElement).toBe(row("order.number"));
    fireEvent.keyDown(row("order.number"), { key: "Enter" });
    fireEvent.click(row("order.number"));
    expect(onInsert).toHaveBeenCalledWith("{{ order.number }}");
    expect(onSubmit).not.toHaveBeenCalled();
  });
});
