import { apiClient } from "@/lib/api/client";
import type { Schemas } from "@/domains/master-data/shared";

/* The address lookup of design 44: the Integrations module asks a provider (PDOK for NL, OpenStreetMap
 * elsewhere) what it knows about a postal code and house number. `found` false means nothing to fill. */

export type AddressLookupResult =
  Schemas["OpenTms.Integrations.AddressLookup.AddressLookupResultDto"];

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
