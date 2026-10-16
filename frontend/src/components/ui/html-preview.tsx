import { useDeferredValue } from "react";

import { cn } from "@/lib/utils";

/* A read-only rendering of user-authored HTML (design 49: the e-mail template body; GAP-08 keeps the source editor).
 * The frame is sandboxed with an EMPTY sandbox attribute: no scripts, no same-origin access, no forms, no popups,
 * no top navigation. Template HTML can therefore never run script in the app's origin or read its storage. Never add
 * `allow-scripts` or `allow-same-origin` here (the security quickscan of design 49 relies on it). The content is
 * deferred, so typing in a large body (up to 200 KB) stays responsive. */
export interface HtmlPreviewProps {
  /** The HTML document or fragment to render. */
  html: string;
  /** The frame's accessible name (already translated), e.g. t("Platform:Preview"). */
  title: string;
  /** Layout only: height and grid placement. */
  className?: string;
}

export function HtmlPreview({ html, title, className }: HtmlPreviewProps) {
  const deferred = useDeferredValue(html);
  return (
    <iframe
      data-slot="html-preview"
      title={title}
      sandbox=""
      referrerPolicy="no-referrer"
      srcDoc={deferred}
      className={cn("w-full rounded-lg border border-border-section bg-background", className)}
    />
  );
}
