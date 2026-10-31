import { useEffect, useState } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { useResetTenantToDataset, type TenantRow, type TestDataset } from "@/domains/platform/tenants";
import { useL } from "@/lib/i18n/LocalizationProvider";

export interface ResetTenantDatasetRequest {
  tenant: TenantRow;
  dataset: TestDataset;
}

interface ResetTenantDatasetDialogProps {
  request: ResetTenantDatasetRequest | undefined;
  onClose: () => void;
  onQueued: () => void;
}

/* "Reset to dataset" (docs/designs/65-tenant-test-datasets.md) permanently replaces the tenant's business data,
 * so, like deleting a tenant, the confirm stays disabled until the tenant's name is typed exactly. The server
 * checks the name again. */
export function ResetTenantDatasetDialog({ request, onClose, onQueued }: ResetTenantDatasetDialogProps) {
  const { t } = useL();
  const reset = useResetTenantToDataset();
  const [typedName, setTypedName] = useState("");

  useEffect(() => {
    setTypedName("");
  }, [request?.tenant.id, request?.dataset]);

  const name = request?.tenant.name ?? "";
  const datasetLabel = request ? t(`Tenants:Dataset:${request.dataset}`) : "";

  return (
    <ConfirmDialog
      open={request != null}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      title={t("Tenants:ResetDialogTitle").replace("{0}", name).replace("{1}", datasetLabel.toLowerCase())}
      description={request ? t(`Tenants:Dataset:${request.dataset}Description`) : ""}
      confirmLabel={t("Tenants:ResetConfirm")}
      destructive
      confirmDisabled={typedName !== name}
      onConfirm={async () => {
        await reset.mutateAsync({ id: request!.tenant.id!, dataset: request!.dataset, confirmationName: typedName });
        onClose();
        onQueued();
        toast.success(t("Tenants:ResetQueued"));
      }}
    >
      <Alert variant="warning">
        <AlertDescription>{t("Tenants:ResetDialogWarning")}</AlertDescription>
      </Alert>
      <FormField label={t("Tenants:ResetDialogTypeName").replace("{0}", name)} htmlFor="rt-confirm-name" floating="always">
        <Input
          id="rt-confirm-name"
          value={typedName}
          onChange={(e) => setTypedName(e.target.value)}
          autoComplete="off"
          spellCheck={false}
        />
      </FormField>
    </ConfirmDialog>
  );
}
