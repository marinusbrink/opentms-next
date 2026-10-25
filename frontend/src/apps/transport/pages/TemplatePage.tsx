import { useAppFeatures } from "@/app/app-features";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { RecordNotFound } from "@/components/ui/record-not-found";
import { OrderEditor } from "@/apps/transport/orders/OrderEditor";
import { PageSkeleton } from "@/apps/transport/pages/OrderPage";
import { useTemplate } from "@/domains/orders/templates";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { recordLoadState } from "@/lib/use-open-record";

export interface TemplatePageProps {
  id: string;
}

/* Design 61, "Templates": the order editor in template mode on the record page /transport/templates/{id}. */
export function TemplatePage({ id }: TemplatePageProps) {
  const { t } = useL();
  const features = useAppFeatures();
  const canView = useIsGranted("Orders.TransportOrderTemplates");
  const canUpdate = useIsGranted("Orders.TransportOrderTemplates.Update");
  const template = useTemplate(canView ? id : undefined);

  if (features.isLoaded && !features.isFeatureEnabled("Apps.Transport.Orders.Templates")) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;

  const state = recordLoadState(template);
  if (state === "loading") return <PageSkeleton />;
  if (state === "not-found") return <RecordNotFound />;
  if (state === "error")
    return (
      <Alert variant="destructive" className="m-6">
        <AlertDescription>{getApiErrorMessage(template.error, t)}</AlertDescription>
      </Alert>
    );
  return <OrderEditor mode="template" template={template.data} readOnly={!canUpdate} />;
}
