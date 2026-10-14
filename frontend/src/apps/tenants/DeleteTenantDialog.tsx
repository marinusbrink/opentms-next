import { useEffect, useState } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { useDeleteTenant, type TenantRow } from "@/domains/platform/tenants";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface DeleteTenantDialogProps {
  tenant: TenantRow | undefined;
  onClose: () => void;
  onDeleted: () => void;
}

/* Deleting a tenant drops its database (PO decision D5, docs/designs/tenants-and-editions.md). That cannot be
 * undone, so the confirm stays disabled until the tenant's name is typed exactly. */
export function DeleteTenantDialog({ tenant, onClose, onDeleted }: DeleteTenantDialogProps) {
  const { t } = useL();
  const deleteTenant = useDeleteTenant();
  const [typedName, setTypedName] = useState("");

  useEffect(() => {
    setTypedName("");
  }, [tenant?.id]);

  const name = tenant?.name ?? "";
  const hasOwnDatabase = tenant?.databaseName != null;

  return (
    <ConfirmDialog
      open={tenant != null}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      title={t("Tenants:DeleteTenant")}
      description={`${t("Tenants:DeleteTenantQuestion").replace("{0}", name)} ${t("ThisCannotBeUndone")}`}
      confirmLabel={t("Delete")}
      busyLabel={t("Deleting")}
      destructive
      confirmDisabled={typedName !== name}
      onConfirm={async () => {
        await deleteTenant.mutateAsync(tenant!.id!);
        onClose();
        onDeleted();
        toast.success(t("Tenants:TenantDeleted"));
      }}
    >
      {hasOwnDatabase && (
        <Alert variant="warning">
          <AlertDescription>{t("Tenants:DeleteDropsDatabase").replace("{0}", tenant?.databaseName ?? "")}</AlertDescription>
        </Alert>
      )}
      <FormField label={t("Tenants:TypeNameToConfirm").replace("{0}", name)} htmlFor="dt-confirm-name" floating="always">
        <Input
          id="dt-confirm-name"
          value={typedName}
          onChange={(e) => setTypedName(e.target.value)}
          autoComplete="off"
          spellCheck={false}
        />
      </FormField>
    </ConfirmDialog>
  );
}
