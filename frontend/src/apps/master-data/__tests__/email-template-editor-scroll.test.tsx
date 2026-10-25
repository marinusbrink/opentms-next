import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The e-mail template editor and its Tags panel under UI-DLG-006 (PO 2026-10-21, after the Tags panel scrolled the
 * whole dialog). The editor takes the fixed full height (panelScroll through MasterDataEditor); side by side (lg) its
 * General tab is a full-height row whose form column (the FormSection) and Tags panel scroll on their own; stacked
 * (narrow) the tab panel scrolls as a whole. In the Tags panel the caption, hint and filter stay fixed and only the
 * groups list scrolls; collapsed, the panel hugs its caption (lg:h-auto lg:self-start) instead of standing as a tall
 * empty card. The dialog body is overflow-hidden and nothing in the popup carries overflow-auto. The domain hooks are
 * real; the generated client, the Select and the lazy body editor are mocked (the body editor's own layout is covered
 * in html-body-field.test.tsx). Risk class: Medium (UI flow). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: { auth: { grantedPolicies: {} }, features: { values: {} }, localization: { languages: [] } },
  }),
}));

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});

/* The body editor is lazy Tiptap; a plain textarea stands in, so the layout around it is what is under test. */
vi.mock("@/apps/master-data/components/HtmlBodyField", () => ({
  HtmlBodyField: (props: { id: string; label: string; value: string; readOnly: boolean; onChange: (v: string) => void }) => (
    <textarea id={props.id} aria-label={props.label} value={props.value} readOnly={props.readOnly} onChange={(e) => props.onChange(e.target.value)} />
  ),
}));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { EmailTemplateTagsPanel } from "@/apps/master-data/components/EmailTemplateTagsPanel";
import { EmailTemplateEditor } from "@/apps/master-data/editors/EmailTemplateEditor";
import { TAG_GROUPS } from "./email-template-test-fixtures";
import { ok } from "./platform-test-helpers";

type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown> } }) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;

const TAGS_PATH = "/api/platform/email-template/tags";
const DEFAULTS_PATH = "/api/platform/email-template/defaults";
const GENERAL = "Platform:Tab.General";

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
});

let tagGroups: unknown;

beforeEach(() => {
  window.localStorage.clear();
  tagGroups = TAG_GROUPS;
  GET.mockReset().mockImplementation(async (path) => {
    if (path === TAGS_PATH) return ok(tagGroups);
    if (path === DEFAULTS_PATH) return ok({});
    return ok([]);
  });
});

afterEach(() => {
  vi.restoreAllMocks();
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}

const renderEditor = (props: Partial<React.ComponentProps<typeof EmailTemplateEditor>> = {}) =>
  renderWith(<EmailTemplateEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} {...props} />);

const classes = (el: Element) => (el.getAttribute("class") ?? "").split(/\s+/).filter(Boolean);
const SCROLL_TOKEN = /^(?:[a-z0-9-]+:)*overflow-(?:x-|y-)?(?:auto|scroll)$/;

const popup = () => document.body.querySelector('[role="dialog"]') as HTMLElement;
const body = () => popup().querySelector('[data-slot="entity-dialog-body"]') as HTMLElement;
const generalPanel = () => popup().querySelector(`[role="tabpanel"][aria-label="${GENERAL}"]`) as HTMLElement;
const formSection = () => popup().querySelector('[data-slot="form-section"][aria-label="Platform:EmailTemplate"]') as HTMLElement;
const tagsPanel = () => document.body.querySelector('[data-slot="email-template-tags"]') as HTMLElement | null;
const tagsList = () => document.getElementById("email-template-tags-list");
/** The groups list: the one <ul> in the panel that scrolls. */
const groupsList = () => tagsPanel()?.querySelector("ul") ?? null;
const scrollers = (root: Element) => [root, ...Array.from(root.querySelectorAll<HTMLElement>("*"))].filter((el) => classes(el).some((c) => SCROLL_TOKEN.test(c)));
const describeEl = (el: Element) => `<${el.tagName.toLowerCase()} class="${el.className}">`;

const tagsLoaded = () => waitFor(() => expect(groupsList()).not.toBeNull());

describe("EmailTemplateEditor – the dialog never scrolls; the form column and the Tags panel scroll (UI-DLG-006)", () => {
  it("takes the fixed full height through MasterDataEditor; the body is overflow-hidden and the General panel scrolls", async () => {
    renderEditor();
    await tagsLoaded();
    expect(classes(popup())).toEqual(expect.arrayContaining(["h-[92vh]", "max-h-[92vh]", "overflow-hidden"]));
    expect(classes(body())).toContain("overflow-hidden");
    expect(classes(body())).not.toContain("overflow-auto");
    expect(classes(generalPanel())).toEqual(expect.arrayContaining(["min-h-0", "overflow-y-auto"]));
    expect(generalPanel().parentElement).toBe(body());
  });

  it("the General tab is a full-height row at lg: form column and Tags panel side by side, each height-bounded and scrolling on its own", async () => {
    renderEditor();
    await tagsLoaded();

    /* MasterDataEditor's column: the form and its wrapper give the tab the panel's full height. */
    const form = generalPanel().querySelector("form")!;
    expect(classes(form)).toEqual(expect.arrayContaining(["h-full", "min-h-0"]));
    const root = formSection().parentElement!;
    expect(classes(root)).toEqual(expect.arrayContaining(["flex", "flex-col", "lg:h-full", "lg:min-h-0", "lg:flex-row"]));
    expect(classes(root.parentElement!)).toEqual(expect.arrayContaining(["flex", "h-full", "min-h-0", "flex-col"]));
    expect(root.parentElement!.parentElement).toBe(form);

    expect(classes(formSection())).toEqual(expect.arrayContaining(["min-w-0", "flex-1", "lg:min-h-0", "lg:overflow-y-auto"]));
    /* Stacked (narrow) the column has no height limit and does not scroll: the panel does. */
    expect(classes(formSection())).not.toContain("overflow-y-auto");
    expect(classes(formSection())).not.toContain("min-h-0");

    const panel = tagsPanel()!;
    expect(panel.parentElement).toBe(root);
    expect(classes(panel)).toEqual(expect.arrayContaining(["lg:h-full", "lg:min-h-0", "lg:w-1/3", "lg:shrink-0"]));
    expect(classes(panel)).not.toContain("lg:self-start");
    expect(classes(panel)).not.toContain("lg:h-auto");
  });

  it("the only scrolling elements of the popup are the tab panels, the form column and the tags list; nothing carries overflow-auto", async () => {
    renderEditor();
    await tagsLoaded();
    const expected = new Set<Element>([...Array.from(popup().querySelectorAll('[role="tabpanel"]')), formSection(), groupsList()!]);
    for (const el of scrollers(popup())) expect(expected.has(el), `unexpected scroll region ${describeEl(el)}`).toBe(true);
    expect(scrollers(popup())).toHaveLength(expected.size);
    for (const el of [popup(), ...Array.from(popup().querySelectorAll<HTMLElement>("*"))]) {
      expect(classes(el).includes("overflow-auto"), `overflow-auto on ${describeEl(el)}`).toBe(false);
    }
  });

  it("in read-only mode there is no Tags panel; the form column keeps its row and the dialog never scrolls", async () => {
    renderEditor({ mode: "edit", id: "et1", readOnly: true });
    await waitFor(() => expect(formSection()).not.toBeNull());
    expect(tagsPanel()).toBeNull();
    expect(classes(popup())).toContain("h-[92vh]");
    expect(classes(body())).toContain("overflow-hidden");
    expect(classes(formSection())).toEqual(expect.arrayContaining(["lg:min-h-0", "lg:overflow-y-auto"]));
  });
});

describe("EmailTemplateTagsPanel – the groups list is the scrolling element; caption, hint and filter stay fixed", () => {
  const renderPanel = (className?: string) => renderWith(<EmailTemplateTagsPanel purpose={1} onInsert={() => {}} className={className} />);
  const section = () => tagsPanel()!;

  it("the list scrolls inside a min-h-0 flex-1 column; the caption, the hint and the filter box are outside the list", async () => {
    renderPanel("lg:h-full lg:min-h-0");
    await tagsLoaded();

    const list = groupsList()!;
    expect(classes(list)).toEqual(expect.arrayContaining(["min-h-0", "flex-1", "overflow-y-auto"]));
    /* -m-1 p-1: the rows' focus rings keep their room inside the scroll container. */
    expect(classes(list)).toEqual(expect.arrayContaining(["-m-1", "p-1"]));
    expect(within(list).getAllByRole("button").length).toBeGreaterThan(0);

    /* The column between the section and the list lets the list shrink; neither it nor the section scrolls. */
    const column = tagsList()!;
    expect(list.parentElement).toBe(column);
    expect(classes(column)).toEqual(expect.arrayContaining(["flex", "min-h-0", "flex-1", "flex-col"]));
    expect(scrollers(section())).toEqual([list]);
    expect(classes(section())).toEqual(expect.arrayContaining(["flex", "flex-col", "lg:h-full", "lg:min-h-0"]));

    const heading = screen.getByRole("heading", { name: "Platform:Tags" });
    const hint = screen.getByText("Platform:TagsHint");
    const filter = screen.getByRole("searchbox", { name: "Platform:FilterTags" });
    for (const fixed of [heading, hint, filter]) {
      expect(list.contains(fixed), `${fixed.tagName} must stay fixed above the list`).toBe(false);
      expect(section().contains(fixed)).toBe(true);
    }
    expect(column.contains(heading), "the caption sits above the shrinking column").toBe(false);
    expect(column.contains(hint)).toBe(true);
    expect(column.contains(filter)).toBe(true);
  });

  it("collapsed, the panel hugs its caption: lg:h-auto lg:self-start replace the editor's lg:h-full; expanded again they go", async () => {
    renderPanel("lg:h-full lg:min-h-0 lg:w-1/3 lg:shrink-0");
    await tagsLoaded();
    const toggle = () => within(section()).getAllByRole("button").find((b) => b.getAttribute("aria-controls") === "email-template-tags-list")!;

    fireEvent.click(toggle());
    expect(toggle()).toHaveAttribute("aria-expanded", "false");
    expect(classes(section())).toEqual(expect.arrayContaining(["gap-0", "lg:h-auto", "lg:self-start", "lg:w-1/3", "lg:shrink-0"]));
    expect(classes(section())).not.toContain("lg:h-full");
    expect(tagsList()).toBeNull();
    expect(scrollers(section())).toEqual([]);

    fireEvent.click(toggle());
    expect(toggle()).toHaveAttribute("aria-expanded", "true");
    expect(classes(section())).toEqual(expect.arrayContaining(["lg:h-full", "lg:min-h-0", "gap-3"]));
    expect(classes(section())).not.toContain("lg:h-auto");
    expect(classes(section())).not.toContain("lg:self-start");
    await tagsLoaded();
  });

  it("a panel that opens collapsed (remembered) hugs its caption from the first render", () => {
    window.localStorage.setItem("opentms.emailTemplate.tagsCollapsed", "1");
    renderPanel("lg:h-full lg:min-h-0");
    expect(classes(section())).toEqual(expect.arrayContaining(["lg:h-auto", "lg:self-start"]));
    expect(classes(section())).not.toContain("lg:h-full");
    expect(tagsList()).toBeNull();
  });

  it("with no groups to show there is no scroll region: the empty text sits in the fixed column", async () => {
    tagGroups = [];
    renderPanel("lg:h-full lg:min-h-0");
    expect(await screen.findByText("Platform:NoTags")).toBeInTheDocument();
    expect(groupsList()).toBeNull();
    expect(scrollers(section())).toEqual([]);
    expect(tagsList()!.contains(screen.getByText("Platform:NoTags"))).toBe(true);
  });
});
