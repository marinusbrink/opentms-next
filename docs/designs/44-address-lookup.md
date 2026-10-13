# 44 · Address lookup by postal code (Integrations)

**Status:** built on the PO's instruction of 2026-10-13 ("implement"); the PO reviews the provider choice below.

## Story

A planner enters a relation's address. After the country, the postal code and the house number the city,
the street and the coordinates fill themselves where they are still empty, so an address takes three
fields instead of eight and the map shows the place. Nothing is overwritten: what the user typed stays.

## Affected area

Integrations module (new application service, no entity, no schema), the relation editor's address
fields (Master Data app). Domain impact: the Integrations module is the anti-corruption layer for
external sources (AD-06 register); Master Data stays unaware of providers and only consumes a DTO.
This extends the *intake* flow of `02-flows.md` (an address entered by hand); no event, nothing stored.

## API contract

`GET /api/integrations/address-lookup?countryCode=NL&postalCode=1012AB&houseNumber=1` →
`AddressLookupResultDto { found, street?, houseNumber?, city?, latitude?, longitude?, provider? }`.
Permission `Integrations.AddressLookup` (both sides); feature `Integrations.AddressLookup`, default off
(constitution rule 4). Input shape is validated (two-letter country, postal code ≤ 16, house number ≤ 16);
a provider that is down or has no answer gives `found = false`, never an error for the user.

## Providers

| Country | Provider | Why |
|---|---|---|
| NL | PDOK Locatieserver (BAG) | authoritative, free, no key; postal code + house number → street, city, coordinates |
| others | OpenStreetMap Nominatim | free, no key; postal code → place and centroid (no street); usage policy: real User-Agent, ≤ 1 request/s, honoured with a process-wide throttle; a self-hosted instance or a commercial provider (Geoapify, LocationIQ, HERE, Google) is a configuration change behind the same interface |

`IAddressLookupProvider` is tried in registration order, the first that supports the country answers.
Settings in section `AddressLookup` (`PdokBaseUrl`, `NominatimEnabled`, `NominatimBaseUrl`, `UserAgent`,
`TimeoutSeconds`, `CacheDays`). Google is not used: its terms require showing results on a Google map,
which the editor's OpenStreetMap map does not.

## Caching and privacy

Answers are public data that rarely change: cached for `CacheDays` (30) through ABP's distributed cache
with `[IgnoreMultiTenancy]` on the cache item, so every tenant shares one entry per key. That is not
tenant data (rule 1 is untouched). The log records the provider and the country of a failure, never the
postal code or house number (rule 5). Outbound traffic: two hosts, configurable; no API key in this run.

## UI

`AddressFields` asks the lookup when the postal code or the house number loses focus while the country
and the postal code are known, and fills only the empty fields among city, street, latitude and longitude
(house number too when the user typed none and the provider confirms one). A failed lookup is silent.
Only while the feature is on for the tenant and the user holds the permission.

## Tests

Application: input validation, provider order, cache hit, provider failure → not found (fake
`HttpMessageHandler`), PDOK and Nominatim parsing from recorded responses, feature off / permission
missing refused. Frontend: the fill-only-empty rule and the blur trigger.

## Open

- Belgium and Germany have national sources (bpost, Deutsche Post Datafactory) that are paid; a PO choice.
- A geocoder for the map of an address without coordinates arrives with Planning (design 43).
