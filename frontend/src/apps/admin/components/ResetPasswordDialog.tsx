import { useRef, useState, type FormEvent } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { useResetPassword } from "@/domains/platform/administration-users";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface ResetPasswordDialogProps {
  userId: string;
  userName: string;
  open: boolean;
  onClose: () => void;
}

/* An action dialog (UI-DLG-002/005/013): plain header, the form, footer [Cancel] [Reset password]. */
export function ResetPasswordDialog({ userId, userName, open, onClose }: ResetPasswordDialogProps) {
  const { t } = useL();
  const resetPassword = useResetPassword();
  const formRef = useRef<HTMLFormElement>(null);

  const [newPassword, setNewPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [touched, setTouched] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  const fieldError = newPassword ? undefined : t("FieldRequired");
  const shownError = touched || submitted ? fieldError : undefined;

  const handleSubmit = async (e?: FormEvent) => {
    e?.preventDefault();
    if (resetPassword.isPending) return;
    setSubmitted(true);
    setServerError(null);
    if (fieldError) {
      formRef.current?.querySelector<HTMLElement>("#rp-password")?.focus();
      return;
    }
    try {
      await resetPassword.mutateAsync({ id: userId, newPassword });
      onClose();
      toast.success(t("Administration:PasswordReset"));
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="sm"
      title={`${t("Administration:ResetPassword")} ${userName}`}
      isBusy={resetPassword.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={resetPassword.isPending}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="reset-password-form" disabled={resetPassword.isPending}>
            {resetPassword.isPending ? t("Saving") : t("Administration:ResetPassword")}
          </Button>
        </>
      }
    >
      <form
        id="reset-password-form"
        ref={formRef}
        onSubmit={(e) => void handleSubmit(e)}
        className="grid grid-cols-1 gap-3"
        noValidate
      >
        {serverError && (
          <Alert variant="destructive">
            <AlertDescription>{serverError}</AlertDescription>
          </Alert>
        )}

        <FormField label={t("Administration:NewPassword")} htmlFor="rp-password" required error={shownError}>
          <div className="relative">
            <Input
              id="rp-password"
              type={showPassword ? "text" : "password"}
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              onBlur={() => setTouched(true)}
              autoComplete="new-password"
              className="pr-16"
            />
            <button
              type="button"
              onClick={() => setShowPassword((v) => !v)}
              className="absolute top-1/2 right-2.5 -translate-y-1/2 text-label text-muted-foreground hover:text-foreground"
            >
              {showPassword ? t("Administration:Hide") : t("Administration:Show")}
            </button>
          </div>
        </FormField>
      </form>
    </EntityDialog>
  );
}
