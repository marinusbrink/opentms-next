import { Save } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";

import { useAppFeatures } from "@/app/app-features";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { FormField } from "@/components/ui/form-field";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import {
  NAME_SIMILARITY_DEFAULT,
  NAME_SIMILARITY_MAX,
  NAME_SIMILARITY_MIN,
  NAME_SIMILARITY_VALUES,
  useOrderIntakeFeature,
  useOrderIntakeSettings,
  useUpdateOrderIntakeSettings,
} from "@/domains/integrations/order-intake";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/** The select's value for a threshold: two decimals, invariant ("0.75"). */
const toKey = (value: number) => value.toFixed(2);

/** The offered value nearest to a stored one (the backend rounds to two decimals and clamps to the range). */
function nearestOffered(value: number | undefined): number {
  const v = value ?? NAME_SIMILARITY_DEFAULT;
  return NAME_SIMILARITY_VALUES.reduce((best, candidate) => (Math.abs(candidate - v) < Math.abs(best - v) ? candidate : best), NAME_SIMILARITY_DEFAULT);
}

/* Integrations → Order intake → Settings (design 72 build note 25): the tenant's settings of the order intake, a settings
 * page with one Save (UI-PAGE-006). One topic, so one field and no section (UI-FORM-005): the name similarity a received
 * name needs to be matched to an existing record instead of creating a new one, 0.50 to 0.95 in steps of 0.05, offered
 * as a select so only allowed values can be chosen. One permission views and changes it (Integrations.OrderIntakeSettings);
 * order intake is a tenant feature and the node an app feature (UI-PERM-006). */
export function IntakeSettingsView() {
  const { t } = useL();
  const f = useFormat();
  const features = useAppFeatures();
  const feature = useOrderIntakeFeature();
  const canManage = useIsGranted("Integrations.OrderIntakeSettings");
  const nodeOff = features.isLoaded && !features.isFeatureEnabled("Apps.Integrations.Intake.Settings");
  const settings = useOrderIntakeSettings(feature === "on" && canManage && !nodeOff);
  const update = useUpdateOrderIntakeSettings();

  const saved = useMemo(() => (settings.data ? nearestOffered(settings.data.nameSimilarityThreshold) : undefined), [settings.data]);
  const [threshold, setThreshold] = useState<number | undefined>();
  const [saveError, setSaveError] = useState<string | undefined>();
  useEffect(() => {
    if (saved != null) setThreshold(saved);
  }, [saved]);

  const save = async () => {
    if (threshold == null) return;
    setSaveError(undefined);
    try {
      await update.mutateAsync({ nameSimilarityThreshold: threshold });
      toast.success(t("Integrations:IntakeSettings.Saved"));
    } catch (e) {
      setSaveError(getApiErrorMessage(e, t));
    }
  };

  const commands = useMemo<AppCommandBarCommand[]>(
    () => [
      {
        id: "save",
        labelKey: update.isPending ? "Saving" : "Save",
        icon: Save,
        isPrimary: true,
        disabled: update.isPending || threshold == null,
        onClick: () => void save(),
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps -- save reads the current value
    [update.isPending, threshold],
  );

  if (feature === "off" || nodeOff) return <FeatureUnavailable />;
  if (!canManage) return <PermissionDenied />;

  const label = (value: number) => {
    const text = f.number(value, { decimals: 2 });
    return value === NAME_SIMILARITY_DEFAULT ? t("Integrations:IntakeSettings.DefaultValue").replace("{0}", text) : text;
  };
  const items = NAME_SIMILARITY_VALUES.map((v) => ({ value: toKey(v), label: label(v) }));
  const hint = t("Integrations:IntakeSettings.NameSimilarity.Hint")
    .replace("{0}", f.number(NAME_SIMILARITY_MIN, { decimals: 2 }))
    .replace("{1}", f.number(NAME_SIMILARITY_MAX, { decimals: 2 }));

  return (
    <div className="flex h-full flex-col" data-slot="intake-settings-view">
      <AppCommandBar commands={commands} />
      <div className="min-h-0 flex-1 overflow-auto p-4">
        {settings.isError ? (
          <Alert variant="destructive">
            <AlertDescription>{getApiErrorMessage(settings.error, t)}</AlertDescription>
          </Alert>
        ) : threshold == null ? (
          <div className="grid max-w-3xl gap-4">
            <Skeleton className="h-24" />
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
            {saveError && (
              <Alert variant="destructive">
                <AlertDescription>{saveError}</AlertDescription>
              </Alert>
            )}
            <p className="text-body text-muted-foreground">{t("Integrations:IntakeSettings.Intro")}</p>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <FormField label={t("Integrations:IntakeSettings.NameSimilarity")} htmlFor="intake-settings-name-similarity" floating="always" hint={hint}>
                <Select items={items} value={toKey(threshold)} disabled={update.isPending} onValueChange={(next) => setThreshold(Number(next))}>
                  <SelectTrigger id="intake-settings-name-similarity" className="h-10 w-full bg-background">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {items.map((item) => (
                      <SelectItem key={item.value} value={item.value}>
                        {item.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </FormField>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
