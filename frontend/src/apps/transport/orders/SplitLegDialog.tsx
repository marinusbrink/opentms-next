import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { toOptions } from "@/apps/transport/components/fields";
import { AddressField, RelationField } from "@/domains/master-data/components/RelationFields";
import { rateLineKeys } from "@/domains/financial/rate-lines";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import type { RelationOption } from "@/domains/master-data/entities";
import { waypointTypes } from "@/domains/orders/reference-data";
import { legEndLabel, useRemoveLegSplit, useSplitLegAtAddress, type LegDto } from "@/domains/planning-execution/legs";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Split leg at … and Remove split (design 61 "Order page review" 5), the row actions of the legs grid in Edit freight
 * item → Stops and legs. The split turns A→B into A→X and X→B at the chosen relation and address X: the arrival activity
 * at X, the departure activity (by default the arrival activity's successive activity of design 51, else the arrival
 * activity itself, as the server defaults it) and the date (by default the leg's origin date). Remove split, on either
 * half, turns them back into A→B. Both are Planning commands run at once (stored legs, not form state); the legs and the
 * order's rate lines are refetched after. The server refuses them while a leg involved is on a trip (`LegOnTrip`), and
 * the refusal shows in the dialog (UI-FB-004). */

export interface SplitLegDialogProps {
  leg: LegDto;
  onClose: () => void;
}

export function SplitLegDialog({ leg, onClose }: SplitLegDialogProps) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const split = useSplitLegAtAddress();
  const inPlace = useCreateInPlace();
  const waypointLookup = waypointTypes.useLookup();
  const waypointOptions = useMemo(() => toOptions(waypointLookup.data), [waypointLookup.data]);
  const [relation, setRelation] = useState<RelationOption | null>(null);
  const [addressId, setAddressId] = useState<string | null>(null);
  const [arrivalId, setArrivalId] = useState<string | null>(null);
  const [departureId, setDepartureId] = useState<string | null>(null);
  const [departureChosen, setDepartureChosen] = useState(false);
  const [date, setDate] = useState<string | null>(leg.origin?.plannedDate ?? null);
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const busy = split.isPending;

  /* The departure follows the arrival (its successive activity, else itself) until the user picks one. */
  const arrival = waypointTypes.useDetail(arrivalId ?? undefined);
  useEffect(() => {
    if (departureChosen || !arrivalId || arrival.data?.id?.toLowerCase() !== arrivalId.toLowerCase()) return;
    setDepartureId(arrival.data.successiveWaypointTypeId ?? arrivalId);
  }, [departureChosen, arrivalId, arrival.data]);

  const required = t("MasterData:Required");
  const relationError = submitted && !relation ? required : undefined;
  const addressError = submitted && relation && !addressId ? required : undefined;
  const arrivalError = submitted && !arrivalId ? required : undefined;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitted(true);
    setError(null);
    if (!relation || !addressId || !arrivalId) return;
    try {
      await split.mutateAsync({ id: leg.id!, dto: { relationId: relation.id, relationAddressId: addressId, arrivalWaypointTypeId: arrivalId, departureWaypointTypeId: departureId, date } });
      void queryClient.invalidateQueries({ queryKey: rateLineKeys.all });
      toast.success(t("PlanningExecution:LegSplit"));
      onClose();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="sm"
      title={t("PlanningExecution:SplitLegAtAddress")}
      isBusy={busy}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="split-leg-form" disabled={busy}>
            {busy ? t("Saving") : t("PlanningExecution:Split")}
          </Button>
        </>
      }
    >
      <form id="split-leg-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        <p className="text-label text-muted-foreground" data-slot="split-leg-hint">
          {t("PlanningExecution:SplitLegAtAddressHint").replace("{0}", legEndLabel(leg.origin)).replace("{1}", legEndLabel(leg.destination))}
        </p>
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <RelationField
          id="split-leg-relation"
          label={t("Orders:Relation")}
          value={relation}
          onChange={(next) => {
            setRelation(next);
            setAddressId(null);
          }}
          required
          error={relationError}
        />
        <AddressField id="split-leg-address" label={t("Orders:Address")} relationId={relation?.id} value={addressId} onChange={setAddressId} required error={addressError} />
        <FormField label={t("PlanningExecution:ArrivalActivity")} htmlFor="split-leg-arrival" required error={arrivalError} floating="always">
          <ReferenceCombobox
            id="split-leg-arrival"
            value={arrivalId}
            onChange={setArrivalId}
            options={waypointOptions}
            required
            aria-invalid={arrivalError != null}
            {...inPlace.offer("WaypointType", busy, (r) => setArrivalId(r.id))}
          />
        </FormField>
        <FormField label={t("PlanningExecution:DepartureActivity")} htmlFor="split-leg-departure" hint={t("PlanningExecution:DepartureActivityHint")} floating="always">
          <ReferenceCombobox
            id="split-leg-departure"
            value={departureId}
            onChange={(next) => {
              setDepartureChosen(true);
              setDepartureId(next);
            }}
            options={waypointOptions}
            {...inPlace.offer("WaypointType", busy, (r) => {
              setDepartureChosen(true);
              setDepartureId(r.id);
            })}
          />
        </FormField>
        <FormField label={t("PlanningExecution:Date")} htmlFor="split-leg-date" hint={t("PlanningExecution:SplitDateHint")} floating="always">
          <DatePicker id="split-leg-date" value={date} onChange={setDate} />
        </FormField>
      </form>
      {inPlace.dialog}
    </EntityDialog>
  );
}

export interface RemoveSplitDialogProps {
  leg: LegDto;
  onClose: () => void;
}

/** Remove split: a confirmation naming the leg (UI-DLG-011); not destructive, the leg can be split again. */
export function RemoveSplitDialog({ leg, onClose }: RemoveSplitDialogProps) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const remove = useRemoveLegSplit();
  return (
    <ConfirmDialog
      open
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      title={t("PlanningExecution:RemoveSplit")}
      description={t("PlanningExecution:RemoveSplitQuestion").replace("{0}", `${legEndLabel(leg.origin)} → ${legEndLabel(leg.destination)}`)}
      confirmLabel={t("PlanningExecution:RemoveSplit")}
      onConfirm={async () => {
        await remove.mutateAsync(leg.id!);
        void queryClient.invalidateQueries({ queryKey: rateLineKeys.all });
        toast.success(t("PlanningExecution:LegSplitRemoved"));
        onClose();
      }}
    />
  );
}
