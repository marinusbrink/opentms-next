import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { Save } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import type { AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { Checkbox } from "@/components/ui/checkbox";
import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { recordIdentificationTab } from "@/components/ui/record-identification-tab";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import {
  useCreateEdition,
  useEditionFeatures,
  useUpdateEdition,
  useUpdateEditionFeatures,
  type EditionFeature,
  type EditionRow,
} from "@/domains/platform/editions";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface EditionFormDialogProps {
  mode: "create" | "edit";
  edition?: EditionRow;
  /** No rename (the Update permission is missing); the Features tab follows canManageFeatures. */
  readOnly?: boolean;
  canManageFeatures: boolean;
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

/** Indent per feature depth; deeper levels than the tree has today use the last step. */
const DEPTH_INDENT = ["", "ps-6", "ps-12", "ps-18"] as const;

const TOGGLE = "ToggleStringValueType";
const SELECTION = "SelectionStringValueType";

/* The edition editor (docs/designs/tenants-and-editions.md): a name, and in edit mode the edition's feature
 * values, rendered from ABP's feature-management response — groups as sections, a toggle as a checkbox, a
 * selection as a select, anything else as a text field. A value set here applies to every tenant on the
 * edition unless the tenant has its own value. */
export function EditionFormDialog({
  mode,
  edition,
  readOnly = false,
  canManageFeatures,
  open,
  onClose,
  onSuccess,
}: EditionFormDialogProps) {
  const { t } = useL();
  const createEdition = useCreateEdition();
  const updateEdition = useUpdateEdition();
  const updateFeatures = useUpdateEditionFeatures();
  const features = useEditionFeatures(mode === "edit" && canManageFeatures ? edition?.id : undefined);
  const formRef = useRef<HTMLFormElement>(null);

  const initialName = mode === "edit" ? (edition?.displayName ?? "") : "";
  const [name, setName] = useState(initialName);
  const [nameTouched, setNameTouched] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState("general");

  const initialFeatureValues = useMemo(() => {
    const values: Record<string, string> = {};
    for (const group of features.data ?? []) {
      for (const feature of group.features) values[feature.name] = feature.value ?? "";
    }
    return values;
  }, [features.data]);
  const [featureValues, setFeatureValues] = useState<Record<string, string>>({});
  useEffect(() => setFeatureValues(initialFeatureValues), [initialFeatureValues]);

  const changedFeatures = Object.entries(featureValues)
    .filter(([key, value]) => initialFeatureValues[key] !== value)
    .map(([key, value]) => ({ name: key, value }));
  const nameChanged = name.trim() !== initialName.trim();
  const isDirty = (!readOnly && nameChanged) || changedFeatures.length > 0;
  const isPending = createEdition.isPending || updateEdition.isPending || updateFeatures.isPending;
  const canSave = !readOnly || (mode === "edit" && canManageFeatures);

  const nameError = name.trim() ? undefined : t("FieldRequired");
  const shownNameError = nameTouched || submitted ? nameError : undefined;

  const handleSubmit = async (e?: FormEvent) => {
    e?.preventDefault();
    if (!canSave || isPending) return;
    setSubmitted(true);
    setServerError(null);
    if (!readOnly && nameError) {
      setActiveTab("general");
      requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>("#ef-name")?.focus());
      return;
    }

    try {
      if (mode === "create") {
        await createEdition.mutateAsync({ displayName: name.trim() });
        toast.success(t("Editions:EditionCreated"));
      } else {
        if (!readOnly && nameChanged) {
          await updateEdition.mutateAsync({ id: edition!.id!, dto: { displayName: name.trim() } });
        }
        if (canManageFeatures && changedFeatures.length > 0) {
          await updateFeatures.mutateAsync({ editionId: edition!.id!, values: changedFeatures });
        }
        toast.success(t("Editions:EditionSaved"));
      }
      onSuccess();
      onClose();
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  const title = mode === "create" ? t("Editions:NewEdition") : `${t("Editions:Edition")} ${edition?.displayName ?? ""}`;

  const commands: AppCommandBarCommand[] = canSave
    ? [
        {
          id: "save",
          labelKey: isPending ? "Saving" : "Save",
          icon: Save,
          isPrimary: true,
          onClick: () => formRef.current?.requestSubmit(),
        },
      ]
    : [];

  const errorAlert = serverError && (
    <Alert variant="destructive">
      <AlertDescription>{serverError}</AlertDescription>
    </Alert>
  );

  const generalTab: EntityDialogTab = {
    value: "general",
    label: t("General"),
    hasError: shownNameError != null,
    content: (
      <form ref={formRef} onSubmit={(e) => void handleSubmit(e)} className="grid grid-cols-1 gap-4" noValidate>
        {errorAlert}
        <FormSection title={t("Editions:Edition")}>
          <FormField label={t("Editions:Name")} htmlFor="ef-name" required error={shownNameError}>
            <Input
              id="ef-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              onBlur={() => setNameTouched(true)}
              maxLength={256}
              disabled={readOnly}
            />
          </FormField>
        </FormSection>
        {mode === "create" && <p className="text-label text-muted-foreground">{t("Editions:FeaturesAfterCreate")}</p>}
        <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
      </form>
    ),
  };

  const featuresTab: EntityDialogTab | null =
    mode === "edit" && canManageFeatures
      ? {
          value: "features",
          label: t("Editions:Features"),
          content: (
            /* Only the feature list scrolls; the dialog itself never does (PO 2026-10-14, panelScroll). */
            <div className="flex h-full min-h-0 flex-col gap-4">
              {activeTab === "features" && errorAlert}
              {features.isLoading && <Skeleton className="h-40 w-full" />}
              {features.isError && (
                <Alert variant="destructive">
                  <AlertDescription>{getApiErrorMessage(features.error, t)}</AlertDescription>
                </Alert>
              )}
              {features.data?.length === 0 && <p className="text-sm text-muted-foreground">{t("Editions:NoFeatures")}</p>}
              <div data-slot="edition-feature-list" className="grid min-h-0 flex-1 grid-cols-1 content-start gap-4 overflow-y-auto pr-1">
                {features.data?.map((group) => (
                  <FormSection key={group.name} title={group.displayName}>
                    <div className="grid grid-cols-1 gap-3">
                      {group.features.map((feature) => (
                        <FeatureInput
                          key={feature.name}
                          feature={feature}
                          value={featureValues[feature.name] ?? ""}
                          onChange={(value) => setFeatureValues((v) => ({ ...v, [feature.name]: value }))}
                        />
                      ))}
                    </div>
                  </FormSection>
                ))}
              </div>
            </div>
          ),
        }
      : null;

  const tabs: EntityDialogTab[] = [
    generalTab,
    ...(featuresTab ? [featuresTab] : []),
    recordIdentificationTab(t, {
      id: mode === "edit" ? edition?.id : undefined,
      creationTime: mode === "edit" ? edition?.creationTime : undefined,
    }),
  ];

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="editor"
      size="md"
      title={title}
      commands={commands}
      tabs={tabs}
      activeTab={activeTab}
      onActiveTabChange={setActiveTab}
      isDirty={isDirty}
      isBusy={isPending}
      panelScroll={featuresTab != null}
    />
  );
}

function FeatureInput({
  feature,
  value,
  onChange,
}: {
  feature: EditionFeature;
  value: string;
  onChange: (value: string) => void;
}) {
  const { t } = useL();
  const id = `ef-feature-${feature.name.replace(/[^A-Za-z0-9_-]/g, "_")}`;
  /* Child features sit under their parent, one step in per level (ABP's depth), on the spacing scale (UI-TOK-001). */
  const indent = DEPTH_INDENT[Math.min(feature.depth, DEPTH_INDENT.length - 1)];
  const kind = feature.valueType?.name;

  if (kind === TOGGLE) {
    return (
      <label className={`flex items-start gap-2 text-sm ${indent}`} htmlFor={id}>
        <Checkbox id={id} checked={value === "true"} onCheckedChange={(checked) => onChange(checked === true ? "true" : "false")} />
        <span className="flex flex-col">
          <span>{feature.displayName}</span>
          {feature.description && <span className="text-label text-muted-foreground">{feature.description}</span>}
        </span>
      </label>
    );
  }

  if (kind === SELECTION) {
    const items = (feature.valueType?.itemSource?.items ?? []).map((item) => ({
      value: item.value ?? "",
      label: item.displayText?.name ? t(item.displayText.name) : (item.value ?? ""),
    }));
    return (
      <div className={indent}>
        <FormField label={feature.displayName} htmlFor={id} hint={feature.description ?? undefined} floating="always">
          <Select items={items} value={value} onValueChange={(next) => onChange(next ?? "")}>
            <SelectTrigger id={id} className="h-10 w-full bg-background">
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
    );
  }

  return (
    <div className={indent}>
      <FormField label={feature.displayName} htmlFor={id} hint={feature.description ?? undefined}>
        <Input id={id} value={value} onChange={(e) => onChange(e.target.value)} />
      </FormField>
    </div>
  );
}
