import type { components } from "@/lib/api/generated/schema";
import { createReferenceDataHooks } from "@/lib/reference-data/crud-hooks";

/* The via-depot rules of Planning & Execution (design 59 "ViaDepotRule"): for a relation, a customer or a region, the
 * depot (a Master Data relation and one of its addresses) the legs run through, the activity on arrival and departure,
 * whether the goods stay overnight. Design-50 list contract (no lookup: rules are never picked). */

type Schemas = components["schemas"];

export type ViaDepotScope = Schemas["OpenTms.PlanningExecution.Legs.ViaDepotScope"];
export const VIA_DEPOT_SCOPES: { value: ViaDepotScope; key: string }[] = [
  { value: 1, key: "PlanningExecution:ViaDepotScope.Relation" },
  { value: 2, key: "PlanningExecution:ViaDepotScope.Customer" },
  { value: 3, key: "PlanningExecution:ViaDepotScope.Region" },
];
export const viaDepotScopeKey = (value: ViaDepotScope | null | undefined) => VIA_DEPOT_SCOPES.find((s) => s.value === value)?.key ?? "";

/** Relation and Customer scopes name a relation; Region names a geographical region (server: InvalidViaDepotScope). */
export const scopeNeedsRelation = (scope: ViaDepotScope) => scope === 1 || scope === 2;
export const scopeNeedsRegion = (scope: ViaDepotScope) => scope === 3;

export const MAX_VIA_DEPOT_POSITION = 10_000;

export type ViaDepotRuleRow = Schemas["OpenTms.PlanningExecution.ViaDepots.ViaDepotRuleRowDto"];
export type ViaDepotRuleDto = Schemas["OpenTms.PlanningExecution.ViaDepots.ViaDepotRuleDto"];
export type ViaDepotRuleCreateDto = Schemas["OpenTms.PlanningExecution.ViaDepots.ViaDepotRuleCreateDto"];
export type ViaDepotRuleUpdateDto = Schemas["OpenTms.PlanningExecution.ViaDepots.ViaDepotRuleUpdateDto"];

export const viaDepotRules = createReferenceDataHooks<ViaDepotRuleRow, ViaDepotRuleDto, ViaDepotRuleCreateDto, ViaDepotRuleUpdateDto, { includeArchived?: boolean }>(
  "viaDepotRules",
  "/api/planning/via-depot-rule",
);

/** The rule's display name: the depot and its city (a rule has no name of its own). */
export function viaDepotRuleName(rule: { depotRelationName?: string | null; depotAddressCity?: string | null }): string {
  return [rule.depotRelationName, rule.depotAddressCity].filter(Boolean).join(", ");
}
