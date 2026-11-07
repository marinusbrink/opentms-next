import { useEffect, useMemo, useState, type FormEvent } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { useAddTripDriver, useCreatePlanboardTrip, type PlanboardCreateTripResultDto, type PlanboardResourceMode } from "@/domains/planning-execution/planboard-scheduler";
import { transportTripTypes, useDefaultTransportTripTypeId, type TransportTripTypeDto } from "@/domains/planning-execution/reference-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* New trip from an empty Scheduler cell (design 70 "Scheduler"; design 68 §6 "Create trip"): the trip type picker
 * defaults to the tenant's default type (else the first); the server fills the type's defaults, puts the row's resource
 * in its mode's slot and plans the departure at the selected time. A driver dropped on an empty cell opens the same
 * dialog with `driverId`: after the create the driver is added to the new trip (unless the row is that driver already,
 * in the Driver mode). Legs or an order dropped on an empty cell open it when there is more than one trip type to choose
 * from (design 74 §10); they go on the new trip, and cancelling undoes the drop: nothing has happened yet. */

export interface CreateTripDialogProps {
  mode: PlanboardResourceMode;
  resourceId: string;
  resourceName: string;
  start: Date;
  /** A driver dropped on the cell: added to the trip after it is created. */
  driverId?: string;
  /** The dropped driver's name, when known (shown in the summary). */
  driverName?: string;
  /** Legs dropped on the cell: they go on the new trip. */
  legIds?: string[];
  /** An order dropped on the cell: its legs go on the new trip; the caller reports the result. */
  orderId?: string;
  onClose: () => void;
  onCreated: (change: PlanboardCreateTripResultDto) => void;
}

export function CreateTripDialog({ mode, resourceId, resourceName, start, driverId, driverName, legIds, orderId, onClose, onCreated }: CreateTripDialogProps) {
  const { t } = useL();
  const f = useFormat();
  const create = useCreatePlanboardTrip();
  const addDriver = useAddTripDriver();
  /* In the Driver mode the row's resource is a driver already; the dropped one is added when it is another. */
  const driverToAdd = driverId && !(mode === "driver" && driverId.toLowerCase() === resourceId.toLowerCase()) ? driverId : null;
  const busy = create.isPending || addDriver.isPending;
  const types = transportTripTypes.useLookup();
  const defaultTypeId = useDefaultTransportTripTypeId();
  const items = useMemo(() => (types.data ?? []).filter((x) => x.id).map((x) => ({ value: x.id!, label: x.name ?? x.code ?? "" })), [types.data]);
  const [typeId, setTypeId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const inPlace = useCreateInPlace();

  /* The default type, once known; else the first type. */
  useEffect(() => {
    if (typeId || defaultTypeId.isLoading || types.isLoading) return;
    const preferred = defaultTypeId.data && items.some((i) => i.value === defaultTypeId.data) ? defaultTypeId.data : items[0]?.value;
    if (preferred) setTypeId(preferred);
  }, [typeId, defaultTypeId.isLoading, defaultTypeId.data, types.isLoading, items]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    try {
      const change = await create.mutateAsync({ transportTripTypeId: typeId, mode, resourceId, plannedDeparture: start.toISOString(), legIds, orderId });
      if (!orderId) toast.success(t(legIds?.length ? "Planboard:Scheduler.TripCreatedWithLegs" : "Planboard:Scheduler.TripCreated"));
      if (driverToAdd && change.id) {
        try {
          await addDriver.mutateAsync({ tripId: change.id, driverId: driverToAdd, concurrencyStamp: change.concurrencyStamp ?? "" });
        } catch (driverError) {
          /* The trip exists; only the driver is missing, so the dialog closes and the failure is a toast. */
          toast.error(t("Planboard:Scheduler.TripCreatedDriverFailed").replace("{0}", getApiErrorMessage(driverError, t)));
        }
      }
      onCreated(change);
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
      title={t("Planboard:Scheduler.CreateTrip")}
      isBusy={busy}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="planboard-create-trip" disabled={busy}>
            {busy ? t("Saving") : t("Planboard:Scheduler.CreateTripConfirm")}
          </Button>
        </>
      }
    >
      <form id="planboard-create-trip" onSubmit={(e) => void submit(e)} noValidate className="flex flex-col gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        {types.error != null && (
          <Alert variant="destructive">
            <AlertDescription>{getApiErrorMessage(types.error, t)}</AlertDescription>
          </Alert>
        )}
        <p className="text-sm">
          {t("Planboard:Scheduler.CreateTripSummary").replace("{0}", resourceName).replace("{1}", f.dateTime(start))}
        </p>
        {driverToAdd && driverName && <p className="text-sm">{t("Planboard:Scheduler.CreateTripDriver").replace("{0}", driverName)}</p>}
        <FormField label={t("Planboard:Scheduler.TripType")} htmlFor="planboard-create-trip-type" floating="always">
          <ReferenceCombobox
            id="planboard-create-trip-type"
            value={typeId}
            onChange={setTypeId}
            options={items}
            disabled={busy}
            {...inPlace.offer<TransportTripTypeDto>("TransportTripType", busy, (r) => r.id && setTypeId(r.id))}
          />
        </FormField>
      </form>
      {inPlace.dialog}
    </EntityDialog>
  );
}
