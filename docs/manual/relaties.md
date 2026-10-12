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
- De kolom *Plaats* toont de plaats van het bezoekadres, of van het eerste adres als er geen
  bezoekadres is.
- Een relatie wordt **gearchiveerd** in plaats van verwijderd zodra er orders, ritten of facturen
  naar verwijzen. Gearchiveerde relaties verdwijnen uit de keuzelijsten en uit het automatisch
  koppelen; met *Toon gearchiveerd* blijven ze in de lijst zichtbaar. Rechtsklikken op een regel
  geeft **Archiveren** of **Dearchiveren**.
- Een relatie die als werkgever van een chauffeur is vastgelegd kan niet worden verwijderd.

## Het record

Het venster heeft vier tabbladen.

**Algemeen**

- *Code* is uniek binnen de tenant en wordt in hoofdletters opgeslagen. *Naam* is verplicht.
- *Rollen*: kies er minimaal één. De rol *Eigen bedrijf* kan maar één relatie per tenant hebben;
  een tweede wordt geweigerd.
- *Contact*: telefoon, mobiel, e-mail, website en de voorkeurstaal waarin de relatie documenten en
  berichten ontvangt (een van de talen van het platform).
- *Juridisch*: KvK-nummer en btw-nummer. Beide zijn uniek binnen de tenant; het btw-nummer begint
  met de landcode (bijvoorbeeld `NL123456789B01`) en wordt zonder spaties en punten opgeslagen.
- *Indeling*: relatiecategorieën, geografische regio's en organisatie-eenheden.

**Adressen**

Per adrestype (bezoekadres, factuuradres, …) heeft een relatie hoogstens één adres. **Nieuw**
opent een venster met adrestype, land, straat, huisnummer, adresregel 2, postcode, plaats,
coördinaten en tijdzone. De tijdzone wordt uit het land overgenomen als u hem leeg laat.

**Contactpersonen**

Naam, rol (planning, facturatie, verkoop, directie of overig), functie, afdeling en
contactgegevens. Deze gegevens zijn persoonsgegevens; zie *Privacy* hieronder.

**Bankrekeningen**

IBAN (wordt gecontroleerd), BIC, bank en rekeninghouder. Precies één rekening is de standaard; de
eerste rekening wordt dat automatisch.

## Automatisch koppelen

Bij het inlezen van orders en bij koppelingen zoekt het systeem een bestaande relatie in deze
volgorde: exacte code, exact btw-nummer, exact KvK-nummer, naam **en** postcode in het land, en
tot slot de naam alleen (hoogstens tien kandidaten, de kortste naam eerst). Gearchiveerde relaties
worden nooit gekoppeld. Het systeem maakt daarbij nooit zelf een relatie aan.

## Privacy

Contactpersonen zijn persoonsgegevens (AVG). Een verzoek om inzage kan per e-mailadres worden
beantwoord; een verzoek om verwijdering wordt afgehandeld door de contactpersoon uit de relatie
te verwijderen. Voor chauffeurs geldt een aparte procedure, zie [Chauffeurs](chauffeurs.md).
