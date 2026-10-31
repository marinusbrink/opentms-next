import { useState, type FormEvent } from "react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { useSendTestEmail, type TestEmailResult } from "@/domains/platform/administration-email";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/* Sends one test mail with the saved settings and shows Postmark's answer in the dialog (UI-FB-004: the result shows
 * where the user is; no toast). The dialog stays open, so another address can be tried. */
export function SendTestEmailDialog({ onClose }: { onClose: () => void }) {
  const { t } = useL();
  const send = useSendTestEmail();
  const [to, setTo] = useState("");
  const [touched, setTouched] = useState(false);
  const [result, setResult] = useState<TestEmailResult | undefined>();
  const [error, setError] = useState<string | undefined>();

  const toError = touched && !EMAIL_PATTERN.test(to.trim()) ? t("Email:FromAddress.Invalid") : undefined;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setTouched(true);
    if (!EMAIL_PATTERN.test(to.trim())) return;
    setError(undefined);
    setResult(undefined);
    try {
      setResult(await send.mutateAsync(to.trim()));
    } catch (e) {
      setError(getApiErrorMessage(e, t));
    }
  };

  return (
    <EntityDialog
      open
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="sm"
      title={t("Email:SendTest")}
      isBusy={send.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={send.isPending}>
            {t("Close")}
          </Button>
          <Button type="submit" form="send-test-email-form" disabled={send.isPending}>
            {send.isPending ? t("Email:TestSending") : t("Email:TestSend")}
          </Button>
        </>
      }
    >
      <form id="send-test-email-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        {result?.succeeded && (
          <Alert>
            <AlertDescription>{t("Email:TestSucceeded").replace("{0}", result.providerMessageId ?? "")}</AlertDescription>
          </Alert>
        )}
        {result && !result.succeeded && (
          <Alert variant="destructive">
            <AlertDescription>{t("Email:TestFailed").replace("{0}", result.error ?? "")}</AlertDescription>
          </Alert>
        )}
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <FormField label={t("Email:TestTo")} htmlFor="send-test-email-to" required error={toError}>
          <Input id="send-test-email-to" type="email" value={to} maxLength={512} autoFocus onChange={(e) => setTo(e.target.value)} onBlur={() => setTouched(true)} />
        </FormField>
      </form>
    </EntityDialog>
  );
}
