import { useMemo, useState, type FormEvent } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { ExternalLink, Pencil, Repeat, Split, Truck, Unlink } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { AmountCell, BoolCell, DateCell, NumberCell } from "@/components/ui/grid-cells";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { LegDialog } from "@/apps/transport/components/LegDialog";
import { AddressField, RelationField } from "@/apps/transport/components/RelationFields";
import { WarningsCell, warningsOf } from "@/apps/transport/components/WarningsCell";
import type { RelationOption } from "@/domains/master-data/entities";
import { fetchLegs, legEndLabel, type LegDto } from "@/domains/planning-execution/legs";
import { tripPickerPage, TRANSPORT_TRIP_STATUSES, useCarryOutSeparately, useCreateReturnLeg, useOutsourceOrigin, useUnassignLegs, type TransportTripDto, type TripPickerOption } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* The Legs tab of the trip editor (design 61): an OpenTmsGrid of the trip's legs with the editable reference, remarks
 * planned dates and time windows in the shared LegDialog, the row actions Unassign, Carry out separately, Return leg, Outsource origin
 * (design 59 commands) and the link to the order; double click opens the order. */

export interface TripLegsTabProps {
  trip: TransportTripDto;
  canAssign: boolean;
  canEditLegs: boolean;
}

export function TripLegsTab({ trip, canAssign, canEditLegs }: TripLegsTabProps) {
  const { t } = useL();
  const f = useFormat();
  const openRecord = useOpenRecord();
  const [editing, setEditing] = useState<LegDto | null>(null);
  const [outsourcing, setOutsourcing] = useState<LegDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const unassign = useUnassignLegs();
  const separately = useCarryOutSeparately();
  const returnLeg = useCreateReturnLeg();
  /* A new fetcher per trip version: the grid refetches after every command and every push (flow 2). */
  const refreshKey = `${trip.id}|${trip.concurrencyStamp ?? ""}|${trip.calculatedAt ?? ""}`;
  const fetchRows = useMemo(
    () => (request: Parameters<typeof fetchLegs>[0]) => fetchLegs(request, { tripId: trip.id }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [refreshKey],
  );

  const run = (action: Promise<unknown>, successKey: string) => {
    setError(null);
    action.then(() => toast.success(t(successKey))).catch((err: unknown) => setError(getApiErrorMessage(err, t)));
  };

  const columns = useMemo<ColDef<LegDto>[]>(
    () => [
      { field: "tripPosition", headerName: t("PlanningExecution:Position"), width: 80, minWidth: 70, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "orderNumber", headerName: t("PlanningExecution:Order"), minWidth: 130 },
      { field: "customerName", headerName: t("PlanningExecution:Customer"), flex: 1, minWidth: 150, sortable: false },
      { colId: "origin", headerName: t("PlanningExecution:Origin"), flex: 1, minWidth: 160, valueGetter: (p) => legEndLabel(p.data?.origin), sortable: false },
      { colId: "destination", headerName: t("PlanningExecution:Destination"), flex: 1, minWidth: 160, valueGetter: (p) => legEndLabel(p.data?.destination), sortable: false },
      { colId: "originDate", headerName: t("PlanningExecution:PlannedDate"), minWidth: 110, valueGetter: (p) => p.data?.origin?.plannedDate, cellRenderer: DateCell, sortable: false },
      { colId: "window", headerName: t("PlanningExecution:Window"), minWidth: 130, valueGetter: (p) => [p.data?.origin?.windowFrom, p.data?.origin?.windowTill].filter(Boolean).map((v) => f.time(v)).join(" - "), sortable: false },
      { colId: "originReference", headerName: t("PlanningExecution:Reference"), minWidth: 120, valueGetter: (p) => [p.data?.origin?.reference, p.data?.destination?.reference].filter(Boolean).join(" / "), sortable: false },
      { field: "quantity", headerName: t("PlanningExecution:Quantity"), minWidth: 90, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      { field: "weightKg", headerName: t("PlanningExecution:WeightKg"), minWidth: 100, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      { colId: "distance", headerName: `${t("PlanningExecution:Distance")} (km)`, minWidth: 100, type: "rightAligned", valueGetter: (p) => (p.data?.distanceM == null ? null : p.data.distanceM / 1000), valueFormatter: (p: ValueFormatterParams<LegDto, number | null>) => f.number(p.value, { maxDecimals: 1 }), cellClass: "tabular-nums", sortable: false },
      { field: "allocatedCostAmount", headerName: t("PlanningExecution:AllocatedCost"), minWidth: 110, cellRenderer: AmountCell, type: "rightAligned", sortable: false },
      { field: "allocatedRevenueAmount", headerName: t("PlanningExecution:AllocatedRevenue"), minWidth: 110, cellRenderer: AmountCell, type: "rightAligned", sortable: false },
      { colId: "warnings", headerName: t("PlanningExecution:Warnings"), minWidth: 100, sortable: false, cellRenderer: (p: { data: LegDto | undefined }) => <WarningsCell value={warningsOf(trip.warnings, { legId: p.data?.id })} /> },
      { field: "remarks", headerName: t("PlanningExecution:Remarks"), minWidth: 140, hide: true, sortable: false },
      { field: "isCarriedOutSeparately", headerName: t("PlanningExecution:IsCarriedOutSeparately"), minWidth: 100, hide: true, sortable: false, cellRenderer: BoolCell },
    ],
    [t, f, trip.warnings],
  );

  return (
    <div className="flex h-full min-h-0 flex-col gap-3">
      {error && (
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      <div className="min-h-64 flex-1">
        <LazyOpenTmsGrid<LegDto>
          gridId="planning.transport.tripLegs"
          columnDefs={columns}
          fetchRows={fetchRows}
          onOpen={(row) => row.orderId && openRecord("TransportOrder", row.orderId)}
          rowActions={(rows) => {
            const leg = rows[0];
            return [
              ...(canEditLegs ? [{ id: "edit", labelKey: "Edit", icon: Pencil, onClick: () => setEditing(leg) }] : []),
              { id: "order", labelKey: "PlanningExecution:OpenOrder", icon: ExternalLink, onClick: () => leg?.orderId && openRecord("TransportOrder", leg.orderId) },
              ...(canAssign
                ? [
                    { id: "unassign", labelKey: "PlanningExecution:UnassignLegs", icon: Unlink, onClick: () => run(unassign.mutateAsync({ id: trip.id!, legIds: rows.map((r) => r.id!) }), "PlanningExecution:LegsUnassigned") },
                    { id: "separately", labelKey: "PlanningExecution:CarryOutSeparately", icon: Split, disabled: leg?.isCarriedOutSeparately === true, disabledReasonKey: "PlanningExecution:AlreadySeparate", onClick: () => run(separately.mutateAsync({ id: trip.id!, legId: leg.id! }), "PlanningExecution:TransportTripSaved") },
                    { id: "return", labelKey: "PlanningExecution:ReturnLeg", icon: Repeat, onClick: () => run(returnLeg.mutateAsync({ id: trip.id!, legId: leg.id! }), "PlanningExecution:ReturnLegCreated") },
                    { id: "outsource", labelKey: "PlanningExecution:OutsourceOrigin", icon: Truck, onClick: () => setOutsourcing(leg) },
                  ]
                : []),
            ];
          }}
        />
      </div>
      {editing && <LegDialog leg={editing} title={`${t("PlanningExecution:Leg")} ${editing.orderNumber ?? ""}`} idPrefix="trip-leg" onClose={() => setEditing(null)} />}
      {outsourcing && <OutsourceDialog tripId={trip.id!} leg={outsourcing} onClose={() => setOutsourcing(null)} />}
    </div>
  );
}

/** Outsource the origin activity to a hub on another trip (design 59, trips.md 24). */
function OutsourceDialog({ tripId, leg, onClose }: { tripId: string; leg: LegDto; onClose: () => void }) {
  const { t } = useL();
  const f = useFormat();
  const outsource = useOutsourceOrigin();
  const [hub, setHub] = useState<RelationOption | null>(null);
  const [hubAddressId, setHubAddressId] = useState<string | null>(null);
  const [toTrip, setToTrip] = useState<TripPickerOption | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  /* The target is another planned or running trip: the picker leaves out the leg's own trip (design 59 build note 14). */
  const tripPage = useMemo(() => tripPickerPage(tripId), [tripId]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitted(true);
    if (!hub || !toTrip) return;
    setError(null);
    try {
      await outsource.mutateAsync({ id: tripId, dto: { legId: leg.id!, hubRelationId: hub.id, hubAddressId, toTripId: toTrip.id } });
      toast.success(t("PlanningExecution:TransportTripSaved"));
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
      title={t("PlanningExecution:OutsourceOrigin")}
      isBusy={outsource.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={outsource.isPending}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="outsource-form" disabled={outsource.isPending}>
            {outsource.isPending ? t("Saving") : t("PlanningExecution:OutsourceOrigin")}
          </Button>
        </>
      }
    >
      <form id="outsource-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <RelationField id="outsource-hub" label={t("PlanningExecution:Hub")} value={hub} onChange={(next) => { setHub(next); setHubAddressId(null); }} required error={submitted && !hub ? t("MasterData:Required") : undefined} />
        <AddressField id="outsource-hub-address" label={t("Orders:Address")} relationId={hub?.id} value={hubAddressId} onChange={setHubAddressId} />
        <FormField label={t("PlanningExecution:TargetTripId")} htmlFor="outsource-trip" required error={submitted && !toTrip ? t("MasterData:Required") : undefined} hint={t("PlanningExecution:TargetTripHint")}>
          {/* DEVIATION(UI-LOOK-004): no "Add new …" in the trip picker: a trip is never created in the Transport app (no New, the Planboard creates trips, PO), approved in docs/designs/61-transport-app.md (Trips list and editor). */}
          <EntityLookup<TripPickerOption>
            id="outsource-trip"
            value={toTrip}
            onChange={setToTrip}
            fetchPage={tripPage}
            required
            aria-invalid={submitted && !toTrip}
            getLabel={(trip) => [trip.number, trip.name].filter(Boolean).join(" · ")}
            renderOption={(trip) => (
              <div className="flex flex-col">
                <span className="font-medium">{[trip.number, trip.name].filter(Boolean).join(" · ")}</span>
                <span className="text-label text-muted-foreground">
                  {[f.date(trip.tripDate), t(TRANSPORT_TRIP_STATUSES.find((s) => s.value === trip.status)?.key ?? ""), trip.carrierName].filter(Boolean).join(" · ")}
                </span>
              </div>
            )}
          />
        </FormField>
      </form>
    </EntityDialog>
  );
}
