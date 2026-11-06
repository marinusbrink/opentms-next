import { useCallback, useMemo } from "react";
import { useLocation, useNavigate } from "@tanstack/react-router";
import { toast } from "sonner";

import { useAppFeatures } from "@/app/app-features";
import { useIsGrantedChecker } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Opening a record from anywhere (UI-DLG-007; design 61 "Library components built in this run").
 *
 * DEVIATION(UI-DLG-007): a record is a page route under its list (/transport/orders/{id}), not a `?open=` search
 * param with a global dialog host; DEVIATION(UI-PAGE-003): the record routes carry an `$id` param. Both approved in
 * design 61 (Assumption 1: "pages, not dialogs, the UI rule's intent"; New uses a dialog for the minimum fields and
 * then opens the page). Everything else of the rule holds: grids, links and other screens open records only through
 * `useOpenRecord`, a record URL can be reloaded and shared, and closing returns to the list.
 *
 * The list's filters live in the URL (`?filter=today`, UI-PAGE-004) and travel with the record route, so Back and
 * Close land on the list exactly as it was. A record the user may not view is not opened: `toast.error(PermissionDenied)`
 * (UI-PERM-004); a record whose app node is off is not opened either (UI-PERM-006). On the page itself, a record that
 * does not exist (deleted meanwhile, another tenant's id) renders `RecordNotFound`; the shell renders
 * `FeatureUnavailable` for a node that is off and the page `PermissionDenied` without the view permission.
 *
 * Design 74 §5: `openRecord(type, id, { returnTo })` puts the screen to come back to in the `returnTo` search param, and
 * `useCloseRecord` goes there instead of the list. The param is in the URL, so it survives a reload and travels with the
 * record's own navigation (a record opened from this record, New handing over to the created record). Only internal
 * paths count (`isInternalPath`): the URL can be shared, so it must not be an open redirect. */

export type RecordType = "TransportOrder" | "TransportOrderTemplate" | "TransportTrip" | "OrderIntake" | "InboundMessage";

export interface RecordRoute {
  /** The list the record belongs to; the record page is `${listPath}/${id}`. */
  listPath: string;
  /** The view permission of the record's list (UI-PERM-004). */
  permission: string;
  /** The app node the list belongs to (UI-PERM-006). */
  feature: string;
}

export const RECORD_ROUTES: Readonly<Record<RecordType, RecordRoute>> = {
  TransportOrder: { listPath: "/transport/orders", permission: "Orders.TransportOrders", feature: "Apps.Transport.Orders" },
  TransportOrderTemplate: { listPath: "/transport/templates", permission: "Orders.TransportOrderTemplates", feature: "Apps.Transport.Orders.Templates" },
  TransportTrip: { listPath: "/transport/trips", permission: "PlanningExecution.TransportTrips", feature: "Apps.Transport.Trips" },
  /* Design 72: the review page of an order intake (DEVIATION(UI-PAGE-003): a page route with an id, approved in
   * docs/designs/72-order-intake.md, "UI design": "Review page /transport/intake/{id}"). */
  OrderIntake: { listPath: "/transport/intake", permission: "Orders.OrderIntakes", feature: "Apps.Transport.Intake" },
  /* Design 72: the trace page of a received message (the same deviation, approved in docs/designs/72-order-intake.md,
   * "UI design": "the trace page /integrations/received/{messageId}"). */
  InboundMessage: { listPath: "/integrations/received", permission: "Integrations.InboundMessages", feature: "Apps.Integrations.Intake.Received" },
};

/** Design 72: the route that opens the trace of the message a received document belongs to (other apps know only the
 *  document); the Integrations app resolves it and replaces it with `/integrations/received/{messageId}`. */
export const DOCUMENT_TRACE_PREFIX = "/integrations/received/document";

/** The trace route of a received document: `/integrations/received/document/{documentId}`. */
export function documentTracePath(documentId: string): string {
  return `${DOCUMENT_TRACE_PREFIX}/${encodeURIComponent(documentId)}`;
}

export interface RecordRef {
  type: RecordType;
  id: string;
}

/** The page route of a record: `/transport/orders/{id}`. */
export function recordPath(type: RecordType, id: string): string {
  return `${RECORD_ROUTES[type].listPath}/${encodeURIComponent(id)}`;
}

/** The record a pathname shows, or undefined for a list or any other page. */
export function parseRecordPath(pathname: string): RecordRef | undefined {
  const clean = pathname.replace(/\/+$/, "");
  for (const [type, route] of Object.entries(RECORD_ROUTES) as [RecordType, RecordRoute][]) {
    if (!clean.startsWith(`${route.listPath}/`)) continue;
    const rest = clean.slice(route.listPath.length + 1);
    if (rest.length > 0 && !rest.includes("/")) return { type, id: decodeURIComponent(rest) };
  }
  return undefined;
}

export interface OpenRecordOptions {
  /** Replace the current history entry (a new record's page handing over to the record it created). */
  replace?: boolean;
  /** Search params that belong to the page being left, not to the record (the new order's `type`). */
  omitSearch?: string[];
  /** Where closing the record goes instead of its list (design 74 §5); ignored unless it is an internal path. */
  returnTo?: string;
}

/** The search param that carries where closing a record goes (design 74 §5). */
export const RETURN_TO_PARAM = "returnTo";

/** Whether a path stays on this origin: it starts with one `/`, and the URL parser agrees (it also reads a backslash or an
 *  embedded tab after that `/` as the start of another host). */
export function isInternalPath(path: unknown): path is string {
  if (typeof path !== "string" || !path.startsWith("/") || path.startsWith("//")) return false;
  const base = "http://internal.invalid";
  try {
    return new URL(path, base).origin === base;
  } catch {
    return false;
  }
}

/** The id a record route takes for a record that does not exist yet (design 61 "Order page, legacy tab structure" 9: New opens
 * the page directly, `/transport/orders/new`); Save creates the record and the route moves to its id. */
export const NEW_RECORD_ID = "new";

export type OpenRecord = (type: RecordType, id: string, options?: OpenRecordOptions) => boolean;

/** The current location's search, kept as is when moving between a list and its records. */
const keepSearch = (previous: Record<string, unknown>) => previous;

/**
 * `const openRecord = useOpenRecord(); openRecord("TransportOrder", id)` navigates to the record's page, keeping the
 * list's filters. Returns false, after a toast, when the user may not view the record or its node is off.
 */
export function useOpenRecord(): OpenRecord {
  const navigate = useNavigate();
  const { t } = useL();
  const isGranted = useIsGrantedChecker();
  const features = useAppFeatures();

  return useCallback(
    (type, id, options) => {
      const route = RECORD_ROUTES[type];
      if (!isGranted(route.permission)) {
        toast.error(t("PermissionDenied"));
        return false;
      }
      if (features.isLoaded && !features.isFeatureEnabled(route.feature)) {
        toast.error(t("FeatureUnavailable"));
        return false;
      }
      const omit = options?.omitSearch ?? [];
      const returnTo = isInternalPath(options?.returnTo) ? options.returnTo : undefined;
      const search =
        omit.length === 0 && !returnTo
          ? keepSearch
          : (previous: Record<string, unknown>) => ({
              ...Object.fromEntries(Object.entries(previous).filter(([key]) => !omit.includes(key))),
              ...(returnTo ? { [RETURN_TO_PARAM]: returnTo } : {}),
            });
      void navigate({ to: recordPath(type, id), search, replace: options?.replace });
      return true;
    },
    [features, isGranted, navigate, t],
  );
}

/**
 * `openNewRecord("TransportOrder", { type: typeId })` opens the page of a record that does not exist yet, keeping the
 * list's filters; the extra search params (the order type of "New per type") travel with it. The page itself checks
 * the Create permission.
 */
export function useOpenNewRecord(): (type: RecordType, extra?: Record<string, string | undefined>) => void {
  const navigate = useNavigate();
  return useCallback(
    (type, extra) => {
      void navigate({
        to: recordPath(type, NEW_RECORD_ID),
        search: (previous: Record<string, unknown>) => {
          const next: Record<string, unknown> = { ...previous };
          for (const [key, value] of Object.entries(extra ?? {})) {
            if (value == null) delete next[key];
            else next[key] = value;
          }
          return next;
        },
      });
    },
    [navigate],
  );
}

/** Closing a record page: to where it was opened from when that was passed (`returnTo`, design 74 §5), else back to its
 *  list with the filters the list had. A list never keeps the param. */
export function useCloseRecord(): (type: RecordType) => void {
  const navigate = useNavigate();
  const { search } = useLocation();
  const returnTo = (search as Record<string, unknown> | undefined)?.[RETURN_TO_PARAM];
  return useCallback(
    (type) => {
      if (isInternalPath(returnTo)) {
        const target = new URL(returnTo, "http://internal.invalid");
        void navigate({ to: target.pathname, search: Object.fromEntries(target.searchParams) });
        return;
      }
      void navigate({
        to: RECORD_ROUTES[type].listPath,
        search: (previous: Record<string, unknown>) => Object.fromEntries(Object.entries(previous).filter(([key]) => key !== RETURN_TO_PARAM)),
      });
    },
    [navigate, returnTo],
  );
}

/** The record the current route shows, or undefined on a list. */
export function useCurrentRecord(): RecordRef | undefined {
  const { pathname } = useLocation();
  return useMemo(() => parseRecordPath(pathname), [pathname]);
}

export type RecordLoadState = "loading" | "ready" | "not-found" | "error";

/** Whether an API error says the record does not exist (ABP's EntityNotFound, or a 404 where the status is known). */
export function isNotFoundError(error: unknown): boolean {
  if (!error || typeof error !== "object") return false;
  const e = error as Record<string, unknown>;
  const status = e["status"] ?? (e["response"] as Record<string, unknown> | undefined)?.["status"];
  if (status === 404) return true;
  const abp = e["error"];
  if (abp && typeof abp === "object") {
    const code = (abp as Record<string, unknown>)["code"];
    if (typeof code === "string" && /EntityNotFound/i.test(code)) return true;
  }
  return false;
}

/** The state of a record page from its detail query (UI-PAGE-005: loading, not found, error, ready). */
export function recordLoadState(query: { isLoading: boolean; error: unknown; data: unknown }): RecordLoadState {
  if (query.data != null) return "ready";
  if (query.isLoading) return "loading";
  if (query.error != null) return isNotFoundError(query.error) ? "not-found" : "error";
  return "not-found";
}
