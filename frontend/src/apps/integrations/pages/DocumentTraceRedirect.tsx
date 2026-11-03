import { useEffect } from "react";
import { useNavigate } from "@tanstack/react-router";

import { useAppFeatures } from "@/app/app-features";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { RecordNotFound } from "@/components/ui/record-not-found";
import { Skeleton } from "@/components/ui/skeleton";
import { useInboundMessageByDocument, useOrderIntakeFeature } from "@/domains/integrations/order-intake";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { recordLoadState, recordPath } from "@/lib/use-open-record";

/* /integrations/received/document/{documentId} (design 72): the trace of the message a document belongs to. Other apps
 * know only the document (an intake's source, an order's source chip); this route resolves its message and replaces
 * itself with the trace page, so Back skips it. Page states per UI-PAGE-005. */
export function DocumentTraceRedirect({ documentId }: { documentId: string }) {
  const { t } = useL();
  const navigate = useNavigate();
  const features = useAppFeatures();
  const feature = useOrderIntakeFeature();
  const canView = useIsGranted("Integrations.InboundMessages");
  const nodeOn = !features.isLoaded || features.isFeatureEnabled("Apps.Integrations.Received");
  const message = useInboundMessageByDocument(canView && feature === "on" && nodeOn ? documentId : undefined);
  const messageId = message.data?.id;

  useEffect(() => {
    if (messageId) void navigate({ to: recordPath("InboundMessage", messageId), replace: true });
  }, [messageId, navigate]);

  if (feature === "off" || !nodeOn) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;
  const state = recordLoadState(message);
  if (state === "not-found" && feature !== "loading") return <RecordNotFound />;
  if (state === "error")
    return (
      <Alert variant="destructive" className="m-6">
        <AlertDescription>{getApiErrorMessage(message.error, t)}</AlertDescription>
      </Alert>
    );
  return (
    <div className="flex h-full flex-col gap-4 p-6" data-slot="record-page-skeleton">
      <Skeleton className="h-10 w-full" />
      <Skeleton className="min-h-0 w-full flex-1" />
    </div>
  );
}
