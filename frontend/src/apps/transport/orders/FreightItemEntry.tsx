import { useEffect, useMemo, useRef, useState, type ClipboardEvent, type KeyboardEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { ClipboardPaste, Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { toOptions } from "@/apps/transport/components/fields";
import { defaultAddressIdOf } from "@/domains/master-data/components/RelationFields";
import { StopStrip } from "@/apps/transport/components/StopStrip";
import { STOP_FIELD_CONTROL, stopFieldId } from "@/apps/transport/components/stop-strip-ids";
import {
  REQUIRED_FLAG,
  emptyFreightEntry,
  freightEntryErrors,
  hasFlag,
  itemFromStrip,
  nextFreightEntry,
  stopProblemsOf,
  stopProblemText,
  stripStopsOf,
  stripWaypointsOf,
  type EditorMode,
  type FreightEntry,
  type FreightEntryField,
  type LineValues,
  type StopValues,
} from "@/apps/transport/orders/order-form";
import { looksLikeRows } from "@/apps/transport/orders/paste-items";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { products, relationAddressesQuery, transportUnits } from "@/domains/master-data/entities";
import { timeSlots, useWaypointTypeActivities, waypointTypes, type TransportOrderTypeDto } from "@/domains/orders/reference-data";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The entry of a new freight item on the Freight items tab (design 61 "Order page, legacy tab structure" 3), the
 * legacy entry modernised, top to bottom:
 * - the stop strip of the new item (StopStrip): it starts with the order type's default waypoints (number and
 *   activities; Load / Unload when the type defines none), at least two stops, + and −;
 * - the freight fields in one row (transport unit, quantity, product, positions, colli, weight, loading metres, volume;
 *   required marks per the order type), Add and Paste from Excel.
 * Enter adds the item (when no list is open: an open lookup list takes its own Enter, UI-KEY-001), keyboard first: Tab
 * moves on and commits a lookup's highlighted option (UI-KEY-004). The new item gets a copy of the strip's stops (a
 * stop whose relation was just picked gets its default address first); the strip keeps its stops, the freight fields
 * clear except the transport unit, and the cursor goes back to the last stop's relation. A stop without a usable
 * address names the stop in its message ("Stop 2 (Unload): …", point 9). A paste of spreadsheet rows anywhere in the
 * entry opens the paste preview instead of filling one field. */

export interface FreightItemEntryProps {
  idPrefix: string;
  mode: EditorMode;
  /** The order date: the strip's stops start on it and follow it until the user changes them. */
  orderDate: string | null;
  type: TransportOrderTypeDto | undefined;
  disabled: boolean;
  onAdd: (item: LineValues) => void;
  /** Opens the paste preview (with the pasted text, or empty from the button). */
  onPasteRows: (text: string) => void;
}

const FREIGHT_FIELD_IDS: Record<FreightEntryField, string> = { transportUnitId: "unit", quantity: "quantity", positions: "positions", colli: "colli", weightKg: "weightKg", loadingMeters: "loadingMeters", volumeM3: "volumeM3" };

export function FreightItemEntry({ idPrefix, mode, orderDate, type, disabled, onAdd, onPasteRows }: FreightItemEntryProps) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const flags = type?.requiredFields as number | undefined;
  const defaults = useMemo(() => type?.defaultWaypoints ?? [], [type?.defaultWaypoints]);
  const activities = useWaypointTypeActivities(!disabled && defaults.length === 0);
  const waypoints = useMemo(() => stripWaypointsOf(defaults, activities.data), [defaults, activities.data]);
  const waypointLookup = waypointTypes.useLookup();
  const waypointOptions = useMemo(() => toOptions(waypointLookup.data), [waypointLookup.data]);
  const slotLookup = timeSlots.useLookup();
  const slotOptions = useMemo(() => toOptions(slotLookup.data), [slotLookup.data]);
  const unitLookup = transportUnits.useLookup();
  const unitOptions = useMemo(() => toOptions(unitLookup.data), [unitLookup.data]);
  const productLookup = products.useLookup();
  const productOptions = useMemo(() => toOptions(productLookup.data), [productLookup.data]);
  const inPlace = useCreateInPlace();
  const stripDate = mode === "order" ? orderDate : null;

  /* The latest stops and fields, also for an Enter that follows a change in the same event (a date committed on Enter). */
  const [stops, setStopsState] = useState<StopValues[]>(() => stripStopsOf(waypoints, stripDate));
  const stopsRef = useRef(stops);
  const [entry, setEntryState] = useState<FreightEntry>(emptyFreightEntry);
  const entryRef = useRef(entry);
  /** The user changed the strip: it no longer follows the order type's default waypoints. */
  const edited = useRef(false);
  const setStops = (next: StopValues[], byUser = true) => {
    if (byUser) edited.current = true;
    stopsRef.current = next;
    setStopsState(next);
  };
  const setEntry = (change: Partial<FreightEntry>) => {
    entryRef.current = { ...entryRef.current, ...change };
    setEntryState(entryRef.current);
    setFreightErrors((current) => Object.fromEntries(Object.entries(current).filter(([key]) => !(key in change))));
  };
  const [stopErrors, setStopErrors] = useState<Record<string, string>>({});
  const [freightErrors, setFreightErrors] = useState<Partial<Record<FreightEntryField, string>>>({});
  const id = (field: string) => `${idPrefix}-entry-${field}`;

  /* The order type (its default waypoints) arrives or changes: an untouched strip starts again with its waypoints; a
   * strip the user already typed in keeps its stops and only gets the activities it has none for yet. */
  const seed = JSON.stringify(waypoints.map((w) => w?.id ?? null));
  useEffect(() => {
    if (!edited.current) setStops(stripStopsOf(waypoints, stripDate), false);
    else setStops(stopsRef.current.map((stop, index) => (stop.waypointTypeId || !waypoints[index] ? stop : { ...stop, waypointTypeId: waypoints[index]!.id, waypointTypeName: waypoints[index]!.name })), false);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the seed names the waypoints; the date follows below
  }, [seed]);
  /* The stops follow the order date until the user types a date of their own. */
  const lastDate = useRef(stripDate);
  useEffect(() => {
    const previous = lastDate.current;
    if (previous === stripDate) return;
    lastDate.current = stripDate;
    setStops(stopsRef.current.map((stop) => (stop.date === previous ? { ...stop, date: stripDate } : stop)), false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stripDate]);

  /** A stop whose relation was picked but whose address is not there yet (still being read) gets its default address. */
  const withAddresses = async (current: StopValues[]): Promise<StopValues[]> =>
    Promise.all(
      current.map(async (stop) => {
        if (!stop.relationId || stop.relationAddressId) return stop;
        try {
          const addresses = await queryClient.fetchQuery(relationAddressesQuery(stop.relationId));
          const addressId = defaultAddressIdOf(addresses);
          return addressId ? { ...stop, relationAddressId: addressId, city: addresses.find((a) => a.id === addressId)?.city ?? stop.city } : stop;
        } catch {
          return stop;
        }
      }),
    );

  const add = async () => {
    const resolved = await withAddresses(stopsRef.current);
    if (resolved.some((stop, index) => stop !== stopsRef.current[index])) setStops(resolved);
    const typed = entryRef.current;
    const problems = stopProblemsOf(resolved, mode);
    const fields = freightEntryErrors(typed);
    /* Each message names its stop ("Stop 2 (Unload): …"), under the field it is about. */
    setStopErrors(Object.fromEntries(problems.map((p) => [`${p.index}:${p.field}`, stopProblemText(t, resolved, p)])));
    setFreightErrors(fields);
    const first = problems[0];
    if (first) {
      document.getElementById(stopFieldId(id("strip"), first.index, STOP_FIELD_CONTROL[first.field]))?.focus();
      return;
    }
    const firstField = (Object.keys(fields) as FreightEntryField[])[0];
    if (firstField) {
      document.getElementById(id(FREIGHT_FIELD_IDS[firstField]))?.focus();
      return;
    }
    onAdd(itemFromStrip(resolved, typed));
    entryRef.current = nextFreightEntry(typed);
    setEntryState(entryRef.current);
    document.getElementById(stopFieldId(id("strip"), resolved.length - 1, "relation"))?.focus();
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== "Enter" || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey || event.defaultPrevented) return;
    const target = event.target as HTMLElement;
    /* An open lookup list takes its own Enter (it selects); a button its own (a click); a text area a new line. */
    if (target.getAttribute("aria-expanded") === "true" || target.tagName === "BUTTON" || target.tagName === "TEXTAREA") return;
    event.preventDefault();
    void add();
  };
  const onPaste = (event: ClipboardEvent<HTMLDivElement>) => {
    const text = event.clipboardData.getData("text/plain");
    if (!looksLikeRows(text)) return;
    event.preventDefault();
    onPasteRows(text);
  };

  const error = (field: FreightEntryField) => (freightErrors[field] ? t(freightErrors[field]!) : undefined);
  const numberField = (field: Exclude<FreightEntryField, "transportUnitId">, labelKey: string, flag?: number) => (
    <FormField label={t(labelKey)} htmlFor={id(FREIGHT_FIELD_IDS[field])} error={error(field)} required={flag != null && hasFlag(flags, flag)}>
      <Input id={id(FREIGHT_FIELD_IDS[field])} inputMode="decimal" className="tabular-nums" value={entry[field]} disabled={disabled} aria-invalid={freightErrors[field] != null} onChange={(e) => setEntry({ [field]: e.target.value })} />
    </FormField>
  );

  return (
    <FormSection title={t("Orders:FastEntry")}>
      <div className="flex flex-col gap-4" data-slot="freight-item-entry" onKeyDown={onKeyDown} onPaste={onPaste}>
        <StopStrip
          idPrefix={id("strip")}
          stops={stops}
          onChange={(next) => {
            setStops(next);
            setStopErrors({});
          }}
          mode={mode}
          readOnly={disabled}
          orderDate={stripDate}
          waypointTypeOptions={waypointOptions}
          timeSlotOptions={slotOptions}
          errors={stopErrors}
          requireReference={hasFlag(flags, REQUIRED_FLAG.LoadingReference) || hasFlag(flags, REQUIRED_FLAG.UnloadingReference)}
          requireWindow={hasFlag(flags, REQUIRED_FLAG.LoadingTimeWindow) || hasFlag(flags, REQUIRED_FLAG.UnloadingTimeWindow)}
        />
        <div className="flex items-start gap-2" data-slot="freight-fields">
          <div className="grid min-w-0 flex-1 grid-cols-12 gap-x-2 gap-y-3">
            <FormField label={t("Orders:TransportUnit")} htmlFor={id("unit")} required error={error("transportUnitId")} floating="always" className="col-span-2">
              <ReferenceCombobox
                id={id("unit")}
                value={entry.transportUnitId}
                onChange={(next) => setEntry({ transportUnitId: next, transportUnitName: unitOptions.find((o) => o.value === next)?.label ?? "" })}
                options={unitOptions}
                required
                disabled={disabled}
                aria-invalid={freightErrors.transportUnitId != null}
                {...inPlace.offer("TransportUnit", disabled, (r) => setEntry({ transportUnitId: r.id, transportUnitName: r.name }))}
              />
            </FormField>
            {numberField("quantity", "Orders:Quantity", REQUIRED_FLAG.QuantityPerLine)}
            <FormField label={t("Orders:Product")} htmlFor={id("product")} required={hasFlag(flags, REQUIRED_FLAG.ProductPerLine)} floating="always" className="col-span-2">
              <ReferenceCombobox
                id={id("product")}
                value={entry.productId}
                onChange={(next) => setEntry({ productId: next, productName: productOptions.find((o) => o.value === next)?.label ?? "" })}
                options={productOptions}
                disabled={disabled}
                {...inPlace.offer("Product", disabled, (r) => setEntry({ productId: r.id, productName: r.name }))}
              />
            </FormField>
            {numberField("positions", "Orders:Positions")}
            {numberField("colli", "Orders:Colli")}
            {numberField("weightKg", "Orders:WeightKg", REQUIRED_FLAG.WeightPerLine)}
            {numberField("loadingMeters", "Orders:LoadingMeters", REQUIRED_FLAG.LoadingMetersPerLine)}
            {numberField("volumeM3", "Orders:VolumeM3", REQUIRED_FLAG.VolumePerLine)}
            <div className="col-span-2 flex h-10 items-center gap-2">
              <Button type="button" variant="outline" disabled={disabled} onClick={() => void add()} aria-label={t("Orders:AddLine")} title={t("Orders:AddLineHint")}>
                <Plus /> {t("Orders:AddLine")}
              </Button>
              <Button type="button" variant="outline" disabled={disabled} onClick={() => onPasteRows("")}>
                <ClipboardPaste /> {t("Orders:PasteFromExcel")}
              </Button>
            </div>
          </div>
        </div>
        {inPlace.dialog}
      </div>
    </FormSection>
  );
}
