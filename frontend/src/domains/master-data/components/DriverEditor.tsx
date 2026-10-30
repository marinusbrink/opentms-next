import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { useQueryClient } from "@tanstack/react-query";
import { Plus, UserX } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DATE_CELL_EDITOR_COL_DEF } from "@/components/ui/date-picker";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { DateCell } from "@/components/ui/grid-cells";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { useCanCreateInPlace, useCanOpenInPlace, useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, TextAreaField, TextField, emailError } from "@/components/record-editor/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/components/record-editor/MasterDataEditor";
import { RelationEditor } from "@/domains/master-data/components/RelationEditor";
import {
  EMPLOYER_ROLES,
  QUALIFICATION_KINDS,
  drivers,
  qualificationKindKey,
  relationLookupPage,
  useAnonymiseDriver,
  userLookupPage,
  type DriverDto,
  type DriverQualificationKind,
  type RelationDto,
  type RelationOption,
  type UserOption,
} from "@/domains/master-data/entities";
import { masterDataQueryKey } from "@/domains/master-data/shared";
import type { UserRow } from "@/domains/platform/administration-users";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useLanguages } from "@/lib/i18n/languages";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The driver editor (design 43, UI design): `md`, General with the sections Person, Contact and
 * Employment (employer as an EntityLookup over the relations with the Carrier or OwnCompany role,
 * with "Add new relation…" per UI-LOOK-004; user account as an EntityLookup over the tenant's
 * users), the Qualifications tab as inline lines with the DatePicker cell editor, then Record
 * identification. Anonymise (GDPR art. 17) is a destructive, confirmed ribbon command behind
 * Drivers.Anonymise; an anonymised driver opens read-only with a notice. */

interface QualificationRow {
  key: string;
  id: string | null;
  kind: DriverQualificationKind;
  number: string;
  validFrom: string | null;
  validUntil: string | null;
  description: string;
}

interface Values {
  code: string;
  firstName: string;
  middleName: string;
  lastName: string;
  initials: string;
  phone: string;
  mobile: string;
  email: string;
  preferredCulture: string | null;
  employer: RelationOption | null;
  user: UserOption | null;
  isArchived: boolean;
  description: string;
  qualifications: QualificationRow[];
}

let rowCounter = 0;

/** A name typed in a driver lookup's "Add new …" (UI-LOOK-004): one word is the last name; with more, the first word is the first name. */
function splitTypedName(typed: string | undefined): { firstName: string; lastName: string } {
  const words = (typed ?? "").trim().split(/\s+/).filter(Boolean);
  return words.length > 1 ? { firstName: words[0], lastName: words.slice(1).join(" ") } : { firstName: "", lastName: words[0] ?? "" };
}

const initialValuesFor = (initialName: string | undefined) => (dto: DriverDto | undefined): Values => ({
  code: dto?.code ?? "",
  firstName: dto ? (dto.firstName ?? "") : splitTypedName(initialName).firstName,
  middleName: dto?.middleName ?? "",
  lastName: dto ? (dto.lastName ?? "") : splitTypedName(initialName).lastName,
  initials: dto?.initials ?? "",
  phone: dto?.phone ?? "",
  mobile: dto?.mobile ?? "",
  email: dto?.email ?? "",
  preferredCulture: dto?.preferredCulture ?? null,
  employer: dto?.employerRelationId ? { id: dto.employerRelationId, code: "", name: dto.employerName ?? "", city: null, countryCode: null, roles: 0 } : null,
  user: dto?.userId ? { id: dto.userId, name: dto.userName ?? "", userName: dto.userName ?? "", email: null } : null,
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
  qualifications: (dto?.qualifications ?? []).map((q) => ({
    key: q.id ?? `new-${++rowCounter}`,
    id: q.id ?? null,
    kind: q.kind ?? 0,
    number: q.number ?? "",
    validFrom: q.validFrom ?? null,
    validUntil: q.validUntil ?? null,
    description: q.description ?? "",
  })),
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.code.trim()) errors.code = "MasterData:CodeRequired";
  if (!v.firstName.trim()) errors.firstName = "MasterData:FirstNameRequired";
  if (!v.lastName.trim()) errors.lastName = "MasterData:LastNameRequired";
  const email = emailError(v.email);
  if (email) errors.email = email;
  return errors;
};

const GENERAL_FIELDS: (keyof Values & string)[] = ["code", "firstName", "middleName", "lastName", "initials", "phone", "mobile", "email", "preferredCulture", "employer", "user", "isArchived", "description"];

function driverTitle(dto: DriverDto): string {
  return dto.fullName ?? [dto.firstName, dto.middleName, dto.lastName].filter(Boolean).join(" ");
}

export function DriverEditor({ mode, id, readOnly: readOnlyProp, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const inPlace = useCreateInPlace();
  const queryClient = useQueryClient();
  const detail = drivers.useDetail(mode === "edit" ? id : undefined);
  const create = drivers.useCreate();
  const update = drivers.useUpdate();
  const remove = drivers.useDelete();
  const anonymise = useAnonymiseDriver();
  const canDelete = useIsGranted("MasterData.Drivers.Delete");
  const canAnonymise = useIsGranted("MasterData.Drivers.Anonymise");
  /* The one "Add new …" gate (permission + node feature chain + not read-only, UI-LOOK-004 / UI-PERM-006). */
  const canCreate = useCanCreateInPlace();
  /* The edit pencil's gate (UI-LOOK-005): edit with the relations' Update, read-only with only their view permission. */
  const relationAccess = useCanOpenInPlace()("Relation");
  const anonymised = detail.data?.isAnonymised === true;
  /* An anonymised record is read-only (design 43): every field disabled, no Save, the notice above the form. */
  const readOnly = readOnlyProp || anonymised;

  const languages = useLanguages();
  const languageOptions = useMemo(() => languages.map((l) => ({ value: l.cultureName, label: l.displayName })), [languages]);
  const employerPage = useMemo(() => relationLookupPage(EMPLOYER_ROLES), []);
  const kindValues = useMemo(() => QUALIFICATION_KINDS.map((k) => k.value), []);

  const [applyValues, setApplyValues] = useState<{ key: number; update: (values: Values) => Values } | undefined>();
  const apply = useCallback((update: (values: Values) => Values) => setApplyValues((previous) => ({ key: (previous?.key ?? 0) + 1, update })), []);
  const [relationDialog, setRelationDialog] = useState<{ mode: "create"; term: string } | { mode: "edit"; id: string } | null>(null);
  const [confirmAnonymise, setConfirmAnonymise] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const handleAnonymise = async () => {
    if (!id) return;
    try {
      await anonymise.mutateAsync(id);
      void queryClient.invalidateQueries({ queryKey: masterDataQueryKey(drivers.entity) });
      setConfirmAnonymise(false);
      toast.success(t("MasterData:DriverAnonymised"));
      onSaved();
      onClose();
    } catch (err) {
      setConfirmAnonymise(false);
      setActionError(getApiErrorMessage(err, t));
    }
  };

  const save = useCallback(
    async (v: Values, dto: DriverDto | undefined) => {
      const text = (value: string) => value.trim() || null;
      const body = {
        code: v.code.trim().toUpperCase(),
        firstName: v.firstName.trim(),
        middleName: text(v.middleName),
        lastName: v.lastName.trim(),
        initials: text(v.initials),
        phone: text(v.phone),
        mobile: text(v.mobile),
        email: text(v.email),
        preferredCulture: v.preferredCulture,
        employerRelationId: v.employer?.id ?? null,
        userId: v.user?.id ?? null,
        isArchived: v.isArchived,
        description: text(v.description),
        qualifications: v.qualifications.map((q) => ({
          id: q.id,
          kind: q.kind,
          number: text(q.number),
          validFrom: q.validFrom,
          validUntil: q.validUntil,
          description: text(q.description),
        })),
      };
      if (mode === "create") {
        return create.mutateAsync(body);
      }
      return update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const qualificationColumns = useMemo<ColDef<QualificationRow>[]>(
    () => [
      {
        field: "kind",
        headerName: t("MasterData:Kind"),
        flex: 2,
        minWidth: 160,
        editable: !readOnly,
        cellEditor: "agRichSelectCellEditor",
        cellEditorParams: {
          values: kindValues,
          formatValue: (value: DriverQualificationKind) => t(qualificationKindKey(value)),
          allowTyping: true,
          filterList: true,
          searchType: "matchAny",
        },
        valueFormatter: (p) => t(qualificationKindKey(p.value as DriverQualificationKind | undefined)),
      },
      { field: "number", headerName: t("MasterData:Number"), flex: 1, minWidth: 120, editable: !readOnly, cellEditor: "agTextCellEditor", cellEditorParams: { maxLength: 64 } },
      { field: "validFrom", headerName: t("MasterData:ValidFrom"), flex: 1, minWidth: 130, editable: !readOnly, ...DATE_CELL_EDITOR_COL_DEF, cellRenderer: DateCell },
      { field: "validUntil", headerName: t("MasterData:ValidUntil"), flex: 1, minWidth: 130, editable: !readOnly, ...DATE_CELL_EDITOR_COL_DEF, cellRenderer: DateCell },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 160, editable: !readOnly, cellEditor: "agTextCellEditor", cellEditorParams: { maxLength: 256 } },
    ],
    [kindValues, readOnly, t],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: GENERAL_FIELDS,
      render: (form) => (
        <div className="grid grid-cols-2 gap-4">
          <FormSection title={t("MasterData:Person")}>
            <div className="grid grid-cols-1 gap-3">
              <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={32} upper />
              <TextField form={form} field="firstName" labelKey="MasterData:FirstName" required maxLength={64} />
              <TextField form={form} field="middleName" labelKey="MasterData:MiddleName" maxLength={32} />
              <TextField form={form} field="lastName" labelKey="MasterData:LastName" required maxLength={64} />
              <TextField form={form} field="initials" labelKey="MasterData:Initials" maxLength={16} />
            </div>
          </FormSection>
          <FormSection title={t("MasterData:Contact")}>
            <div className="grid grid-cols-1 gap-3">
              <TextField form={form} field="phone" labelKey="MasterData:Phone" maxLength={32} />
              <TextField form={form} field="mobile" labelKey="MasterData:Mobile" maxLength={32} />
              <TextField form={form} field="email" labelKey="MasterData:Email" maxLength={256} />
              <FormField label={t("MasterData:PreferredCulture")} htmlFor={form.id("preferredCulture")} floating="always">
                <ReferenceCombobox
                  id={form.id("preferredCulture")}
                  value={form.values.preferredCulture}
                  onChange={(next) => form.set("preferredCulture", next)}
                  options={languageOptions}
                  disabled={form.readOnly}
                />
              </FormField>
            </div>
          </FormSection>
          <FormSection title={t("MasterData:Employment")} className="col-span-2">
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <FormField label={t("MasterData:Employer")} htmlFor={form.id("employer")} floating="always">
                <EntityLookup<RelationOption>
                  id={form.id("employer")}
                  value={form.values.employer}
                  onChange={(next) => form.set("employer", next)}
                  fetchPage={employerPage}
                  getLabel={(r) => r.name}
                  renderOption={(r) => (
                    <>
                      <div className="font-medium">{r.name}</div>
                      <div className="text-label text-muted-foreground">{[r.code, [r.city, r.countryCode].filter(Boolean).join(" ")].filter(Boolean).join(", ")}</div>
                    </>
                  )}
                  onCreateNew={canCreate("Relation", form.readOnly) ? (term) => setRelationDialog({ mode: "create", term }) : undefined}
                  createLabel={t("MasterData:AddNewRelation")}
                  onEdit={relationAccess ? (r) => setRelationDialog({ mode: "edit", id: r.id }) : undefined}
                  editLabel={relationAccess === "view" ? t("Open") : undefined}
                  disabled={form.readOnly}
                />
              </FormField>
              <FormField label={t("MasterData:UserAccount")} htmlFor={form.id("user")} floating="always">
                <EntityLookup<UserOption>
                  id={form.id("user")}
                  value={form.values.user}
                  onChange={(next) => form.set("user", next)}
                  fetchPage={userLookupPage}
                  {...inPlace.offer<UserRow>("User", form.readOnly, (r) =>
                    form.set("user", { id: r.id, name: [r.dto.name, r.dto.surname].filter(Boolean).join(" "), userName: r.dto.userName ?? "", email: r.dto.email ?? null }),
                  )}
                  getLabel={(u) => u.name || u.userName}
                  renderOption={(u) => (
                    <>
                      <div className="font-medium">{u.name || u.userName}</div>
                      <div className="text-label text-muted-foreground">{[u.userName, u.email].filter(Boolean).join(", ")}</div>
                    </>
                  )}
                  disabled={form.readOnly}
                />
              </FormField>
              <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" className="col-span-2" />
            </div>
          </FormSection>
          <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} className="col-span-2" />
        </div>
      ),
    },
    {
      value: "qualifications",
      labelKey: "MasterData:Qualifications",
      fields: ["qualifications"],
      render: (form) => (
        <div className="flex h-80 flex-col gap-2">
          <LazyLinesGrid<QualificationRow>
            rows={form.values.qualifications}
            onRowsChange={(rows) => form.set("qualifications", rows)}
            getRowId={(row) => row.key}
            columnDefs={qualificationColumns}
            readOnly={form.readOnly}
            toolbar={
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() =>
                  form.set("qualifications", [
                    ...form.values.qualifications,
                    { key: `new-${++rowCounter}`, id: null, kind: 0, number: "", validFrom: null, validUntil: null, description: "" },
                  ])
                }
              >
                <Plus /> {t("MasterData:AddQualification")}
              </Button>
            }
          />
        </div>
      ),
    },
  ];

  const badges =
    mode === "edit" && detail.data ? (
      <>
        {anonymised && <Badge variant="secondary">{t("MasterData:Anonymised")}</Badge>}
        {detail.data.isArchived && <Badge variant="outline">{t("MasterData:Archived")}</Badge>}
      </>
    ) : undefined;

  return (
    <>
      <MasterDataEditor<Values, DriverDto>
        entityKey="Driver"
        queryEntity={drivers.entity}
        size="md"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={detail}
        titleOf={driverTitle}
        initialValues={initialValues}
        validate={validate}
        save={save}
        isSaving={create.isPending || update.isPending}
        tabs={tabs}
        extraCommands={
          mode === "edit" && !readOnly && canAnonymise
            ? [
                {
                  id: "anonymise",
                  labelKey: "MasterData:Anonymise",
                  icon: UserX,
                  variant: "destructive",
                  groupKey: "MasterData:Driver",
                  disabled: !detail.data,
                  onClick: () => setConfirmAnonymise(true),
                },
              ]
            : undefined
        }
        deleteRecord={mode === "edit" && canDelete ? remove : undefined}
        notice={
          <>
            {anonymised && (
              <Alert variant="warning">
                <AlertDescription>{t("MasterData:AnonymisedReadOnly")}</AlertDescription>
              </Alert>
            )}
            {actionError && (
              <Alert variant="destructive">
                <AlertDescription>{actionError}</AlertDescription>
              </Alert>
            )}
          </>
        }
        badges={badges}
        applyValues={applyValues}
      />
      {relationDialog && (
        /* "Add new relation…" and the edit pencil of the employer lookup (UI-LOOK-004/005): the relation
         * editor on top; the saved record becomes (or refreshes) the selected employer. */
        <RelationEditor
          mode={relationDialog.mode}
          id={relationDialog.mode === "edit" ? relationDialog.id : undefined}
          initialName={relationDialog.mode === "create" ? relationDialog.term : undefined}
          readOnly={relationDialog.mode === "edit" && relationAccess !== "edit"}
          open
          onClose={() => setRelationDialog(null)}
          onSaved={(saved) => {
            const dto = saved as RelationDto | undefined;
            /* A read-only form keeps its values (the pencil opened the relation only to look or edit it there). */
            if (!dto?.id || (relationDialog.mode === "edit" && readOnly)) return;
            const option: RelationOption = { id: dto.id, code: dto.code ?? "", name: dto.name ?? "", city: null, countryCode: null, roles: dto.roles ?? 0 };
            apply((values) => ({ ...values, employer: option }));
          }}
        />
      )}
      <ConfirmDialog
        open={confirmAnonymise}
        onOpenChange={(next) => {
          if (!next) setConfirmAnonymise(false);
        }}
        title={t("MasterData:AnonymiseDriver")}
        description={t("MasterData:AnonymiseDriverQuestion").replace("{0}", detail.data ? driverTitle(detail.data) : "")}
        confirmLabel={t("MasterData:Anonymise")}
        destructive
        onConfirm={handleAnonymise}
      />
      {inPlace.dialog}
    </>
  );
}
