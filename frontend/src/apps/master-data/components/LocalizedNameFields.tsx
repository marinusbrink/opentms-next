import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import type { FormContext } from "@/apps/master-data/components/MasterDataEditor";
import { CONFIGURED_CULTURES, DEFAULT_CULTURE, type LocalizedNameValues } from "@/domains/master-data/localized-name";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* One Name field per configured language (design 41, "LocalizedName"); the default culture's
 * field is required, the others optional. The form keeps the texts under `names`. */
export function LocalizedNameFields<TValues extends { names: LocalizedNameValues }>({ form }: { form: FormContext<TValues> }) {
  const { t } = useL();
  return (
    <>
      {CONFIGURED_CULTURES.map((culture) => (
        <FormField
          key={culture}
          label={t("MasterData:NameIn").replace("{0}", culture)}
          htmlFor={form.id(`names-${culture}`)}
          required={culture === DEFAULT_CULTURE}
          error={culture === DEFAULT_CULTURE ? form.error("names") : undefined}
        >
          <Input
            id={form.id(`names-${culture}`)}
            value={form.values.names[culture]}
            maxLength={128}
            disabled={form.readOnly}
            onChange={(e) => form.set("names", { ...form.values.names, [culture]: e.target.value })}
            onBlur={() => form.blur("names")}
          />
        </FormField>
      ))}
    </>
  );
}
