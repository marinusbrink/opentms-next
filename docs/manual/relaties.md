# Relaties

De lijst **Relaties** in de app [Stamgegevens](stamgegevens.md) bevat alle partijen waarmee u
zaken doet: klanten, vervoerders, crediteuren, depots en uw eigen bedrijf. Eén record kan meerdere
rollen hebben; een klant die ook voor u rijdt is dus één relatie met de rollen *Klant* en
*Vervoerder*. De lijst staat in de groep *Relaties* van het navigatiepaneel en is per tenant
ingeschakeld met de functie *Relaties* (via de editie van de tenant). De rechten *Relaties bekijken*, *aanmaken*,
*wijzigen* en *verwijderen* bepalen wat een gebruiker mag.

## De lijst

- Zoeken werkt op code, naam, volledige naam, btw-nummer, KvK-nummer en plaats; in code en naam
  maken accenten en hoofdletters geen verschil (*Müller* vindt *muller*).
- De kolom *Plaats* toont de plaats van het bezoekadres, anders van het algemene adres, anders van
  het eerste adres.
- Een relatie wordt **gearchiveerd** in plaats van verwijderd zodra er orders, ritten of facturen
  naar verwijzen. Gearchiveerde relaties verdwijnen uit de keuzelijsten en uit het automatisch
  koppelen; met *Toon gearchiveerd* blijven ze in de lijst zichtbaar. Rechtsklikken op een regel
  geeft **Archiveren** of **Dearchiveren**.
- Een relatie die als werkgever van een chauffeur is vastgelegd kan niet worden verwijderd.

## Het record

Het venster heeft drie tabbladen.

**Algemeen**

- Bovenaan: de *Rollen* (kies er minimaal één; de rol *Eigen bedrijf* kan maar één relatie per
  tenant hebben) met de schakelaars *Gearchiveerd* en *Geblokkeerd*; zodra *Geblokkeerd* aan staat
  verschijnt de verplichte *Reden blokkade* (betalingsachterstand, kredietlimiet overschreden,
  insolventie, geschil of overig; een blokkade is nu alleen een kenmerk, wat zij tegenhoudt bepalen de
  orders en de facturatie later). Daaronder de categorieën en de geografische regio's.
- Daaronder links *Code* (uniek binnen de tenant, in hoofdletters), *Naam* en *Volledige naam* (de
  statutaire naam als die afwijkt; zoeken werkt ook op de volledige naam), met daaronder de
  *Contactgegevens*: telefoon, mobiel, e-mail en website. Rechts het blok *Adressen*: **Alle adressen
  gelijk** met het adres (land als tweeletterige code, postcode, plaats, tijdzone; straat, nr.,
  bijvoegsel; gebouw, lengte- en breedtegraad) en daaronder de tabbladen *Kaart* (een kaart van de
  ingevulde coördinaten; zonder coördinaten blijft de kaart leeg) en *Opmerkingen*.
- Met de functie *Adres opzoeken op postcode* (per tenant in te schakelen; recht *Adres opzoeken*) vult
  het systeem na de postcode en het huisnummer de plaats, de straat en de coördinaten in voor zover die
  nog leeg zijn (Nederland via PDOK, andere landen via OpenStreetMap). Wat u zelf had ingevuld blijft
  staan; vindt de bron niets, dan gebeurt er niets. Voor Nederland toont het veld *Postcode* tijdens het
  typen een lijst: vier cijfers geven de postcodes met hun straat, een volledige postcode de adressen op
  die postcode. Een keuze uit de lijst vult postcode, straat, huisnummer, bijvoegsel, plaats en
  coördinaten in.
- *Alle adressen gelijk* staat bij een nieuwe relatie aan: het ene adres geldt dan voor elk adrestype
  (bezoek, post, …). Zet het uit als de adressen per type verschillen: naast de schakelaar verschijnt
  dan een tabblad per adrestype met dezelfde velden. Een leeg tabblad is geen adres; zodra u iets
  invult zijn land, straat, postcode en plaats verplicht. Een adres per type gaat altijd vóór het
  algemene adres.

**Administratie**

- *Bedrijf*: debiteurnummer en crediteurnummer (elk uniek binnen de tenant), KvK-nummer en btw-nummer
  (beide uniek binnen de tenant; het btw-nummer begint met de landcode, bijvoorbeeld `NL123456789B01`,
  en wordt zonder spaties en punten opgeslagen), vervoerder code, afdeling (organisatie-eenheden),
  valuta (een nieuwe relatie begint met de standaardvaluta van de tenant) en taal.
- *Bank*: de bankrekeningen, tot tien per relatie: IBAN (wordt gecontroleerd), BIC, bank en
  rekeninghouder. Precies één rekening is de standaard; de eerste rekening wordt dat automatisch.
- *Externe identificatienummers*: het nummer van de relatie in een ander systeem (Id) met de naam van
  dat systeem (Bron). Een combinatie van bron en id hoort bij één relatie.
- *Transportkenmerken*: de kenmerken die de relatie vraagt of biedt, met code en omschrijving. Kies een
  kenmerk in het zoekveld boven de lijst (typ twee tekens om te zoeken op code, naam of omschrijving);
  bestaat het nog niet, dan maakt *Nieuw transportkenmerk toevoegen…* het meteen aan. Verwijderen gaat
  met het prullenbakje op de regel.

**Contactpersonen**

Naam, rol (planning, facturatie, verkoop, directie of overig), functie, afdeling en
contactgegevens. Deze gegevens zijn persoonsgegevens; zie *Privacy* hieronder.

## Automatisch koppelen

Bij het inlezen van orders en bij koppelingen zoekt het systeem een bestaande relatie in deze
volgorde: exacte code, exact btw-nummer, exact KvK-nummer, naam **en** postcode in het land, en
tot slot de naam alleen (hoogstens tien kandidaten, de kortste naam eerst). Gearchiveerde relaties
worden nooit gekoppeld. Het systeem maakt daarbij nooit zelf een relatie aan.

## Privacy

Contactpersonen zijn persoonsgegevens (AVG). Een verzoek om inzage kan per e-mailadres worden
beantwoord; een verzoek om verwijdering wordt afgehandeld door de contactpersoon uit de relatie
te verwijderen. Voor chauffeurs geldt een aparte procedure, zie [Chauffeurs](chauffeurs.md).
