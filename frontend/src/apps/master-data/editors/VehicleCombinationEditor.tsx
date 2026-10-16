import { useCallback, useMemo } from "react";
import type { ColDef } from "ag-grid-community";

import { Badge } from "@/components/ui/badge";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { useCreateInPlace } from "@/apps/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import {
  MOTORISED_KINDS,
  TRAILER_KINDS,
  vehicleCombinations,
  vehicleLookupPage,
  vehicleTypeKindKey,
  type VehicleCombinationDto,
  type VehicleDto,
  type VehicleOption,
  type VehicleTypeKind,
} from "@/domains/master-data/entities";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The vehicle combination editor (design 48, UI design): `md`; Name (empty = the vehicles' names joined by " + "),
 * Archived, Description and the members as lines with their position. The vehicle picker above the lines is
 * restricted by the position it fills: motorised kinds (tractor, rigid, van) for position 1 (while the
 * combination has no motorised vehicle; the new one goes first), trailers after.
 * The rules (2–4 vehicles, the first motorised, the others trailers, each once) are checked on submit; the server
 * checks them again, plus "not in another active combination". */

const MAX_MEMBERS = 4;
const MIN_MEMBERS = 2;

interface MemberLine {
  key: string;
  vehicleId: string;
  name: string;
  kind: VehicleTypeKind;
  licencePlate: string;
  position: number;
}

interface Values {
  name: string;
  isArchived: boolean;
  description: string;
  members: MemberLine[];
}

const initialValuesFor = (initialName: string | undefined) => (dto: VehicleCombinationDto | undefined): Values => ({
  name: dto?.name ?? initialName ?? "",
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
  members: (dto?.members ?? []).map((m) => ({
    key: m.vehicleId ?? "",
    vehicleId: m.vehicleId ?? "",
    name: m.name ?? "",
    kind: m.kind ?? 1,
    licencePlate: m.licencePlate ?? "",
    position: m.position ?? 0,
  })),
});

const ordered = (members: MemberLine[]) => [...members].sort((a, b) => a.position - b.position);

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  const members = ordered(v.members);
  if (members.length < MIN_MEMBERS || members.length > MAX_MEMBERS) errors.members = "MasterData:CombinationMembersRequired";
  else if (new Set(members.map((m) => m.vehicleId)).size !== members.length) errors.members = "MasterData:CombinationVehicleTwice";
  else if (!MOTORISED_KINDS.includes(members[0].kind) || members.slice(1).some((m) => !TRAILER_KINDS.includes(m.kind))) {
    errors.members = "MasterData:CombinationMotorisedFirst";
  }
  return errors;
};

export function VehicleCombinationEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const inPlace = useCreateInPlace();
  const detail = vehicleCombinations.useDetail(mode === "edit" ? id : undefined);
  const create = vehicleCombinations.useCreate();
  const update = vehicleCombinations.useUpdate();
  const remove = vehicleCombinations.useDelete();
  const canDelete = useIsGranted("MasterData.VehicleCombinations.Delete");
  const motorisedPage = useMemo(() => vehicleLookupPage(MOTORISED_KINDS), []);
  const trailerPage = useMemo(() => vehicleLookupPage(TRAILER_KINDS), []);

  const save = useCallback(
    async (v: Values, dto: VehicleCombinationDto | undefined) => {
      const body = {
        name: v.name.trim() || null,
        isArchived: v.isArchived,
        description: v.description.trim() || null,
        members: ordered(v.members).map((m, index) => ({ vehicleId: m.vehicleId, position: index + 1 })),
      };
      if (mode === "create") {
        return create.mutateAsync(body);
      }
      return update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const memberColumns = useMemo<ColDef<MemberLine>[]>(
    () => [
      { field: "position", headerName: t("MasterData:Position"), width: 110, minWidth: 90, editable: !readOnly, cellEditor: "agNumberCellEditor", cellEditorParams: { min: 1, max: MAX_MEMBERS, precision: 0 }, type: "rightAligned", sort: "asc" },
      { field: "name", headerName: t("MasterData:Vehicle"), flex: 2, minWidth: 150 },
      { field: "kind", headerName: t("MasterData:Kind"), valueFormatter: (p) => t(vehicleTypeKindKey(p.value)), flex: 1, minWidth: 110 },
      { field: "licencePlate", headerName: t("MasterData:LicencePlate"), flex: 1, minWidth: 120 },
    ],
    [readOnly, t],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["name", "isArchived", "description", "members"],
      render: (form) => {
        /* The picker fills the motorised position while the combination has no motorised vehicle, else a trailer position. */
        const wantMotorised = !form.values.members.some((m) => MOTORISED_KINDS.includes(m.kind));
        const canAdd = form.values.members.length < MAX_MEMBERS;
        const addMember = (vehicle: VehicleOption) => {
          if (form.values.members.some((m) => m.vehicleId === vehicle.id)) return;
          const line: MemberLine = { key: vehicle.id, vehicleId: vehicle.id, name: vehicle.name, kind: vehicle.kind, licencePlate: vehicle.licencePlate ?? "", position: 0 };
          const lines = wantMotorised ? [line, ...ordered(form.values.members)] : [...ordered(form.values.members), line];
          form.set("members", lines.map((m, index) => ({ ...m, position: index + 1 })));
          form.blur("members");
        };
        return (
          <div className="flex flex-col gap-4">
            <FormSection title={t("MasterData:VehicleCombination")}>
              <div className="grid grid-cols-2 gap-x-4 gap-y-3">
                <div className="flex flex-col gap-1">
                  <TextField form={form} field="name" labelKey="MasterData:Name" maxLength={64} />
                  <p className="text-label text-muted-foreground">{t("MasterData:CombinationNameHint")}</p>
                </div>
                <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
                <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} className="col-span-2" />
              </div>
            </FormSection>
            <FormSection title={t("MasterData:Members")}>
              <div className="flex h-60 flex-col gap-2">
                {form.error("members") && <p className="text-label text-destructive">{form.error("members")}</p>}
                <LazyLinesGrid<MemberLine>
                  rows={form.values.members}
                  onRowsChange={(rows) => form.set("members", rows)}
                  getRowId={(row) => row.key}
                  columnDefs={memberColumns}
                  readOnly={form.readOnly}
                  toolbar={
                    canAdd ? (
                      <>
                        <label htmlFor={form.id("members")} className="sr-only">
                          {t("MasterData:AddMember")}
                        </label>
                        <EntityLookup<VehicleOption>
                          /* Position 1 takes a motorised vehicle, every later position a trailer (design 48). */
                          key={wantMotorised ? "motorised" : "trailer"}
                          id={form.id("members")}
                          value={null}
                          onChange={(vehicle) => {
                            if (vehicle) addMember(vehicle);
                          }}
                          fetchPage={wantMotorised ? motorisedPage : trailerPage}
                          getLabel={(v) => v.name}
                          renderOption={(v) => (
                            <>
                              <div className="font-medium">{v.name}</div>
                              <div className="text-label text-muted-foreground">
                                {[v.licencePlate, v.vehicleTypeName, t(vehicleTypeKindKey(v.kind))].filter(Boolean).join(", ")}
                              </div>
                            </>
                          )}
                          placeholder={t("MasterData:AddMember")}
                          className="w-72"
                          {...inPlace.offer<VehicleDto>("Vehicle", form.readOnly, (r) =>
                            /* The new vehicle joins the lines like a picked one; a wrong kind is reported on submit. */
                            addMember({ id: r.id, name: r.name, kind: r.dto.kind ?? 1, licencePlate: r.dto.licencePlate ?? null, vehicleTypeName: r.dto.vehicleTypeName ?? null }),
                          )}
                        />
                      </>
                    ) : undefined
                  }
                />
              </div>
            </FormSection>
          </div>
        );
      },
    },
  ];

  return (
    <>
    <MasterDataEditor<Values, VehicleCombinationDto>
      entityKey="VehicleCombination"
      queryEntity={vehicleCombinations.entity}
      size="md"
      mode={mode}
      readOnly={readOnly}
      open={open}
      onClose={onClose}
      onSaved={onSaved}
      detail={detail}
      titleOf={(dto) => dto.name ?? ""}
      initialValues={initialValues}
      validate={validate}
      save={save}
      isSaving={create.isPending || update.isPending}
      tabs={tabs}
      deleteRecord={mode === "edit" && canDelete ? remove : undefined}
      badges={mode === "edit" && detail.data?.isArchived ? <Badge variant="outline">{t("MasterData:Archived")}</Badge> : undefined}
    />
    {inPlace.dialog}
    </>
  );
}
