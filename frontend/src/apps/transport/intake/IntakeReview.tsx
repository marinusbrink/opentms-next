import { Fragment } from "react";

import { DatePicker } from "@/components/ui/date-picker";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { relationOptionOf } from "@/domains/master-data/components/RelationFields";
import { products, temperatures, transportUnits } from "@/domains/master-data/entities";
import { useLookupOptions } from "@/domains/master-data/shared";
import {
  INTAKE_STOP_ROLES,
  fieldPathLabel,
  intakeEnumLabel,
  type OrderIntakeDetail,
  type OrderIntakeFieldResolution,
  type OrderIntakePayload,
} from "@/domains/orders/order-intakes";
import { transportOrderTypes, waypointTypes } from "@/domains/orders/reference-data";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { AddressPicker, ReferencePicker, RelationPicker, ResolutionRow, type Pick } from "@/apps/transport/intake/ResolutionRow";
import {
  LINE_FIELDS,
  ORDER_FIELDS,
  STOP_FIELDS,
  linePath,
  orderPath,
  receivedAddressText,
  receivedText,
  stopPath,
  type ReceivedField,
} from "@/apps/transport/intake/review-model";

/* The Review tab (design 72 "Review", "UI design"): the received values grouped as order / lines / stops, each pickable
 * field with its resolution and a picker (UI-LOOK-004: "Add new …" on every picker, prefilled with the received text),
 * and every received value editable (a correction is stored as the reviewed payload). Read-only once the intake left
 * the review or without Orders.OrderIntakes.Resolve. */

const CUSTOMER_ROLE = 1;

export interface IntakeReviewProps {
  detail: OrderIntakeDetail;
  /** What was received (the reviewed payload when the clerk corrected it before). */
  base: OrderIntakePayload;
  overrides: Readonly<Record<string, string>>;
  onOverride: (path: string, text: string) => void;
  picks: Readonly<Record<string, Pick>>;
  onPick: (path: string, pick: Pick | undefined) => void;
  /** Validation keys by path (numbers), shown under the field. */
  errors: Readonly<Record<string, string>>;
  readOnly: boolean;
  resolution: Map<string, OrderIntakeFieldResolution>;
}

export function IntakeReview({ detail, base, overrides, onOverride, picks, onPick, errors, readOnly, resolution }: IntakeReviewProps) {
  const { t } = useL();
  const typeOptions = useLookupOptions(transportOrderTypes.useLookup(!readOnly));
  const waypointOptions = useLookupOptions(waypointTypes.useLookup(!readOnly));
  const unitOptions = useLookupOptions(transportUnits.useLookup(!readOnly));
  const productOptions = useLookupOptions(products.useLookup(!readOnly));
  const temperatureOptions = useLookupOptions(temperatures.useLookup(!readOnly));

  const field = (path: string, spec: ReceivedField) => {
    const id = `intake-${path.replace(/[^a-zA-Z0-9]/g, "-")}`;
    const value = receivedText(base, overrides, path);
    const label = fieldPathLabel(t, spec.name);
    const error = errors[path] ? t(errors[path]) : undefined;
    const className = spec.wide ? "col-span-4" : undefined;
    if (spec.kind === "date")
      return (
        <FormField key={path} label={label} htmlFor={id} error={error} className={className} floating="always">
          <DatePicker id={id} value={value || null} onChange={(next) => onOverride(path, next ?? "")} disabled={readOnly} />
        </FormField>
      );
    if (spec.kind === "multiline")
      return (
        <FormField key={path} label={label} htmlFor={id} error={error} className={className} floating="always">
          <Textarea id={id} value={value} rows={2} disabled={readOnly} onChange={(e) => onOverride(path, e.target.value)} />
        </FormField>
      );
    return (
      <FormField key={path} label={label} htmlFor={id} error={error} className={className}>
        <Input
          id={id}
          value={value}
          inputMode={spec.kind === "number" ? "decimal" : undefined}
          placeholder={spec.kind === "time" ? "HH:mm" : undefined}
          disabled={readOnly}
          className={spec.kind === "number" ? "tabular-nums" : undefined}
          onChange={(e) => onOverride(path, e.target.value)}
        />
      </FormField>
    );
  };

  /* A pickable field: its resolution row with the picker that fits the kind of row. */
  const resolved = (path: string, label: string, received: string, picker: (value: string | null, onChange: (pick: Pick | undefined) => void) => React.ReactNode) => {
    const entry = resolution.get(path);
    const pick = picks[path];
    return (
      <ResolutionRow key={path} label={label} path={path} resolution={entry} received={received} pick={pick} onPick={(next) => onPick(path, next)} readOnly={readOnly}>
        {picker(pick?.id ?? entry?.targetId ?? null, (next) => onPick(path, next))}
      </ResolutionRow>
    );
  };

  const customerName = receivedText(base, overrides, orderPath("customer.name"));
  const customerText = [customerName, base.customer?.code, base.customer?.emailAddress, base.customer?.vatNumber].filter(Boolean).join(", ");
  const typeName = receivedText(base, overrides, orderPath("orderTypeName"));
  const pickId = (path: string) => `intake-pick-${path.replace(/[^a-zA-Z0-9]/g, "-")}`;

  /* Fields the resolver looked at that have no place below (a future path) still show, so nothing open is hidden. */
  const known = /^(order\.(customer|orderType|orderDate|lines)|lines\[\d+\]\.(transportUnit|product|temperature|stops\[\d+\]\.(address|waypointType|date|timeFrom|timeTill)))$/;
  const others = [...resolution.values()].filter((entry) => entry.fieldPath && !known.test(entry.fieldPath) && entry.status !== 1);

  return (
    <div className="grid gap-4" data-slot="intake-review">
      <FormSection title={t("Orders:IntakeOrderSection")}>
        <div className="grid gap-3">
          <div className="grid grid-cols-2 gap-3">
            {resolved("order.customer", t("Orders:Customer"), customerText, (value, onChange) => (
              <RelationPicker
                id={pickId("order.customer")}
                label={t("Orders:Customer")}
                value={value ? (picks["order.customer"] ? relationOptionOf(value, picks["order.customer"].name) : relationOptionOf(value, resolution.get("order.customer")?.targetName)) : null}
                onChange={(next) => onChange(next ? { id: next.id, name: next.name } : undefined)}
                roles={CUSTOMER_ROLE}
                receivedName={customerName}
              />
            ))}
            {resolved("order.orderType", t("Orders:TransportOrderType"), typeName, (value, onChange) => (
              <ReferencePicker id={pickId("order.orderType")} label={t("Orders:TransportOrderType")} entity="TransportOrderType" options={typeOptions} value={value} onChange={onChange} receivedName={typeName} />
            ))}
            {resolution.has("order.orderDate") && resolved("order.orderDate", t("Orders:OrderDate"), receivedText(base, overrides, orderPath("orderDate")), () => null)}
          </div>
          <div className="grid grid-cols-4 gap-x-4 gap-y-3">{ORDER_FIELDS.map((spec) => field(orderPath(spec.name), spec))}</div>
          {(base.references ?? []).length > 0 && (
            <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
              {(base.references ?? []).map((reference, index) => (
                <Fragment key={index}>
                  <dt className="text-label text-muted-foreground">{reference.type}</dt>
                  <dd>{reference.value}</dd>
                </Fragment>
              ))}
            </dl>
          )}
        </div>
      </FormSection>

      {(base.lines ?? []).map((line, lineIndex) => {
        const unitName = receivedText(base, overrides, linePath(lineIndex, "transportUnitName"));
        const productName = receivedText(base, overrides, linePath(lineIndex, "productName"));
        const temperatureName = receivedText(base, overrides, linePath(lineIndex, "temperatureName"));
        return (
          <FormSection key={lineIndex} title={t("Orders:IntakeLineN").replace("{0}", String(lineIndex + 1))}>
            <div className="grid gap-3">
              <div className="grid grid-cols-3 gap-3">
                {resolved(linePath(lineIndex, "transportUnit"), t("Orders:IntakeField.transportUnit"), unitName, (value, onChange) => (
                  <ReferencePicker id={pickId(linePath(lineIndex, "transportUnit"))} label={t("Orders:IntakeField.transportUnit")} entity="TransportUnit" options={unitOptions} value={value} onChange={onChange} receivedName={unitName} />
                ))}
                {(resolution.has(linePath(lineIndex, "product")) || productName) &&
                  resolved(linePath(lineIndex, "product"), t("Orders:IntakeField.product"), productName, (value, onChange) => (
                    <ReferencePicker id={pickId(linePath(lineIndex, "product"))} label={t("Orders:IntakeField.product")} entity="Product" options={productOptions} value={value} onChange={onChange} receivedName={productName} />
                  ))}
                {(resolution.has(linePath(lineIndex, "temperature")) || temperatureName) &&
                  resolved(linePath(lineIndex, "temperature"), t("Orders:IntakeField.temperature"), temperatureName, (value, onChange) => (
                    <ReferencePicker id={pickId(linePath(lineIndex, "temperature"))} label={t("Orders:IntakeField.temperature")} entity="Temperature" options={temperatureOptions} value={value} onChange={onChange} receivedName={temperatureName} />
                  ))}
              </div>
              <div className="grid grid-cols-4 gap-x-4 gap-y-3">{LINE_FIELDS.map((spec) => field(linePath(lineIndex, spec.name), spec))}</div>
              {(line.stops ?? []).map((stop, stopIndex) => {
                const addressPath = stopPath(lineIndex, stopIndex, "address");
                const waypointPath = stopPath(lineIndex, stopIndex, "waypointType");
                const datePath = stopPath(lineIndex, stopIndex, "date");
                const addressText = receivedAddressText({
                  name: receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.name")),
                  street: receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.street")),
                  houseNumber: receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.houseNumber")),
                  postalCode: receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.postalCode")),
                  city: receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.city")),
                  countryCode: receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.countryCode")),
                  fullText: receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.fullText")),
                });
                const waypointName = receivedText(base, overrides, stopPath(lineIndex, stopIndex, "waypointTypeName"));
                const role = stop.role != null ? intakeEnumLabel(t, "IntakeStopRole", INTAKE_STOP_ROLES, stop.role) : t("Orders:IntakeStopN").replace("{0}", String(stopIndex + 1));
                return (
                  <section key={stopIndex} className="grid gap-3 rounded-lg border border-border-section p-3" aria-label={role} data-stop={`${lineIndex}-${stopIndex}`}>
                    <h4 className="text-label font-semibold text-brand">{role}</h4>
                    <div className="grid grid-cols-2 gap-3">
                      {resolved(addressPath, t("Orders:IntakeField.address"), addressText, (value, onChange) => (
                        <AddressPicker
                          id={pickId(addressPath)}
                          value={value}
                          onChange={onChange}
                          receivedName={receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.name"))}
                          receivedAddress={{
                            street: receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.street")),
                            houseNumber: receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.houseNumber")),
                            postalCode: receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.postalCode")),
                            city: receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.city")),
                            countryCode: receivedText(base, overrides, stopPath(lineIndex, stopIndex, "address.countryCode")),
                          }}
                        />
                      ))}
                      {resolved(waypointPath, t("Orders:IntakeField.waypointType"), waypointName, (value, onChange) => (
                        <ReferencePicker id={pickId(waypointPath)} label={t("Orders:IntakeField.waypointType")} entity="WaypointType" options={waypointOptions} value={value} onChange={onChange} receivedName={waypointName} />
                      ))}
                      {resolution.has(datePath) && resolution.get(datePath)?.status !== 1 && resolved(datePath, t("Orders:IntakeField.date"), receivedText(base, overrides, datePath), () => null)}
                    </div>
                    <div className="grid grid-cols-4 gap-x-4 gap-y-3">{STOP_FIELDS.map((spec) => field(stopPath(lineIndex, stopIndex, spec.name), spec))}</div>
                  </section>
                );
              })}
            </div>
          </FormSection>
        );
      })}

      {others.length > 0 && (
        <FormSection title={t("Orders:IntakeOtherFields")}>
          <div className="grid grid-cols-2 gap-3">
            {others.map((entry) => (
              <ResolutionRow
                key={entry.fieldPath}
                label={fieldPathLabel(t, entry.fieldPath!, detail.payload)}
                path={entry.fieldPath!}
                resolution={entry}
                received=""
                pick={picks[entry.fieldPath!]}
                onPick={(next) => onPick(entry.fieldPath!, next)}
                readOnly={readOnly}
              />
            ))}
          </div>
        </FormSection>
      )}
    </div>
  );
}
