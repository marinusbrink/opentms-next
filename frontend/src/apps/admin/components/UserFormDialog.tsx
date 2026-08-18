import { useState } from "react";
import { Dialog } from "@base-ui/react/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { RoleMultiSelect } from "@/components/ui/role-multi-select";
import { useCreateUser, useUpdateUser, type UserRow, type UserCreateDto, type UserUpdateDto } from "@/domains/platform/administration-users";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface UserFormDialogProps {
  mode: "create" | "edit";
  user?: UserRow;
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
  if (typeof e["detail"] === "string") return e["detail"];
  return "";
}

export function UserFormDialog({ mode, user, open, onClose, onSuccess }: UserFormDialogProps) {
  const { t } = useL();
  const createUser = useCreateUser();
  const updateUser = useUpdateUser();

  const [userName, setUserName] = useState(mode === "edit" ? (user?.userName ?? "") : "");
  const [email, setEmail] = useState(mode === "edit" ? (user?.email ?? "") : "");
  const [name, setName] = useState(mode === "edit" ? (user?.name ?? "") : "");
  const [surname, setSurname] = useState(mode === "edit" ? (user?.surname ?? "") : "");
  const [password, setPassword] = useState("");
  const [isActive, setIsActive] = useState(mode === "edit" ? (user?.isActive ?? true) : true);
  const [roleNames, setRoleNames] = useState<string[]>(
    mode === "edit" ? (user?.roleNames ?? []) : [],
  );
  const [error, setError] = useState("");
  const isPending = createUser.isPending || updateUser.isPending;

  const handleClose = () => {
    if (isPending) return;
    onClose();
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");

    try {
      if (mode === "create") {
        const dto: UserCreateDto = {
          userName,
          email,
          name: name || null,
          surname: surname || null,
          password,
          roleNames,
        };
        await createUser.mutateAsync(dto);
      } else {
        const dto: UserUpdateDto = {
          userName,
          email,
          name: name || null,
          surname: surname || null,
          isActive,
          roleNames,
        };
        await updateUser.mutateAsync({ id: user!.id!, dto });
      }
      onSuccess();
      onClose();
    } catch (err) {
      setError(extractApiError(err) || t("Administration:SaveFailed"));
    }
  };

  const title =
    mode === "create"
      ? t("Administration:NewUser")
      : t("Administration:EditUser") + " " + (user?.userName ?? "");

  return (
    <Dialog.Root open={open} onOpenChange={(o: boolean) => { if (!o) handleClose(); }}>
      <Dialog.Portal>
        <Dialog.Backdrop className="fixed inset-0 z-40 bg-black/50 data-open:animate-in data-open:fade-in-0 data-closed:animate-out data-closed:fade-out-0 duration-150" />
        <Dialog.Popup className="fixed left-1/2 top-1/2 z-50 w-full max-w-md -translate-x-1/2 -translate-y-1/2 rounded-xl bg-background p-6 shadow-xl ring-1 ring-foreground/10 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 duration-150">
          <Dialog.Title className="mb-4 text-base font-semibold">{title}</Dialog.Title>

          <form onSubmit={(e) => { void handleSubmit(e); }} className="flex flex-col gap-3">
            <div className="flex flex-col gap-1">
              <Label htmlFor="uf-username">{t("Administration:Username")} *</Label>
              <Input
                id="uf-username"
                value={userName}
                onChange={(e) => setUserName(e.target.value)}
                required
                autoComplete="off"
              />
            </div>

            <div className="flex flex-col gap-1">
              <Label htmlFor="uf-email">{t("Administration:Email")} *</Label>
              <Input
                id="uf-email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                autoComplete="off"
              />
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div className="flex flex-col gap-1">
                <Label htmlFor="uf-name">{t("Administration:FirstName")}</Label>
                <Input
                  id="uf-name"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                />
              </div>
              <div className="flex flex-col gap-1">
                <Label htmlFor="uf-surname">{t("Administration:Surname")}</Label>
                <Input
                  id="uf-surname"
                  value={surname}
                  onChange={(e) => setSurname(e.target.value)}
                />
              </div>
            </div>

            {mode === "create" && (
              <div className="flex flex-col gap-1">
                <Label htmlFor="uf-password">{t("Administration:Password")} *</Label>
                <Input
                  id="uf-password"
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                  autoComplete="new-password"
                />
              </div>
            )}

            {mode === "edit" && (
              <div className="flex items-center gap-2">
                <input
                  id="uf-active"
                  type="checkbox"
                  checked={isActive}
                  onChange={(e) => setIsActive(e.target.checked)}
                  className="size-4 accent-primary"
                />
                <Label htmlFor="uf-active">{t("Administration:Active")}</Label>
              </div>
            )}

            <div className="flex flex-col gap-1">
              <Label>{t("Administration:Roles")}</Label>
              <RoleMultiSelect value={roleNames} onChange={setRoleNames} />
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
