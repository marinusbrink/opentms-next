import { lazy, Suspense, useMemo, useState } from "react";
import { Save } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityDialog, type EntityDialogTab, type RibbonCommand } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { AddressField, relationOptionOf, RelationField } from "@/domains/master-data/components/RelationFields";
import { waypointTypes } from "@/domains/orders/reference-data";
import {
  isStopDetailDirty,
  stopActualsDto,
  stopCoordinates,
  stopDetailValues,
  stopUpdateDto,
  validateStopDetail,
  type StopDetailValues,
} from "@/domains/planning-execution/components/stop-detail-form";
import { useRecordStopActuals, useUpdateStop, type TripStopDto, type TripStopLegDto } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The stop detail window (design 74 §3), opened from the Planboard's timeline and Transport's Stops tab. General shows the
 * stop, its contact details and its times, with a map on the right when it has coordinates; Legs lists its legs. Editable:
 * the actual times, remarks, planned duration and, on a stop without legs, the relation and address. A save sends only what
 * changed: the stop's update and, when the actual times changed, `record-stop-actuals`. When the second call fails the
 * first has been saved, so the window stays open with the error and a retry sends only what is left. */

const StopDetailMap = lazy(() => import("@/domains/planning-execution/components/StopDetailMap"));

export interface StopDetailDialogProps {
  tripId: string;
  /** The stop as the trip has it now; the form keeps the values it opened with. */
  stop: TripStopDto;
  /** May manage the stops: the permission and the trip's state together (UI-PERM-004: otherwise read-only). */
  canEdit: boolean;
  onClose: () => void;
}

/** A read-only value with its label; nothing without a value. */
function ReadOnlyValue({ label, value }: { label: string; value: string | null | undefined }) {
  if (!value) return null;
  return (
    <div className="min-w-0">
      <dt className="text-label text-muted-foreground">{label}</dt>
      <dd className="text-sm wrap-break-word tabular-nums">{value}</dd>
    </div>
  );
}

export function StopDetailDialog({ tripId, stop, canEdit, onClose }: StopDetailDialogProps) {
  const { t } = useL();
  const f = useFormat();
  const update = useUpdateStop();
  const record = useRecordStopActuals();
  const waypointLookup = waypointTypes.useLookup();

  const [initial, setInitial] = useState<StopDetailValues>(() => stopDetailValues(stop));
  const [values, setValues] = useState<StopDetailValues>(initial);
  const [submitted, setSubmitted] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const legs = stop.legs ?? [];
  const canEditPlace = canEdit && legs.length === 0;
  const errors = useMemo(() => validateStopDetail(initial, values), [initial, values]);
  const shownError = (field: keyof typeof errors) => (submitted && errors[field] ? t(errors[field]) : undefined);
  const set = <K extends keyof StopDetailValues>(field: K, value: StopDetailValues[K]) => setValues((current) => ({ ...current, [field]: value }));
  const dirty = isStopDetailDirty(initial, values);

  const save = async () => {
    setSubmitted(true);
    if (Object.keys(errors).length > 0) return;
    setError(null);
    setSaving(true);
    try {
      const updateDto = stopUpdateDto(initial, values);
      if (updateDto) {
        await update.mutateAsync({ id: tripId, stopId: stop.id!, dto: updateDto });
        /* Saved: what is still different from here on is the actual times, should they fail. */
        setInitial((current) => ({ ...values, arrivalDate: current.arrivalDate, arrivalTime: current.arrivalTime, departureDate: current.departureDate, departureTime: current.departureTime }));
      }
      const actualsDto = stopActualsDto(initial, values);
      if (actualsDto) await record.mutateAsync({ id: tripId, stopId: stop.id!, dto: actualsDto });
      toast.success(t("PlanningExecution:StopSaved"));
      onClose();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    } finally {
      setSaving(false);
    }
  };

  const commands: RibbonCommand[] = canEdit ? [{ id: "save", labelKey: saving ? "Saving" : "Save", icon: Save, isPrimary: true, onClick: () => void save() }] : [];
  const name = stop.relationName ?? stop.name ?? "";
  const activity = waypointLookup.data?.find((w) => w.id === stop.waypointTypeId)?.name;
  const coordinates = stopCoordinates(stop);
  const street = [stop.street, stop.houseNumber].filter(Boolean).join(" ");
  const place = [stop.postalCode, stop.city].filter(Boolean).join(" ");
  const address = [street, place, stop.countryCode].filter(Boolean).join(", ");
  const distance = stop.plannedCumulativeDistanceM == null ? null : f.number(stop.plannedCumulativeDistanceM / 1000, { maxDecimals: 1 });
  const contact = [stop.relationPhone, stop.relationMobile, stop.relationEmail].some(Boolean);

  const general = (
    <div className="flex flex-col gap-4">
      {error && (
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      <div className={coordinates ? "grid grid-cols-3 gap-4" : "grid grid-cols-1 gap-4"}>
        <div className="col-span-2 flex min-w-0 flex-col gap-4">
          <FormSection title={t("PlanningExecution:Stop")}>
            {canEditPlace && (
              <div className="mb-3 grid grid-cols-2 gap-x-4 gap-y-3">
                <RelationField
                  id="stop-detail-relation"
                  label={t("PlanningExecution:Relation")}
                  value={relationOptionOf(values.relationId, values.relationName)}
                  onChange={(next) => {
                    if (!next) return;
                    setValues((current) => ({ ...current, relationId: next.id, relationName: next.name, relationAddressId: next.id === current.relationId ? current.relationAddressId : null }));
                  }}
                  required
                  disabled={saving}
                />
                <AddressField
                  id="stop-detail-address"
                  label={t("Orders:Address")}
                  relationId={values.relationId}
                  value={values.relationAddressId}
                  onChange={(next) => set("relationAddressId", next)}
                  disabled={saving}
                />
              </div>
            )}
            <dl className="grid grid-cols-2 gap-x-4 gap-y-2">
              {!canEditPlace && <ReadOnlyValue label={t("PlanningExecution:Relation")} value={name} />}
              <ReadOnlyValue label={t("Orders:Activity")} value={activity} />
              {!canEditPlace && <ReadOnlyValue label={t("Orders:Address")} value={address} />}
              <ReadOnlyValue label={`${t("PlanningExecution:CumulativeDistance")} (km)`} value={distance} />
            </dl>
          </FormSection>

          {contact && (
            <FormSection title={t("PlanningExecution:StopDetail.Contact")}>
              <dl className="grid grid-cols-3 gap-x-4 gap-y-2">
                <ReadOnlyValue label={t("MasterData:Phone")} value={stop.relationPhone} />
                <ReadOnlyValue label={t("MasterData:Mobile")} value={stop.relationMobile} />
                <ReadOnlyValue label={t("MasterData:Email")} value={stop.relationEmail} />
              </dl>
            </FormSection>
          )}

          <FormSection title={t("PlanningExecution:StopDetail.Times")}>
            <dl className="mb-3 grid grid-cols-2 gap-x-4 gap-y-2">
              <ReadOnlyValue label={t("PlanningExecution:PlannedArrival")} value={stop.plannedArrival ? f.dateTime(stop.plannedArrival) : null} />
              <ReadOnlyValue label={t("PlanningExecution:PlannedDeparture")} value={stop.plannedDeparture ? f.dateTime(stop.plannedDeparture) : null} />
              <ReadOnlyValue label={t("PlanningExecution:EstimatedArrival")} value={stop.estimatedArrival ? f.dateTime(stop.estimatedArrival) : null} />
              <ReadOnlyValue label={t("PlanningExecution:EstimatedDeparture")} value={stop.estimatedDeparture ? f.dateTime(stop.estimatedDeparture) : null} />
            </dl>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <FormField label={t("PlanningExecution:ActualArrival")} htmlFor="stop-detail-arrival-date" error={shownError("arrivalDate")} floating="always">
                <DatePicker id="stop-detail-arrival-date" value={values.arrivalDate} onChange={(next) => set("arrivalDate", next)} disabled={!canEdit || saving} />
              </FormField>
              <FormField label={t("Orders:Time")} htmlFor="stop-detail-arrival-time" error={shownError("arrivalTime")} floating="always">
                <Input id="stop-detail-arrival-time" value={values.arrivalTime} onChange={(e) => set("arrivalTime", e.target.value)} placeholder="HH:mm" disabled={!canEdit || saving} />
              </FormField>
              <FormField label={t("PlanningExecution:ActualDeparture")} htmlFor="stop-detail-departure-date" error={shownError("departureDate")} floating="always">
                <DatePicker id="stop-detail-departure-date" value={values.departureDate} onChange={(next) => set("departureDate", next)} disabled={!canEdit || saving} />
              </FormField>
              <FormField label={t("Orders:Time")} htmlFor="stop-detail-departure-time" error={shownError("departureTime")} floating="always">
                <Input id="stop-detail-departure-time" value={values.departureTime} onChange={(e) => set("departureTime", e.target.value)} placeholder="HH:mm" disabled={!canEdit || saving} />
              </FormField>
              <FormField label={t("Orders:PlannedDurationMin")} htmlFor="stop-detail-duration" error={shownError("plannedDurationMin")}>
                <Input id="stop-detail-duration" inputMode="numeric" className="tabular-nums" value={values.plannedDurationMin} onChange={(e) => set("plannedDurationMin", e.target.value)} disabled={!canEdit || saving} />
              </FormField>
              <FormField label={t("PlanningExecution:Remarks")} htmlFor="stop-detail-remarks" className="col-span-2" floating="always">
                <Textarea id="stop-detail-remarks" rows={3} maxLength={2500} value={values.remarks} onChange={(e) => set("remarks", e.target.value)} disabled={!canEdit || saving} />
              </FormField>
            </div>
          </FormSection>
        </div>
        {coordinates && (
          <div className="col-span-1 min-h-96 min-w-0">
            <Suspense fallback={<Skeleton className="h-full w-full" />}>
              <StopDetailMap latitude={coordinates.latitude} longitude={coordinates.longitude} />
            </Suspense>
          </div>
        )}
      </div>
    </div>
  );

  const tabs: EntityDialogTab[] = [
    { value: "general", label: t("General"), content: general },
    { value: "legs", label: t("PlanningExecution:Legs"), content: <StopLegsList legs={legs} /> },
  ];

  return (
    <EntityDialog
      open
      onOpenChange={(next) => !next && onClose()}
      variant="editor"
      size="lg"
      title={`${t("PlanningExecution:Stop")} ${[stop.position, name].filter((part) => part != null && part !== "").join(". ")}`}
      commands={commands}
      tabs={tabs}
      isDirty={canEdit && dirty}
      isBusy={saving}
    />
  );
}

const endText = (name: string | null | undefined, city: string | null | undefined) => [name, city].filter(Boolean).join(", ");

function StopLegsList({ legs }: { legs: readonly TripStopLegDto[] }) {
  const { t } = useL();
  const f = useFormat();
  if (legs.length === 0) return <p className="text-sm text-muted-foreground">{t("PlanningExecution:StopDetail.NoLegs")}</p>;
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>{t("PlanningExecution:Order")}</TableHead>
          <TableHead>{t("PlanningExecution:Customer")}</TableHead>
          <TableHead>{t("PlanningExecution:Origin")}</TableHead>
          <TableHead>{t("PlanningExecution:Destination")}</TableHead>
          <TableHead className="text-right">{t("PlanningExecution:Quantity")}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {legs.map((leg) => (
          <TableRow key={`${leg.legId}-${leg.role}`}>
            <TableCell className="tabular-nums">{leg.orderNumber}</TableCell>
            <TableCell>{leg.customerName}</TableCell>
            <TableCell>{endText(leg.originName, leg.originCity)}</TableCell>
            <TableCell>{endText(leg.destinationName, leg.destinationCity)}</TableCell>
            <TableCell className="text-right tabular-nums">{f.number(leg.quantity)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
