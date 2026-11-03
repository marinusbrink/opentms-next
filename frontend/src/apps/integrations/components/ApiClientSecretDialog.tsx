import { TriangleAlert } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { CopyButton } from "@/components/ui/copy-button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { tokenCurlExample, type ApiClientSecret } from "@/domains/integrations/order-intake";
import { useL } from "@/lib/i18n/LocalizationProvider";

export interface ApiClientSecretDialogProps {
  /** The answer of create or regenerate; null: closed. */
  secret: ApiClientSecret | null;
  /** The client's name, for the title. */
  name: string;
  onClose: () => void;
}

/* The one-time credentials of an API client (design 72 "UI design", API clients): the client id, the secret (shown
 * once and never again: the warning says so), the token URL, the scope and an example `curl` of the client-credentials
 * call, each with a copy button. An action dialog with only its closing verb: nothing here is an operation the user
 * could cancel. */
export function ApiClientSecretDialog({ secret, name, onClose }: ApiClientSecretDialogProps) {
  const { t } = useL();
  const field = (id: string, labelKey: string, value: string | null | undefined, mono = false) => (
    <div className="flex items-start gap-2">
      <FormField label={t(labelKey)} htmlFor={id} className="min-w-0 flex-1">
        <Input id={id} value={value ?? ""} readOnly className={mono ? "font-mono" : undefined} onFocus={(e) => e.currentTarget.select()} />
      </FormField>
      {value && <CopyButton value={value} label={t("Integrations:CopyValue").replace("{0}", t(labelKey))} className="mt-2" />}
    </div>
  );
  const curl = secret ? tokenCurlExample(secret) : "";

  const footer = (
    <div className="flex justify-end gap-2">
      <Button onClick={onClose}>{t("Close")}</Button>
    </div>
  );

  return (
    <EntityDialog
      open={secret != null}
      onOpenChange={(next) => !next && onClose()}
      variant="action"
      size="md"
      title={t("Integrations:ApiClientCredentials").replace("{0}", name)}
      footer={footer}
    >
      <div className="grid grid-cols-1 gap-3" data-slot="api-client-secret">
        <Alert variant="warning">
          <TriangleAlert />
          <AlertDescription>{t("Integrations:SecretShownOnce")}</AlertDescription>
        </Alert>
        {field("api-client-secret-client-id", "Integrations:ClientId", secret?.clientId, true)}
        {field("api-client-secret-secret", "Integrations:ClientSecret", secret?.clientSecret, true)}
        {field("api-client-secret-token-url", "Integrations:TokenUrl", secret?.tokenUrl)}
        {field("api-client-secret-scope", "Integrations:Scope", secret?.scope)}
        <div className="grid gap-1">
          <div className="flex items-center justify-between">
            <span className="text-label font-medium text-muted-foreground">{t("Integrations:ExampleCall")}</span>
            <CopyButton value={curl} label={t("Integrations:CopyValue").replace("{0}", t("Integrations:ExampleCall"))} />
          </div>
          <pre className="rounded-lg border border-border-section bg-surface-section p-3 font-mono text-label break-all whitespace-pre-wrap">{curl}</pre>
        </div>
      </div>
    </EntityDialog>
  );
}
