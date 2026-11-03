import { useCallback } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { transportOrderKeys } from "@/domains/orders/transport-orders";
import { apiClient } from "@/lib/api/client";
import { withStatus } from "@/lib/api/errors";
import type { components } from "@/lib/api/generated/schema";

/* The order intake on the Orders side (design 72 "Orders: intake, resolution, review"; route root
 * /api/orders/order-intake): the review queue, one intake with its resolution per field, resolve (create the order
 * with the clerk's picks, optionally confirmed), reject, and the learned aliases. Also the helpers of the canonical
 * model `OrderIntakeDto` every channel shares: the labels and values of its field paths (`order.customerReference`,
 * `lines[0].stops[1].address.postalCode`), used by the review and by Integrations' trace. */

type Schemas = components["schemas"];

export type OrderIntakeRow = Schemas["OpenTms.Orders.Intake.OrderIntakeRowDto"];
export type OrderIntakeDetail = Schemas["OpenTms.Orders.Intake.OrderIntakeDetailDto"];
export type OrderIntakePayload = Schemas["OpenTms.Orders.Intake.OrderIntakeDto"];
export type OrderIntakeLine = Schemas["OpenTms.Orders.Intake.OrderIntakeLineDto"];
export type OrderIntakeStop = Schemas["OpenTms.Orders.Intake.OrderIntakeStopDto"];
export type OrderIntakeFieldResolution = Schemas["OpenTms.Orders.Intake.OrderIntakeFieldResolutionDto"];
export type OrderIntakeCandidate = Schemas["OpenTms.Orders.Intake.OrderIntakeCandidateDto"];
export type OrderIntakeStatus = Schemas["OpenTms.Orders.Intake.OrderIntakeStatus"];
export type OrderIntakeResolveDto = Schemas["OpenTms.Orders.Intake.OrderIntakeResolveDto"];
export type IntakeAlias = Schemas["OpenTms.Orders.Intake.IntakeAliasDto"];
export type IntakeAliasKind = Schemas["OpenTms.Orders.Intake.IntakeAliasKind"];

/* ── Enums: value → name; the localization key is `Orders:<Enum>.<name>` ────────────────────────────────────── */

export const ORDER_INTAKE_STATUSES = { 1: "NeedsReview", 2: "OrderCreated", 3: "Rejected" } as const;
export const INTAKE_RESOLUTION_STATUSES = { 1: "Resolved", 2: "Ambiguous", 3: "Unresolved" } as const;
export const INTAKE_RESOLUTION_METHODS = { 1: "Given", 2: "Exact", 3: "Alias", 4: "Match", 5: "Default", 6: "Pattern", 7: "User" } as const;
export const INTAKE_ALIAS_KINDS = { 1: "Customer", 2: "Address", 3: "OrderType", 4: "WaypointType", 5: "TransportUnit", 6: "Product", 7: "Temperature" } as const;
export const INTAKE_STOP_ROLES = { 1: "Pickup", 2: "Delivery" } as const;

/** The localized label of an Orders intake enum value: `Orders:<enumKey>.<name>`. */
export function intakeEnumLabel(t: (key: string) => string, enumKey: string, map: Readonly<Record<number, string>>, value: number | null | undefined): string {
  const name = value == null ? undefined : map[value];
  return name ? t(`Orders:${enumKey}.${name}`) : value == null ? "" : String(value);
}

/** The status name a StatusBadge takes (entity "OrderIntake"). */
export function orderIntakeStatusName(status: OrderIntakeStatus | null | undefined): string | undefined {
  return status == null ? undefined : ORDER_INTAKE_STATUSES[status];
}

/* ── Field paths ─────────────────────────────────────────────────────────────────────────────────────────────── */

type PathToken = { kind: "field"; name: string } | { kind: "index"; name: string; index: number };

/** `lines[0].stops[1].address.postalCode` → tokens; a leading `order.` names the intake itself and is dropped. */
export function parseFieldPath(path: string): PathToken[] {
  return path
    .split(".")
    .filter((part, i) => !(i === 0 && part === "order"))
    .map((part): PathToken => {
      const match = /^([a-zA-Z]+)\[(\d+)\]$/.exec(part);
      return match ? { kind: "index", name: match[1], index: Number(match[2]) } : { kind: "field", name: part };
    });
}

/** The value at a field path of an intake, or undefined. */
export function valueAtPath(payload: unknown, path: string): unknown {
  let current: unknown = payload;
  for (const token of parseFieldPath(path)) {
    if (current == null || typeof current !== "object") return undefined;
    const next = (current as Record<string, unknown>)[token.name];
    current = token.kind === "index" ? (Array.isArray(next) ? next[token.index] : undefined) : next;
  }
  return current;
}

/** A copy of the intake with `value` at the path (objects on the way are created; arrays must exist). */
export function withValueAtPath<T>(payload: T, path: string, value: unknown): T {
  const tokens = parseFieldPath(path);
  const root = structuredClone(payload ?? {}) as Record<string, unknown>;
  let current: Record<string, unknown> = root;
  tokens.forEach((token, i) => {
    const last = i === tokens.length - 1;
    if (token.kind === "index") {
      const list = (current[token.name] as unknown[] | undefined) ?? [];
      current[token.name] = list;
      if (last) {
        list[token.index] = value;
        return;
      }
      list[token.index] = (list[token.index] as Record<string, unknown> | undefined) ?? {};
      current = list[token.index] as Record<string, unknown>;
    } else if (last) {
      current[token.name] = value;
    } else {
      current[token.name] = (current[token.name] as Record<string, unknown> | undefined) ?? {};
      current = current[token.name] as Record<string, unknown>;
    }
  });
  return root as T;
}

/**
 * Every filled value of an intake as field path → value, in the paths the provenance and the resolution use
 * (`order.customer.name`, `order.references[0].value`, `lines[0].stops[1].address.postalCode`). A stop's role is part of
 * its label, not a value of its own.
 */
export function flattenIntake(payload: OrderIntakePayload | null | undefined): { path: string; value: unknown }[] {
  const out: { path: string; value: unknown }[] = [];
  const walk = (value: unknown, path: string) => {
    if (value == null || value === "") return;
    if (Array.isArray(value)) {
      value.forEach((item, index) => walk(item, `${path}[${index}]`));
      return;
    }
    if (typeof value === "object") {
      for (const [key, child] of Object.entries(value as Record<string, unknown>)) {
        if (key === "role") continue;
        walk(child, path ? `${path}.${key}` : key);
      }
      return;
    }
    out.push({ path, value });
  };
  for (const [key, value] of Object.entries(payload ?? {})) {
    walk(value, key === "lines" ? "lines" : `order.${key}`);
  }
  return out;
}

/** A value as text for a table cell or an input: numbers in the invariant form the model holds. */
export function fieldValueText(value: unknown): string {
  if (value == null) return "";
  if (typeof value === "object") return "";
  return String(value);
}

/**
 * The label of a field path (design 72, the trace's and the review's field column): the field's name, prefixed by its
 * line and stop ("Line 1 · Pickup · Postal code"). Names come from `Orders:IntakeField.<name>`; an unknown name shows
 * as it is, so a new field is visible rather than hidden.
 */
export function fieldPathLabel(t: (key: string) => string, path: string, payload?: OrderIntakePayload | null): string {
  const label = (name: string) => {
    const key = `Orders:IntakeField.${name}`;
    const text = t(key);
    return text === key ? name : text;
  };
  const parts: string[] = [];
  const tokens = parseFieldPath(path);
  let lineIndex: number | undefined;
  const fieldNames: string[] = [];
  for (const token of tokens) {
    if (token.kind === "index" && token.name === "lines") {
      lineIndex = token.index;
      parts.push(t("Orders:IntakeLineN").replace("{0}", String(token.index + 1)));
    } else if (token.kind === "index" && token.name === "stops") {
      const role = lineIndex != null ? payload?.lines?.[lineIndex]?.stops?.[token.index]?.role : undefined;
      parts.push(role ? intakeEnumLabel(t, "IntakeStopRole", INTAKE_STOP_ROLES, role) : t("Orders:IntakeStopN").replace("{0}", String(token.index + 1)));
    } else if (token.kind === "index") {
      parts.push(`${label(token.name)} ${token.index + 1}`);
    } else {
      fieldNames.push(token.name);
    }
  }
  if (fieldNames.length > 0) parts.push(label(fieldNames.join(".")));
  return parts.join(" · ");
}

/* ── Query keys ──────────────────────────────────────────────────────────────────────────────────────────────── */

export const orderIntakeKeys = {
  all: ["orders", "orderIntakes"] as const,
  detail: (id: string | undefined) => ["orders", "orderIntakes", "detail", id?.toLowerCase()] as const,
  aliases: ["orders", "orderIntakes", "aliases"] as const,
};

/* ── List ────────────────────────────────────────────────────────────────────────────────────────────────────── */

export interface OrderIntakeListFilters {
  status?: OrderIntakeStatus[];
  receivedFrom?: string;
  receivedTo?: string;
}

const toPage = <TRow,>(data: { rows?: TRow[] | null; totalCount?: number; filteredCount?: number } | undefined): GridResponse<TRow> => ({
  rows: data?.rows ?? [],
  totalCount: Number(data?.totalCount ?? 0),
  filteredCount: Number(data?.filteredCount ?? 0),
});

export async function fetchOrderIntakes(request: GridRequest, filters: OrderIntakeListFilters): Promise<GridResponse<OrderIntakeRow>> {
  const sort = request.sortModels[0];
  const { data, error } = await apiClient.GET("/api/orders/order-intake", {
    params: {
      query: {
        StartRow: request.startRow,
        EndRow: request.endRow,
        WildcardSearch: request.wildcardSearch ?? undefined,
        SortBy: sort?.colId,
        SortDir: sort?.sort,
        Status: filters.status?.length ? filters.status : undefined,
        ReceivedFrom: filters.receivedFrom || undefined,
        ReceivedTo: filters.receivedTo || undefined,
      },
    },
  });
  if (error) throw error;
  return toPage<OrderIntakeRow>(data);
}

/** The grid fetcher of the Intake list; stable while the filters are the same. */
export function useOrderIntakesGridFetcher(filters: OrderIntakeListFilters, refreshKey = 0) {
  const key = JSON.stringify(filters);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  return useCallback((request: GridRequest) => fetchOrderIntakes(request, JSON.parse(key) as OrderIntakeListFilters), [key, refreshKey]);
}

/* ── Record ──────────────────────────────────────────────────────────────────────────────────────────────────── */

export function useOrderIntake(id: string | undefined) {
  return useQuery({
    queryKey: orderIntakeKeys.detail(id),
    enabled: id != null,
    queryFn: async (): Promise<OrderIntakeDetail> => {
      const { data, error, response } = await apiClient.GET("/api/orders/order-intake/{id}", { params: { path: { id: id! } } });
      if (error) throw withStatus(error, response);
      return data!;
    },
  });
}

function useIntakeMutation<TArgs>(run: (args: TArgs) => Promise<OrderIntakeDetail>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: (dto) => {
      if (dto?.id) queryClient.setQueryData(orderIntakeKeys.detail(dto.id), dto);
      void queryClient.invalidateQueries({ queryKey: orderIntakeKeys.all });
      void queryClient.invalidateQueries({ queryKey: transportOrderKeys.list() });
    },
  });
}

/** Create order / Create and confirm: re-runs the resolver with the picks (every pick on an open field becomes an alias). */
export function useResolveOrderIntake() {
  return useIntakeMutation(async ({ id, dto }: { id: string; dto: OrderIntakeResolveDto }) => {
    const { data, error, response } = await apiClient.POST("/api/orders/order-intake/{id}/resolve", { params: { path: { id } }, body: dto });
    if (error) throw withStatus(error, response);
    return data!;
  });
}

export function useRejectOrderIntake() {
  return useIntakeMutation(async ({ id, reason }: { id: string; reason: string }) => {
    const { data, error, response } = await apiClient.POST("/api/orders/order-intake/{id}/reject", { params: { path: { id } }, body: { reason } });
    if (error) throw withStatus(error, response);
    return data!;
  });
}

/* ── Aliases ─────────────────────────────────────────────────────────────────────────────────────────────────── */

export async function fetchIntakeAliases(request: GridRequest, kind?: IntakeAliasKind): Promise<GridResponse<IntakeAlias>> {
  const sort = request.sortModels[0];
  const { data, error } = await apiClient.GET("/api/orders/order-intake/aliases", {
    params: { query: { StartRow: request.startRow, EndRow: request.endRow, WildcardSearch: request.wildcardSearch ?? undefined, SortBy: sort?.colId, SortDir: sort?.sort, Kind: kind } },
  });
  if (error) throw error;
  return toPage<IntakeAlias>(data);
}

export function useDeleteIntakeAlias() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (aliasId: string) => {
      const { error } = await apiClient.DELETE("/api/orders/order-intake/aliases/{aliasId}", { params: { path: { aliasId } } });
      if (error) throw error;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: orderIntakeKeys.aliases }),
  });
}
