import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { useChangeMyPassword } from "@/domains/platform/my-profile";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface ChangePasswordDialogProps {
  open: boolean;
  onClose: () => void;
}

/* "Change password" (docs/designs/63-user-menu.md): ABP's own change-password, with the tenant's password rules, checked
 * by the server. The new password is typed twice; the dialog only checks that both match. */
export function ChangePasswordDialog({ open, onClose }: ChangePasswordDialogProps) {
  const { t } = useL();
  const change = useChangeMyPassword();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const mismatch = confirm.length > 0 && next !== confirm ? t("UserMenu:PasswordsDoNotMatch") : undefined;
  const required = (value: string) => (submitted && !value ? t("FieldRequired") : undefined);
  const canSubmit = !!current && !!next && next === confirm && !change.isPending;

  const submit = async (e?: FormEvent) => {
    e?.preventDefault();
    setSubmitted(true);
    if (!canSubmit) return;
    setError(null);
    try {
      await change.mutateAsync({ currentPassword: current, newPassword: next });
      toast.success(t("UserMenu:PasswordChanged"));
      onClose();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open={open}
      onOpenChange={(isOpen) => {
        if (!isOpen) onClose();
      }}
      variant="action"
      size="sm"
      title={t("UserMenu:ChangePassword")}
      isBusy={change.isPending}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={change.isPending}>
            {t("Cancel")}
          </Button>
          <Button onClick={() => void submit()} disabled={change.isPending}>
            {t("UserMenu:ChangePassword")}
          </Button>
        </>
      }
    >
      <form onSubmit={(e) => void submit(e)} className="flex flex-col gap-3" noValidate>
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <FormField label={t("UserMenu:CurrentPassword")} htmlFor="cp-current" required error={required(current)}>
          <Input
            id="cp-current"
            type="password"
            autoComplete="current-password"
            autoFocus
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
          />
        </FormField>
        <FormField label={t("UserMenu:NewPassword")} htmlFor="cp-new" required error={required(next)}>
          <Input id="cp-new" type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} />
        </FormField>
        <FormField label={t("UserMenu:ConfirmPassword")} htmlFor="cp-confirm" required error={mismatch ?? required(confirm)}>
          <Input
            id="cp-confirm"
            type="password"
            autoComplete="new-password"
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
          />
        </FormField>
        <button type="submit" hidden />
      </form>
    </EntityDialog>
  );
}
