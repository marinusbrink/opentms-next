# AI connector: questions with known answers

Design 75 ("Answer quality") asks for 50 to 100 questions, run on staging per release on ChatGPT, Claude and Gemini, with the
results going to the PO. This is the first list of 50.

> **The PO supplies the real customer questions.** These 50 were written to cover the tools, not taken from customers, and real
> questions are added to this list as they arrive.
>
> **Expected values come from the demo dataset at run time.** This list states the expected tool call and the type of outcome. It
> holds no numbers, because the data and the date change. Before a run, make each expected call as the demo tenant's `admin` and
> note the value in the results; the question passes when the AI tool's answer states that value with its definition.

## How to run it

- **Tenant:** `demo` on staging, with features `Apps.Integrations.AiConnector` and `Reporting.Metrics` on, data sharing accepted and
  the backfill finished, unless a question's set-up says otherwise.
- **User:** `admin`, unless a question names a set-up. `<X>`, `<T>` and similar placeholders are filled in from the demo dataset
  before the run, and relative periods resolve on the day of the run.
- **Per question:** a new conversation in each AI tool. Record the tool calls made, the answer and pass or fail.
- **Calls:** the expected call is what a good answer needs. Another call that returns the same figure also passes, and so does an
  extra `list_metrics` first, which the tool description invites. A `→` means the first call supplies an id for the next.
- **A fail, whatever else is right:** a number stated without a tool result behind it; rows of different currencies added up; calculated
  revenue called invoiced revenue; a refusal answered with a guess.

## Outcome types

| Type | Passes when |
|---|---|
| `ok` | The answer states the noted value, the period and the definition (for revenue: calculated, not invoiced), and reports what `coverage` left out when that is not zero |
| `ok, split by currency` | As `ok`, with one figure per currency, not added up |
| `ok, may warn` | As `ok`; when the answer carries `mixed_currencies`, the AI tool says the amount is not a clean total |
| `ok, truncated` | As `ok`, and the AI tool says it shows the top groups of `totalGroups` |
| `ok, compared` | Several `ok` results, with the comparison worked out by the AI tool correctly from them |
| `catalog` | The AI tool describes what `list_metrics` returned, and nothing more |
| `record` | The AI tool reports the record's fields as returned; a record that does not exist is said not to exist |
| `list` | The AI tool reports the rows and `total`, and says so when `total` is more than it shows |
| `amount` | As `record`, with amounts in their stated currency and never added across currencies |
| `not available` | The AI tool says this cannot be answered, without a number, a name or a guess. A refusal status where one is named, or no call at all after reading `list_metrics`, both pass |
| `refusal: <status>` | The tool answers that status, and the AI tool passes on its message and hint without a number. Where the AI tool avoids the refusal by asking again correctly, the entry says so |

## Order metrics

| # | Question | Expected tool call | Expected outcome |
|---|---|---|---|
| 1 | How many orders did we have last month? | `query_metrics(metrics=[order_count], dateFrom, dateTo)` | `ok` |
| 2 | Hoeveel orders hadden we dit kwartaal per klant? | `query_metrics(metrics=[order_count], groupBy=[customer], dateFrom, dateTo)` | `ok` |
| 3 | Wat was de omzet per klant per maand dit jaar? | `query_metrics(metrics=[order_revenue], groupBy=[customer, month], dateFrom, dateTo)` | `ok, split by currency` |
| 4 | What was our revenue per order type last quarter? | `query_metrics(metrics=[order_revenue], groupBy=[order_type], dateFrom, dateTo)` | `ok, split by currency` |
| 5 | Wat was de kostprijs van onze orders per organisatie-eenheid vorig jaar? | `query_metrics(metrics=[order_cost], groupBy=[organization_unit], dateFrom, dateTo)` | `ok, may warn` |
| 6 | Show the order margin per week for the last eight weeks, oldest first. | `query_metrics(metrics=[order_margin], groupBy=[week], sort=week, dateFrom, dateTo)` | `ok, may warn` |
| 7 | Hoeveel CO₂ hebben onze orders vorige week per dag gekost? | `query_metrics(metrics=[order_co2_kg], groupBy=[day], sort=day, dateFrom, dateTo)` | `ok` |
| 8 | How many orders per status this month, drafts and cancelled ones included? | `query_metrics(metrics=[order_count], groupBy=[status], filters.status=[Draft, Open, ReadyForInvoicing, Invoiced, Archived, Cancelled], dateFrom, dateTo)` | `ok` |
| 9 | Wat was de omzet van klant `<X>` vorig jaar, per maand? | `search_relations(text=<X>)` → `query_metrics(metrics=[order_revenue], groupBy=[month], filters.customerRelationId, dateFrom, dateTo)` | `ok, split by currency` |
| 10 | How many orders of order type `<T>` did we have this year? | `query_metrics(metrics=[order_count], groupBy=[order_type], dateFrom, dateTo)`, which already answers it, or → `query_metrics(..., filters.transportOrderTypeId)` with the id from that row | `ok` |
| 11 | Hoeveel omzet maakte organisatie-eenheid `<U>` vorige maand? | `query_metrics(metrics=[order_revenue], groupBy=[organization_unit], dateFrom, dateTo)`, or → `query_metrics(..., filters.organizationUnitId)` with the id from that row | `ok, split by currency` |
| 12 | Wie zijn dit jaar onze top 5 klanten op omzet? | `query_metrics(metrics=[order_revenue], groupBy=[customer], sort=order_revenue, top=5, dateFrom, dateTo)` | `ok, truncated` |
| 13 | Give me revenue, cost and margin of last month side by side. | `query_metrics(metrics=[order_revenue, order_cost, order_margin], dateFrom, dateTo)` | `ok, may warn` |
| 14 | Welke klant daalde dit kwartaal het meest in omzet ten opzichte van vorig kwartaal? | `query_metrics(metrics=[order_revenue], groupBy=[customer], ...)` twice, once per quarter | `ok, compared` |

## Trip metrics

| # | Question | Expected tool call | Expected outcome |
|---|---|---|---|
| 15 | How many trips did each carrier run last week? | `query_metrics(metrics=[trip_count], groupBy=[carrier], dateFrom, dateTo)` | `ok` |
| 16 | Wat waren de ritkosten per voertuig vorige maand? | `query_metrics(metrics=[trip_cost], groupBy=[vehicle], dateFrom, dateTo)` | `ok, may warn`; the AI tool mentions that a trip counts under the vehicle in first position |
| 17 | How much revenue was allocated per trip type this quarter? | `query_metrics(metrics=[trip_revenue], groupBy=[trip_type], dateFrom, dateTo)` | `ok, may warn` |
| 18 | Wat is de marge op eigen vloot tegenover uitbesteed dit jaar? | `query_metrics(metrics=[trip_margin], groupBy=[execution], dateFrom, dateTo)` | `ok, may warn`; `coverage` names the trips left without a value |
| 19 | How much CO₂ did the trips of each organisation unit emit last year? | `query_metrics(metrics=[trip_co2_kg], groupBy=[organization_unit], dateFrom, dateTo)` | `ok` |
| 20 | Wat was de gemiddelde beladingsgraad per maand dit jaar? | `query_metrics(metrics=[average_load_factor], groupBy=[month], sort=month, dateFrom, dateTo)` | `ok`; stated as a plain average, not weighted by distance |
| 21 | Compare planned and actual kilometres per week for the last four weeks. | `query_metrics(metrics=[planned_distance_km, actual_distance_km], groupBy=[week], sort=week, dateFrom, dateTo)` | `ok` |
| 22 | Hoeveel kilometer reden uitbestede ritten vorige maand per vervoerder? | `query_metrics(metrics=[actual_distance_km], groupBy=[carrier], filters.execution=Subcontracted, dateFrom, dateTo)` | `ok` |
| 23 | What percentage of stops was on time per day last week? | `query_metrics(metrics=[on_time_stop_percentage], groupBy=[day], sort=day, dateFrom, dateTo)` | `ok`; states the 15 minutes, and that it is measured against the current plan |
| 24 | Welke vervoerder was vorig kwartaal het minst op tijd? | `query_metrics(metrics=[on_time_stop_percentage], groupBy=[carrier], top=100, dateFrom, dateTo)` | `ok, compared` |
| 25 | How many trips per status this month, cancelled ones included? | `query_metrics(metrics=[trip_count], groupBy=[status], filters.status=[Planned, InProgress, Completed, Cancelled], dateFrom, dateTo)` | `ok` |
| 26 | Hoeveel ritten en kilometers deed vervoerder `<C>` per maand dit jaar? | `search_relations(text=<C>)` → `query_metrics(metrics=[trip_count, actual_distance_km], groupBy=[month], filters.carrierRelationId, dateFrom, dateTo)` | `ok` |
| 27 | What did the vehicle with licence plate `<P>` cost in trips last month? | `search_vehicles(text=<P>)` → `query_metrics(metrics=[trip_cost], filters.vehicleId, dateFrom, dateTo)` | `ok, may warn` |
| 28 | Hoeveel ritten van ritsoort `<R>` hadden we per organisatie-eenheid vorige maand? | `query_metrics(metrics=[trip_count], groupBy=[trip_type, organization_unit], dateFrom, dateTo)`, or → with `filters.transportTripTypeId` from that row | `ok` |

## The catalog and refusals

| # | Question | Set-up | Expected tool call | Expected outcome |
|---|---|---|---|---|
| 29 | Which numbers can you give me about our transport? | | `list_metrics()` | `catalog` |
| 30 | Wat is de omzet per regio? | | `query_metrics(groupBy=[region], ...)`, or none after `list_metrics` | `not available`; `refusal: unsupported_combination` if called |
| 31 | What is our customer satisfaction score this year? | | `query_metrics(metrics=[customer_satisfaction], ...)`, or none after `list_metrics` | `not available`; `refusal: unknown_metric` if called |
| 32 | Hoeveel omzet maken we per product? | | None after `list_metrics` | `not available` |
| 33 | Which driver drove the most kilometres last month, and what is their phone number? | | None after `list_metrics` | `not available`; no driver name, no phone number |
| 34 | Geef in één tabel de orderomzet en de ritkosten per maand. | | `query_metrics(metrics=[order_revenue, trip_cost], ...)`, or one call per kind | `refusal: unsupported_combination`, or `ok, compared` from two correct calls |
| 35 | Omzet per klant, per ordersoort, per organisatie-eenheid, per maand graag. | | `query_metrics(metrics=[order_revenue], groupBy=[customer, order_type, organization_unit, month], ...)` | `refusal: unsupported_combination` (at most two breakdowns); the AI tool may then offer a narrower question |
| 36 | Give me trip count, cost, revenue, margin, CO₂ and load factor of last month in one call. | | `query_metrics` with six trip metrics, or two calls | `refusal: unsupported_combination` (at most five metrics), or `ok, compared` from two correct calls |
| 37 | Hoeveel ritten deden we vorige maand voor klant `<X>`? | | `query_metrics(metrics=[trip_count], filters.customerRelationId, ...)`, or `search_trips(text=<X>, dateFrom, dateTo)` | `refusal: unsupported_combination`, or `list` with its `total` and the note that this is a text search |
| 38 | Wat was de omzet van de afgelopen zes jaar? | | `query_metrics(metrics=[order_revenue], dateFrom, dateTo)` over six years | `refusal: invalid_period`, or `ok, compared` from several calls of at most five years |
| 39 | What was revenue from 1 March until 1 February this year? | | `query_metrics(metrics=[order_revenue], dateFrom=03-01, dateTo=02-01)`, or none and a question back | `refusal: invalid_period`, or the AI tool asks which period is meant |
| 40 | Wat was de marge vorige maand? | A user with `Orders.TransportOrders`, without `Financial.RateLines` | `query_metrics(metrics=[order_margin], ...)`, or none, because `list_metrics` does not show it | `refusal: not_allowed`, or `not available`; no amount |
| 41 | Hoeveel orders hadden we vorige maand? | A tenant whose backfill has not finished | `query_metrics(metrics=[order_count], dateFrom, dateTo)` | `refusal: not_ready`; passes on the progress in the hint |
| 42 | Give me the number of trips per vehicle per day for the last five years. | Record it when it happens: whether this passes 2 seconds depends on the data | `query_metrics(metrics=[trip_count], groupBy=[vehicle, day], dateFrom, dateTo)` | `refusal: timeout` with its hint and no partial figures, or `ok, truncated` |
| 43 | What will our revenue be next month, and how do we compare with other transport companies? | | None after `list_metrics` | `not available`; no forecast presented as a figure, no other organisation's data |

## Lookups

| # | Question | Expected tool call | Expected outcome |
|---|---|---|---|
| 44 | Welke orders van klant `<X>` staan deze week open? | `search_relations(text=<X>)` → `search_orders(status=Open, customerRelationId, dateFrom, dateTo)` | `list` |
| 45 | Waar is order `<N>` en wat zit erin? | `search_orders(text=<N>)` → `get_order(id)` | `record` |
| 46 | Which trips of carrier `<C>` are planned for tomorrow? | `search_relations(text=<C>)` → `search_trips(status=Planned, carrierRelationId, dateFrom, dateTo)` | `list` |
| 47 | Geef de stops en de werkelijke tijden van rit `<N>`, en de legs ervan. | `search_trips(text=<N>)` → `get_trip(id)`, `get_legs(tripId)` | `record`; no driver names and no remarks |
| 48 | Which vehicle has licence plate `<P>`, and what are its fuel type and euro class? | `search_vehicles(text=<P>)` | `list` |
| 49 | Wat zijn de tarieven van order `<N>`? | `search_orders(text=<N>)` → `get_order_rates(orderId)` | `amount`; says so when `state` is not up to date |
| 50 | Wat was de omzet van klant `<X>` in het eerste kwartaal? | `search_relations(text=<X>)` → `get_customer_revenue(customerRelationId, dateFrom, dateTo)`, or `query_metrics(metrics=[order_revenue], filters.customerRelationId, ...)`, which must give the same figure | `amount`; calculated, not invoiced, and it reports `ordersWithoutAmount` and the excluded drafts and cancelled orders |
