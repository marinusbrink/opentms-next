import { useState } from "react";
import { Dialog } from "@base-ui/react/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useCreateRole, useUpdateRole, type RoleRow, type RoleCreateUpdateDto } from "@/domains/platform/administration-roles";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface RoleFormDialogProps {
  mode: "create" | "edit";
  role?: RoleRow;
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

export function RoleFormDialog({ mode, role, open, onClose, onSuccess }: RoleFormDialogProps) {
  const { t } = useL();
  const createRole = useCreateRole();
  const updateRole = useUpdateRole();

  const [roleName, setRoleName] = useState(mode === "edit" ? (role?.name ?? "") : "");
  const [isDefault, setIsDefault] = useState(mode === "edit" ? (role?.isDefault ?? false) : false);
  const [isPublic, setIsPublic] = useState(mode === "edit" ? (role?.isPublic ?? false) : false);
  const [error, setError] = useState("");
  const isPending = createRole.isPending || updateRole.isPending;

  const handleClose = () => {
    if (isPending) return;
    onClose();
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");

    const dto: RoleCreateUpdateDto = { name: roleName, isDefault, isPublic };

    try {
      if (mode === "create") {
        await createRole.mutateAsync(dto);
      } else {
        await updateRole.mutateAsync({ id: role!.id!, dto });
      }
      onSuccess();
      onClose();
    } catch (err) {
      setError(extractApiError(err) || t("Administration:SaveFailed"));
    }
  };

  const title =
    mode === "create"
      ? t("Administration:NewRole")
      : t("Administration:EditRole") + " " + (role?.name ?? "");

  return (
    <Dialog.Root open={open} onOpenChange={(o: boolean) => { if (!o) handleClose(); }}>
      <Dialog.Portal>
        <Dialog.Backdrop className="fixed inset-0 z-40 bg-black/50 data-open:animate-in data-open:fade-in-0 data-closed:animate-out data-closed:fade-out-0 duration-150" />
        <Dialog.Popup className="fixed left-1/2 top-1/2 z-50 w-full max-w-sm -translate-x-1/2 -translate-y-1/2 rounded-xl bg-background p-6 shadow-xl ring-1 ring-foreground/10 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 duration-150">
          <Dialog.Title className="mb-4 text-base font-semibold">{title}</Dialog.Title>

          <form onSubmit={(e) => { void handleSubmit(e); }} className="flex flex-col gap-3">
            <div className="flex flex-col gap-1">
              <Label htmlFor="rf-name">{t("Administration:RoleName")} *</Label>
              <Input
                id="rf-name"
                value={roleName}
                onChange={(e) => setRoleName(e.target.value)}
                required
                maxLength={256}
              />
            </div>

            <div className="flex flex-col gap-2">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={isDefault}
                  onChange={(e) => setIsDefault(e.target.checked)}
                  className="size-4 accent-primary"
                />
                {t("Administration:IsDefault")}
              </label>

              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={isPublic}
                  onChange={(e) => setIsPublic(e.target.checked)}
                  className="size-4 accent-primary"
                />
                {t("Administration:IsPublic")}
              </label>
            </div>

            {error && (
              <p className="text-sm text-destructive">{error}</p>
            )}

            <div className="mt-2 flex justify-end gap-2">
              <Button type="button" variant="outline" onClick={handleClose} disabled={isPending}>
                {t("Administration:Cancel")}
              </Button>
              <Button type="submit" disabled={isPending}>
                {isPending ? t("Administration:Saving") : t("Administration:Save")}
              </Button>
            </div>
          </form>
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
