import { APPS, type AppView } from "@/app/apps.config";

/* The saved filters of the Transport lists (design 61; UI-PAGE-004): nav children of the Orders and Trips nodes in
 * apps.config.ts that route to the list with `?filter=`. The values here are the URL values; the screens map them to
 * the API's `view` parameter (Today, Tomorrow, ThisWeek, Open, Drafts, All; design 58). */

export const FILTER_SEARCH_KEY = "filter";

export const transportApp = APPS.find((app) => app.id === "transport")!;

function listNode(path: string): AppView {
  const node = transportApp.views?.find((view) => view.path === path);
  if (!node) throw new Error(`apps.config.ts has no Transport node at ${path}`);
  return node;
}

/** The filter values of a list, in nav order; the first is the list's default. */
export function filtersOf(listPath: string): string[] {
  return (listNode(listPath).children ?? []).map((child) => child.search?.[FILTER_SEARCH_KEY]).filter((value): value is string => value != null);
}

export const ORDER_FILTERS = filtersOf("/transport/orders");
export const TRIP_FILTERS = filtersOf("/transport/trips");

/** The filter of a location's search, or undefined when absent or unknown for the list. */
export function filterOf(search: Record<string, unknown> | undefined, listPath: string): string | undefined {
  const value = search?.[FILTER_SEARCH_KEY];
  return typeof value === "string" && filtersOf(listPath).includes(value) ? value : undefined;
}
