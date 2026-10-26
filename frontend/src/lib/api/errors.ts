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

/** The error of a generated-client call with the HTTP status attached: the ABP error body carries no status, and a
 * caller that must tell a concurrency refusal from a business-rule 409 needs it (`isConcurrencyConflict`). */
export function withStatus(error: unknown, response: Response | undefined): unknown {
  if (!error || typeof error !== "object" || !response) return error;
  return { ...(error as object), status: response.status };
}

/**
 * True for ABP's optimistic-concurrency refusal: someone else saved the record after it was loaded (the sent
 * concurrency stamp is stale). ABP answers it with 409 and an error without a code (`AbpDbConcurrencyException`);
 * the domain's own 409 refusals always carry their error code. Needs the status (`withStatus`).
 */
export function isConcurrencyConflict(error: unknown): boolean {
  if (!error || typeof error !== "object") return false;
  const e = error as { status?: unknown; error?: { code?: unknown } | null };
  if (e.status !== 409) return false;
  const code = e.error && typeof e.error === "object" ? e.error.code : undefined;
  return code == null || code === "" || (typeof code === "string" && /concurrency/i.test(code));
}
