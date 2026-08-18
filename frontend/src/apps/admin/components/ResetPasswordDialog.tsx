import { useState } from "react";
import { Dialog } from "@base-ui/react/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Toast } from "@/components/ui/toast";
import { useResetPassword } from "@/domains/platform/administration-users";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface ResetPasswordDialogProps {
  userId: string;
  userName: string;
  open: boolean;
  onClose: () => void;
}

function extractApiError(error: unknown): string {
  if (!error || typeof error !== "object") return String(error ?? "");
  const e = error as Record<string, unknown>;
  const abpMsg = (e["error"] as Record<string, unknown> | undefined)?.["message"];
  if (typeof abpMsg === "string") return abpMsg;
  if (typeof e["title"] === "string") return e["title"];
  return "";
}

export function ResetPasswordDialog({ userId, userName, open, onClose }: ResetPasswordDialogProps) {
  const { t } = useL();
  const resetPassword = useResetPassword();

  const [newPassword, setNewPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState("");
  const [showSuccess, setShowSuccess] = useState(false);

  const handleClose = () => {
    if (resetPassword.isPending) return;
    setNewPassword("");
    setError("");
    onClose();
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");

    try {
      await resetPassword.mutateAsync({ id: userId, newPassword });
      setShowSuccess(true);
      setTimeout(() => {
        setShowSuccess(false);
        handleClose();
      }, 1500);
    } catch (err) {
      setError(extractApiError(err) || t("Administration:SaveFailed"));
    }
  };

  return (
    <>
      <Dialog.Root open={open} onOpenChange={(o: boolean) => { if (!o) handleClose(); }}>
        <Dialog.Portal>
          <Dialog.Backdrop className="fixed inset-0 z-40 bg-black/50 data-open:animate-in data-open:fade-in-0 data-closed:animate-out data-closed:fade-out-0 duration-150" />
          <Dialog.Popup className="fixed left-1/2 top-1/2 z-50 w-full max-w-sm -translate-x-1/2 -translate-y-1/2 rounded-xl bg-background p-6 shadow-xl ring-1 ring-foreground/10 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 duration-150">
            <Dialog.Title className="mb-1 text-base font-semibold">
              {t("Administration:ResetPassword")}
            </Dialog.Title>
            <Dialog.Description className="mb-4 text-sm text-muted-foreground">
              {userName}
            </Dialog.Description>

            <form onSubmit={(e) => { void handleSubmit(e); }} className="flex flex-col gap-3">
              <div className="flex flex-col gap-1">
                <Label htmlFor="rp-password">{t("Administration:NewPassword")} *</Label>
                <div className="relative">
                  <Input
                    id="rp-password"
                    type={showPassword ? "text" : "password"}
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    required
                    autoComplete="new-password"
                    className="pr-16"
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((v) => !v)}
                    className="absolute right-2.5 top-1/2 -translate-y-1/2 text-xs text-muted-foreground hover:text-foreground"
                  >
                    {showPassword ? t("Administration:Hide") : t("Administration:Show")}
                  </button>
                </div>
              </div>

              {error && <p className="text-sm text-destructive">{error}</p>}

              <div className="mt-2 flex justify-end gap-2">
                <Button
                  type="button"
                  variant="outline"
                  onClick={handleClose}
                  disabled={resetPassword.isPending}
                >
                  {t("Administration:Cancel")}
                </Button>
                <Button type="submit" disabled={resetPassword.isPending}>
                  {resetPassword.isPending ? t("Administration:Saving") : t("Administration:ResetPassword")}
                </Button>
              </div>
            </form>
          </Dialog.Popup>
        </Dialog.Portal>
      </Dialog.Root>

      {showSuccess && <Toast message={t("Administration:PasswordReset")} />}
    </>
  );
}
