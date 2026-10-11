# Beheer — Logboek

> **Vertaling:** deze pagina is de bronversie (Nederlands). De Engelse vertaling
> wordt gegenereerd als onderdeel van de documentatiestap na deze draft.

---

## Wat kun je hier doen?

In de **Beheer**-app vind je twee logboeken. Ze laten zien wat er in OpenTMS is
gebeurd, zonder dat je er zelf iets in hoeft te zetten: het systeem legt alles
automatisch vast.

- Het **Functioneel logboek** toont elke wijziging aan gegevens: wie heeft wanneer
  welk record aangemaakt, gewijzigd of verwijderd, en welke velden daarbij zijn
  veranderd (oude en nieuwe waarde). Dit logboek staat standaard **aan**.
- Het **Technisch logboek** toont elk verzoek dat aan de server is gedaan: het
  tijdstip, de gebruiker, de aangeroepen URL, de duur, en of er een fout is
  opgetreden. Dit logboek is bedoeld voor beheerders en support.

De logboeken zijn **alleen-lezen**: je kunt regels openen en doorzoeken, maar niet
wijzigen of verwijderen. Alleen gebruikers met de juiste rechten zien ze.

---

## Het logboek openen

1. Open de **Beheer**-app via het wafel-icoon (negen stippen) rechts bovenin.
2. Klik in het navigatiepaneel links op **Auditlogboeken** om de groep open te
   klappen, en daarna op **Functioneel logboek** of **Technisch logboek**. Staat
   een van de logboeken al open, dan is de groep al uitgeklapt.

<!-- SCREENSHOT: de Beheer-app met het navigatiepaneel en Functioneel logboek actief -->

---

## Functioneel logboek

### De lijst lezen

Elke regel is één wijziging aan één record. De nieuwste wijziging staat bovenaan.

| Kolom | Betekenis |
|---|---|
| **Tijdstip** | Wanneer de wijziging is vastgelegd (jaar-maand-dag uur:minuut:seconde). |
| **Wijziging** | **Aangemaakt**, **Gewijzigd** of **Verwijderd**. |
| **Entiteittype** | Het soort record, bijvoorbeeld *IdentityUser* (gebruiker) of *OrganizationUnit* (organisatie-eenheid). |
| **Entiteit-id** | De technische identificatie van het record. |
| **Gebruiker** | Wie de wijziging heeft gedaan. |

Via **Kolommen** rechts in de werkbalk kun je ook de volledige naam van het
entiteittype tonen.

### Filteren en zoeken

- **Zoeken:** typ in het zoekvak links in de werkbalk. Er wordt gezocht in het
  entiteittype, de entiteit-id en de gebruikersnaam.
- **Periode:** kies hoe ver terug je wilt kijken (afgelopen uur tot alles).
  Standaard staat de periode op **Afgelopen 6 maanden**. Je laatste keuze wordt
  onthouden.
- **Wijziging:** toon alleen aangemaakte, gewijzigde of verwijderde records.
- **Kolomfilters:** open het menu van een kolom om op entiteittype, entiteit-id of
  gebruiker te filteren.

<!-- SCREENSHOT: de werkbalk van het functioneel logboek met de filters Periode en Wijziging -->

### Een wijziging bekijken

Dubbelklik op een regel (of druk op Enter) om de wijziging te openen. Je ziet dan:

- het tijdstip, het soort wijziging, het record en de gebruiker;
- onder **Gewijzigde velden** per veld de **oude waarde** en de **nieuwe waarde**.

Met **Verzoek openen** (ook via de rechtermuisknop op de regel) spring je naar het
bijbehorende verzoek in het technisch logboek. Die knop zie je alleen als je ook het
technisch logboek mag bekijken.

<!-- SCREENSHOT: het dialoogvenster van een wijziging met de gewijzigde velden -->

---

## Technisch logboek

### De lijst lezen

Elke regel is één verzoek aan de server. De nieuwste staat bovenaan.

| Kolom | Betekenis |
|---|---|
| **Tijdstip** | Wanneer het verzoek is uitgevoerd. |
| **Gebruiker** | Wie het verzoek deed (leeg bij niet-aangemelde verzoeken). |
| **Methode** | De HTTP-methode: GET (lezen), POST, PUT, DELETE (schrijven). |
| **URL** | Het aangeroepen adres. |
| **Status** | De HTTP-statuscode, bijvoorbeeld 200 (gelukt) of 500 (fout). |
| **Duur (ms)** | Hoe lang het verzoek duurde, in milliseconden. |
| **Fout** | Een vinkje als er een fout is opgetreden. |
| **Wijzigingen** | Hoeveel records dit verzoek heeft gewijzigd. |

Via **Kolommen** kun je ook Client-IP, Applicatie en Correlatie-id tonen.

### Filteren en zoeken

- **Zoeken:** zoekt in de gebruikersnaam, de URL, het client-IP en de correlatie-id.
- **Periode:** net als in het functioneel logboek; standaard **Afgelopen 6 maanden**.
- **Alleen fouten:** vink aan om alleen verzoeken met een fout te zien.
- **Kolomfilters:** op gebruiker, methode, URL en correlatie-id.

### Een verzoek bekijken

Dubbelklik op een regel om het verzoek te openen. Het dialoogvenster heeft drie
tabbladen:

- **Algemeen:** de gegevens van het verzoek en de client, en de volledige foutmelding
  als er een fout was.
- **Acties:** welke onderdelen van de applicatie zijn aangeroepen, met hun duur en
  parameters.
- **Wijzigingen:** de records die dit verzoek heeft gewijzigd, met per record de
  gewijzigde velden.

<!-- SCREENSHOT: het dialoogvenster van een verzoek met het tabblad Wijzigingen -->

---

## Rechten

| Recht | Wat het toestaat |
|---|---|
| **Logboeken bekijken** | De overkoepelende toestemming; verschijnt als groep in het rollenbeheer. |
| **Functioneel logboek bekijken** | Het functioneel logboek openen en doorzoeken. |
| **Technisch logboek bekijken** | Het technisch logboek openen en doorzoeken. |

De rol **admin** heeft deze rechten standaard. Een gebruiker zonder het recht ziet
in plaats van de lijst de melding *Je hebt geen toestemming om dit te bekijken*.

> **Let op:** de logboeken tonen alleen gegevens van je eigen organisatie (tenant).
> Beheerders van de hostomgeving zien alleen de logboeken van de host.

---

## Wat wordt vastgelegd?

Het functioneel logboek legt wijzigingen vast aan bedrijfsgegevens: gebruikers,
rollen, organisatie-eenheden, rechten, instellingen op tenantniveau en (zodra die er
zijn) orders, ritten en stamgegevens. Technische tabellen zoals aanmeldsessies,
tokens, achtergrondtaken en opgeslagen bestanden worden **niet** in het functioneel
logboek opgenomen. Wachtwoorden worden nooit vastgelegd, ook niet versleuteld.

Het technisch logboek legt schrijvende verzoeken (POST, PUT, DELETE) en verzoeken
met een fout vast. Gewone leesverzoeken (GET) worden niet gelogd.
