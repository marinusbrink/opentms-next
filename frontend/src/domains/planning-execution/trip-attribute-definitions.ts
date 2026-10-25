import type { components } from "@/lib/api/generated/schema";
import { createReferenceDataHooks } from "@/lib/reference-data/crud-hooks";

/* The tenant's custom trip attributes (design 56 "Attributes", design 59): a key, a localized name, a position.
 * Design-50 list contract. */

type Schemas = components["schemas"];

/** PlanningExecutionConsts.MaxAttributeDefinitionKeyLength; the position range of the DTO. */
export const MAX_TRIP_ATTRIBUTE_KEY_LENGTH = 32;
export const MAX_TRIP_ATTRIBUTE_POSITION = 10_000;

/** The server's key shape (TripAttributeDefinition.KeyShape): a letter, then letters, digits or underscores. */
export const TRIP_ATTRIBUTE_KEY_SHAPE = /^[A-Za-z][A-Za-z0-9_]*$/;

export type TripAttributeDefinitionRow = Schemas["OpenTms.PlanningExecution.TransportTrips.TripAttributeDefinitionRowDto"];
export type TripAttributeDefinitionDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TripAttributeDefinitionDto"];
export type TripAttributeDefinitionCreateDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TripAttributeDefinitionCreateDto"];
export type TripAttributeDefinitionUpdateDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TripAttributeDefinitionUpdateDto"];

export const tripAttributeDefinitions = createReferenceDataHooks<
  TripAttributeDefinitionRow,
  TripAttributeDefinitionDto,
  TripAttributeDefinitionCreateDto,
  TripAttributeDefinitionUpdateDto,
  { includeArchived?: boolean }
>("tripAttributeDefinitions", "/api/planning/trip-attribute-definition");
