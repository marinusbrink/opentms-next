import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { fallbackFileName, fileNameFromContentDisposition, openPendingTab, saveBlob } from "@/lib/browser";

/* The browser helpers for commands that leave the SPA (design 55: Test document downloads a file, Edit template opens
 * the provider's editor in a new tab). Risk class: Medium (UI flow). openPendingTab carries a security property: the
 * provider's page must not reach the SPA through window.opener. */

describe("fileNameFromContentDisposition", () => {
  it.each([
    [null, null],
    [undefined, null],
    ["", null],
    ["attachment", null],
    ["inline", null],
    ['attachment; filename=""', null],
  ])("%j → %j (no name)", (header, expected) => {
    expect(fileNameFromContentDisposition(header)).toBe(expected);
  });

  it.each([
    ['attachment; filename="factuur 12.pdf"', "factuur 12.pdf"],
    ["attachment; filename=factuur-12.pdf", "factuur-12.pdf"],
    ["attachment; filename=factuur-12.pdf; size=1024", "factuur-12.pdf"],
    ['attachment;filename = "spaced.pdf" ', "spaced.pdf"],
    ['ATTACHMENT; FILENAME="upper.pdf"', "upper.pdf"],
    ['attachment; filename="a \\"quoted\\" name.pdf"', 'a "quoted" name.pdf'],
    ['attachment; filename="semi;colon.pdf"', "semi;colon.pdf"],
  ])("plain filename: %s → %s", (header, expected) => {
    expect(fileNameFromContentDisposition(header)).toBe(expected);
  });

  it.each([
    ["attachment; filename*=UTF-8''Factuur%20%E2%82%AC%2012.pdf", "Factuur € 12.pdf"],
    ["attachment; filename*=utf-8''r%C3%A9sum%C3%A9.pdf", "résumé.pdf"],
    ["attachment; filename*=UTF-8'nl'bestand.pdf", "bestand.pdf"],
  ])("RFC 5987 filename*: %s → %s", (header, expected) => {
    expect(fileNameFromContentDisposition(header)).toBe(expected);
  });

  it("prefers filename* over filename, whichever comes first (ASP.NET Core sends both)", () => {
    expect(fileNameFromContentDisposition("attachment; filename=Factuur_12.pdf; filename*=UTF-8''Factuur%20%E2%82%AC%2012.pdf")).toBe("Factuur € 12.pdf");
    expect(fileNameFromContentDisposition("attachment; filename*=UTF-8''echt%20bestand.pdf; filename=\"fallback.pdf\"")).toBe("echt bestand.pdf");
  });

  it("falls back to filename when filename* is malformed", () => {
    expect(fileNameFromContentDisposition("attachment; filename*=UTF-8''kapot%E2%82.pdf; filename=\"heel.pdf\"")).toBe("heel.pdf");
    expect(fileNameFromContentDisposition("attachment; filename*=UTF-8''kapot%E2%82.pdf")).toBeNull();
  });
});

describe("fallbackFileName", () => {
  it.each([
    ["Invoice NL", "application/pdf", "Invoice NL.pdf"],
    ["Invoice NL", "application/pdf; charset=binary", "Invoice NL.pdf"],
    ["Invoice NL", "text/html", "Invoice NL.html"],
    ["Invoice NL", "TEXT/HTML; charset=utf-8", "Invoice NL.html"],
    ["Export", "application/zip", "Export.zip"],
    ["Export", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Export.xlsx"],
    ["Invoice NL", "application/octet-stream", "Invoice NL.pdf"],
    ["Invoice NL", null, "Invoice NL.pdf"],
    ["Invoice NL", undefined, "Invoice NL.pdf"],
    ["Invoice NL", "", "Invoice NL.pdf"],
  ])("%s with %j → %s", (name, contentType, expected) => {
    expect(fallbackFileName(name, contentType)).toBe(expected);
  });

  it("uses the default extension for an unknown type", () => {
    expect(fallbackFileName("Data", "application/octet-stream", ".bin")).toBe("Data.bin");
  });

  it("an empty or blank name becomes document", () => {
    expect(fallbackFileName("", "application/pdf")).toBe("document.pdf");
    expect(fallbackFileName("   ", "application/pdf")).toBe("document.pdf");
  });

  it("replaces characters a file system refuses and trims", () => {
    expect(fallbackFileName(' Factuur 2026/10: "NL" ', "application/pdf")).toBe("Factuur 2026_10_ _NL_.pdf");
    expect(fallbackFileName("a\\b|c<d>e*f?g", "application/pdf")).toBe("a_b_c_d_e_f_g.pdf");
  });

  it("does not add the extension twice (case-insensitive)", () => {
    expect(fallbackFileName("factuur.pdf", "application/pdf")).toBe("factuur.pdf");
    expect(fallbackFileName("factuur.PDF", "application/pdf")).toBe("factuur.PDF");
    expect(fallbackFileName("factuur.pdf", "text/html")).toBe("factuur.pdf.html");
  });
});

describe("saveBlob", () => {
  const createObjectURL = vi.fn(() => "blob:http://localhost/abc");
  const revokeObjectURL = vi.fn();
  let clicked: { download: string; href: string; rel: string; attached: boolean; hidden: boolean }[];

  beforeEach(() => {
    vi.useFakeTimers();
    createObjectURL.mockClear();
    revokeObjectURL.mockClear();
    Object.assign(URL, { createObjectURL, revokeObjectURL });
    clicked = [];
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push({ download: this.download, href: this.href, rel: this.rel, attached: document.body.contains(this), hidden: this.style.display === "none" });
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("clicks a hidden, attached download anchor for the blob's object URL, then removes it", () => {
    const blob = new Blob(["%PDF-1.7"], { type: "application/pdf" });
    saveBlob(blob, "Factuur € 12.pdf");
    expect(createObjectURL).toHaveBeenCalledWith(blob);
    expect(clicked).toEqual([{ download: "Factuur € 12.pdf", href: "blob:http://localhost/abc", rel: "noopener", attached: true, hidden: true }]);
    expect(document.querySelectorAll("a[download]")).toHaveLength(0);
  });

  it("revokes the object URL on the next task, not during the click", () => {
    saveBlob(new Blob(["x"]), "x.pdf");
    expect(revokeObjectURL).not.toHaveBeenCalled();
    vi.runAllTimers();
    expect(revokeObjectURL).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:http://localhost/abc");
  });

  it("still revokes the object URL when the click throws", () => {
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {
      throw new Error("blocked");
    });
    expect(() => saveBlob(new Blob(["x"]), "x.pdf")).toThrow("blocked");
    vi.runAllTimers();
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:http://localhost/abc");
  });
});

describe("openPendingTab", () => {
  function fakeTab() {
    return { opener: {} as unknown, closed: false, location: { replace: vi.fn() }, close: vi.fn() };
  }

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("opens a blank tab at once, without noopener, and cuts its opener", () => {
    const tab = fakeTab();
    const open = vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    openPendingTab();
    expect(open).toHaveBeenCalledTimes(1);
    expect(open).toHaveBeenCalledWith("about:blank", "_blank");
    expect(tab.opener).toBeNull();
  });

  it("navigate sends the tab to the URL (replace: the blank page stays out of its history)", () => {
    const tab = fakeTab();
    const open = vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    openPendingTab().navigate("https://provider.example/editor?token=t");
    expect(tab.location.replace).toHaveBeenCalledWith("https://provider.example/editor?token=t");
    expect(open).toHaveBeenCalledTimes(1);
  });

  it("close closes the blank tab; a tab the user already closed is left alone", () => {
    const tab = fakeTab();
    vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    const pending = openPendingTab();
    pending.close();
    expect(tab.close).toHaveBeenCalledTimes(1);

    const closedTab = fakeTab();
    vi.spyOn(window, "open").mockReturnValue(closedTab as unknown as Window);
    const other = openPendingTab();
    closedTab.closed = true;
    other.close();
    expect(closedTab.close).not.toHaveBeenCalled();
  });

  it("navigate opens a new noopener tab when the user closed the blank tab meanwhile", () => {
    const tab = fakeTab();
    const open = vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    const pending = openPendingTab();
    tab.closed = true;
    pending.navigate("https://provider.example/editor");
    expect(tab.location.replace).not.toHaveBeenCalled();
    expect(open).toHaveBeenLastCalledWith("https://provider.example/editor", "_blank", "noopener,noreferrer");
  });

  it("when the browser refused the blank tab (null): navigate falls back to a noopener window.open, close is a no-op", () => {
    const open = vi.spyOn(window, "open").mockReturnValue(null);
    const pending = openPendingTab();
    expect(() => pending.close()).not.toThrow();
    pending.navigate("https://provider.example/editor");
    expect(open).toHaveBeenCalledTimes(2);
    expect(open).toHaveBeenLastCalledWith("https://provider.example/editor", "_blank", "noopener,noreferrer");
  });

  it("when window.open throws, it behaves as refused", () => {
    const open = vi.spyOn(window, "open").mockImplementationOnce(() => {
      throw new Error("blocked");
    });
    const pending = openPendingTab();
    open.mockReturnValue(null);
    pending.navigate("https://provider.example/editor");
    expect(open).toHaveBeenLastCalledWith("https://provider.example/editor", "_blank", "noopener,noreferrer");
  });

  it("a read-only opener does not break the command", () => {
    const tab = { closed: false, location: { replace: vi.fn() }, close: vi.fn() };
    Object.defineProperty(tab, "opener", {
      get: () => ({}),
      set: () => {
        throw new TypeError("read-only");
      },
    });
    vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    const pending = openPendingTab();
    pending.navigate("https://provider.example/editor");
    expect(tab.location.replace).toHaveBeenCalledWith("https://provider.example/editor");
  });
});
