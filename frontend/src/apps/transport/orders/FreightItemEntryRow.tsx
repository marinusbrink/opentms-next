import { useEffect, useMemo, useRef, useState, type ClipboardEvent, type KeyboardEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { toOptions } from "@/apps/transport/components/fields";
import { RelationField, defaultAddressIdOf } from "@/apps/transport/components/RelationFields";
import {
  EMPTY_END,
  REQUIRED_FLAG,
  emptyEntry,
  entryErrors,
  hasFlag,
  itemFromEntry,
  nextEntry,
  type EditorMode,
  type EntryEnd,
  type EntryWaypoints,
  type ItemEntry,
  type LineValues,
} from "@/apps/transport/orders/order-form";
import { looksLikeRows } from "@/apps/transport/orders/paste-items";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { relationAddressesQuery, transportUnits, type RelationOption } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The fast entry row under the freight item list (design 61 "Order page, revised" 3), keyboard first: From, To,
 * date (template: day offset) and time, transport unit, quantity, weight, reference. Tab moves on (and commits the
 * highlighted option of a lookup, UI-KEY-004); Enter adds the item and keeps From, date, time and unit for the next
 * one, with the cursor back in To. An unknown relation or unit offers "Add new …" from the keyboard: Enter on a typed
 * term without a match opens the editor with it (UI-LOOK-004). The relation's default (or only) address is taken;
 * From and To get the order type's first and last default waypoint type. Enter inside an open lookup list selects
 * there and never adds (UI-KEY-001: the lookup's own key). A paste of spreadsheet rows (a tab or a second line) opens
 * the paste preview instead of filling one field. */

export interface FreightItemEntryRowProps {
  idPrefix: string;
  mode: EditorMode;
  /** The order date: the first item's date. */
  orderDate: string | null;
  waypoints: EntryWaypoints;
  /** The order type's required-field flags (UI-FORM-002 marks). */
  flags: number | undefined;
  disabled: boolean;
  onAdd: (item: LineValues) => void;
  onPasteRows: (text: string) => void;
}

type EntryField = keyof ReturnType<typeof entryErrors>;

export function FreightItemEntryRow({ idPrefix, mode, orderDate, waypoints, flags, disabled, onAdd, onPasteRows }: FreightItemEntryRowProps) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const unitLookup = transportUnits.useLookup();
  const unitOptions = useMemo(() => toOptions(unitLookup.data), [unitLookup.data]);
  const inPlace = useCreateInPlace();
  /* The latest entry, also for an Enter that follows a change in the same event (a date committed on Enter). */
  const [entry, setEntryState] = useState<ItemEntry>(() => emptyEntry(orderDate));
  const entryRef = useRef(entry);
  const setEntry = (change: (current: ItemEntry) => ItemEntry) => {
    entryRef.current = change(entryRef.current);
    setEntryState(entryRef.current);
  };
  const [errors, setErrors] = useState<ReturnType<typeof entryErrors>>({});
  const id = (field: string) => `${idPrefix}-entry-${field}`;
  const error = (field: EntryField) => (errors[field] ? t(errors[field]!) : undefined);
  /* The entry follows the order date until the user types a date of their own. */
  const lastOrderDate = useRef(orderDate);
  useEffect(() => {
    const previous = lastOrderDate.current;
    if (previous === orderDate) return;
    lastOrderDate.current = orderDate;
    if (entryRef.current.date === previous) {
      entryRef.current = { ...entryRef.current, date: orderDate };
      setEntryState(entryRef.current);
    }
  }, [orderDate]);

  /** The relation with its default (or only) address and that address's city. */
  const resolve = async (relation: RelationOption | null): Promise<EntryEnd> => {
    if (!relation) return EMPTY_END;
    try {
      const addresses = await queryClient.fetchQuery(relationAddressesQuery(relation.id));
      const addressId = defaultAddressIdOf(addresses);
      return { relation, addressId, city: addresses.find((a) => a.id === addressId)?.city ?? relation.city ?? "" };
    } catch {
      /* Without the address list the item still gets the relation; its address is picked in the item's detail. */
      return { relation, addressId: null, city: relation.city ?? "" };
    }
  };
  const pending = useRef<Partial<Record<"from" | "to", Promise<EntryEnd>>>>({});
  const changeEnd = (end: "from" | "to", relation: RelationOption | null) => {
    setEntry((current) => ({ ...current, [end]: { relation, addressId: null, city: relation?.city ?? "" } }));
    setErrors((current) => ({ ...current, [end]: undefined }));
    const resolving = resolve(relation);
    pending.current[end] = resolving;
    void resolving.then((resolved) => {
      if (pending.current[end] !== resolving) return;
      setEntry((current) => (current[end].relation?.id === relation?.id ? { ...current, [end]: resolved } : current));
    });
  };

  const add = async () => {
    const typed = entryRef.current;
    const found = entryErrors(typed, mode);
    setErrors(found);
    const first = (Object.keys(found) as EntryField[])[0];
    if (first) {
      document.getElementById(id(first === "transportUnitId" ? "unit" : first))?.focus();
      return;
    }
    /* An address still being read is awaited: Enter right after picking a relation takes its default address too. */
    const from = typed.from.addressId ? typed.from : await (pending.current.from ?? resolve(typed.from.relation));
    const to = typed.to.addressId ? typed.to : await (pending.current.to ?? resolve(typed.to.relation));
    onAdd(itemFromEntry({ ...typed, from, to }, waypoints));
    setEntry((current) => nextEntry({ ...current, from }));
    setErrors({});
    pending.current = { from: pending.current.from };
    document.getElementById(id("to"))?.focus();
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== "Enter" || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey || event.defaultPrevented) return;
    const target = event.target as HTMLElement;
    /* An open lookup list takes its own Enter (it selects); a button its own (a click). */
    if (target.getAttribute("aria-expanded") === "true" || target.tagName === "BUTTON") return;
    event.preventDefault();
    void add();
  };
  const onPaste = (event: ClipboardEvent<HTMLDivElement>) => {
    const text = event.clipboardData.getData("text/plain");
    if (!looksLikeRows(text)) return;
    event.preventDefault();
    onPasteRows(text);
  };

  const set = (change: Partial<ItemEntry>) => {
    setEntry((current) => ({ ...current, ...change }));
    setErrors((current) => Object.fromEntries(Object.entries(current).filter(([key]) => !(key in change))));
  };

  return (
    <div className="flex items-start gap-2" data-slot="freight-item-entry" onKeyDown={onKeyDown} onPaste={onPaste} aria-label={t("Orders:FastEntry")} role="group">
      <div className="grid min-w-0 flex-1 grid-cols-12 gap-x-2 gap-y-3">
        <RelationField id={id("from")} label={t("Orders:FromRelation")} value={entry.from.relation} onChange={(next) => changeEnd("from", next)} required disabled={disabled} error={error("from")} className="col-span-2" />
        <RelationField id={id("to")} label={t("Orders:ToRelation")} value={entry.to.relation} onChange={(next) => changeEnd("to", next)} required disabled={disabled} error={error("to")} className="col-span-2" />
        {mode === "order" ? (
          <FormField label={t("Orders:Date")} htmlFor={id("date")} required error={error("date")} floating="always" className="col-span-2">
            <DatePicker id={id("date")} value={entry.date} onChange={(next) => set({ date: next })} required disabled={disabled} commitWhileTyping />
          </FormField>
        ) : (
          <FormField label={t("Orders:DayOffset")} htmlFor={id("dayOffset")} error={error("dayOffset")} className="col-span-2">
            <Input id={id("dayOffset")} inputMode="numeric" className="tabular-nums" value={entry.dayOffset} disabled={disabled} onChange={(e) => set({ dayOffset: e.target.value })} />
          </FormField>
        )}
        <FormField label={t("Orders:Time")} htmlFor={id("time")} error={error("time")} required={hasFlag(flags, REQUIRED_FLAG.LoadingTimeWindow)}>
          <Input id={id("time")} inputMode="numeric" className="tabular-nums" value={entry.time} disabled={disabled} onChange={(e) => set({ time: e.target.value })} />
        </FormField>
        <FormField label={t("Orders:TransportUnit")} htmlFor={id("unit")} required error={error("transportUnitId")} floating="always" className="col-span-2">
          <ReferenceCombobox
            id={id("unit")}
            value={entry.transportUnitId}
            onChange={(next) => set({ transportUnitId: next, transportUnitName: unitOptions.find((o) => o.value === next)?.label ?? "" })}
            options={unitOptions}
            required
            disabled={disabled}
            aria-invalid={errors.transportUnitId != null}
            {...inPlace.offer("TransportUnit", disabled, (r) => set({ transportUnitId: r.id, transportUnitName: r.name }))}
          />
        </FormField>
        <FormField label={t("Orders:Quantity")} htmlFor={id("quantity")} error={error("quantity")} required={hasFlag(flags, REQUIRED_FLAG.QuantityPerLine)}>
          <Input id={id("quantity")} inputMode="decimal" className="tabular-nums" value={entry.quantity} disabled={disabled} onChange={(e) => set({ quantity: e.target.value })} />
        </FormField>
        <FormField label={t("Orders:WeightKg")} htmlFor={id("weightKg")} error={error("weightKg")} required={hasFlag(flags, REQUIRED_FLAG.WeightPerLine)}>
          <Input id={id("weightKg")} inputMode="decimal" className="tabular-nums" value={entry.weightKg} disabled={disabled} onChange={(e) => set({ weightKg: e.target.value })} />
        </FormField>
        <FormField label={t("Orders:Reference")} htmlFor={id("reference")} required={hasFlag(flags, REQUIRED_FLAG.LoadingReference)}>
          <Input id={id("reference")} value={entry.reference} maxLength={128} disabled={disabled} onChange={(e) => set({ reference: e.target.value })} />
        </FormField>
      </div>
      <Button type="button" variant="outline" className="h-10" disabled={disabled} onClick={() => void add()} aria-label={t("Orders:AddLine")} title={t("Orders:AddLineHint")}>
        <Plus /> {t("Orders:AddLine")}
      </Button>
      {inPlace.dialog}
    </div>
  );
}
