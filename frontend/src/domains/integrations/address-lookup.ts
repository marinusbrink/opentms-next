import { apiClient } from "@/lib/api/client";
import type { Schemas } from "@/domains/master-data/shared";

/* The address lookup of design 44: the Integrations module asks a provider (PDOK for NL, OpenStreetMap
 * elsewhere) what it knows about a postal code and house number. `found` false means nothing to fill. */

export type AddressLookupResult =
  Schemas["OpenTms.Integrations.AddressLookup.AddressLookupResultDto"];
export type AddressSuggestion =
  Schemas["OpenTms.Integrations.AddressLookup.AddressSuggestionDto"];

export const ADDRESS_LOOKUP_FEATURE = "Integrations.AddressLookup";
export const ADDRESS_LOOKUP_PERMISSION = "Integrations.AddressLookup";

export async function lookupAddress(
  countryCode: string,
  postalCode: string,
  houseNumber: string | null,
): Promise<AddressLookupResult | null> {
  const { data, error } = await apiClient.GET(
    "/api/integrations/address-lookup",
    {
      params: {
        query: {
          countryCode,
          postalCode,
          houseNumber: houseNumber ?? undefined,
        },
      },
    },
  );
  if (error) throw error;
  return data?.found ? data : null;
}

/** The postal-code dropdown (PO 2026-10-13): a partial postal code lists postal codes, a full one the addresses on it. */
export async function fetchAddressSuggestions(
  countryCode: string,
  term: string,
): Promise<AddressSuggestion[]> {
  const { data, error } = await apiClient.GET(
    "/api/integrations/address-lookup/suggestions",
    {
      params: { query: { countryCode, term } },
    },
  );
  if (error) throw error;
  return data ?? [];
}
