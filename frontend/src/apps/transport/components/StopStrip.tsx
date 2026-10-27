import { ArrowDown, ArrowUp, Minus, Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox, type ReferenceOption } from "@/components/ui/reference-combobox";
import { AddressField, ContactPersonField, RelationField, relationOptionOf } from "@/apps/transport/components/RelationFields";
import { stopFieldId } from "@/apps/transport/components/stop-strip-ids";
import { MAX_STOPS, MIN_STOPS, newStop, type EditorMode, type StopField, type StopValues } from "@/apps/transport/orders/order-form";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { useRelationAddresses } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The stops of a freight item as a strip of rows, one row per stop (design 61 "Order page, legacy tab structure" 3
 * and 4; the legacy waypoint strip): activity (waypoint type), relation and its address (the default or only address
 * preselected), contact, date (template: day offset), time slot, the window, reference and remarks. At least two
 * stops: + adds a stop after its row, − removes one (never below two); in the Edit freight item dialog a stop also
 * moves up and down. Keyboard first: Tab runs through a row's fields and on to the next row, every picker offers
 * "Add new …" from the keyboard (UI-LOOK-004). The same editor serves the stop strip of the item being entered and
 * the item's own stops. Field ids end in `-stop-<index>-<field>` (the entry puts the cursor back in the last stop's
 * relation after Add). */

export interface StopStripProps {
  idPrefix: string;
  stops: StopValues[];
  onChange: (stops: StopValues[]) => void;
  mode: EditorMode;
  readOnly: boolean;
  /** The date a new stop starts with (order mode). */
  orderDate: string | null;
  waypointTypeOptions: ReferenceOption[];
  timeSlotOptions: ReferenceOption[];
  /** The messages to show under the fields, by `${index}:${field}` (already localized). */
  errors?: Partial<Record<string, string>>;
  /** Move up / down per row (the Edit freight item dialog). */
  movable?: boolean;
  /** The order type's required marks (UI-FORM-002): references and windows of the stops. */
  requireReference?: boolean;
  requireWindow?: boolean;
}

export function StopStrip({ idPrefix, stops, onChange, mode, readOnly, orderDate, waypointTypeOptions, timeSlotOptions, errors = {}, movable = false, requireReference, requireWindow }: StopStripProps) {
  const { t } = useL();
  const change = (index: number, patch: Partial<StopValues>) => onChange(stops.map((stop, i) => (i === index ? { ...stop, ...patch } : stop)));
  const insertAfter = (index: number) => {
    if (stops.length >= MAX_STOPS) return;
    /* The new stop takes the date of the one before it: a stop strip mostly runs on one day. */
    const stop = newStop(mode === "order" ? (stops[index]?.date ?? orderDate) : null);
    onChange([...stops.slice(0, index + 1), stop, ...stops.slice(index + 1)]);
  };
  const remove = (index: number) => {
    if (stops.length <= MIN_STOPS) return;
    onChange(stops.filter((_, i) => i !== index));
  };
  const move = (index: number, delta: number) => {
    const target = index + delta;
    if (target < 0 || target >= stops.length) return;
    const next = [...stops];
    [next[index], next[target]] = [next[target], next[index]];
    onChange(next);
  };

  return (
    <div className="flex flex-col gap-3" data-slot="stop-strip" role="group" aria-label={t("Orders:Stops")}>
      {stops.map((stop, index) => (
        <StopRow
          key={stop.key}
          idPrefix={idPrefix}
          index={index}
          count={stops.length}
          stop={stop}
          mode={mode}
          readOnly={readOnly}
          onChange={(patch) => change(index, patch)}
          onInsert={() => insertAfter(index)}
          onRemove={() => remove(index)}
          onMove={movable ? (delta) => move(index, delta) : undefined}
          waypointTypeOptions={waypointTypeOptions}
          timeSlotOptions={timeSlotOptions}
          error={(field) => errors[`${index}:${field}`]}
          requireReference={requireReference}
          requireWindow={requireWindow}
        />
      ))}
    </div>
  );
}

interface StopRowProps {
  idPrefix: string;
  index: number;
  count: number;
  stop: StopValues;
  mode: EditorMode;
  readOnly: boolean;
  onChange: (patch: Partial<StopValues>) => void;
  onInsert: () => void;
  onRemove: () => void;
  onMove?: (delta: number) => void;
  waypointTypeOptions: ReferenceOption[];
  timeSlotOptions: ReferenceOption[];
  error: (field: StopField) => string | undefined;
  requireReference?: boolean;
  requireWindow?: boolean;
}

function StopRow({ idPrefix, index, count, stop, mode, readOnly, onChange, onInsert, onRemove, onMove, waypointTypeOptions, timeSlotOptions, error, requireReference, requireWindow }: StopRowProps) {
  const { t } = useL();
  const id = (field: string) => stopFieldId(idPrefix, index, field);
  /* The address list of the stop's relation (shared with AddressField's query): the grid shows the address's city. */
  const addresses = useRelationAddresses(stop.relationId ?? undefined);
  const inPlace = useCreateInPlace();
  const labelOf = (options: ReferenceOption[], value: string | null) => options.find((o) => o.value === value)?.label ?? "";
  const position = index + 1;

  return (
    <div className="flex items-start gap-2" data-slot="stop-row" data-index={index} role="group" aria-label={`${t("Orders:Stop")} ${position}`}>
      <span className="flex h-10 w-6 shrink-0 items-center justify-end text-label font-semibold text-muted-foreground tabular-nums" aria-hidden="true">
        {position}
      </span>
      <div className="grid min-w-0 flex-1 grid-cols-12 gap-x-2 gap-y-3">
        <FormField label={t("Orders:Activity")} htmlFor={id("activity")} required error={error("waypointTypeId")} floating="always">
          <ReferenceCombobox
            id={id("activity")}
            value={stop.waypointTypeId}
            onChange={(next) => onChange({ waypointTypeId: next, waypointTypeName: labelOf(waypointTypeOptions, next) })}
            options={waypointTypeOptions}
            required
            disabled={readOnly}
            aria-invalid={error("waypointTypeId") != null}
            {...inPlace.offer("WaypointType", readOnly, (r) => onChange({ waypointTypeId: r.id, waypointTypeName: r.name }))}
          />
        </FormField>
        <RelationField
          id={id("relation")}
          label={t("Orders:Relation")}
          value={relationOptionOf(stop.relationId, stop.relationName)}
          onChange={(next) => onChange({ relationId: next?.id ?? null, relationName: next?.name ?? "", relationAddressId: null, city: next?.city ?? "", contactPersonId: null })}
          required
          disabled={readOnly}
          error={error("relationId")}
          className="col-span-2"
        />
        <AddressField
          id={id("address")}
          label={t("Orders:Address")}
          relationId={stop.relationId}
          value={stop.relationAddressId}
          onChange={(next) => onChange({ relationAddressId: next, city: addresses.data?.find((a) => a.id === next)?.city ?? stop.city })}
          required
          disabled={readOnly}
          error={error("relationAddressId")}
          className="col-span-2"
        />
        <ContactPersonField id={id("contact")} labelKey="Orders:ContactPerson" relationId={stop.relationId} value={stop.contactPersonId} onChange={(next) => onChange({ contactPersonId: next })} disabled={readOnly} />
        {mode === "order" ? (
          <FormField label={t("Orders:Date")} htmlFor={id("date")} required error={error("date")} floating="always">
            <DatePicker id={id("date")} value={stop.date} onChange={(next) => onChange({ date: next })} required disabled={readOnly} commitWhileTyping />
          </FormField>
        ) : (
          <FormField label={t("Orders:DayOffset")} htmlFor={id("dayOffset")}>
            <Input id={id("dayOffset")} inputMode="numeric" className="tabular-nums" value={stop.dayOffset} disabled={readOnly} onChange={(e) => onChange({ dayOffset: e.target.value })} />
          </FormField>
        )}
        <FormField label={t("Orders:TimeSlot")} htmlFor={id("slot")} floating="always">
          <ReferenceCombobox
            id={id("slot")}
            value={stop.timeSlotId}
            onChange={(next) => onChange({ timeSlotId: next })}
            options={timeSlotOptions}
            disabled={readOnly}
            {...inPlace.offer("TimeSlot", readOnly, (r) => onChange({ timeSlotId: r.id }))}
          />
        </FormField>
        <FormField label={t("Orders:WindowFrom")} htmlFor={id("from")} required={requireWindow} error={error("window")}>
          <Input id={id("from")} inputMode="numeric" className="tabular-nums" value={stop.windowFrom} disabled={readOnly} onChange={(e) => onChange({ windowFrom: e.target.value })} />
        </FormField>
        <FormField label={t("Orders:WindowTill")} htmlFor={id("till")} required={requireWindow}>
          <Input id={id("till")} inputMode="numeric" className="tabular-nums" value={stop.windowTill} disabled={readOnly} onChange={(e) => onChange({ windowTill: e.target.value })} />
        </FormField>
        <FormField label={t("Orders:Reference")} htmlFor={id("reference")} required={requireReference}>
          <Input id={id("reference")} value={stop.reference} maxLength={128} disabled={readOnly} onChange={(e) => onChange({ reference: e.target.value })} />
        </FormField>
        <FormField label={t("Orders:Remarks")} htmlFor={id("remarks")}>
          <Input id={id("remarks")} value={stop.remarks} maxLength={2500} disabled={readOnly} onChange={(e) => onChange({ remarks: e.target.value })} />
        </FormField>
      </div>
      {!readOnly && (
        <div className="flex h-10 shrink-0 items-center gap-0.5">
          {onMove && (
            <>
              <Button type="button" variant="ghost" size="icon-sm" aria-label={t("Orders:MoveUp")} title={t("Orders:MoveUp")} disabled={index === 0} onClick={() => onMove(-1)}>
                <ArrowUp />
              </Button>
              <Button type="button" variant="ghost" size="icon-sm" aria-label={t("Orders:MoveDown")} title={t("Orders:MoveDown")} disabled={index === count - 1} onClick={() => onMove(1)}>
                <ArrowDown />
              </Button>
            </>
          )}
          <Button type="button" variant="ghost" size="icon-sm" aria-label={t("Orders:AddStop")} title={t("Orders:AddStop")} disabled={count >= MAX_STOPS} onClick={onInsert}>
            <Plus />
          </Button>
          <Button type="button" variant="ghost" size="icon-sm" aria-label={t("Orders:RemoveStop")} title={count <= MIN_STOPS ? t("Orders:LineNeedsTwoStopsHint") : t("Orders:RemoveStop")} disabled={count <= MIN_STOPS} onClick={onRemove}>
            <Minus />
          </Button>
        </div>
      )}
      {inPlace.dialog}
    </div>
  );
}
