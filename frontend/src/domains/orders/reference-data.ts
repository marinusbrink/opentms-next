import { useMutation, useQuery } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { createReferenceDataHooks } from "@/lib/reference-data/crud-hooks";

/* The Orders reference data shown in the Master data app (design 51). Row = grid, Dto = editor, Create/Update = what
 * the editor sends; the schema names carry the backend namespaces. */

type Schemas = components["schemas"];

export type WaypointActivity = Schemas["OpenTms.Orders.TransportOrders.WaypointActivity"];
export const WAYPOINT_ACTIVITIES: { value: WaypointActivity; key: string }[] = [
  { value: 1, key: "Orders:WaypointActivity.Load" },
  { value: 2, key: "Orders:WaypointActivity.Unload" },
  { value: 3, key: "Orders:WaypointActivity.Via" },
  { value: 4, key: "Orders:WaypointActivity.CoupleTrailer" },
  { value: 5, key: "Orders:WaypointActivity.UncoupleTrailer" },
  { value: 6, key: "Orders:WaypointActivity.Customs" },
  { value: 7, key: "Orders:WaypointActivity.Cleaning" },
  { value: 8, key: "Orders:WaypointActivity.Rest" },
  { value: 9, key: "Orders:WaypointActivity.Refuel" },
  { value: 10, key: "Orders:WaypointActivity.Other" },
];

/** A [Flags] set on the server; the generated type lists the single flags only, so the combined value is a number. */
export type TransportOrderRequiredFields = number;
export const REQUIRED_FIELDS: { flag: number; key: string }[] = [
  { flag: 1, key: "Orders:RequiredField.CustomerReference" },
  { flag: 2, key: "Orders:RequiredField.LoadingReference" },
  { flag: 4, key: "Orders:RequiredField.UnloadingReference" },
  { flag: 8, key: "Orders:RequiredField.LoadingTimeWindow" },
  { flag: 16, key: "Orders:RequiredField.UnloadingTimeWindow" },
  { flag: 32, key: "Orders:RequiredField.ProductPerLine" },
  { flag: 64, key: "Orders:RequiredField.TransportUnitPerLine" },
  { flag: 128, key: "Orders:RequiredField.QuantityPerLine" },
  { flag: 256, key: "Orders:RequiredField.WeightPerLine" },
  { flag: 512, key: "Orders:RequiredField.VolumePerLine" },
  { flag: 1024, key: "Orders:RequiredField.LoadingMetersPerLine" },
  { flag: 2048, key: "Orders:RequiredField.Temperature" },
];

export type TransportOrderTypeRow = Schemas["OpenTms.Orders.TransportOrders.TransportOrderTypeRowDto"];
export type TransportOrderTypeDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderTypeDto"];
export type TransportOrderTypeCreateDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderTypeCreateDto"];
export type TransportOrderTypeUpdateDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderTypeUpdateDto"];

export type WaypointTypeRow = Schemas["OpenTms.Orders.TransportOrders.WaypointTypeRowDto"];
export type WaypointTypeDto = Schemas["OpenTms.Orders.TransportOrders.WaypointTypeDto"];
export type WaypointTypeCreateDto = Schemas["OpenTms.Orders.TransportOrders.WaypointTypeCreateDto"];
export type WaypointTypeUpdateDto = Schemas["OpenTms.Orders.TransportOrders.WaypointTypeUpdateDto"];

export type TimeSlotRow = Schemas["OpenTms.Orders.TransportOrders.TimeSlotRowDto"];
export type TimeSlotDto = Schemas["OpenTms.Orders.TransportOrders.TimeSlotDto"];
export type TimeSlotCreateDto = Schemas["OpenTms.Orders.TransportOrders.TimeSlotCreateDto"];
export type TimeSlotUpdateDto = Schemas["OpenTms.Orders.TransportOrders.TimeSlotUpdateDto"];

export const transportOrderTypes = createReferenceDataHooks<
  TransportOrderTypeRow,
  TransportOrderTypeDto,
  TransportOrderTypeCreateDto,
  TransportOrderTypeUpdateDto,
  { includeArchived?: boolean }
>("transportOrderTypes", "/api/orders/transport-order-type");

export const waypointTypes = createReferenceDataHooks<
  WaypointTypeRow,
  WaypointTypeDto,
  WaypointTypeCreateDto,
  WaypointTypeUpdateDto,
  { includeArchived?: boolean }
>("waypointTypes", "/api/orders/waypoint-type");

export const timeSlots = createReferenceDataHooks<
  TimeSlotRow,
  TimeSlotDto,
  TimeSlotCreateDto,
  TimeSlotUpdateDto,
  { includeArchived?: boolean }
>("timeSlots", "/api/orders/time-slot");

/** The active waypoint types with their activity (the lookup has none): the order page's fast entry falls back to the
 *  first Load and the first Unload type when the order type names no default waypoints (design 61 "Order page, revised"). */
export function useWaypointTypeActivities(enabled = true) {
  return useQuery({
    queryKey: ["referenceData", "waypointTypes", "activities"],
    enabled,
    staleTime: 60 * 1000,
    queryFn: async (): Promise<WaypointTypeRow[]> => {
      const { data, error } = await apiClient.GET("/api/orders/waypoint-type", { params: { query: { StartRow: 0, EndRow: 200, IncludeArchived: false } } });
      if (error) throw error;
      return data?.rows ?? [];
    },
  });
}

/** Moves the tenant's one default transport order type (design 51). */
export function useSetDefaultTransportOrderType() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST("/api/orders/transport-order-type/{id}/set-default", { params: { path: { id } } });
      if (error) throw error;
    },
  });
}

/** "08:00:00" (the server's TimeOnly) → "08:00", the HH:mm the screens show (UI-FMT-004). */
export function timeOfDay(value: string | null | undefined): string {
  return value ? value.slice(0, 5) : "";
}

/** "8:05", "0805" or "08:05" → "08:05:00"; null when it is not a time of day. */
export function parseTimeOfDay(text: string): string | null {
  const match = /^(\d{1,2}):?(\d{2})$/.exec(text.trim());
  if (!match) return null;
  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  if (hours > 23 || minutes > 59) return null;
  return `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:00`;
}
