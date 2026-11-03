import { useEffect } from "react";
import { useLocation, useNavigate } from "@tanstack/react-router";

import { APPS } from "@/app/apps.config";
import { useAppFeatures } from "@/app/app-features";
import { DocumentTraceRedirect } from "@/apps/integrations/pages/DocumentTraceRedirect";
import { TracePage } from "@/apps/integrations/pages/TracePage";
import { ApiClientsView } from "@/apps/integrations/views/ApiClientsView";
import { ImportProfilesView } from "@/apps/integrations/views/ImportProfilesView";
import { IntakeSettingsView } from "@/apps/integrations/views/IntakeSettingsView";
import { MailboxesView } from "@/apps/integrations/views/MailboxesView";
import { ReceivedView } from "@/apps/integrations/views/ReceivedView";
import { DOCUMENT_TRACE_PREFIX, parseRecordPath } from "@/lib/use-open-record";

/* The Integrations app (design 72): a view on the Integrations domain, routed by pathname like the Transport app. The
 * bare app path and the group path Order intake (/integrations/intake) go to the first view under them that is on for
 * this tenant ("App features", tenants-and-editions.md); the order-intake setup lists (Mailboxes, API clients, Import
 * profiles; in this app since PO decision 2026-11-03; the mailbox OAuth return lands on Mailboxes), the order intake's
 * Settings; a record
 * path (/integrations/received/{messageId}) renders the message's trace page; a document path
 * (/integrations/received/document/{documentId}) resolves the document's message and moves on to its trace page. */

const integrationsApp = APPS.find((app) => app.id === "integrations")!;

export function IntegrationsApp() {
  const location = useLocation();
  const navigate = useNavigate();
  const features = useAppFeatures();
  const pathname = location.pathname.replace(/\/+$/, "") || "/integrations";
  const record = parseRecordPath(pathname);
  const redirect =
    pathname === "/integrations" || pathname === "/integrations/intake"
      ? features.firstEnabledPath(integrationsApp, pathname === "/integrations" ? undefined : pathname)
      : undefined;

  useEffect(() => {
    if (redirect) void navigate({ to: redirect, replace: true });
  }, [redirect, navigate]);

  /* The trace of the message a document belongs to (/integrations/received/document/{documentId}). */
  if (pathname.startsWith(`${DOCUMENT_TRACE_PREFIX}/`)) return <DocumentTraceRedirect documentId={decodeURIComponent(pathname.slice(DOCUMENT_TRACE_PREFIX.length + 1))} />;
  if (record?.type === "InboundMessage") return <TracePage key={record.id} id={record.id} />;
  if (pathname.startsWith("/integrations/mailboxes")) return <MailboxesView />;
  if (pathname.startsWith("/integrations/api-clients")) return <ApiClientsView />;
  if (pathname.startsWith("/integrations/import-profiles")) return <ImportProfilesView />;
  if (pathname.startsWith("/integrations/intake-settings")) return <IntakeSettingsView />;
  if (redirect) return null;
  return <ReceivedView />;
}
