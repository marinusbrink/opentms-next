import { useCallback, useMemo, useState } from "react";

import { Badge } from "@/components/ui/badge";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { useCanCreateInPlace, useCreateInPlace } from "@/apps/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { RelationEditor } from "@/apps/master-data/editors/RelationEditor";
import {
  productRelationCodes,
  products,
  relationLookupPage,
  transportUnits,
  type ProductRelationCodeDto,
  type RelationDto,
  type RelationOption,
} from "@/domains/master-data/entities";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The product relation code editor (design 48, UI design): `md`; Code, Archived, Destination (EntityLookup over the
 * relations, with "Add new relation…"), Product and Transport unit (ReferenceCombobox), Description. At least one of
 * destination, product and transport unit is required; the code is unique per tenant regardless of case. */

interface Values {
  code: string;
  isArchived: boolean;
  relation: RelationOption | null;
  productId: string | null;
  transportUnitId: string | null;
  description: string;
}

const initialValuesFor = (initialName: string | undefined) => (dto: ProductRelationCodeDto | undefined): Values => ({
  code: dto?.code ?? initialName ?? "",
  isArchived: dto?.isArchived ?? false,
  relation: dto?.relationId
    ? { id: dto.relationId, code: dto.relationCode ?? "", name: dto.relationName ?? "", city: dto.relationCity ?? null, countryCode: null, roles: 0 }
    : null,
  productId: dto?.productId ?? null,
  transportUnitId: dto?.transportUnitId ?? null,
  description: dto?.description ?? "",
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.code.trim()) errors.code = "MasterData:CodeRequired";
  if (!v.relation && !v.productId && !v.transportUnitId) errors.relation = "MasterData:ProductRelationCodeTargetRequired";
  return errors;
};

export function ProductRelationCodeEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const detail = productRelationCodes.useDetail(mode === "edit" ? id : undefined);
  const create = productRelationCodes.useCreate();
  const update = productRelationCodes.useUpdate();
  const remove = productRelationCodes.useDelete();
  const canDelete = useIsGranted("MasterData.ProductRelationCodes.Delete");
  /* The one "Add new …" gate (permission + node feature chain + not read-only, UI-LOOK-004 / UI-PERM-006). */
  const canCreate = useCanCreateInPlace();
  const canUpdateRelation = useIsGranted("MasterData.Relations.Update");
  const relationPage = useMemo(() => relationLookupPage(), []);
  const productLookup = products.useLookup(open);
  const unitLookup = transportUnits.useLookup(open);
  const productOptions = useMemo(() => (productLookup.data ?? []).map((i) => ({ value: i.id ?? "", label: i.name ?? "" })), [productLookup.data]);
  const unitOptions = useMemo(() => (unitLookup.data ?? []).map((i) => ({ value: i.id ?? "", label: i.name ?? "" })), [unitLookup.data]);

  const inPlace = useCreateInPlace();
  const [applyValues, setApplyValues] = useState<{ key: number; update: (values: Values) => Values } | undefined>();
  const [relationDialog, setRelationDialog] = useState<{ mode: "create"; term: string } | { mode: "edit"; id: string } | null>(null);

  const save = useCallback(
    async (v: Values, dto: ProductRelationCodeDto | undefined) => {
      const body = {
        code: v.code.trim(),
        isArchived: v.isArchived,
        relationId: v.relation?.id ?? null,
        productId: v.productId,
        transportUnitId: v.transportUnitId,
        description: v.description.trim() || null,
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["code", "isArchived", "relation", "productId", "transportUnitId", "description"],
      render: (form) => (
        <FormSection title={t("MasterData:ProductRelationCode")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={64} />
            <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
            <FormField label={t("MasterData:Destination")} htmlFor={form.id("relation")} error={form.error("relation")} floating="always" className="col-span-2">
              <EntityLookup<RelationOption>
                id={form.id("relation")}
                value={form.values.relation}
                onChange={(next) => {
                  form.set("relation", next);
                  form.blur("relation");
                }}
                fetchPage={relationPage}
                getLabel={(r) => r.name}
                renderOption={(r) => (
                  <>
                    <div className="font-medium">{r.name}</div>
                    <div className="text-label text-muted-foreground">{[r.code, [r.city, r.countryCode].filter(Boolean).join(" ")].filter(Boolean).join(", ")}</div>
                  </>
                )}
                onCreateNew={canCreate("Relation", form.readOnly) ? (term) => setRelationDialog({ mode: "create", term }) : undefined}
                createLabel={t("MasterData:AddNewRelation")}
                onEdit={canUpdateRelation && !form.readOnly ? (r) => setRelationDialog({ mode: "edit", id: r.id }) : undefined}
                aria-invalid={form.error("relation") != null}
                disabled={form.readOnly}
              />
            </FormField>
            {/* DEVIATION(UI-LOOK-001): product is a ReferenceCombobox, not an EntityLookup: run-1 lists without a lookup-page endpoint, small per tenant (the lookup's cap of 500 holds), approved in docs/designs/48-master-data-vehicles.md (UI design, product relation code editor). */}
            <FormField label={t("MasterData:Product")} htmlFor={form.id("productId")} floating="always">
              <ReferenceCombobox
                id={form.id("productId")}
                value={form.values.productId}
                onChange={(next) => form.set("productId", next)}
                options={productOptions}
                disabled={form.readOnly}
                {...inPlace.offer("Product", form.readOnly, (r) => form.set("productId", r.id))}
              />
            </FormField>
            {/* DEVIATION(UI-LOOK-001): transport unit is a ReferenceCombobox, not an EntityLookup: run-1 lists without a lookup-page endpoint, small per tenant (the lookup's cap of 500 holds), approved in docs/designs/48-master-data-vehicles.md (UI design, product relation code editor). */}
            <FormField label={t("MasterData:TransportUnit")} htmlFor={form.id("transportUnitId")} floating="always">
              <ReferenceCombobox
                id={form.id("transportUnitId")}
                value={form.values.transportUnitId}
                onChange={(next) => form.set("transportUnitId", next)}
                options={unitOptions}
                disabled={form.readOnly}
                {...inPlace.offer("TransportUnit", form.readOnly, (r) => form.set("transportUnitId", r.id))}
              />
            </FormField>
            <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} className="col-span-2" />
          </div>
        </FormSection>
      ),
    },
  ];

  return (
    <>
      <MasterDataEditor<Values, ProductRelationCodeDto>
        entityKey="ProductRelationCode"
        queryEntity={productRelationCodes.entity}
        size="md"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={detail}
        titleOf={(dto) => dto.code ?? ""}
        initialValues={initialValues}
        validate={validate}
        save={save}
        isSaving={create.isPending || update.isPending}
        tabs={tabs}
        deleteRecord={mode === "edit" && canDelete ? remove : undefined}
        badges={mode === "edit" && detail.data?.isArchived ? <Badge variant="outline">{t("MasterData:Archived")}</Badge> : undefined}
        applyValues={applyValues}
      />
      {relationDialog && (
        /* "Add new relation…" and the edit pencil of the destination lookup (UI-LOOK-004/005). */
        <RelationEditor
          mode={relationDialog.mode}
          id={relationDialog.mode === "edit" ? relationDialog.id : undefined}
          initialName={relationDialog.mode === "create" ? relationDialog.term : undefined}
          readOnly={false}
          open
          onClose={() => setRelationDialog(null)}
          onSaved={(saved) => {
            const dto = saved as RelationDto | undefined;
            if (!dto?.id) return;
            const option: RelationOption = { id: dto.id, code: dto.code ?? "", name: dto.name ?? "", city: null, countryCode: null, roles: dto.roles ?? 0 };
            setApplyValues((previous) => ({ key: (previous?.key ?? 0) + 1, update: (values) => ({ ...values, relation: option }) }));
          }}
        />
      )}
      {inPlace.dialog}
    </>
  );
}
