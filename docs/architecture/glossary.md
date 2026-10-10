# Glossary

The user manual for opentms-next is written in Dutch and a PBI may use Dutch domain terms;
code, designs and this folder are in English. Use the English term in code and designs.

## Domain terms

| Dutch | English | Note |
|---|---|---|
| transportopdracht | transport order | "order" for short |
| orderregel | order line | |
| conceptorder | draft order | An order a boundary agent was not sure about; a person confirms it |
| rit | trip | One vehicle's run; holds legs and stops |
| traject | leg | Load at A, unload at B |
| stop | stop | |
| planbord | planboard | |
| vervoerder | carrier | |
| charter | subcontracted carrier | A carrier hired for a trip; "carrier" where the difference does not matter |
| inkoopfactuur (van een charter) | purchase invoice, carrier invoice | |
| verkoopfactuur | sales invoice | |
| chartermatch | carrier invoice matching | Matching a carrier's invoice against the trip |
| tarief, tariefregel | rate, rate line | |
| vaste prijs | fixed price | A rate line that overrides the sum beneath it |
| kostenallocatie | cost allocation | Spreading a trip's cost over its legs |
| kostprijs | cost price | |
| omzet | revenue | |
| afleverbewijs | proof of delivery (POD) | |
| CMR | CMR | The international consignment note; not translated |
| boordcomputer | on-board computer | |
| eigen rijder | owner-operator | |
| stamgegevens | master data | |
| relatie | relation | The domain map's word for a business partner (customer, carrier, address owner) |
| voertuig | vehicle | |
| chauffeur | driver | |
| opdrachtgever | customer (of the tenant) | Not the tenant itself |

## Architecture terms

| Dutch (as used in the sessions) | English | Meaning |
|---|---|---|
| logistiek besturingssysteem | logistics operating system | The layer of decision agents, policies and the exception inbox on top of the TMS core |
| LOS (logistiek besturingssysteem) | LOS, the logistics operating system app | The app for the decision agents, the exception inbox, policies and the autonomy ladder (AD-19). "Steering" / *besturing* was its working name |
| uitzonderingen-inbox | exception inbox | Where people handle what agents did not decide |
| autonomieladder | autonomy ladder | How far an agent may go by itself, set step by step |
| policy | policy | A customer-set rule that bounds an agent |
| invoerkanaal | input channel | A way in for input that becomes a command: boundary agent, EDI adapter, public API, MCP server |
| invoer-agent | boundary agent | Translates outside input into a command |
| beslis-agent | decision agent | Decides the next operational step after an event |
| feit | fact | Something recorded in the core |
| melding (van buiten) | report (from outside) | A fact the outside world reports; goes through ingest |
| event-spine | event spine | The channel that carries events: Google Pub/Sub |
| app-balk, tegel | app bar, tile | The shell's app launcher and its entries |
| rol | role | One of the four ways the backend image is started |
| tenant | tenant | A customer organisation of the SaaS; has its own database |

## Words to keep apart

- **Customer** can mean the tenant (our customer) or the tenant's customer (the shipper who
  orders transport). Say which.
- **Carrier** covers both the tenant's own fleet acting as carrier and a subcontracted
  carrier. Where the purchase side matters, say "subcontracted carrier".
- **Event** means a domain event the core publishes. A thing that arrives from outside is a
  **signal** (to interpret) or a **report** (to record), never an event.
- **LOS** is the logistics operating system app (PO, 2026-10-10). The old "LOS" in the domain
  map's Reporting row meant something else and is retired; do not read old documents' "LOS" as
  this app without checking the date.
