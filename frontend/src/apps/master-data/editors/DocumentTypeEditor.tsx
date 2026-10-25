import { useCallback, useMemo } from "react";
import type { ColDef } from "ag-grid-community";
import { Plus } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { resolveLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";
import {
  DOCUMENT_TRANSFORMATION_KINDS,
  KINDS_WITH_ARGUMENT,
  MAX_DOCUMENT_TYPE_CODE_LENGTH,
  MAX_DOCUMENT_TYPE_TRANSFORMATIONS,
  MAX_PLATFORM_DESCRIPTION_LENGTH,
  MAX_PLATFORM_NAME_LENGTH,
  MAX_TRANSFORMATION_ARGUMENT_LENGTH,
  documentTransformationKindKey,
  documentTypes,
  type DocumentTransformationKind,
  type DocumentTypeDto,
} from "@/domains/platform/master-data";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The document type editor (design 54, UI design): `md`; tab General (Code upper-cased, the Name in the user's
 * language as the other LocalizedName editors (PO 2026-10-11), the three switches, Archived, Description) and tab
 * Transformations: a LinesGrid whose order is the position, a kind and an argument per row. Each kind at most once,
 * so at most four rows; RenameByParentProperty and SplitPdfByReferencePattern need an argument. The server checks the
 * same and compiles the regex. */

interface TransformationLine {
  key: string;
  position: number;
  kind: DocumentTransformationKind;
  argument: string | null;
}

interface Values {
  code: string;
  name: string;
  isFreightDocument: boolean;
  includeWithInvoice: boolean;
  showInListIndicator: boolean;
  isArchived: boolean;
  description: string;
  transformations: TransformationLine[];
}

let lineCounter = 0;

const initialValuesFor = (culture: string, initialName: string | undefined) => (dto: DocumentTypeDto | undefined): Values => ({
  code: dto?.code ?? "",
  name: dto ? resolveLocalizedName(dto.names, culture) || dto.name || "" : (initialName ?? ""),
  isFreightDocument: dto?.isFreightDocument ?? false,
  includeWithInvoice: dto?.includeWithInvoice ?? false,
  showInListIndicator: dto?.showInListIndicator ?? false,
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
  transformations: [...(dto?.transformations ?? [])]
    .sort((a, b) => (a.position ?? 0) - (b.position ?? 0))
    .map((row, index) => ({ key: `kind-${row.kind ?? index}`, position: index + 1, kind: row.kind ?? 1, argument: row.argument ?? null })),
});

const ordered = (lines: TransformationLine[]) => [...lines].sort((a, b) => a.position - b.position);

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.code.trim()) errors.code = "Platform:Required";
  if (!v.name.trim()) errors.name = "Platform:Required";
  const kinds = v.transformations.map((line) => line.kind);
  if (new Set(kinds).size !== kinds.length) errors.transformations = "Platform:TransformationKindTwice";
  else if (v.transformations.some((line) => KINDS_WITH_ARGUMENT.includes(line.kind) && !(line.argument ?? "").trim())) {
    errors.transformations = "Platform:TransformationArgumentMissing";
  }
  return errors;
};

export function DocumentTypeEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t, culture } = useL();
  const initialValues = useMemo(() => initialValuesFor(culture, mode === "create" ? initialName : undefined), [culture, mode, initialName]);
  const detail = documentTypes.useDetail(mode === "edit" ? id : undefined);
  const create = documentTypes.useCreate();
  const update = documentTypes.useUpdate();
  const remove = documentTypes.useDelete();
  const canDelete = useIsGranted("Platform.DocumentTypes.Delete");

  const save = useCallback(
    async (v: Values, dto: DocumentTypeDto | undefined) => {
      const body = {
        code: v.code.trim().toUpperCase(),
        names: withTextForCulture(dto?.names, culture, v.name),
        isFreightDocument: v.isFreightDocument,
        includeWithInvoice: v.includeWithInvoice,
        showInListIndicator: v.showInListIndicator,
        isArchived: v.isArchived,
        description: v.description.trim() || null,
        transformations: ordered(v.transformations).map((line, index) => ({
          position: index + 1,
          kind: line.kind,
          argument: (line.argument ?? "").trim() || null,
        })),
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id, culture],
  );

  const kindValues = useMemo(() => DOCUMENT_TRANSFORMATION_KINDS.map((k) => k.value), []);

  const transformationColumns = useMemo<ColDef<TransformationLine>[]>(
    () => [
      {
        field: "position",
        headerName: t("Platform:Position"),
        width: 110,
        minWidth: 90,
        editable: !readOnly,
        cellEditor: "agNumberCellEditor",
        cellEditorParams: { min: 1, max: MAX_DOCUMENT_TYPE_TRANSFORMATIONS, precision: 0 },
        type: "rightAligned",
        sort: "asc",
      },
      {
        field: "kind",
        headerName: t("Platform:Kind"),
        flex: 2,
        minWidth: 200,
        editable: !readOnly,
        cellEditor: "agRichSelectCellEditor",
        cellEditorParams: {
          values: kindValues,
          formatValue: (value: DocumentTransformationKind) => t(documentTransformationKindKey(value)),
        },
        valueFormatter: (p) => t(documentTransformationKindKey(p.value as DocumentTransformationKind | undefined)),
      },
      {
        field: "argument",
        headerName: t("Platform:Argument"),
        flex: 2,
        minWidth: 180,
        editable: !readOnly,
        cellEditor: "agTextCellEditor",
        cellEditorParams: { maxLength: MAX_TRANSFORMATION_ARGUMENT_LENGTH },
      },
    ],
    [kindValues, readOnly, t],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "Platform:Tab.General",
      fields: ["code", "name", "isFreightDocument", "includeWithInvoice", "showInListIndicator", "isArchived", "description"],
      render: (form) => (
        <FormSection title={t("Platform:DocumentType")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="code" labelKey="Platform:Code" required maxLength={MAX_DOCUMENT_TYPE_CODE_LENGTH} upper />
            <TextField form={form} field="name" labelKey="Platform:Name" required maxLength={MAX_PLATFORM_NAME_LENGTH} />
            <CheckboxField form={form} field="isFreightDocument" labelKey="Platform:IsFreightDocument" />
            <CheckboxField form={form} field="includeWithInvoice" labelKey="Platform:IncludeWithInvoice" />
            <CheckboxField form={form} field="showInListIndicator" labelKey="Platform:ShowInListIndicator" />
            <CheckboxField form={form} field="isArchived" labelKey="Platform:IsArchived" />
            <TextAreaField form={form} field="description" labelKey="Platform:Description" maxLength={MAX_PLATFORM_DESCRIPTION_LENGTH} className="col-span-2" />
          </div>
        </FormSection>
      ),
    },
    {
      value: "transformations",
      labelKey: "Platform:Tab.Transformations",
      fields: ["transformations"],
      render: (form) => {
        const used = new Set(form.values.transformations.map((line) => line.kind));
        const nextKind = DOCUMENT_TRANSFORMATION_KINDS.find((k) => !used.has(k.value))?.value;
        const canAdd = nextKind != null && form.values.transformations.length < MAX_DOCUMENT_TYPE_TRANSFORMATIONS;
        return (
          <div className="flex h-72 flex-col gap-2">
            {form.error("transformations") && <p className="text-label text-destructive">{form.error("transformations")}</p>}
            <LazyLinesGrid<TransformationLine>
              rows={form.values.transformations}
              onRowsChange={(rows) => {
                form.set("transformations", rows);
                form.blur("transformations");
              }}
              getRowId={(row) => row.key}
              columnDefs={transformationColumns}
              readOnly={form.readOnly}
              emptyText={t("Platform:NoTransformations")}
              toolbar={
                canAdd ? (
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={() =>
                      form.set("transformations", [
                        ...ordered(form.values.transformations),
                        { key: `new-${++lineCounter}`, position: form.values.transformations.length + 1, kind: nextKind!, argument: null },
                      ])
                    }
                  >
                    <Plus /> {t("Platform:AddTransformation")}
                  </Button>
                ) : undefined
              }
            />
          </div>
        );
      },
    },
  ];

  return (
    <MasterDataEditor<Values, DocumentTypeDto>
      entityKey="DocumentType"
      resource="Platform"
      queryEntity={documentTypes.entity}
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
      badges={mode === "edit" && detail.data?.isArchived ? <Badge variant="outline">{t("Platform:IsArchived")}</Badge> : undefined}
    />
  );
}
