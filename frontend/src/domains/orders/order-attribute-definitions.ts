import type { components } from "@/lib/api/generated/schema";
import { createReferenceDataHooks } from "@/lib/reference-data/crud-hooks";

/* The tenant's custom attributes of an order or of its lines (design 58 "Attribute definitions", Q12): a key unique
 * per target, a localized name, a position. Design-50 list contract with the target as a list filter. */

type Schemas = components["schemas"];

export type OrderAttributeTarget = Schemas["OpenTms.Orders.TransportOrders.OrderAttributeTarget"];
export const ORDER_ATTRIBUTE_TARGETS: { value: OrderAttributeTarget; key: string }[] = [
  { value: 1, key: "Orders:OrderAttributeTarget.Order" },
  { value: 2, key: "Orders:OrderAttributeTarget.Line" },
];
export const orderAttributeTargetKey = (value: OrderAttributeTarget | null | undefined) => ORDER_ATTRIBUTE_TARGETS.find((o) => o.value === value)?.key ?? "";

/** OrdersConsts.MaxAttributeKeyLength; the position range of the DTO. The server stores the key as typed (trimmed). */
export const MAX_ATTRIBUTE_KEY_LENGTH = 32;
export const MAX_ORDER_ATTRIBUTE_POSITION = 1000;

export type OrderAttributeDefinitionRow = Schemas["OpenTms.Orders.TransportOrders.OrderAttributeDefinitionRowDto"];
export type OrderAttributeDefinitionDto = Schemas["OpenTms.Orders.TransportOrders.OrderAttributeDefinitionDto"];
export type OrderAttributeDefinitionCreateDto = Schemas["OpenTms.Orders.TransportOrders.OrderAttributeDefinitionCreateDto"];
export type OrderAttributeDefinitionUpdateDto = Schemas["OpenTms.Orders.TransportOrders.OrderAttributeDefinitionUpdateDto"];

export type OrderAttributeDefinitionListExtra = { target?: OrderAttributeTarget; includeArchived?: boolean };

export const orderAttributeDefinitions = createReferenceDataHooks<
  OrderAttributeDefinitionRow,
  OrderAttributeDefinitionDto,
  OrderAttributeDefinitionCreateDto,
  OrderAttributeDefinitionUpdateDto,
  OrderAttributeDefinitionListExtra
>("orderAttributeDefinitions", "/api/orders/order-attribute-definition");
