# Diensten (host)

> Deze pagina beschrijft de weergave **Diensten** in de app Beheer. Schermafbeeldingen volgen.

Met **Diensten** stel je de externe diensten in waar OpenTMS van afhangt. Elke dienst heeft een eigen tabblad:
**E-mail**, **Documenten** en **Routering**. De instellingen gelden voor **alle tenants** tegelijk. Je ziet de weergave
alleen als host; een tabblad zie je alleen met het recht ervoor.

Laat je een veld leeg, dan geldt de waarde uit de configuratie van de uitrol; die staat onder het veld. Opslaan werkt
direct op de instantie die je verzoek beantwoordt en binnen een minuut op de andere instanties. Of een dienst het doet,
zie je ook op **Beheer → Onderhoud → Gezondheid** (zie [Onderhoud](beheer-onderhoud.md)).

## E-mail

OpenTMS verstuurt e-mail via de maildienst Postmark. Van elke verstuurde e-mail wordt bijgehouden of die is
afgeleverd, gebouncet, als spam gemeld, geopend of aangeklikt.

Of een tenant echt e-mail verstuurt, bepaal je per tenant of editie met de functie **E-mailverzending** (zie
[Tenants en edities](beheer-tenants-en-edities.md)). Staat die uit, dan wordt de e-mail van die tenant alleen gelogd.

### De instellingen

Bovenaan zie je of het versturen van e-mail is ingesteld. Laat je een veld leeg, dan geldt de standaard van de server;
die staat onder het veld.

| Onderdeel | Veld | Uitleg |
|---|---|---|
| Maildienst (Postmark) | **Server API-token** | Het token van de Postmark-server (Postmark → Servers → API Tokens). Het opgeslagen token wordt nooit getoond. Laat het veld leeg om het te houden; met **Opgeslagen token verwijderen** geldt weer het token uit de serverconfiguratie. |
| Afzender | **Afzenderadres** | Het adres waarvan alle e-mail komt, bijvoorbeeld `noreply@ryd-software.com`. Het domein moet in Postmark geverifieerd zijn. |
| Afzender | **Afzendernaam** | De naam die de ontvanger ziet, bijvoorbeeld `OpenTMS`. |
| Tracking | **Openen bijhouden** | Houdt bij wanneer een e-mail voor het eerst en voor het laatst is geopend. |
| Tracking | **Klikken op links bijhouden** | Houdt bij wanneer voor het eerst en voor het laatst op een link is geklikt. |
| Afleverberichten (webhook) | **Webhook-URL** | Vul deze URL in bij Postmark (Servers → je server → Webhooks), met de gebruikersnaam en het wachtwoord hieronder. Kies daar de berichten Delivery, Bounce, Spam complaint, Open en Click. |
| Afleverberichten (webhook) | **Gebruikersnaam webhook**, **Wachtwoord webhook** | Postmark stuurt deze mee met elk afleverbericht. Zonder deze gegevens worden afleverberichten geweigerd. |

Klik op **Opslaan** om de wijzigingen te bewaren. Ze gelden direct voor alle tenants.

<!-- SCREENSHOT: Beheer → Diensten, tabblad E-mail -->

### Een test-e-mail versturen

1. Sla eerst je wijzigingen op: de test-e-mail gebruikt de opgeslagen instellingen.
2. Klik op **Test-e-mail versturen**.
3. Vul het adres in en klik op **Versturen**.

De test-e-mail wordt direct verstuurd. In het venster zie je het antwoord van Postmark: het bericht-id als Postmark de
e-mail heeft aangenomen, of de foutmelding als dat niet lukte (bijvoorbeeld een afzenderadres dat niet bij Postmark is
geverifieerd). Je kunt daarna nog een ander adres proberen.

## Documenten

OpenTMS maakt elk document (CMR, facturen, rapporten) uit zijn sjabloon via de dienst pdfgeneratorapi. Eén account
geldt voor alle tenants.

| Veld | Uitleg |
|---|---|
| **Adres (URL)** | Het adres van de dienst, bijvoorbeeld `https://us1.pdfgeneratorapi.com/api/v4/`. Meestal laat je dit leeg. |
| **Sleutel**, **Geheim** | De API-sleutel en het geheim van het pdfgeneratorapi-account. Ze worden versleuteld opgeslagen en nooit getoond. Laat een veld leeg om de opgeslagen waarde te houden; met **Opgeslagen waarde verwijderen** geldt weer de waarde uit de uitrol. |

Klik op **Opslaan**, daarna op **Documenten testen**: OpenTMS vraagt pdfgeneratorapi om een antwoord en toont de
uitslag, met wat je moet doen als het niet lukt.

<!-- SCREENSHOT: Beheer → Diensten, tabblad Documenten -->

## Routering

Routering levert afstanden, rijtijden en routes over de weg. De routefunctie berekent ook CO₂; VROOM stelt de
stopvolgorde voor.

| Veld | Uitleg |
|---|---|
| **Routeringsprovider** | **OSRM** of **Routefunctie (Valhalla)**: wie afstanden en routes levert. |
| **OSRM-adres** | Het adres van de OSRM-server, bijvoorbeeld `https://osrm.voorbeeld.nl/`. |
| **Adres routefunctie**, **Sleutel routefunctie** | De routefunctie; de sleutel wordt versleuteld opgeslagen en nooit getoond. Zonder routefunctie gebruikt CO₂ een schatting in rechte lijn. |
| **VROOM-adres (stopvolgorde)** | De VROOM-server. Zonder VROOM bepaalt een ingebouwde heuristiek de stopvolgorde. |

Klik op **Opslaan**, daarna op **Routering testen**: je ziet de uitslag van de provider, de routefunctie en VROOM.

<!-- SCREENSHOT: Beheer → Diensten, tabblad Routering -->

## Rechten

| Recht | Technische naam | Wat het toestaat |
|---|---|---|
| **E-mailinstellingen** | `Platform.Administration.EmailSettings` | Het tabblad E-mail openen. |
| **E-mailinstellingen wijzigen** | `Platform.Administration.EmailSettings.Update` | De e-mailinstellingen opslaan. |
| **Test-e-mail versturen** | `Platform.Administration.EmailSettings.SendTest` | Een test-e-mail versturen. |
| **Diensten** | `Integrations.ServiceSettings` | De tabbladen Documenten en Routering openen. |
| **Dienstinstellingen wijzigen** | `Integrations.ServiceSettings.Update` | De instellingen van documenten en routering opslaan. |
| **Diensten testen** | `Integrations.ServiceSettings.Test` | Documenten en routering testen. |

Deze rechten bestaan alleen aan de hostkant. Het navigatie-item **Diensten** heeft de functie (feature)
`Apps.Administration.Services`; die staat voor tenants uit.
