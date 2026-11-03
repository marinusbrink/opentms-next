import { PlugZap, Save } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { Checkbox } from "@/components/ui/checkbox";
import { CopyButton } from "@/components/ui/copy-button";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import {
  DEFAULT_AI_MODEL,
  ROUTING_PROVIDERS,
  useServiceSettings,
  useTestService,
  useUpdateServiceSettings,
  type ServiceSettings,
  type TestedService,
  type UpdateServiceSettings,
} from "@/domains/integrations/service-settings";
import { healthComponentTexts, healthStatusKey, type HealthCheckEntry } from "@/domains/platform/administration-maintenance";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted, useIsHostSide } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/** The select's value for "use the deployment default" (no stored value). */
const DEFAULT = "default";

type UrlField = "pdfGeneratorApiBaseUrl" | "osrmBaseUrl" | "routeFunctionBaseUrl" | "vroomBaseUrl";
const URL_FIELDS: UrlField[] = ["pdfGeneratorApiBaseUrl", "osrmBaseUrl", "routeFunctionBaseUrl", "vroomBaseUrl"];

interface FormValues {
  pdfGeneratorApiBaseUrl: string;
  pdfGeneratorApiKey: string;
  clearPdfGeneratorApiKey: boolean;
  pdfGeneratorApiSecret: string;
  clearPdfGeneratorApiSecret: boolean;
  /** "default" or a ROUTING_PROVIDERS entry */
  routingProvider: string;
  osrmBaseUrl: string;
  routeFunctionBaseUrl: string;
  routeFunctionApiKey: string;
  clearRouteFunctionApiKey: boolean;
  vroomBaseUrl: string;
  /* Design 72, the Integrations tab. */
  aiApiKey: string;
  clearAiApiKey: boolean;
  aiLearningModel: string;
  aiExtractionModel: string;
  googleClientId: string;
  googleClientSecret: string;
  clearGoogleClientSecret: boolean;
  microsoftClientId: string;
  microsoftClientSecret: string;
  clearMicrosoftClientSecret: boolean;
}

function toForm(s: ServiceSettings): FormValues {
  return {
    pdfGeneratorApiBaseUrl: s.pdfGeneratorApiBaseUrl ?? "",
    pdfGeneratorApiKey: "",
    clearPdfGeneratorApiKey: false,
    pdfGeneratorApiSecret: "",
    clearPdfGeneratorApiSecret: false,
    routingProvider: s.routingProvider ?? DEFAULT,
    osrmBaseUrl: s.osrmBaseUrl ?? "",
    routeFunctionBaseUrl: s.routeFunctionBaseUrl ?? "",
    routeFunctionApiKey: "",
    clearRouteFunctionApiKey: false,
    vroomBaseUrl: s.vroomBaseUrl ?? "",
    aiApiKey: "",
    clearAiApiKey: false,
    aiLearningModel: s.aiLearningModel ?? "",
    aiExtractionModel: s.aiExtractionModel ?? "",
    googleClientId: s.googleClientId ?? "",
    googleClientSecret: "",
    clearGoogleClientSecret: false,
    microsoftClientId: s.microsoftClientId ?? "",
    microsoftClientSecret: "",
    clearMicrosoftClientSecret: false,
  };
}

function toInput(v: FormValues): UpdateServiceSettings {
  return {
    pdfGeneratorApiBaseUrl: v.pdfGeneratorApiBaseUrl.trim() || null,
    pdfGeneratorApiKey: v.pdfGeneratorApiKey.trim() || null,
    clearPdfGeneratorApiKey: v.clearPdfGeneratorApiKey,
    pdfGeneratorApiSecret: v.pdfGeneratorApiSecret.trim() || null,
    clearPdfGeneratorApiSecret: v.clearPdfGeneratorApiSecret,
    routingProvider: v.routingProvider === DEFAULT ? null : v.routingProvider,
    osrmBaseUrl: v.osrmBaseUrl.trim() || null,
    routeFunctionBaseUrl: v.routeFunctionBaseUrl.trim() || null,
    routeFunctionApiKey: v.routeFunctionApiKey.trim() || null,
    clearRouteFunctionApiKey: v.clearRouteFunctionApiKey,
    vroomBaseUrl: v.vroomBaseUrl.trim() || null,
    aiApiKey: v.aiApiKey.trim() || null,
    clearAiApiKey: v.clearAiApiKey,
    aiLearningModel: v.aiLearningModel.trim() || null,
    aiExtractionModel: v.aiExtractionModel.trim() || null,
    googleClientId: v.googleClientId.trim() || null,
    googleClientSecret: v.googleClientSecret.trim() || null,
    clearGoogleClientSecret: v.clearGoogleClientSecret,
    microsoftClientId: v.microsoftClientId.trim() || null,
    microsoftClientSecret: v.microsoftClientSecret.trim() || null,
    clearMicrosoftClientSecret: v.clearMicrosoftClientSecret,
  };
}

/* The Integrations tab's test answers one entry per section (design 72 "Host settings"); each section shows its own. */
const INTEGRATIONS_CHECKS = { ai: "anthropic", google: "google-oauth", microsoft: "microsoft-oauth" } as const;

function isUrl(value: string): boolean {
  if (value.trim() === "") return true;
  try {
    const url = new URL(value.trim());
    return url.protocol === "http:" || url.protocol === "https:";
  } catch {
    return false;
  }
}

/* The outcome of one service's test: the same checks and texts as the Health tab's components. */
function TestResult({ entries }: { entries: HealthCheckEntry[] }) {
  const { t } = useL();
  return (
    <div className="grid gap-2" aria-label={t("Services:TestResult")}>
      {entries.map((entry) => {
        const texts = healthComponentTexts(entry, t);
        return (
          <Alert key={entry.name} variant={entry.status === "Unhealthy" ? "destructive" : entry.status === "Degraded" ? "warning" : "default"}>
            <AlertDescription>
              <span className={cn("font-medium", entry.status === "Degraded" && "text-tone-warning")}>
                {texts.title}: {t(healthStatusKey(entry.status))}
              </span>
              <span className="block">{texts.finding}</span>
              {entry.error && <span className="block break-words">{entry.error}</span>}
            </AlertDescription>
          </Alert>
        );
      })}
    </div>
  );
}

/* The test entry of one section of the Integrations tab; a section the test did not answer shows nothing. */
function SectionResult({ entries, name }: { entries: HealthCheckEntry[]; name: string }) {
  const own = entries.filter((entry) => entry.name === name);
  return own.length === 0 ? null : (
    <div className="mt-3">
      <TestResult entries={own} />
    </div>
  );
}

/* One tab of Administration → Services (docs/designs/maintenance-view.md, "Services"), modelled on the e-mail settings of
 * design 64: the settings of one service with one Save (UI-PAGE-006) and its connection test. Host only: the permission
 * is host-side, and the view refuses on the tenant side as well. Keys and secrets are write-only: empty keeps what is
 * stored. An empty field falls back to the deployment's configuration, shown as the field's hint. A save sends the other
 * service's stored values back unchanged (its secrets empty, so kept). */
export function ServiceSettingsView({ service }: { service: TestedService }) {
  const { t } = useL();
  const isHost = useIsHostSide();
  const canView = useIsGranted("Integrations.ServiceSettings");
  const canUpdate = useIsGranted("Integrations.ServiceSettings.Update");
  const canTest = useIsGranted("Integrations.ServiceSettings.Test");
  const settings = useServiceSettings();
  const update = useUpdateServiceSettings();
  const test = useTestService();

  const [values, setValues] = useState<FormValues | undefined>();
  const [submitted, setSubmitted] = useState(false);
  /* UI-FORM-006: an address is validated when the field loses focus, and every field again on Save. */
  const [touched, setTouched] = useState<ReadonlySet<UrlField>>(new Set());
  const [saveError, setSaveError] = useState<string | undefined>();
  const [result, setResult] = useState<HealthCheckEntry[] | undefined>();
  const [testError, setTestError] = useState<string | undefined>();

  const saved = useMemo(() => (settings.data ? toForm(settings.data) : undefined), [settings.data]);
  useEffect(() => {
    if (saved) setValues(saved);
  }, [saved]);

  /* UI-FORM-009: dirty compares values. */
  const isDirty = values != null && saved != null && JSON.stringify(values) !== JSON.stringify(saved);
  const urlError = (field: UrlField) =>
    values && (submitted || touched.has(field)) && !isUrl(values[field]) ? t("Services:Url.Invalid") : undefined;
  const blur = (field: UrlField) => () => setTouched((current) => new Set(current).add(field));
  const testLabelKey = service === "documents" ? "Services:TestDocuments" : service === "routing" ? "Services:TestRouting" : "Services:TestIntegrations";

  const save = async () => {
    if (!values) return;
    setSubmitted(true);
    if (URL_FIELDS.some((field) => !isUrl(values[field]))) return;
    setSaveError(undefined);
    try {
      await update.mutateAsync(toInput(values));
      setSubmitted(false);
      setResult(undefined);
      toast.success(t("Services:Saved"));
    } catch (e) {
      setSaveError(getApiErrorMessage(e, t));
    }
  };

  const runTest = async () => {
    setTestError(undefined);
    try {
      setResult(await test.mutateAsync(service));
    } catch (e) {
      setTestError(getApiErrorMessage(e, t));
    }
  };

  const commands = useMemo<AppCommandBarCommand[]>(() => {
    const list: AppCommandBarCommand[] = [];
    if (canUpdate) {
      list.push({ id: "save", labelKey: "Services:Save", icon: Save, isPrimary: true, disabled: update.isPending, onClick: () => void save() });
    }
    if (canTest) {
      /* UI-PERM-005: disabled for a state (unsaved changes, a test running), never for a permission. */
      const reason = isDirty ? "Services:TestUnsaved" : undefined;
      list.push({ id: "test", labelKey: testLabelKey, icon: PlugZap, disabled: reason != null || test.isPending, disabledReasonKey: reason, onClick: () => void runTest() });
    }
    return list;
    // eslint-disable-next-line react-hooks/exhaustive-deps -- save and runTest read the current values
  }, [canUpdate, canTest, isDirty, update.isPending, test.isPending, values, service]);

  if (!isHost || !canView) {
    return <PermissionDenied />;
  }

  const set = <K extends keyof FormValues>(key: K, value: FormValues[K]) => setValues((v) => (v ? { ...v, [key]: value } : v));
  const readOnly = !canUpdate;
  const defaults = settings.data?.defaults;
  const defaultHint = (value: string | null | undefined) => (value ? t("Services:Default").replace("{0}", value) : t("Services:DefaultNone"));
  const secretHint = (stored: boolean | undefined, inDeployment: boolean | undefined) =>
    stored ? t("Services:Secret.Stored") : inDeployment ? t("Services:Secret.FromConfiguration") : t("Services:Secret.None");
  const providerLabel = (provider: string | null | undefined) => (provider ? t(`Services:Provider.${provider}`) : null);

  const secretField = (
    id: string,
    label: string,
    value: string,
    clear: boolean,
    stored: boolean | undefined,
    inDeployment: boolean | undefined,
    onValue: (v: string) => void,
    onClear: (c: boolean) => void,
  ) => (
    <div className="grid gap-2">
      <FormField label={label} htmlFor={id} hint={secretHint(stored, inDeployment)}>
        <Input id={id} type="password" autoComplete="off" value={value} maxLength={256} disabled={readOnly || clear} onChange={(e) => onValue(e.target.value)} />
      </FormField>
      {stored && (
        <label className="flex items-center gap-2 text-body">
          <Checkbox checked={clear} disabled={readOnly} onCheckedChange={(c) => onClear(c === true)} />
          {t("Services:Clear")}
        </label>
      )}
    </div>
  );

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
            <p className="text-label text-muted-foreground">{t("Services:Notice")}</p>
            {saveError && (
              <Alert variant="destructive">
                <AlertDescription>{saveError}</AlertDescription>
              </Alert>
            )}
            {testError && (
              <Alert variant="destructive">
                <AlertDescription>{testError}</AlertDescription>
              </Alert>
            )}

            {service === "documents" && (
              <FormSection title={t("Services:Section.Documents")}>
                <p className="mb-3 text-label text-muted-foreground">{t("Services:Section.Documents.Description")}</p>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <FormField
                    label={t("Services:BaseUrl")}
                    htmlFor="services-pdf-url"
                    error={urlError("pdfGeneratorApiBaseUrl")}
                    hint={defaultHint(defaults?.pdfGeneratorApiBaseUrl)}
                    className="sm:col-span-2"
                  >
                    <Input
                      id="services-pdf-url"
                      type="url"
                      value={values.pdfGeneratorApiBaseUrl}
                      maxLength={512}
                      disabled={readOnly}
                      onChange={(e) => set("pdfGeneratorApiBaseUrl", e.target.value)} onBlur={blur("pdfGeneratorApiBaseUrl")}
                    />
                  </FormField>
                  {secretField(
                    "services-pdf-key",
                    t("Services:Key"),
                    values.pdfGeneratorApiKey,
                    values.clearPdfGeneratorApiKey,
                    settings.data.hasPdfGeneratorApiKey,
                    defaults?.hasPdfGeneratorApiKey,
                    (v) => set("pdfGeneratorApiKey", v),
                    (c) => set("clearPdfGeneratorApiKey", c),
                  )}
                  {secretField(
                    "services-pdf-secret",
                    t("Services:Secret"),
                    values.pdfGeneratorApiSecret,
                    values.clearPdfGeneratorApiSecret,
                    settings.data.hasPdfGeneratorApiSecret,
                    defaults?.hasPdfGeneratorApiSecret,
                    (v) => set("pdfGeneratorApiSecret", v),
                    (c) => set("clearPdfGeneratorApiSecret", c),
                  )}
                </div>
                {result && (
                  <div className="mt-3">
                    <TestResult entries={result} />
                  </div>
                )}
              </FormSection>
            )}

            {service === "routing" && (
              <FormSection title={t("Services:Section.Routing")}>
                <p className="mb-3 text-label text-muted-foreground">{t("Services:Section.Routing.Description")}</p>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <FormField
                    label={t("Services:Provider")}
                    htmlFor="services-routing-provider"
                    floating="always"
                    hint={defaultHint(providerLabel(defaults?.routingProvider))}
                    className="sm:col-span-2"
                  >
                    <Select
                      items={[{ value: DEFAULT, label: t("Services:UseDefault") }, ...ROUTING_PROVIDERS.map((p) => ({ value: p, label: t(`Services:Provider.${p}`) }))]}
                      value={values.routingProvider}
                      disabled={readOnly}
                      onValueChange={(next) => set("routingProvider", String(next))}
                    >
                      <SelectTrigger id="services-routing-provider" className="h-10 w-full bg-background">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value={DEFAULT}>{t("Services:UseDefault")}</SelectItem>
                        {ROUTING_PROVIDERS.map((p) => (
                          <SelectItem key={p} value={p}>
                            {t(`Services:Provider.${p}`)}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </FormField>
                  <FormField label={t("Services:OsrmBaseUrl")} htmlFor="services-osrm-url" error={urlError("osrmBaseUrl")} hint={defaultHint(defaults?.osrmBaseUrl)}>
                    <Input id="services-osrm-url" type="url" value={values.osrmBaseUrl} maxLength={512} disabled={readOnly} onChange={(e) => set("osrmBaseUrl", e.target.value)} onBlur={blur("osrmBaseUrl")} />
                  </FormField>
                  <FormField label={t("Services:VroomBaseUrl")} htmlFor="services-vroom-url" error={urlError("vroomBaseUrl")} hint={defaultHint(defaults?.vroomBaseUrl)}>
                    <Input id="services-vroom-url" type="url" value={values.vroomBaseUrl} maxLength={512} disabled={readOnly} onChange={(e) => set("vroomBaseUrl", e.target.value)} onBlur={blur("vroomBaseUrl")} />
                  </FormField>
                  <FormField
                    label={t("Services:RouteFunctionBaseUrl")}
                    htmlFor="services-function-url"
                    error={urlError("routeFunctionBaseUrl")}
                    hint={defaultHint(defaults?.routeFunctionBaseUrl)}
                  >
                    <Input
                      id="services-function-url"
                      type="url"
                      value={values.routeFunctionBaseUrl}
                      maxLength={512}
                      disabled={readOnly}
                      onChange={(e) => set("routeFunctionBaseUrl", e.target.value)} onBlur={blur("routeFunctionBaseUrl")}
                    />
                  </FormField>
                  {secretField(
                    "services-function-key",
                    t("Services:RouteFunctionApiKey"),
                    values.routeFunctionApiKey,
                    values.clearRouteFunctionApiKey,
                    settings.data.hasRouteFunctionApiKey,
                    defaults?.hasRouteFunctionApiKey,
                    (v) => set("routeFunctionApiKey", v),
                    (c) => set("clearRouteFunctionApiKey", c),
                  )}
                </div>
                {result && (
                  <div className="mt-3">
                    <TestResult entries={result} />
                  </div>
                )}
              </FormSection>
            )}

            {service === "integrations" && (
              <>
                <FormSection title={t("Services:Section.Ai")}>
                  <p className="mb-3 text-label text-muted-foreground">{t("Services:Section.Ai.Description")}</p>
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <div className="sm:col-span-2">
                      {secretField(
                        "services-ai-key",
                        t("Services:AiApiKey"),
                        values.aiApiKey,
                        values.clearAiApiKey,
                        settings.data.hasAiApiKey,
                        defaults?.hasAiApiKey,
                        (v) => set("aiApiKey", v),
                        (c) => set("clearAiApiKey", c),
                      )}
                    </div>
                    <FormField label={t("Services:AiLearningModel")} htmlFor="services-ai-learning-model" hint={defaultHint(defaults?.aiLearningModel || DEFAULT_AI_MODEL)}>
                      <Input id="services-ai-learning-model" autoComplete="off" value={values.aiLearningModel} maxLength={64} disabled={readOnly} onChange={(e) => set("aiLearningModel", e.target.value)} />
                    </FormField>
                    <FormField label={t("Services:AiExtractionModel")} htmlFor="services-ai-extraction-model" hint={defaultHint(defaults?.aiExtractionModel || DEFAULT_AI_MODEL)}>
                      <Input id="services-ai-extraction-model" autoComplete="off" value={values.aiExtractionModel} maxLength={64} disabled={readOnly} onChange={(e) => set("aiExtractionModel", e.target.value)} />
                    </FormField>
                  </div>
                  {result && <SectionResult entries={result} name={INTEGRATIONS_CHECKS.ai} />}
                </FormSection>

                <FormSection title={t("Services:Section.Google")}>
                  <p className="mb-3 text-label text-muted-foreground">{t("Services:Section.Google.Description")}</p>
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <FormField label={t("Services:ClientId")} htmlFor="services-google-client-id" hint={defaultHint(defaults?.googleClientId)}>
                      <Input id="services-google-client-id" autoComplete="off" value={values.googleClientId} maxLength={256} disabled={readOnly} onChange={(e) => set("googleClientId", e.target.value)} />
                    </FormField>
                    {secretField(
                      "services-google-secret",
                      t("Services:ClientSecret"),
                      values.googleClientSecret,
                      values.clearGoogleClientSecret,
                      settings.data.hasGoogleClientSecret,
                      defaults?.hasGoogleClientSecret,
                      (v) => set("googleClientSecret", v),
                      (c) => set("clearGoogleClientSecret", c),
                    )}
                  </div>
                  {result && <SectionResult entries={result} name={INTEGRATIONS_CHECKS.google} />}
                </FormSection>

                <FormSection title={t("Services:Section.Microsoft")}>
                  <p className="mb-3 text-label text-muted-foreground">{t("Services:Section.Microsoft.Description")}</p>
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <FormField label={t("Services:ClientId")} htmlFor="services-microsoft-client-id" hint={defaultHint(defaults?.microsoftClientId)}>
                      <Input id="services-microsoft-client-id" autoComplete="off" value={values.microsoftClientId} maxLength={256} disabled={readOnly} onChange={(e) => set("microsoftClientId", e.target.value)} />
                    </FormField>
                    {secretField(
                      "services-microsoft-secret",
                      t("Services:ClientSecret"),
                      values.microsoftClientSecret,
                      values.clearMicrosoftClientSecret,
                      settings.data.hasMicrosoftClientSecret,
                      defaults?.hasMicrosoftClientSecret,
                      (v) => set("microsoftClientSecret", v),
                      (c) => set("clearMicrosoftClientSecret", c),
                    )}
                  </div>
                  {result && <SectionResult entries={result} name={INTEGRATIONS_CHECKS.microsoft} />}
                </FormSection>

                {/* The redirect URI to register at Google and Microsoft (read-only, from App:SelfUrl). */}
                <FormSection title={t("Services:MailboxRedirectUri")}>
                  <div className="flex items-start gap-2">
                    <FormField label={t("Services:MailboxRedirectUri")} htmlFor="services-redirect-uri" hint={t("Services:MailboxRedirectUri.Hint")} className="flex-1">
                      <Input id="services-redirect-uri" value={settings.data.mailboxRedirectUri ?? ""} readOnly />
                    </FormField>
                    {settings.data.mailboxRedirectUri && (
                      <CopyButton value={settings.data.mailboxRedirectUri} label={t("Services:CopyRedirectUri")} className="mt-2" />
                    )}
                  </div>
                </FormSection>
              </>
            )}
          </form>
        )}
      </div>
    </div>
  );
}
