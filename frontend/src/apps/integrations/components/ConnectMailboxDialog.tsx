import { useEffect, useState } from "react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { MAIL_PROVIDERS, useConnectMailbox, type MailProvider } from "@/domains/integrations/order-intake";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

export interface ConnectMailboxDialogProps {
  /** The provider to connect; null: closed. */
  provider: MailProvider | null;
  onOpenChange: (open: boolean) => void;
  /** Leaves the SPA for the provider's sign-in (design 72 "Connect"); a test replaces it. */
  navigate?: (url: string) => void;
}

/* Connect Gmail / Connect Microsoft 365 (design 72 "UI design", Mailboxes): a small action dialog that asks the display
 * name, creates the connection (waiting for sign-in) and sends the browser to the provider's consent page. The provider
 * returns to /integrations/mailboxes?connected={id} (or ?connectError={code}), which the list handles; a return to the
 * old /master-data/integrations/mailboxes address is sent on there with its search (PO 2026-11-03).
 * A refusal (the provider is not set up yet) stays in the dialog (UI-FB-004). */
export function ConnectMailboxDialog({ provider, onOpenChange, navigate = (url) => window.location.assign(url) }: ConnectMailboxDialogProps) {
  const { t } = useL();
  const connect = useConnectMailbox();
  const [name, setName] = useState("");
  const [touched, setTouched] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const providerName = provider ? MAIL_PROVIDERS[provider] : undefined;

  useEffect(() => {
    if (provider != null) {
      setName("");
      setTouched(false);
      setError(null);
    }
  }, [provider]);

  const nameError = touched && !name.trim() ? t("MasterData:Required") : undefined;
  /* The navigation away is pending: the dialog stays busy until the browser leaves. */
  const busy = connect.isPending || connect.isSuccess;

  const submit = async () => {
    setTouched(true);
    if (!provider || !name.trim()) return;
    setError(null);
    try {
      const result = await connect.mutateAsync({ provider, displayName: name.trim() });
      if (result?.authorizeUrl) navigate(result.authorizeUrl);
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  const footer = (
    <div className="flex justify-end gap-2">
      <Button variant="outline" onClick={() => onOpenChange(false)} disabled={busy}>
        {t("Cancel")}
      </Button>
      <Button onClick={() => void submit()} disabled={busy}>
        {busy ? t("Integrations:Connecting") : t("Integrations:Connect")}
      </Button>
    </div>
  );

  return (
    <EntityDialog
      open={provider != null}
      onOpenChange={onOpenChange}
      variant="action"
      size="sm"
      title={providerName ? t(`Integrations:Connect${providerName}`) : ""}
      isBusy={busy}
      footer={footer}
    >
      <form
        className="grid grid-cols-1 gap-3"
        noValidate
        onSubmit={(e) => {
          e.preventDefault();
          void submit();
        }}
      >
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <FormField label={t("Integrations:MailboxName")} htmlFor="connect-mailbox-name" required error={nameError} hint={t("Integrations:MailboxName.Hint")}>
          <Input id="connect-mailbox-name" value={name} maxLength={128} autoFocus disabled={busy} onChange={(e) => setName(e.target.value)} onBlur={() => setTouched(true)} />
        </FormField>
        <p className="text-label text-muted-foreground">{t("Integrations:ConnectExplanation")}</p>
      </form>
    </EntityDialog>
  );
}
