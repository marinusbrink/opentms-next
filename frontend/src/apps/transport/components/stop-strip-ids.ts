import type { StopField } from "@/apps/transport/orders/order-form";

/** The id of a stop strip field (StopStrip): `<prefix>-stop-<index>-<field>`; the entry puts the cursor there. */
export const stopFieldId = (idPrefix: string, index: number, field: string) => `${idPrefix}-stop-${index}-${field}`;

/** The control a stop problem's field is shown at (and focused on). */
export const STOP_FIELD_CONTROL: Record<StopField, string> = { waypointTypeId: "activity", relationId: "relation", relationAddressId: "address", date: "date", window: "from" };
