# Relaties

De lijst **Relaties** in de app [Stamgegevens](stamgegevens.md) bevat alle partijen waarmee u
zaken doet: klanten, vervoerders, crediteuren, depots en uw eigen bedrijf. Eén record kan meerdere
rollen hebben; een klant die ook voor u rijdt is dus één relatie met de rollen *Klant* en
*Vervoerder*. De lijst staat in de groep *Relaties* van het navigatiepaneel en is per tenant
ingeschakeld met de functie *Relaties en chauffeurs*. De rechten *Relaties bekijken*, *aanmaken*,
*wijzigen* en *verwijderen* bepalen wat een gebruiker mag.

## De lijst

- Zoeken werkt op code, naam, btw-nummer, KvK-nummer en plaats; in code en naam maken accenten en
  hoofdletters geen verschil (*Müller* vindt *muller*).
- De kolom *Plaats* toont de plaats van het bezoekadres, anders van het algemene adres, anders van
  het eerste adres.
- Een relatie wordt **gearchiveerd** in plaats van verwijderd zodra er orders, ritten of facturen
  naar verwijzen. Gearchiveerde relaties verdwijnen uit de keuzelijsten en uit het automatisch
  koppelen; met *Toon gearchiveerd* blijven ze in de lijst zichtbaar. Rechtsklikken op een regel
  geeft **Archiveren** of **Dearchiveren**.
- Een relatie die als werkgever van een chauffeur is vastgelegd kan niet worden verwijderd.

## Het record

Het venster heeft vijf tabbladen.

**Algemeen**

- Bovenaan: categorieën, de schakelaars *Gearchiveerd* en *Geblokkeerd* met de *Reden blokkade*
  (betalingsachterstand, kredietlimiet overschreden, insolventie, geschil of overig; verplicht zodra
  de relatie geblokkeerd is; een blokkade is nu alleen een kenmerk, wat zij tegenhoudt bepalen de
  orders en de facturatie later), de geografische regio's en de *Rollen* (kies er minimaal één; de
  rol *Eigen bedrijf* kan maar één relatie per tenant hebben).
- Daaronder links *Code* (uniek binnen de tenant, in hoofdletters) en *Naam*; rechts **Alle adressen
  gelijk** met het adres (land, postcode, plaats, straat, huisnummer, adresregel 2, tijdzone,
  coördinaten) en daaronder de tabbladen *Kaart* (een kaart van de ingevulde coördinaten; zonder
  coördinaten blijft de kaart leeg) en *Opmerkingen*.
- Onderaan de *Contactgegevens*: telefoon, mobiel, e-mail en website.
- *Alle adressen gelijk* staat bij een nieuwe relatie aan: het ene adres geldt dan voor elk adrestype
  (bezoek, factuur, …). Zet het uit als de adressen per type verschillen; dan beheert u ze op het
  tabblad *Adressen*. Een adres per type gaat altijd vóór het algemene adres.

**Administratie**

KvK-nummer en btw-nummer (beide uniek binnen de tenant; het btw-nummer begint met de landcode,
bijvoorbeeld `NL123456789B01`, en wordt zonder spaties en punten opgeslagen), de voorkeurstaal
waarin de relatie documenten en berichten ontvangt, en de organisatie-eenheden.

**Financieel**

De bankrekeningen: IBAN (wordt gecontroleerd), BIC, bank en rekeninghouder. Precies één rekening is
de standaard; de eerste rekening wordt dat automatisch.

**Adressen**

Per adrestype (bezoekadres, factuuradres, …) heeft een relatie hoogstens één adres. **Nieuw**
opent een venster met adrestype, land, straat, huisnummer, adresregel 2, postcode, plaats,
coördinaten en tijdzone. De tijdzone wordt uit het land overgenomen als u hem leeg laat.

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
