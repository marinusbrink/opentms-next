import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { HtmlPreview } from "@/components/ui/html-preview";

/* The sandboxed preview of user-authored HTML (design 49, security quickscan: "the preview iframe is sandboxed without
 * allow-scripts, so template HTML cannot run script in the app's origin"). The frame must carry an EMPTY sandbox
 * attribute (every restriction on), render through srcdoc (never a URL or document.write into the app's origin) and
 * follow the edited body. Risk class: Critical for the sandbox attribute (script in the app's origin is a tenant data
 * leak), Medium for the rest. */

const SCRIPTED = '<p>Hi {{ name }}</p><script>window.parent.document.title = "pwned"</script><img src=x onerror="alert(1)">';

describe("HtmlPreview", () => {
  it("is an iframe with an empty sandbox: no allow-scripts, no allow-same-origin, nothing else allowed", () => {
    render(<HtmlPreview html={SCRIPTED} title="Preview" />);
    const frame = screen.getByTitle("Preview");
    expect(frame.tagName).toBe("IFRAME");
    expect(frame.hasAttribute("sandbox")).toBe(true);
    expect(frame.getAttribute("sandbox")).toBe("");
    /* jsdom has no HTMLIFrameElement.sandbox token list: read the tokens from the attribute. */
    const tokens = (frame.getAttribute("sandbox") ?? "").split(/\s+/).filter(Boolean);
    expect(tokens).toEqual([]);
    expect(tokens).not.toContain("allow-scripts");
    expect(tokens).not.toContain("allow-same-origin");
  });

  it("renders the HTML through srcdoc, never through src, and sends no referrer", () => {
    render(<HtmlPreview html={SCRIPTED} title="Preview" />);
    const frame = screen.getByTitle("Preview");
    expect(frame.getAttribute("srcdoc")).toBe(SCRIPTED);
    expect(frame.hasAttribute("src")).toBe(false);
    expect(frame.getAttribute("referrerpolicy")).toBe("no-referrer");
  });

  it("the script in the template never reaches the app's document", () => {
    const before = document.title;
    render(<HtmlPreview html={SCRIPTED} title="Preview" />);
    expect(document.title).toBe(before);
    expect(document.querySelectorAll("script").length).toBe(0);
  });

  it("follows the edited content", async () => {
    const { rerender } = render(<HtmlPreview html="<p>One</p>" title="Preview" />);
    expect(screen.getByTitle("Preview").getAttribute("srcdoc")).toBe("<p>One</p>");
    rerender(<HtmlPreview html="<p>Two</p>" title="Preview" />);
    await waitFor(() => expect(screen.getByTitle("Preview").getAttribute("srcdoc")).toBe("<p>Two</p>"));
    rerender(<HtmlPreview html="" title="Preview" />);
    await waitFor(() => expect(screen.getByTitle("Preview").getAttribute("srcdoc")).toBe(""));
    expect(screen.getByTitle("Preview").getAttribute("sandbox")).toBe("");
  });

  it("takes the accessible name and the layout class it is given", () => {
    render(<HtmlPreview html="" title="Platform:Preview Platform:Language.nl" className="min-h-96" />);
    const frame = screen.getByTitle("Platform:Preview Platform:Language.nl");
    expect(frame.className).toContain("min-h-96");
    expect(frame.getAttribute("data-slot")).toBe("html-preview");
  });
});
