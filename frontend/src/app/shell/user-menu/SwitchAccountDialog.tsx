import { useState, type FormEvent } from "react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { switchSession } from "@/auth/session-switch";
import { useSwitchAccount, type MyAccount } from "@/domains/platform/my-accounts";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface SwitchAccountDialogProps {
  account: MyAccount;
  /** The account's display name: the tenant's name, or "Host". */
  name: string;
  open: boolean;
  onClose: () => void;
}

/* Asks for the password of the user's account in another organisation when it differs from the one the session knows
 * (docs/designs/63-user-menu.md). An action dialog (UI-DLG-002) whose primary is the verb of its title (UI-DLG-013).
 * "Remember password" stores no password: the server remembers the switch until either password changes. */
export function SwitchAccountDialog({ account, name, open, onClose }: SwitchAccountDialogProps) {
  const { t } = useL();
  const switchAccount = useSwitchAccount();
  const [password, setPassword] = useState("");
  const [remember, setRemember] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e?: FormEvent) => {
    e?.preventDefault();
    if (!password || switchAccount.isPending) return;
    setError(null);
    try {
      const url = await switchAccount.mutateAsync({ tenantId: account.tenantId ?? null, password, rememberPassword: remember });
      switchSession(url, account.tenantId ? { id: account.tenantId, name: account.tenantName ?? "" } : undefined);
    } catch (err) {
      setError(getApiErrorMessage(err, t));
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
      title={t("UserMenu:SwitchTitle").replace("{0}", name)}
      isBusy={switchAccount.isPending}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={switchAccount.isPending}>
            {t("Cancel")}
          </Button>
          <Button onClick={() => void submit()} disabled={!password || switchAccount.isPending}>
            {switchAccount.isPending ? t("UserMenu:Switching") : t("UserMenu:Switch")}
          </Button>
        </>
      }
    >
      <form onSubmit={(e) => void submit(e)} className="flex flex-col gap-3" noValidate>
        <p className="text-sm text-muted-foreground">{t("UserMenu:SwitchExplanation").replace("{0}", name)}</p>
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <FormField label={t("UserMenu:Password")} htmlFor="switch-password" required>
          <Input
            id="switch-password"
            type="password"
            autoComplete="current-password"
            autoFocus
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </FormField>
        <label className="flex items-start gap-2 text-sm" htmlFor="switch-remember">
          <Checkbox id="switch-remember" checked={remember} onCheckedChange={(checked) => setRemember(checked === true)} />
          <span className="flex flex-col">
            <span>{t("UserMenu:RememberPassword")}</span>
            <span className="text-label text-muted-foreground">{t("UserMenu:RememberPasswordHint")}</span>
          </span>
        </label>
        {/* Enter in the password field submits. */}
        <button type="submit" hidden />
      </form>
    </EntityDialog>
  );
}
