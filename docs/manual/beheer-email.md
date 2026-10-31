# E-mailinstellingen (host)

> Deze pagina beschrijft de weergave **E-mail** in de app Beheer. Schermafbeeldingen volgen.

Met **E-mail** stel je in hoe OpenTMS e-mail verstuurt. De instellingen gelden voor **alle tenants** tegelijk. Je ziet
de weergave alleen als host, met het recht **E-mailinstellingen**.

OpenTMS verstuurt e-mail via de maildienst Postmark. Van elke verstuurde e-mail wordt bijgehouden of die is
afgeleverd, gebouncet, als spam gemeld, geopend of aangeklikt.

Of een tenant echt e-mail verstuurt, bepaal je per tenant of editie met de functie **E-mailverzending** (zie
[Tenants en edities](beheer-tenants-en-edities.md)). Staat die uit, dan wordt de e-mail van die tenant alleen gelogd.

## De instellingen

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

<!-- SCREENSHOT: de weergave E-mail in de app Beheer -->

## Een test-e-mail versturen

1. Sla eerst je wijzigingen op: de test-e-mail gebruikt de opgeslagen instellingen.
2. Klik op **Test-e-mail versturen**.
3. Vul het adres in en klik op **Versturen**.

De test-e-mail wordt direct verstuurd. In het venster zie je het antwoord van Postmark: het bericht-id als Postmark de
e-mail heeft aangenomen, of de foutmelding als dat niet lukte (bijvoorbeeld een afzenderadres dat niet bij Postmark is
geverifieerd). Je kunt daarna nog een ander adres proberen.

## Rechten

| Recht | Technische naam | Wat het toestaat |
|---|---|---|
| **E-mailinstellingen** | `Platform.Administration.EmailSettings` | De weergave E-mail openen. |
| **E-mailinstellingen wijzigen** | `Platform.Administration.EmailSettings.Update` | De instellingen opslaan. |
| **Test-e-mail versturen** | `Platform.Administration.EmailSettings.SendTest` | Een test-e-mail versturen. |

Deze rechten bestaan alleen aan de hostkant.
