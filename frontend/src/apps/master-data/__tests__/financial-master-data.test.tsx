import { act, fireEvent, render, screen, within } from "@testing-library/react";
import type { ColDef } from "ag-grid-community";
import type { ComponentType, ReactNode } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The Financial master data editors (design 52): the VAT percentage editor (rates as validity rows: a rate that has
 * taken effect is read-only with a lock and cannot be removed, a new rate on an existing category must start after
 * today, the percentage range, the DTO with the country's ISO code) and the discount or surcharge editor (the currency
 * column only for amounts, the seven day toggles, never no day, the values and regions in the DTO), plus the value
 * formatting of the list and the client-side validity rule. The domain hooks are mocks; the pickers and the grid are
 * native stand-ins. Risk class: Medium (UI); the server tests prove the same rules. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: {} } } }),
}));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./run2-test-mocks")).MockReferenceCombobox }));

/* A stand-in LinesGrid that honours editable callbacks and renders the cell renderers (the lock of a fixed row). */
vi.mock("@/components/ui/lines-grid-lazy", () => ({
  LazyLinesGrid: function MockGrid<TRow extends { key: string }>(props: {
    rows: TRow[];
    onRowsChange: (rows: TRow[]) => void;
    columnDefs: ColDef<TRow>[];
    toolbar?: ReactNode;
    readOnly?: boolean;
    canRemoveRow?: (row: TRow) => boolean;
  }) {
    const { rows, onRowsChange, columnDefs, toolbar, readOnly, canRemoveRow } = props;
    return (
      <div data-slot="grid">
        {!readOnly && toolbar}
        <div data-slot="columns">
          {columnDefs.map((c) => (
            <span key={c.field as string} data-column={c.field as string}>
              {c.headerName}
            </span>
          ))}
        </div>
        {rows.map((row, index) => (
          <div key={row.key} data-slot="line" data-index={index}>
            {columnDefs.map((col) => {
              const field = col.field as keyof TRow & string;
              const value = row[field];
              const editable = typeof col.editable === "function" ? col.editable({ data: row } as never) : col.editable === true;
              if (editable && !readOnly) {
                const numeric = col.cellEditor === "agNumberCellEditor";
                return (
                  <input
                    key={field}
                    aria-label={`${col.headerName} ${index}`}
                    value={value == null ? "" : String(value)}
                    onChange={(e) =>
                      onRowsChange(rows.map((r) => (r === row ? { ...r, [field]: numeric ? (e.target.value === "" ? null : Number(e.target.value)) : e.target.value || null } : r)))
                    }
                  />
                );
              }
              const Renderer = col.cellRenderer as ComponentType<Record<string, unknown>> | undefined;
              return (
                <span key={field} data-cell={field}>
                  {Renderer ? <Renderer value={value} data={row} {...(col.cellRendererParams as object)} /> : String(value ?? "")}
                </span>
              );
            })}
            {!readOnly && (!canRemoveRow || canRemoveRow(row)) && (
              <button type="button" onClick={() => onRowsChange(rows.filter((r) => r !== row))}>
                Remove {index}
              </button>
            )}
          </div>
        ))}
      </div>
    );
  },
}));

const { createMutateAsync, updateMutateAsync, state, hooks } = vi.hoisted(() => {
  const create = vi.fn();
  const update = vi.fn();
  const shared: { detail: { data: unknown; isLoading: boolean; error: unknown } } = { detail: { data: undefined, isLoading: false, error: null } };
  return {
    createMutateAsync: create,
    updateMutateAsync: update,
    state: shared,
    hooks: (entity: string) => ({
      entity,
      useDetail: () => shared.detail,
      useLookup: () => ({ data: [{ id: "vat-high", code: "HIGH", name: "High" }], isLoading: false, error: null }),
      useCreate: () => ({ mutateAsync: create, isPending: false }),
      useUpdate: () => ({ mutateAsync: update, isPending: false }),
      useDelete: () => ({ mutateAsync: vi.fn(), isPending: false }),
    }),
  };
});
vi.mock("@/domains/financial/reference-data", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/domains/financial/reference-data")>()),
  vatPercentages: hooks("vatPercentages"),
  discountsOrSurcharges: hooks("discountsOrSurcharges"),
}));
vi.mock("@/domains/master-data/entities", () => ({
  countries: { useLookup: () => ({ data: [{ id: "c-nl", code: "NL", name: "Netherlands" }, { id: "c-be", code: "BE", name: "Belgium" }] }) },
  currencies: { useLookup: () => ({ data: [{ id: "cur-eur", code: "EUR", name: "Euro" }] }) },
  geographicalRegions: { useLookup: () => ({ data: [{ id: "r-1", name: "Randstad" }, { id: "r-2", name: "Limburg" }] }) },
}));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
});

beforeEach(() => {
  createMutateAsync.mockReset().mockResolvedValue({});
  updateMutateAsync.mockReset().mockResolvedValue({});
  grants = {};
  state.detail = { data: undefined, isLoading: false, error: null };
});

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { validityError } from "@/apps/master-data/components/validity-rows";
import { DiscountOrSurchargeEditor } from "@/apps/master-data/editors/DiscountOrSurchargeEditor";
import { VatPercentageEditor } from "@/apps/master-data/editors/VatPercentageEditor";
import { formatDiscountOrSurchargeValue } from "@/domains/financial/reference-data";
import { createFormatter } from "@/lib/format";

const wrap = (node: ReactNode) => <QueryClientProvider client={new QueryClient()}>{node}</QueryClientProvider>;

const inputByLabel = (label: string) => screen.getByLabelText(new RegExp(`^${label.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}`)) as HTMLInputElement;

const submit = () =>
  act(async () => {
    fireEvent.submit(document.querySelector("form")!);
    await Promise.resolve();
  });

const PAST = "2001-01-01";
const FUTURE = "2099-01-01";

describe("validityError", () => {
  it("needs a row, a date per row, each date once, and on an existing record only future rows besides the fixed ones", () => {
    const line = (validFrom: string | null, isFixed = false) => ({ key: `${validFrom}`, validFrom, isFixed });
    expect(validityError([], "create")).toBe("Financial:ValidityRowRequiredHint");
    expect(validityError([line(null)], "create")).toBe("MasterData:Required");
    expect(validityError([line(PAST), line(PAST)], "create")).toBe("Financial:DuplicateValidFromHint");
    expect(validityError([line(PAST)], "create", "2026-10-23")).toBeUndefined();
    expect(validityError([line(PAST, true), line("2026-10-23")], "edit", "2026-10-23")).toBe("Financial:FutureDateRequiredHint");
    expect(validityError([line(PAST, true), line("2026-10-24")], "edit", "2026-10-23")).toBeUndefined();
  });
});

describe("formatDiscountOrSurchargeValue", () => {
  const f = createFormatter("en", (k) => k);
  const t = (k: string) => (k === "Financial:PerKilometreSuffix" ? "/km" : k);
  it("formats by kind with the currency code, never a symbol", () => {
    expect(formatDiscountOrSurchargeValue(f, t, 2, 12.5, null)).toBe("12.5%");
    expect(formatDiscountOrSurchargeValue(f, t, 1, 25, "EUR")).toBe("25.00 EUR");
    expect(formatDiscountOrSurchargeValue(f, t, 3, 0.1275, "EUR")).toBe("0.1275 EUR/km");
    expect(formatDiscountOrSurchargeValue(f, t, 1, null, "EUR")).toBe("");
  });
});

describe("VatPercentageEditor", () => {
  const stored = {
    id: "v1",
    code: "HIGH",
    name: "High",
    names: { en: "High", nl: "Hoog" },
    kind: 1,
    countryCode: "NL",
    salesLedgerAccount: null,
    purchaseLedgerAccount: null,
    currentPercentage: 21,
    rates: [
      { validFrom: PAST, percentage: 19, isFixed: true },
      { validFrom: FUTURE, percentage: 23, isFixed: false },
    ],
    isDefault: true,
    isArchived: false,
    description: null,
    concurrencyStamp: "stamp-1",
    creationTime: "2026-10-22T10:00:00Z",
  };

  it("creates with the upper-cased code, the country's ISO code and the rates in date order", async () => {
    render(wrap(<VatPercentageEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} />));
    fireEvent.change(inputByLabel("MasterData:Code"), { target: { value: "high" } });
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "High" } });
    fireEvent.change(inputByLabel("MasterData:Country"), { target: { value: "NL" } });
    fireEvent.click(screen.getByRole("tab", { name: "Financial:Rates" }));
    fireEvent.click(screen.getByRole("button", { name: /Financial:AddRate/ }));
    fireEvent.click(screen.getByRole("button", { name: /Financial:AddRate/ }));
    fireEvent.change(screen.getByLabelText("MasterData:ValidFrom 0"), { target: { value: "2012-10-01" } });
    fireEvent.change(screen.getByLabelText("Financial:Percentage 0"), { target: { value: "21" } });
    fireEvent.change(screen.getByLabelText("MasterData:ValidFrom 1"), { target: { value: PAST } });
    fireEvent.change(screen.getByLabelText("Financial:Percentage 1"), { target: { value: "19" } });
    await submit();

    expect(createMutateAsync).toHaveBeenCalledTimes(1);
    expect(createMutateAsync.mock.calls[0][0]).toMatchObject({
      code: "HIGH",
      names: { en: "High" },
      kind: 1,
      countryCode: "NL",
      rates: [
        { validFrom: PAST, percentage: 19 },
        { validFrom: "2012-10-01", percentage: 21 },
      ],
    });
  });

  it("refuses a percentage above 100 or with three decimals", async () => {
    render(wrap(<VatPercentageEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} />));
    fireEvent.change(inputByLabel("MasterData:Code"), { target: { value: "X" } });
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "X" } });
    fireEvent.change(inputByLabel("MasterData:Country"), { target: { value: "NL" } });
    fireEvent.click(screen.getByRole("tab", { name: "Financial:Rates" }));
    fireEvent.click(screen.getByRole("button", { name: /Financial:AddRate/ }));
    fireEvent.change(screen.getByLabelText("Financial:Percentage 0"), { target: { value: "100.5" } });
    await submit();
    expect(createMutateAsync).not.toHaveBeenCalled();
    expect(screen.getByText("Financial:PercentageRangeHint")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Financial:Percentage 0"), { target: { value: "21.005" } });
    await submit();
    expect(createMutateAsync).not.toHaveBeenCalled();
  });

  it("shows a rate that has taken effect read-only with the lock, without Remove; a future rate stays editable", () => {
    state.detail = { data: stored, isLoading: false, error: null };
    render(wrap(<VatPercentageEditor mode="edit" id="v1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />));
    fireEvent.click(screen.getByRole("tab", { name: "Financial:Rates" }));

    const [fixed, future] = Array.from(document.querySelectorAll<HTMLElement>('[data-slot="line"]'));
    expect(within(fixed).getByRole("img", { name: "Financial:FixedRateTooltip" })).toBeInTheDocument();
    expect(within(fixed).queryByRole("textbox")).toBeNull();
    expect(within(fixed).queryByRole("button", { name: /Remove/ })).toBeNull();
    expect(within(future).getAllByRole("textbox")).toHaveLength(2);
    expect(within(future).getByRole("button", { name: /Remove/ })).toBeInTheDocument();
  });

  it("refuses a new rate dated today or earlier on an existing category and sends the fixed rate back unchanged", async () => {
    state.detail = { data: stored, isLoading: false, error: null };
    render(wrap(<VatPercentageEditor mode="edit" id="v1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />));
    fireEvent.click(screen.getByRole("tab", { name: "Financial:Rates" }));
    fireEvent.click(screen.getByRole("button", { name: /Financial:AddRate/ }));
    fireEvent.change(screen.getByLabelText("MasterData:ValidFrom 2"), { target: { value: "2020-01-01" } });
    fireEvent.change(screen.getByLabelText("Financial:Percentage 2"), { target: { value: "20" } });
    await submit();
    expect(updateMutateAsync).not.toHaveBeenCalled();
    expect(screen.getByText("Financial:FutureDateRequiredHint")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Remove 2" }));
    await submit();
    expect(updateMutateAsync).toHaveBeenCalledTimes(1);
    const { dto } = updateMutateAsync.mock.calls[0][0];
    expect(dto.concurrencyStamp).toBe("stamp-1");
    expect(dto.names).toEqual({ en: "High", nl: "Hoog" });
    expect(dto.rates).toEqual([
      { validFrom: PAST, percentage: 19 },
      { validFrom: FUTURE, percentage: 23 },
    ]);
  });
});

describe("DiscountOrSurchargeEditor", () => {
  const fillGeneral = () => {
    fireEvent.change(inputByLabel("MasterData:Code"), { target: { value: "diesel" } });
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "Diesel surcharge" } });
  };

  it("shows the currency column only for amounts", () => {
    render(wrap(<DiscountOrSurchargeEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} />));
    fireEvent.click(screen.getByRole("tab", { name: "Financial:Values" }));
    expect(document.querySelector('[data-column="currencyCode"]')).not.toBeNull();

    fireEvent.click(screen.getByRole("tab", { name: "General" }));
    fireEvent.change(inputByLabel("Financial:ValueKind"), { target: { value: "2" } });
    fireEvent.click(screen.getByRole("tab", { name: "Financial:Values" }));
    expect(document.querySelector('[data-column="currencyCode"]')).toBeNull();
    expect(document.querySelector('[data-column="value"]')).toHaveTextContent("Financial:Value (%)");
  });

  it("refuses no day of the week and an amount without a currency", async () => {
    render(wrap(<DiscountOrSurchargeEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} />));
    fillGeneral();
    for (const day of ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]) {
      fireEvent.click(screen.getByRole("button", { name: `Financial:Day.${{ Mon: "Monday", Tue: "Tuesday", Wed: "Wednesday", Thu: "Thursday", Fri: "Friday", Sat: "Saturday", Sun: "Sunday" }[day]}` }));
    }
    await submit();
    expect(createMutateAsync).not.toHaveBeenCalled();
    expect(screen.getByText("Financial:DaysOfWeekRequiredHint")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Financial:Day.Saturday" }));
    fireEvent.click(screen.getByRole("tab", { name: "Financial:Values" }));
    fireEvent.click(screen.getByRole("button", { name: /Financial:AddValue/ }));
    fireEvent.change(screen.getByLabelText("Financial:Value 0"), { target: { value: "25" } });
    await submit();
    expect(createMutateAsync).not.toHaveBeenCalled();
    expect(screen.getByText("Financial:CurrencyRequiredHint")).toBeInTheDocument();
  });

  it("creates with the days as flags, the values and the regions in one request", async () => {
    render(wrap(<DiscountOrSurchargeEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} />));
    fillGeneral();
    fireEvent.change(inputByLabel("Financial:ValueKind"), { target: { value: "3" } });
    fireEvent.click(screen.getByRole("button", { name: "Financial:Day.Sunday" }));
    fireEvent.click(screen.getByRole("tab", { name: "Financial:Values" }));
    fireEvent.click(screen.getByRole("button", { name: /Financial:AddValue/ }));
    fireEvent.change(screen.getByLabelText("MasterData:ValidFrom 0"), { target: { value: PAST } });
    fireEvent.change(screen.getByLabelText("Financial:Value 0"), { target: { value: "0.1275" } });
    fireEvent.change(screen.getByLabelText("MasterData:Currency 0"), { target: { value: "EUR" } });
    fireEvent.click(screen.getByRole("tab", { name: "Financial:Regions" }));
    fireEvent.click(screen.getByRole("button", { name: /Financial:AddRegion/ }));
    fireEvent.change(screen.getByLabelText("MasterData:GeographicalRegion 0"), { target: { value: "r-1" } });
    await submit();

    expect(createMutateAsync).toHaveBeenCalledTimes(1);
    expect(createMutateAsync.mock.calls[0][0]).toMatchObject({
      code: "DIESEL",
      valueKind: 3,
      daysOfWeek: 63,
      values: [{ validFrom: PAST, value: 0.1275, currencyCode: "EUR" }],
      regions: [{ geographicalRegionId: "r-1", appliesAt: 3 }],
    });
  });
});
