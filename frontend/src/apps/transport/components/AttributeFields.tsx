import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The dynamic attribute fields of an order, a line or a trip (design 56, Q12: one `Attributes` dictionary with a
 * tenant-level definition list per target). One Input per definition, in the definitions' position order; a stored
 * key without a definition (archived meanwhile) stays visible and editable so no value is lost. Keys render as text. */

export interface AttributeDefinition {
  key?: string | null;
  name?: string | null;
}

export interface AttributeFieldsProps {
  idPrefix: string;
  definitions: AttributeDefinition[] | undefined;
  value: Record<string, string>;
  onChange: (value: Record<string, string>) => void;
  readOnly?: boolean;
  /** The section caption; omit to render the fields without a section (inside another section). */
  title?: string;
  className?: string;
}

const MAX_VALUE_LENGTH = 500;

export function AttributeFields({ idPrefix, definitions, value, onChange, readOnly, title, className }: AttributeFieldsProps) {
  const { t } = useL();
  const defined = (definitions ?? []).filter((d): d is { key: string; name?: string | null } => !!d.key);
  const orphans = Object.keys(value).filter((key) => !defined.some((d) => d.key === key));
  const fields = [...defined.map((d) => ({ key: d.key, label: d.name ?? d.key })), ...orphans.map((key) => ({ key, label: key }))];
  if (fields.length === 0) return null;

  const set = (key: string, next: string) => {
    const copy = { ...value };
    if (next === "") delete copy[key];
    else copy[key] = next;
    onChange(copy);
  };

  const grid = (
    <div className={`grid grid-cols-2 gap-x-4 gap-y-3 ${title ? "" : (className ?? "")}`} data-slot="attribute-fields">
      {fields.map((field) => (
        <FormField key={field.key} label={field.label} htmlFor={`${idPrefix}-attr-${field.key}`}>
          <Input id={`${idPrefix}-attr-${field.key}`} value={value[field.key] ?? ""} maxLength={MAX_VALUE_LENGTH} disabled={readOnly} onChange={(e) => set(field.key, e.target.value)} />
        </FormField>
      ))}
    </div>
  );

  return title ? (
    <FormSection title={title ?? t("Orders:Attributes")} className={className}>
      {grid}
    </FormSection>
  ) : (
    grid
  );
}
