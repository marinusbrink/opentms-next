# Design: sign-in by e-mail with tenant detection, no self-registration

**PBI:** none; built on the PO's direct instruction in a working session on 2026-10-27 ("ABP.IO supports detecting
the tenant from the user name or e-mail address; when it is known in several tenants, the user selects the tenant via
an auto-complete without a dropdown. New users can never register through the sign-in screen: remove that in frontend
and backend", followed the same day by "no longer allow user name, but e-mail address only", the three steps below and
the compact language selector). The PO's wording plus the answers below are the design; this note records them so
gate 2 can review against it.
**Status:** in review
**Date:** 2026-10-27

> **Platform change — risk class critical** (`/CLAUDE.md` rule 4): authentication and tenant isolation. Design and
> implementation travel in one PR because the PO asked for the build directly; gate 1 and gate 2 are the same review.
>
> **DEVIATION(constitution-1): the sign-in page looks an e-mail address up across all tenants and the host before any
> tenant is known, through a host-owned lookup of hashes and ids, approved in this document (PO 2026-10-27, Q1).** The
> lookup holds no readable personal data, only a tenant where the password is valid is ever offered, and the
> credentials are always checked in the tenant's own database. The call sites carry the reason in their comments.
>
> **DEVIATION(constitution-4): no feature flag, approved in this document (PO 2026-10-27, Q4).** The sign-in page runs
> before any tenant is known, so a per-tenant flag has nothing to be read from.
>
> **DEVIATION(constitution-2): `POST /api/account/register` is removed from the API, approved in this document (PO
> 2026-10-27: "remove that in frontend and backend").** No client used it (the SPA never called it and nothing is
> deployed), so no consumer breaks.

## What ABP offers

ABP's tenant detection at sign-in is a feature of the commercial Account Pro module. The open-source account module
this repository uses (`Volo.Abp.Account.Web` 10.6) signs in within the tenant chosen up front with the tenant switch
(cookie or `__tenant` query parameter) and looks the user up there only. So the detection is built here, on ABP's own
sign-in machinery (`SignInManager`, its lockout and pre-sign-in rules, its security log).

## PO decisions (2026-10-27)

| # | Question | Decision |
|---|---|---|
| Q1 | How are the tenants of an address found, with a database per tenant? | A **lookup in the host database**: a hash of the normalized e-mail address to the user's tenant, kept current from the user changes and rebuilt by the DbMigrator. Not a scan of every tenant database per attempt. |
| Q2 | How does the user pick the tenant when the credentials are valid in several? | **Auto-complete without a dropdown, with a "contains" search** ("opem" finds "filogic-opemtms"). No list of names is ever shown, which the PO finds safer. |
| Q3 | Does the host take part? | **Yes**: host users are found too; the host is a choice named "Host". |
| Q4 | Feature flag? | **None** (see the deviation above). |
| Q5 | User name or e-mail address? | **E-mail address only.** |
| Q6 | Steps | 1: e-mail address (with "Remember me"); an address no active tenant or the host knows goes no further. 2: password. 3: the organisation, only when the credentials are valid in several places. |
| Q7 | What does "Remember me" remember? | **The address and the session**: the next visit starts at step 2 with the address filled in, and the session survives closing the browser (ABP's own "Remember me"). |
| Q8 | Step 1 tells that an address is unknown, before the password. That lets anyone test whether an address has an account. | **Accepted**: step 1 refuses an unknown address with a clear message. |
| Q9 | Language selector | The card above the page that held it (and ABP's tenant switch) goes; the languages take less space. |
| Q10 | The organisation chosen last time | **Remembered and filled in** at step 3 on the next sign-in (PO 2026-10-27, "remember and auto-fill last logged tenant"). |

## Sign-in flow

The SPA sends an unauthenticated user straight to the auth server's sign-in page (no welcome step). The page always
runs in the host: `SignInPageTenantResolveContributor` resolves the host for `/Account/Login` right after the
signed-in-user resolver, before ABP's cookie and query-string resolvers, so a `__tenant` the SPA still sends is
ignored and ABP's tenant switch is not shown.

1. **E-mail address.** `SignInTenantResolver.IsKnownEmailAsync` hashes the normalized address (`ILookupNormalizer`,
   as Identity's `NormalizedEmail`) and asks the lookup; an address of a user in an active tenant or the host is
   known. No tenant database is asked. An invalid or unknown address stays at step 1 with a message (Q8). A known one
   is sealed (`SignInEmailProtector`: data protection with the shared keys) into an HttpOnly cookie on `/Account`,
   `SameSite=Lax` so it survives the SPA's redirect: for the browser session, or for 30 days with "Remember me".
   The page then redirects to itself and shows step 2. A remembered address that is no longer known is forgotten and
   step 1 shows. "Another e-mail address" forgets the address and goes back to step 1.
2. **Password.** For each candidate in an active tenant (or the host), `IdentitySignInCredentialChecker` runs ABP's
   `CheckPasswordSignInAsync` inside that tenant, with that tenant's identity settings, in a unit of work of its own,
   **without counting a failure**. A candidate whose user is gone or has another address now (a stale row) is skipped.
   A tenant whose database throws is left out of this sign-in and logged as an error; it never fails the sign-in for
   the others.
   - Valid in **one** place: signed in there (`SignInManager.SignInAsync` with "Remember me" as `isPersistent`, the
     security log in that tenant, the dynamic claims cache cleared), on to the authorize endpoint.
   - Valid in **several**: step 3.
   - Valid **nowhere**: only now each candidate where the password was wrong counts a failed attempt
     (`RecordFailureAsync`), so signing in to one tenant never locks the same address out of another. The page tells,
     in this order: locked out (also when this attempt locked the account), not allowed (only to a user who knows the
     password, as ABP), "signing in is not possible right now" when a tenant could not be asked, "the password is
     incorrect". The failed attempt is written to the host's security log, as ABP does without a tenant.
3. **Organisation.** The places where the password was valid are sealed (`SignInTenantSelectionProtector`:
   time-limited to five minutes, purpose-bound) into an HttpOnly, `SameSite=Strict` cookie on `/Account`; the password
   is not kept. The page asks for part of the organisation's name. While the user types, `?handler=FindTenant` answers
   only within the sealed places (`SignInTenantMatch`): the full name of one place always wins (so "acme" next to
   "acme-transport", the host next to a tenant with "host" in its name, and a name shorter than three characters can
   all be chosen); otherwise, after three characters, exactly one place containing the text gives its name ("Found:
   filogic-opemtms", Tab or → takes it), more than one gives "type more" without names, none gives "none of your
   organisations". Submitting signs in to the one match; the server decides, not the browser.
   The organisation chosen here is remembered (`SignInLastTenantProtector`: the address's lookup hash and the tenant
   id, sealed into an HttpOnly, `SameSite=Strict` cookie on `/Account` for 90 days). The next time step 3 shows for
   the same address, its name is filled in, selected, and confirmed at once ("Found: …"), so Enter signs in; typing
   replaces it. It is filled in only for that address and only when that organisation is one of the places the
   password is valid in now, so one user's organisation never shows to another on a shared browser.

Before signing in (steps 2 and 3) the user is read again in the chosen tenant: one deactivated or locked out since the
password was checked is refused.

## Where the browser lands

PO 2026-10-27: "as soon as I sign in I land in the wrong environment, not in opentms-next". The sign-in itself returned
correctly to the authorize request and from there to the SPA (or to Swagger UI's `oauth2-redirect.html`); the browser
went wrong when the sign-in page was opened without a request to return to, by a bookmark or after
`/Account/Logout`, which redirects to `/Account/Login` bare. ABP then sends the signed-in user to the auth server's
root, which listed the OpenID Connect applications. Now nobody stays on the auth server:

- the sign-in page opened without a local return URL sends the browser to the SPA, which starts a proper sign-in (with
  its return URL, and so the app's look); a sign-in that still ends without one goes to the SPA too;
- the backend's root (`Pages/Index`) no longer lists the OpenID Connect applications: it opens Swagger UI (PO
  2026-10-28: "the backend should not have a sign-in screen but go directly to Swagger, which has its own sign-in").
  Swagger UI's Authorize button opens the API sign-in page. The root answers 200 with an immediate refresh to
  `/swagger`, not a 302: the dev tooling that waits for the root to be ready accepts only a page and left the backend
  "starting" on any redirect, which looked like a slow start.

The SPA's address is the first of `App:RedirectAllowedUrls` (`SpaUrl.Root`, shared with "Log in as"), never a URL
from the request.

## Sign-in page layout

PO 2026-10-27: "it doesn't look right yet", with app.opentms.nl, tms.filogic.nl, app.qargo.com and Microsoft's sign-in
as inspiration, "make a nice logo for OpenTMS NEXT and a nice stock photo background". All four put a logistics photo
behind one plain card; this page keeps that and moves the card to the right, with the brand on the photo.

- **Layout.** `Themes/LeptonXLite/Layouts/Account.cshtml` in the host replaces LeptonX Lite's account layout (theme
  5.6.0; the head and the scripts are the theme's, keep them in step when it is upgraded). The photo fills the screen;
  the logo and the tagline "Logistics Operating System" (PO 2026-10-27, both languages) sit bottom left on its dark
  tree line; one slightly see-through white card (blurring the photo behind it) on the right holds the
  languages ("NL · EN", one line), the alerts, ABP's tenant switch on the account pages that still use it (forgotten
  password) and the page. On a phone the photo is a band above the card. The card fades in once on load (not with
  reduced motion). Styles: `wwwroot/sign-in.css`.
- **Look.** The page runs before any tenant is known, so it carries the product's own look rather than a tenant
  colour: the SPA's font (Geist, self-hosted under `wwwroot/fonts/geist` with its OFL licence, no Google Fonts request)
  and colours (`--brand` #0F6CBD for the logo, links and focus; the SPA's near-black primary button, since the brand
  colour is never a button fill, UI-TOK-002). UI-TOK-001/003 bind the SPA's feature code; this Razor page keeps its
  few literal colours in one stylesheet.
- **Logo.** OpenTMS NEXT (PO 2026-10-27, concept "the chain" out of three, in FiLogic orange #FBA91A): an "O" of three
  legs (order, trip, invoice) with the stops between them, dark on an orange tile; then "OpenTMS" bold and "NEXT" in
  orange. Inline in the page (`Pages/Shared/_OpenTmsNextLogo.cshtml`, taking the page's font) and as files under
  `wwwroot/images/logo/` (`opentms-next.svg`, `opentms-next-white.svg`, `opentms-next-mark.svg`). The mark is the
  favicon of the account pages and Swagger UI (`wwwroot/favicon.svg`) and of the SPA (`frontend/public/favicon.svg`,
  replacing Vite's). It is the product's mark, not a tenant colour (UI-TOK-003 is about the tenant brand).
- **App or API.** PO 2026-10-27/28: "there must be a clear distinction between the sign-in screens of the frontend and
  Swagger", then "the backend sign-in must be a completely different page". The page reads the OpenID Connect request
  in its return URL; a request from the Swagger client (`AuthServer:SwaggerClientId`, `SignInClient.IsApi`) gets a
  page of its own: no photo but a dark dotted field, on the left the logo, "Sign in to the API", what the token is for,
  and the API's base URL and specification (`/swagger/v1/swagger.json`); on the right a dark card with
  FiLogic-orange accents. The SPA's sign-in keeps the photo, the light card and "Logistics Operating System". The steps
  and the rules are the same for both.
- **Photo.** `wwwroot/images/sign-in/background.jpg`, "Highway traffic under warm orange sky" by Leo Tang
  (unsplash.com/photos/witlTfO65b8), 2400 × 1600, under the Unsplash License (free commercial use, no attribution
  required; PO 2026-10-27: no credit on the page). Chosen by the PO from three candidates.

## Lookup

`platform."UserSignInLookups"` (`UserSignInLookup`): `Id` = the user's id, `UserTenantId` (null for the host),
`EmailHash` (hex SHA-256 of the normalized address with a fixed prefix). Indexed on the hash and on the tenant. A user
without an e-mail address has no row (and cannot sign in). Host-owned: written and read in the host database only; the
table exists in every database because the schema is shared, and tenant databases keep it empty (as
`DataProtectionKeys`). `HostUserSignInLookupStore` switches to the host and runs each call in a unit of work of its own.

Kept current by `UserSignInLookupSynchronizer` (Platform.Domain, so the DbMigrator and the provisioning job run it too;
Platform.Domain now references `Volo.Abp.Identity.Domain` for it):

- every created, changed or deleted `IdentityUser` rewrites its row; nothing is written when the row is current, so a
  sign-in (which updates the user) costs one indexed read;
- a deleted tenant loses its rows;
- the user and the lookup live in different databases and never share a transaction, so the write runs after the
  user's unit of work has committed, with the store taken from that unit of work's services (the handler's own scope
  is gone by then). Two concurrent writes for one user retry once. A failed write is logged as an error: until the
  next DbMigrator run the user cannot be found. A crash between commit and write, or two changes of one user whose
  writes land out of order, leave the same gap or a stale hint (harmless: the credentials are checked in the tenant).

`UserSignInLookupDataSeedContributor` rebuilds the rows of the tenant (or host) being seeded, reading the users in pages
of 1,000, so every DbMigrator run repairs the lookup and fills it for users that existed before it. Idempotent: a
tenant's rows are replaced.

Not the transactional outbox: ABP's identity changes publish no OpenTms event, and the lookup is a hint that the seed
repairs, not a fact.

## No self-registration

Users are created by an administrator (Administration app) or by tenant provisioning only.

- ABP's register page (`/Account/Register`) is replaced by one that answers 404, external-login sign-ups included.
- `POST /api/account/register` is removed from the API (`RemoveSelfRegistrationEndpointConvention`); the rest of ABP's
  account controller stays. `RegisterDto` and `IdentityUserDto` left the OpenAPI spec with it.
- The setting `Abp.Account.IsSelfRegistrationEnabled` defaults to false and is not sent to clients, so ABP's account
  service refuses a registration that would reach it another way (defined in OpenTms.Application: ABP defines the
  setting in its account application module, which loads before it).
- The SPA had no registration of its own; nothing changes there.

## Domain impact

Platform only (horizontal: identity, tenancy). AD-03 (a database per tenant, 100–1,000 tenants) is why the lookup is a
host table and not a scan of every tenant database. AD-16 (the outbox) is deliberately not used: see "Lookup". None of
the three flows of `docs/architecture/02-flows.md` is extended: sign-in precedes them. No PROPOSED or OPEN item is
relied on. No status in `decisions.md` changes.

## Privacy (constitution rule 5)

The lookup holds hashes and ids, no readable e-mail address. The hashes are unkeyed SHA-256, so a known address can be
tested against them by someone with access to the host database; that person can read the tenant databases anyway.
Step 1 tells anyone whether an address has an account (Q8, accepted); the response time of step 2 grows with the
number of places an address has. Log lines carry user and tenant ids only. The lookup is derived data: erasing a user
(art. 17) deletes the user and with it the row; it is not part of an art. 15 export.

## Cost & SLO (constitution rule 6)

Step 1: one indexed read in the host database plus the tenant store. Step 2: one password check per candidate in the
candidate's own database (usually one), plus a lockout write per candidate only when the password was valid nowhere.
Per user change: one indexed read, rarely a write. Storage: about 150 bytes per user in the host database. No new
service, job or instance.

## Tests

- `test/OpenTms.HttpApi.Host.Tests/SignIn/SignInHostTests.cs` (unit, ports faked): the resolver (known addresses in
  active places only and without asking a tenant database; single, several, host first; a tenant with a wrong password
  never offered and its lockout untouched when another place succeeds; failures counted everywhere when valid nowhere;
  the locking failure says so; inactive and deleted tenants skipped before the check; a failing tenant left out and
  "unavailable" when nothing succeeds; stale rows; locked out / not allowed precedence; lookup asked by hash only); the
  match (contains, case, position, three characters, ambiguity without names, the full name winning, only the sealed
  places searched); the sealed address and selection (round trip encrypted, forged, tampered, expired, other purpose);
  the page pinned to the host while a `__tenant` is present; the register page 404 and the endpoint removal; the
  remembered organisation (filled in for its own address and a current choice only; sealed, forged); the API sign-in
  recognised for the configured Swagger client only.
- `test/OpenTms.EntityFrameworkCore.Tests/EntityFrameworkCore/Platform/SignIn/UserSignInLookupTests.cs` (SQLite): a
  created user's row holds the hash only; rebuilding one tenant leaves other tenants and the host alone (negative
  tenant-isolation test); the same address in two tenants gives two candidates; a deleted tenant loses its rows;
  self-registration is off and hidden from clients.
- `test/OpenTms.EntityFrameworkCore.Tests/PostgreSql/Platform/UserSignInLookupPostgreSqlTests.cs` (PostgreSQL, tenant in
  its own database): a user created in the tenant database lands in the host lookup and never in the tenant's; a
  changed and a deleted address; the seed rebuilds one tenant only; the migration seeds the host and every tenant and
  is the newest, with nothing pending.
- `frontend/e2e/support/auth.ts`: the E2E sign-in follows the three steps.

## Open

- **No integration test drives the page itself** (handlers, cookies, redirect to the authorize endpoint) or the real
  credential checker against Identity; the browser walk-through covered them by hand. A Playwright spec for the three
  steps is the follow-up.
- **ForgotPassword** still uses ABP's tenant switch: a tenant user resetting a password picks the tenant there first.
- **The SPA's "Switch organisation"** (user menu) stores a tenant and signs out; the sign-in page now ignores that
  tenant, so the menu item amounts to signing out. Removing it, with the stored tenant and the `__tenant` header, is
  for the PO to decide.
- **`POST /api/account/login`** (ABP's cookie-login API, unused by the SPA) still signs in by user name within the
  tenant of the request.
