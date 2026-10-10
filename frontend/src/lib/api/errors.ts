/* API error text for the UI (UI-FB-004). One place reads the ABP error shape:
 * `error.error.message`, then a problem-details `title`, then `detail`, else a
 * localized fallback. Features never parse error objects themselves. */
export function getApiErrorMessage(error: unknown, t: (key: string) => string): string {
  if (error && typeof error === "object") {
    const e = error as Record<string, unknown>;
    const abp = e["error"];
    if (abp && typeof abp === "object") {
      const message = (abp as Record<string, unknown>)["message"];
      if (typeof message === "string" && message.trim()) return message;
    }
    for (const key of ["title", "detail"] as const) {
      const value = e[key];
      if (typeof value === "string" && value.trim()) return value;
    }
  }
  return t("UnexpectedError");
}
