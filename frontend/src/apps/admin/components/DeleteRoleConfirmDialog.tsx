import { Dialog } from "@base-ui/react/dialog";
import { Button } from "@/components/ui/button";
import { useDeleteRole } from "@/domains/platform/administration-roles";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useState } from "react";

interface DeleteRoleConfirmDialogProps {
  roleId: string;
  roleName: string;
  userCount: number;
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

function extractApiError(error: unknown): string {
  if (!error || typeof error !== "object") return String(error ?? "");
  const e = error as Record<string, unknown>;
  const abpMsg = (e["error"] as Record<string, unknown> | undefined)?.["message"];
  if (typeof abpMsg === "string") return abpMsg;
  if (typeof e["title"] === "string") return e["title"];
  return "";
}

export function DeleteRoleConfirmDialog({
  roleId,
  roleName,
  userCount,
  open,
  onClose,
  onSuccess,
}: DeleteRoleConfirmDialogProps) {
  const { t } = useL();
  const deleteRole = useDeleteRole();
  const [error, setError] = useState("");

  const handleClose = () => {
    if (deleteRole.isPending) return;
    setError("");
    onClose();
  };

  const handleConfirm = async () => {
    setError("");
    try {
      await deleteRole.mutateAsync({ id: roleId, force: true });
      onSuccess();
      onClose();
    } catch (err) {
      setError(extractApiError(err) || t("Administration:DeleteFailed"));
    }
  };

  const warningMessage = t("Administration:RoleHasUsersWarning")
    .replace("{0}", roleName)
    .replace("{1}", String(userCount));

  return (
    <Dialog.Root open={open} onOpenChange={(o: boolean) => { if (!o) handleClose(); }}>
      <Dialog.Portal>
        <Dialog.Backdrop className="fixed inset-0 z-40 bg-black/50 data-open:animate-in data-open:fade-in-0 data-closed:animate-out data-closed:fade-out-0 duration-150" />
        <Dialog.Popup className="fixed left-1/2 top-1/2 z-50 w-full max-w-md -translate-x-1/2 -translate-y-1/2 rounded-xl bg-background p-6 shadow-xl ring-1 ring-foreground/10 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 duration-150">
          <Dialog.Title className="mb-4 text-base font-semibold">
            {t("Administration:DeleteRole")}
          </Dialog.Title>

          <div className="flex flex-col gap-4">
            <p className="text-sm">{warningMessage}</p>

            {error && <p className="text-sm text-destructive">{error}</p>}

            <div className="flex justify-end gap-2">
              <Button
                variant="outline"
                onClick={handleClose}
                disabled={deleteRole.isPending}
              >
                {t("Administration:Cancel")}
              </Button>
              <Button
                variant="destructive"
                onClick={() => { void handleConfirm(); }}
                disabled={deleteRole.isPending}
              >
                {deleteRole.isPending
                  ? t("Administration:Deleting")
                  : t("Administration:DeleteRole")}
              </Button>
            </div>
          </div>
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
