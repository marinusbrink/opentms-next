/* Browser helpers for commands that leave the SPA: a file the server returned, saved as a download, and a URL the
 * server returns opened in a new tab. Features never create object URLs or call window.open themselves. */

/**
 * The file name of a Content-Disposition header: the RFC 5987 `filename*` (UTF-8, percent-encoded) when present, else
 * the plain `filename` (quoted or a token). Null without a header or a name.
 *
 * Cross-origin (the dev SPA on :5173, the API on :44301) the browser only exposes the header when the API lists it in
 * Access-Control-Expose-Headers; callers therefore always pass a fallback name to `saveBlob`.
 */
export function fileNameFromContentDisposition(header: string | null | undefined): string | null {
  if (!header) return null;
  const extended = /filename\*\s*=\s*([^']*)'[^']*'([^;]+)/i.exec(header);
  if (extended) {
    try {
      const name = decodeURIComponent(extended[2].trim().replace(/^"(.*)"$/, "$1"));
      if (name) return name;
    } catch {
      /* A malformed encoding: fall through to the plain filename. */
    }
  }
  const plain = /filename\s*=\s*("((?:\\.|[^"\\])*)"|[^;]+)/i.exec(header);
  if (!plain) return null;
  const name = (plain[2] !== undefined ? plain[2].replace(/\\(.)/g, "$1") : plain[1]).trim();
  return name || null;
}

const EXTENSIONS: Record<string, string> = {
  "application/pdf": ".pdf",
  "text/html": ".html",
  "application/zip": ".zip",
  "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet": ".xlsx",
};

/** A safe download name from a display name and a content type, for when the server's file name is not readable. */
export function fallbackFileName(name: string, contentType: string | null | undefined, defaultExtension = ".pdf"): string {
  const base = name.replace(/[\\/:*?"<>|]+/g, "_").trim() || "document";
  const extension = EXTENSIONS[(contentType ?? "").split(";")[0].trim().toLowerCase()] ?? defaultExtension;
  return base.toLowerCase().endsWith(extension) ? base : `${base}${extension}`;
}

/** Saves a blob as a download under `fileName` and releases its object URL afterwards. */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  try {
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.rel = "noopener";
    link.style.display = "none";
    document.body.appendChild(link);
    link.click();
    link.remove();
  } finally {
    /* The click starts the download synchronously; revoking on the next task keeps it valid until then. */
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }
}

export interface PendingTab {
  /** Sends the tab to the URL (or opens one when the browser refused the blank tab). */
  navigate: (url: string) => void;
  /** Closes the blank tab after a failure. */
  close: () => void;
}

/**
 * Opens a blank tab NOW, inside the click, for a URL that only arrives after a server call. Browsers only allow a
 * popup during the click's user activation; Safari ends it at the first await, so `window.open(url)` after the POST
 * would be blocked there. The tab is opened without "noopener" (with it, window.open returns null and the tab could
 * not be navigated later) and its opener is cut at once, which is what "noopener" does: the provider's page cannot
 * reach the SPA through window.opener. When the browser refused even the blank tab, `navigate` falls back to a
 * `noopener,noreferrer` window.open, which works where the activation is still valid.
 */
export function openPendingTab(): PendingTab {
  let tab: Window | null = null;
  try {
    tab = window.open("about:blank", "_blank");
  } catch {
    tab = null;
  }
  if (tab) {
    try {
      tab.opener = null;
    } catch {
      /* Some browsers make opener read-only for a blank tab; the provider's page is cross-origin either way. */
    }
  }
  return {
    navigate: (url) => {
      if (tab && !tab.closed) {
        tab.location.replace(url);
      } else {
        window.open(url, "_blank", "noopener,noreferrer");
      }
    },
    close: () => {
      if (tab && !tab.closed) tab.close();
    },
  };
}
