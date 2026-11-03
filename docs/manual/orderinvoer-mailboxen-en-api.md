# Orderinvoer: mailboxen, API en importprofielen

> Deze pagina beschrijft hoe orders automatisch binnenkomen: per e-mail (Gmail of Microsoft 365) of via de API.
> Schermafbeeldingen volgen.

Klanten sturen hun transportorders op allerlei manieren: een PDF in een e-mail, een Excel-lijst, een XML-bestand, of
rechtstreeks vanuit hun eigen systeem. Met **orderinvoer** worden die orders in OpenTMS ingelezen zonder dat iemand ze
overtypt. Waar het systeem niet zeker is, controleert een medewerker de order op één plek: **Transport → Orderinvoer**
(zie [Transport](transport.md#orderinvoer)).

Zo werkt het, in het kort:

1. Een e-mail komt binnen in een gekoppelde mailbox, of een klantsysteem stuurt een order via de API.
2. OpenTMS bewaart het bericht en elke bijlage ongewijzigd, zodat je altijd kunt terugzien wat er binnenkwam.
3. Elk document wordt gelezen. Kent OpenTMS de opmaak al, dan leest een **importprofiel** het document, zonder AI en
   zonder kosten. Is de opmaak nieuw en staat AI aan, dan leert een taalmodel de opmaak één keer en stelt het een
   importprofiel voor.
4. De gelezen waarden (klant, adressen, transporteenheden, data) worden opgezocht in de stamgegevens. Is alles
   gevonden, dan wordt de order aangemaakt; anders komt hij in de controlelijst.
5. Van elke stap is te zien wat er gebeurde: de **herkomst** (zie [De herkomst bekijken](#de-herkomst-bekijken)).

## Aan- en uitzetten

Orderinvoer is een functie per tenant, standaard **uit**. De host zet hem aan per tenant of editie (zie
[Tenants en edities](beheer-tenants-en-edities.md)):

| Functie | Wat het doet |
|---|---|
| **Orderinvoer** (`Integrations.OrderIntake`) | Gekoppelde mailboxen worden gelezen, de API antwoordt en ontvangen documenten worden orders. Staat hij uit, dan tonen de schermen van deze pagina dat de functie niet bij je pakket hoort. |
| **AI-mapping** (`Integrations.OrderIntake.AiMapping`) | Documenten die geen importprofiel kan lezen, gaan naar het taalmodel (Anthropic) om hun opmaak te leren. |

**Let op, privacy:** met AI-mapping aan gaat de inhoud van een onbekend document, met de namen van je ordersoorten,
stopsoorten en transporteenheden, naar Anthropic. Relatie- en adresgegevens uit je stamgegevens gaan nooit mee.
Anthropic traint niet op deze gegevens. Het aanzetten is de beslissing van de tenant.

De schermen staan in de app **Stamgegevens**, groep **Integraties → Orderinvoer** (Mailboxen, API-clients,
Importprofielen), in de app **Integraties** (Ontvangen) en in de app **Transport** (Orderinvoer). Elk navigatie-item
is ook een eigen functie, standaard aan.

## Een mailbox koppelen

OpenTMS leest één label (Gmail) of één map (Microsoft 365) van een mailbox. Het leest alleen: er wordt niets
verplaatst, gelabeld of als gelezen gemarkeerd. Alleen mail die **na** het koppelen in dat label of die map binnenkomt,
wordt ingelezen; oudere mail niet.

1. Ga naar **Stamgegevens → Integraties → Orderinvoer → Mailboxen**.
2. Klik op **Gmail koppelen** of **Microsoft 365 koppelen**.
3. Geef de mailbox een naam, bijvoorbeeld *Orders klantenservice*, en klik op **Koppelen**.
4. Je gaat naar Google of Microsoft. Log in met het account van de mailbox en sta het lezen van e-mail toe.
5. Je komt terug in OpenTMS; het venster van de mailbox opent met de melding dat je een map moet kiezen.
6. Kies bij **Label of map** het label of de map waar de orders in binnenkomen (bijvoorbeeld *Inbox/Orders*).
7. Kies eventueel een **Standaard klant** (voor mail waarvan de klant niet te herkennen is) en een **Standaard
   ordersoort**.
8. Klik op **Opslaan**. De status wordt *Actief*: vanaf nu wordt nieuwe mail in die map elke minuut opgehaald.

Lukt het koppelen niet (je annuleerde het inloggen, of de provider is nog niet ingesteld), dan meldt de lijst waarom.
De mailbox blijft staan met de status *Wacht op aanmelding*; met **Opnieuw koppelen** probeer je het nog eens.

<!-- SCREENSHOT: Stamgegevens → Mailboxen met het venster Gmail koppelen -->

### Orders automatisch bevestigen

Met **Orders automatisch bevestigen** uit (de standaard) wordt elke order uit de mailbox een **concept** dat iemand
controleert en bevestigt. Zet je het aan, dan wordt een order direct bevestigd, maar alleen als:

- een **actief** (vertrouwd) importprofiel het document las, en
- elke waarde zonder twijfel in de stamgegevens is gevonden (exact, of via een geleerde alias).

Is één van beide niet zo, dan blijft de order een concept.

### Statussen en acties

| Status | Betekenis | Wat je doet |
|---|---|---|
| **Wacht op aanmelding** | Het inloggen bij de provider is niet afgerond. | **Opnieuw koppelen**. |
| **Kies een map** | Gekoppeld, maar er is nog geen map gekozen. | Open de mailbox en kies de map. |
| **Actief** | De map wordt elke minuut gelezen. | Met **Pauzeren** stop je tijdelijk. |
| **Gepauzeerd** | Er wordt niets opgehaald. | Met **Hervatten** gaat het verder, vanaf waar het was. |
| **Opnieuw koppelen** | De provider accepteert de aanmelding niet meer (wachtwoord gewijzigd, toegang ingetrokken). Er gaat niets verloren. | **Opnieuw koppelen** met het account van de mailbox zelf. |

De kolommen **Laatst gecontroleerd** en **Laatste fout** laten zien of het ophalen loopt. **Verwijderen** stopt het
lezen; de toegang die bij Google of Microsoft is gegeven, wordt daarmee niet ingetrokken. Dat doe je in het
Google- of Microsoft-account zelf.

## API-clients

Een klant die orders vanuit zijn eigen systeem wil sturen, krijgt een **API-client**: een client-ID en een secret
waarmee zijn systeem een token opvraagt (OAuth client credentials).

1. Ga naar **Stamgegevens → Integraties → Orderinvoer → API-clients** en klik op **Nieuwe API-client**.
2. Vul een **Naam** in en kies de **Klant**: alle orders van deze client zijn orders van die klant. Kies eventueel een
   **Standaard ordersoort** en of orders **automatisch bevestigd** worden.
3. Klik op **Opslaan**. Er opent een venster met de **Client-ID**, het **Clientgeheim**, de **Token-URL**, de
   **Scope** (`OpenTms`) en een voorbeeld van de aanroep. Kopieer ze met de kopieerknoppen en geef ze veilig door
   aan de klant.

**Het secret wordt maar één keer getoond.** Kwijt? Kies **Nieuw secret maken**: het oude secret werkt dan direct niet
meer. Met **Deactiveren** weiger je tijdelijk alle aanroepen van de client; **Activeren** zet hem weer aan.
**Verwijderen** haalt de client definitief weg.

Het klantsysteem stuurt een order als JSON naar `/api/integrations/intake/orders` of een bestand naar
`/api/integrations/intake/documents`, steeds met een eigen uniek kenmerk (`externalId`). Hetzelfde kenmerk twee keer
sturen maakt geen tweede order. Een JSON-order wordt direct verwerkt en het antwoord zegt of de order is aangemaakt.

## Importprofielen en leren

Een **importprofiel** beschrijft hoe een bepaalde opmaak gelezen wordt: welke kolom, cel, regel of welk XML-element
welke waarde bevat. Profielen worden niet getypt maar **geleerd**:

1. Komt er een document binnen met een opmaak die nog geen profiel kent, en staat AI-mapping aan, dan leest het
   taalmodel het document en stelt het regels voor.
2. OpenTMS controleert de regels meteen: ze moeten precies dezelfde waarden lezen als het taalmodel. Alleen dan wordt
   het profiel opgeslagen, met de status **Voorgesteld**.
3. Elke order die met het profiel gelezen is en zonder correctie wordt bevestigd, telt mee. Na drie bevestigingen
   zonder correctie (in te stellen per tenant) wordt het profiel **Actief**: vanaf dan mag het tot automatisch
   bevestigde orders leiden.
4. Verbetert iemand bij de controle een waarde, dan telt dat als correctie. Een actief profiel gaat terug naar
   *Voorgesteld* en wordt hersteld op basis van de verbeterde waarden (met AI, alleen bij een correctie).

Een bekende opmaak kost dus geen AI meer. Een gescande PDF zonder tekstlaag kan geen profiel krijgen; die wordt elke
keer door het taalmodel gelezen.

In **Stamgegevens → Integraties → Orderinvoer → Importprofielen** zie je per profiel de afzender, het formaat, de
status, de versie en hoe vaak het bevestigd en gecorrigeerd is. Open een profiel om:

- de **naam** te wijzigen (en op te slaan);
- op het tabblad **Regels** te zien welk veld waaruit gelezen wordt (*veld ← bron → bewerkingen*) en welke
  **kenmerken** een document moet hebben om het profiel te gebruiken;
- via **Herkomst bekijken** het document te openen waarvan het profiel geleerd is;
- het profiel met **Activeren** direct te vertrouwen, of met **Buiten gebruik stellen** niet meer te gebruiken.

De regels zelf zijn alleen-lezen: een verkeerde waarde verbeter je bij de controle van de order, het profiel leert
daarvan.

## AI-instelling (host)

De host stelt de AI-leverancier en de koppelingen met Google en Microsoft in bij **Beheer → Diensten → Integraties**
(zie [Diensten](beheer-diensten.md#integraties)): de Anthropic-sleutel, het leermodel en het extractiemodel, en de
OAuth-gegevens van Google en Microsoft. Zonder Google- of Microsoft-gegevens kan een tenant die mailboxen niet
koppelen; zonder Anthropic-sleutel worden onbekende documenten niet geleerd en wachten ze in **Ontvangen**.

## De herkomst bekijken

In de app **Integraties → Ontvangen** staat elk bericht dat binnenkwam: ontvangen, kanaal, mailbox of API-client, van,
onderwerp, het aantal documenten en de status. Filter op status, kanaal en periode. Dubbelklik op een bericht om de
**herkomst** te openen:

- bovenaan het bericht: kanaal, mailbox of API-client, afzender, onderwerp en ontvangstmoment;
- een tabblad per document (de tekst van de mail en elke bijlage), met:
  - **Origineel downloaden**: het bestand precies zoals het binnenkwam;
  - het formaat, de opmaak-vingerafdruk en het **importprofiel** dat het las (klik om het profiel te openen);
  - de **stappen**: wanneer wat gebeurde, met een pictogram voor gelukt, waarschuwing of fout. Bij een AI-stap staan
    het taalmodel, het aantal tokens en de kosten; bij een stap van een medewerker zijn naam;
  - per gelezen order een **veldentabel**: elk veld, de waarde en waar die vandaan kwam (cel, regel met het stukje
    tekst, XML-element), met links naar de **orderinvoer** en de **order**.

Met **Bericht downloaden** haal je het hele oorspronkelijke bericht op (de e-mail als `.eml`). **Opnieuw verwerken**
leest het geopende document opnieuw, bijvoorbeeld nadat AI-mapping is aangezet of een profiel buiten gebruik is
gesteld; orders die al bestaan worden niet nog eens aangemaakt.

Vanuit de orderinvoer (**Herkomst bekijken**) en vanaf de orderpagina (de knop **Herkomst bekijken** naast *Import* of
*API*) kom je direct op de herkomst van het document waar de order uit kwam.

Een Customer Success Manager ziet deze herkomst door als gebruiker van de tenant mee te kijken (impersonatie).

<!-- SCREENSHOT: Integraties → Ontvangen, de herkomst van een bericht -->

## Rechten

| Recht | Technische naam | Wat het toestaat |
|---|---|---|
| **Mailboxen** | `Integrations.Mailboxes` | Mailboxen bekijken. |
| **Mailboxen koppelen en beheren** | `Integrations.Mailboxes.Manage` | Koppelen, opnieuw koppelen, wijzigen, pauzeren en verwijderen. |
| **API-clients** | `Integrations.ApiClients` | API-clients bekijken. |
| **API-clients aanmaken en beheren** | `Integrations.ApiClients.Manage` | Aanmaken, wijzigen, nieuw secret maken, (de)activeren en verwijderen. |
| **Importprofielen** | `Integrations.ImportProfiles` | Importprofielen bekijken. |
| **Importprofielen beheren** | `Integrations.ImportProfiles.Manage` | Activeren, buiten gebruik stellen, hernoemen en verwijderen. |
| **Ontvangen berichten** | `Integrations.InboundMessages` | Ontvangen berichten en hun herkomst bekijken, originelen downloaden. |
| **Opnieuw verwerken** | `Integrations.InboundMessages.Reprocess` | Een document opnieuw verwerken. |
| **Orders via de API insturen** | `Integrations.IntakeApi.Submit` | Alleen voor API-clients, nooit voor een rol. |

De rechten voor de controlelijst staan bij [Transport](transport.md#orderinvoer).

De functies van de navigatie-items: `Apps.MasterData.Integrations`, `Apps.MasterData.Integrations.Intake`,
`Apps.MasterData.Integrations.Intake.Mailboxes`, `Apps.MasterData.Integrations.Intake.ApiClients`,
`Apps.MasterData.Integrations.Intake.ImportProfiles` en `Apps.Integrations.Received`.
