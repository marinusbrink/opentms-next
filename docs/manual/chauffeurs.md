# Chauffeurs

De lijst **Chauffeurs** in de app [Stamgegevens](stamgegevens.md) bevat de chauffeurs die ritten
rijden: eigen chauffeurs en chauffeurs van charters. De lijst staat in de groep *Wagenpark* van het
navigatiepaneel en is per tenant ingeschakeld met de functie *Relaties en chauffeurs*. De rechten
*Chauffeurs bekijken*, *aanmaken*, *wijzigen*, *verwijderen* en *anonimiseren* bepalen wat een
gebruiker mag.

## De lijst

- Zoeken werkt op code, naam en e-mail; accenten en hoofdletters maken geen verschil.
- De kolom *Verloopt* telt de kwalificaties die binnen 90 dagen verlopen (of al verlopen zijn).
- Een chauffeur wordt **gearchiveerd** in plaats van verwijderd zodra er ritten naar verwijzen.
  Gearchiveerde chauffeurs verdwijnen uit de keuzelijsten; met *Toon gearchiveerd* blijven ze in de
  lijst zichtbaar. Rechtsklikken op een regel geeft **Archiveren** of **Dearchiveren**.

## Het record

Het venster heeft twee tabbladen.

**Algemeen**

- *Persoon*: code (uniek binnen de tenant), voornaam, tussenvoegsel, achternaam en initialen.
- *Contact*: telefoon, mobiel, e-mail en voorkeurstaal.
- *Dienstverband*: de **werkgever** is een relatie met de rol *Vervoerder* of *Eigen bedrijf*;
  typ in het veld om te zoeken. Het **gebruikersaccount** koppelt de chauffeur aan een gebruiker van
  deze tenant (bijvoorbeeld voor de chauffeurs-app); een account kan aan één chauffeur zijn
  gekoppeld.

**Kwalificaties**

Per regel een soort (rijbewijs B, C of CE, code 95, ADR-certificaat, tachograafkaart,
heftruckcertificaat of overig), het nummer, *geldig vanaf*, *geldig tot* en een toelichting. De
datums typt u in de notatie van uw taal (bijvoorbeeld `31-12-2026`) of kiest u in de kalender.

## Anonimiseren (AVG)

Een chauffeur is een persoon. Bij een verzoek om verwijdering (recht op vergetelheid) gebruikt u
**Anonimiseren** in het record (apart recht). Naam, contactgegevens, kwalificaties en de koppeling
met het gebruikersaccount worden daarbij onomkeerbaar vervangen; de code en het record zelf blijven
bestaan zodat ritten en afrekeningen uit het verleden kloppen. Een geanonimiseerde chauffeur is
alleen-lezen.
