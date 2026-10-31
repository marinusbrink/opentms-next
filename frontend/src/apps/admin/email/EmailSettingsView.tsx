import { Copy, Save, Send } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { SendTestEmailDialog } from "@/apps/admin/email/SendTestEmailDialog";
import {
  EMAIL_SETTING_SOURCE,
  TRACK_LINKS_VALUES,
  useEmailSettings,
  useUpdateEmailSettings,
  type EmailSettings,
  type UpdateEmailSettings,
} from "@/domains/platform/administration-email";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted, useIsHostSide } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
/** The select's value for "use the server default" (no stored value). */
const DEFAULT = "default";

interface FormValues {
  serverToken: string;
  clearServerToken: boolean;
  fromAddress: string;
  fromName: string;
  /** "default" | "true" | "false" */
  trackOpens: string;
  /** "default" or a TRACK_LINKS_VALUES entry */
  trackLinks: string;
  webhookUsername: string;
  webhookPassword: string;
  clearWebhookPassword: boolean;
}

function toForm(s: EmailSettings): FormValues {
  return {
    serverToken: "",
    clearServerToken: false,
    fromAddress: s.fromAddress ?? "",
    fromName: s.fromName ?? "",
    trackOpens: s.trackOpens == null ? DEFAULT : String(s.trackOpens),
    trackLinks: s.trackLinks ?? DEFAULT,
    webhookUsername: s.webhookUsername ?? "",
    webhookPassword: "",
    clearWebhookPassword: false,
  };
}

function toInput(v: FormValues): UpdateEmailSettings {
  return {
    serverToken: v.serverToken.trim() || null,
    clearServerToken: v.clearServerToken,
    fromAddress: v.fromAddress.trim() || null,
    fromName: v.fromName.trim() || null,
    trackOpens: v.trackOpens === DEFAULT ? null : v.trackOpens === "true",
    trackLinks: v.trackLinks === DEFAULT ? null : v.trackLinks,
    webhookUsername: v.webhookUsername.trim() || null,
    webhookPassword: v.webhookPassword || null,
    clearWebhookPassword: v.clearWebhookPassword,
  };
}

/* The host's e-mail settings (design 64, "Host e-mail settings"): a settings page with one Save (UI-PAGE-006) and a
 * test mail. Host only: the permission is host-side, and the view refuses on the tenant side as well. The token and
 * the webhook password are write-only: empty keeps what is stored; the hint says whether one is stored. An empty
 * sender or tracking field falls back to the server's configuration, shown as the field's hint. */
export function EmailSettingsView() {
  const { t } = useL();
  const isHost = useIsHostSide();
  const canView = useIsGranted("Platform.Administration.EmailSettings");
  const canUpdate = useIsGranted("Platform.Administration.EmailSettings.Update");
  const canTest = useIsGranted("Platform.Administration.EmailSettings.SendTest");
  const settings = useEmailSettings();
  const update = useUpdateEmailSettings();

  const [values, setValues] = useState<FormValues | undefined>();
  const [submitted, setSubmitted] = useState(false);
  const [saveError, setSaveError] = useState<string | undefined>();
  const [testOpen, setTestOpen] = useState(false);

  const saved = useMemo(() => (settings.data ? toForm(settings.data) : undefined), [settings.data]);
  useEffect(() => {
    if (saved) setValues(saved);
  }, [saved]);

  /* UI-FORM-009: dirty compares values. */
  const isDirty = values != null && saved != null && JSON.stringify(values) !== JSON.stringify(saved);
  const fromAddressError =
    values && submitted && values.fromAddress.trim() !== "" && !EMAIL_PATTERN.test(values.fromAddress.trim()) ? t("Email:FromAddress.Invalid") : undefined;
  const effective = settings.data?.effective;

  const save = async () => {
    if (!values) return;
    setSubmitted(true);
    if (values.fromAddress.trim() !== "" && !EMAIL_PATTERN.test(values.fromAddress.trim())) return;
    setSaveError(undefined);
    try {
      await update.mutateAsync(toInput(values));
      setSubmitted(false);
      toast.success(t("Email:Saved"));
    } catch (e) {
      setSaveError(getApiErrorMessage(e, t));
    }
  };

  const commands = useMemo<AppCommandBarCommand[]>(() => {
    const list: AppCommandBarCommand[] = [];
    if (canUpdate) {
      list.push({ id: "save", labelKey: "Email:Save", icon: Save, isPrimary: true, disabled: update.isPending, onClick: () => void save() });
    }
    if (canTest) {
      /* UI-PERM-005: disabled for a state (unsaved changes, nothing configured), never for a permission. */
      const reason = isDirty ? "Email:SendTestUnsaved" : effective?.isConfigured ? undefined : "Email:SendTestNotConfigured";
      list.push({ id: "send-test", labelKey: "Email:SendTest", icon: Send, disabled: reason != null, disabledReasonKey: reason, onClick: () => setTestOpen(true) });
    }
    return list;
    // eslint-disable-next-line react-hooks/exhaustive-deps -- save reads the current values
  }, [canUpdate, canTest, isDirty, effective?.isConfigured, update.isPending, values]);

  if (!isHost || !canView) {
    return <PermissionDenied />;
  }

  const set = <K extends keyof FormValues>(key: K, value: FormValues[K]) => setValues((v) => (v ? { ...v, [key]: value } : v));
  const readOnly = !canUpdate;
  const defaultHint = (value: string | null | undefined) => (value ? t("Email:Default").replace("{0}", value) : undefined);
  const tokenHint = settings.data?.hasServerToken
    ? t("Email:ServerToken.Stored")
    : effective?.serverTokenSource === EMAIL_SETTING_SOURCE.Configuration
      ? t("Email:ServerToken.FromConfiguration")
      : t("Email:ServerToken.None");

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} />
      <div className="min-h-0 flex-1 overflow-auto p-4">
        {settings.isError ? (
          <Alert variant="destructive">
            <AlertDescription>{getApiErrorMessage(settings.error, t)}</AlertDescription>
          </Alert>
        ) : !values || !settings.data ? (
          <div className="grid max-w-3xl gap-4">
            <Skeleton className="h-32" />
            <Skeleton className="h-32" />
          </div>
        ) : (
          <form
            className="grid max-w-3xl gap-4"
            noValidate
            onSubmit={(e) => {
              e.preventDefault();
              void save();
            }}
          >
            <Alert variant={effective?.isConfigured ? "default" : "destructive"}>
              <AlertDescription>{t(effective?.isConfigured ? "Email:Status.Configured" : "Email:Status.NotConfigured")}</AlertDescription>
            </Alert>
            {saveError && (
              <Alert variant="destructive">
                <AlertDescription>{saveError}</AlertDescription>
              </Alert>
            )}

            <FormSection title={t("Email:Section.Service")}>
              <div className="grid grid-cols-1 gap-3">
                <FormField label={t("Email:ServerToken")} htmlFor="email-server-token" hint={tokenHint}>
                  <Input
                    id="email-server-token"
                    type="password"
                    autoComplete="off"
                    value={values.serverToken}
                    maxLength={256}
                    disabled={readOnly || values.clearServerToken}
                    onChange={(e) => set("serverToken", e.target.value)}
                  />
                </FormField>
                {settings.data.hasServerToken && (
                  <label className="flex items-center gap-2 text-body">
                    <Checkbox checked={values.clearServerToken} disabled={readOnly} onCheckedChange={(c) => set("clearServerToken", c === true)} />
                    {t("Email:ClearServerToken")}
                  </label>
                )}
              </div>
            </FormSection>

            <FormSection title={t("Email:Section.Sender")}>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <FormField label={t("Email:FromAddress")} htmlFor="email-from-address" error={fromAddressError} hint={defaultHint(effective?.fromAddress)}>
                  <Input id="email-from-address" type="email" value={values.fromAddress} maxLength={512} disabled={readOnly} onChange={(e) => set("fromAddress", e.target.value)} />
                </FormField>
                <FormField label={t("Email:FromName")} htmlFor="email-from-name" hint={defaultHint(effective?.fromName)}>
                  <Input id="email-from-name" value={values.fromName} maxLength={100} disabled={readOnly} onChange={(e) => set("fromName", e.target.value)} />
                </FormField>
              </div>
            </FormSection>

            <FormSection title={t("Email:Section.Tracking")}>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <FormField label={t("Email:TrackOpens")} htmlFor="email-track-opens" floating="always" hint={defaultHint(effective ? t(effective.trackOpens ? "Administration:Yes" : "Administration:No") : undefined)}>
                  <Select
                    items={[
                      { value: DEFAULT, label: t("Email:UseDefault") },
                      { value: "true", label: t("Administration:Yes") },
                      { value: "false", label: t("Administration:No") },
                    ]}
                    value={values.trackOpens}
                    disabled={readOnly}
                    onValueChange={(next) => set("trackOpens", String(next))}
                  >
                    <SelectTrigger id="email-track-opens" className="h-10 w-full bg-background">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={DEFAULT}>{t("Email:UseDefault")}</SelectItem>
                      <SelectItem value="true">{t("Administration:Yes")}</SelectItem>
                      <SelectItem value="false">{t("Administration:No")}</SelectItem>
                    </SelectContent>
                  </Select>
                </FormField>
                <FormField
                  label={t("Email:TrackLinks")}
                  htmlFor="email-track-links"
                  floating="always"
                  hint={defaultHint(effective?.trackLinks ? t(`Email:TrackLinks.${effective.trackLinks}`) : undefined)}
                >
                  <Select
                    items={[{ value: DEFAULT, label: t("Email:UseDefault") }, ...TRACK_LINKS_VALUES.map((v) => ({ value: v, label: t(`Email:TrackLinks.${v}`) }))]}
                    value={values.trackLinks}
                    disabled={readOnly}
                    onValueChange={(next) => set("trackLinks", String(next))}
                  >
                    <SelectTrigger id="email-track-links" className="h-10 w-full bg-background">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={DEFAULT}>{t("Email:UseDefault")}</SelectItem>
                      {TRACK_LINKS_VALUES.map((v) => (
                        <SelectItem key={v} value={v}>
                          {t(`Email:TrackLinks.${v}`)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </FormField>
              </div>
            </FormSection>

            <FormSection title={t("Email:Section.Webhook")}>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div className="flex items-start gap-2 sm:col-span-2">
                  <FormField label={t("Email:WebhookUrl")} htmlFor="email-webhook-url" hint={t("Email:WebhookUrl.Hint")} className="flex-1">
                    <Input id="email-webhook-url" value={settings.data.webhookUrl ?? ""} readOnly />
                  </FormField>
                  {settings.data.webhookUrl && (
                    <Button
                      type="button"
                      variant="outline"
                      className="h-10"
                      aria-label={t("Email:Copy")}
                      onClick={() => void navigator.clipboard?.writeText(settings.data!.webhookUrl!)}
                    >
                      <Copy className="size-4" />
                    </Button>
                  )}
                </div>
                <FormField label={t("Email:WebhookUsername")} htmlFor="email-webhook-username">
                  <Input id="email-webhook-username" autoComplete="off" value={values.webhookUsername} maxLength={256} disabled={readOnly} onChange={(e) => set("webhookUsername", e.target.value)} />
                </FormField>
                <FormField
                  label={t("Email:WebhookPassword")}
                  htmlFor="email-webhook-password"
                  hint={settings.data.hasWebhookPassword ? t("Email:WebhookPassword.Stored") : undefined}
                >
                  <Input
                    id="email-webhook-password"
                    type="password"
                    autoComplete="new-password"
                    value={values.webhookPassword}
                    maxLength={256}
                    disabled={readOnly || values.clearWebhookPassword}
                    onChange={(e) => set("webhookPassword", e.target.value)}
                  />
                </FormField>
                {settings.data.hasWebhookPassword && (
                  <label className="flex items-center gap-2 text-body sm:col-span-2">
                    <Checkbox checked={values.clearWebhookPassword} disabled={readOnly} onCheckedChange={(c) => set("clearWebhookPassword", c === true)} />
                    {t("Email:ClearWebhookPassword")}
                  </label>
                )}
              </div>
            </FormSection>
          </form>
        )}
      </div>
      {testOpen && <SendTestEmailDialog onClose={() => setTestOpen(false)} />}
    </div>
  );
}
