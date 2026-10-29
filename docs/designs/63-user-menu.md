# Design: the user menu — account switch, Customer Success Manager, my settings

**PBI:** none; built on the PO's direct instruction in a working session on 2026-10-29 ("refactor the user menu at
the right top of the main application bar": profile picture, a list of the tenants the user's e-mail address has an
account in instead of "Switch organisation", a static host role "Customer Success Manager" that sees every tenant and
logs in through the default impersonation, and the menu items Change password, Change profile picture and My
settings). The PO's wording plus the answers below are the design; this note records them so gate 2 can review
against it.
**Status:** in review
**Date:** 2026-10-29

> **Platform change — risk class critical** (`/CLAUDE.md` rule 4): authentication, sessions and cross-tenant access.
> Design and implementation travel in one PR because the PO asked for the build directly; gate 1 and gate 2 are the
> same review.
>
> **DEVIATION(constitution-1): the user menu reads the host's sign-in lookup (design 62) for the current user's e-mail
> address and keeps remembered switches in a host-owned table, approved in this document (PO 2026-10-29, Q1).** Both
> hold ids and hashes only; every account is read in its own tenant, and only accounts of the same e-mail address are
> ever offered. The call sites carry the reason in their comments.
>
> **DEVIATION(constitution-4): no feature flag, approved in this document (PO 2026-10-29, Q2).** The menu is the shell
> every user has, as the sign-in page of design 62.
>
> **DEVIATION(constitution-2): the "Log in as" endpoints move from `api/platform/tenants/{id}/users` and
> `api/platform/tenants/{id}/impersonate/{userId}` to `api/platform/impersonation/tenants/{id}/users[/{userId}]`,
> approved in this document.** The Tenants controller requires the Tenants permission for every action, which the
> Customer Success Manager does not have (Q4). The SPA is the only consumer and nothing is deployed, so no consumer
> breaks.

## PO decisions (2026-10-29)

| # | Question | Decision |
|---|---|---|
| Q1 | The password may differ per tenant. When is it asked, and what does "Remember password" remember? | **Asked only when it differs.** The sign-in page already checks the password in every place the address has an account (design 62); the session remembers where it was valid, and the menu switches there without asking. Elsewhere the switch asks for that account's password. **"Remember password" stores no password**: the server remembers the switch (a link between the two accounts, both ways), which ends when either password changes. |
| Q2 | Feature flag? | **None** (see the deviation above). |
| Q3 | My settings | **First name and surname** (as the Administration user dialog), phone number, and **language stored per user on the server** (Dutch, English, or the browser's). The e-mail address is shown, not edited. |
| Q4 | What may the Customer Success Manager do? | **Menu impersonation only**: the static host role holds `Platform.Tenants.Impersonation` and nothing else, not the Tenants app. In the host its menu has "Log in to a tenant…": every tenant, searchable and paged, then the existing "Log in as" user picker. |

## The menu

`frontend/src/app/shell/UserMenu.tsx` replaces `UserTenantMenu.tsx`. Trigger: the resolved tenant's name and the
user's picture (initials without one). Content, top to bottom:

1. The picture, full name (or user name), e-mail address, the organisation ABP resolved, and its tenant id (PO
   2026-10-29, after the old OpenTMS menu's "My tenant ID is"; the GUID, selectable in one click; none for the host).
2. **Switch to**: the other accounts of the address (`GET api/platform/my-accounts`), the host first, then by name;
   a lock marks one whose password will be asked. A long list scrolls inside the menu. Hidden when there are none and
   while impersonating.
3. **Log in to a tenant…** — host side, permission `Platform.Tenants.Impersonation`, not while impersonating.
4. **My settings**, **Change profile picture**, **Change password**, then **Sign out**.

The picture is the one set in the Administration app's user dialog or through Change profile picture: both write the
same blob through `ProfilePictureManager` (extracted from `UserAppService`). The Administration dialog refreshes the
menu's profile after a save, since the edited user may be the signed-in one.

## Account switch

- **List.** `MyAccountsAppService.GetListAsync` hashes the current user's normalized e-mail address and asks the
  sign-in lookup; it keeps candidates in active tenants and the host, drops the current tenant, and marks each
  `NeedsPassword` (neither in the session's password places nor remembered) and `IsRemembered`.
- **Password places.** The sign-in page signs in with the claim `opentms_password_tenants` (`AccountSwitchClaims`):
  one entry per place the password was valid in, `tenant|user|stamp-hash` ("host" for the host; the first 16 hex
  characters of the SHA-256 of that user's security stamp). An entry lets the switch through only for that very user
  (an e-mail address can change, so the address alone never decides) and only while its stamp is unchanged (a password
  change ends it). `CarriedSessionClaims` carries it, with the
  impersonator claims, from the auth cookie into the access and identity tokens and through Identity's cookie refresh.
  Ids only, of the user's own accounts.
- **Switch.** `POST api/platform/my-accounts/switch {tenantId, password?, rememberPassword}`:
  refused while impersonating; the target is read in its own tenant (with that tenant's identity settings, in a unit
  of work of its own) and must have the same address, be active, not have to change its password, and not be locked
  out. A given password is checked there; a wrong one counts towards that account's lockout (committed before the
  refusal). Without a password the target must be a password place that holds (same tenant, same user, same stamp)
  or have a remembered link whose two stored security stamps (this account's and the target's) both equal the current
  ones; otherwise `OpenTms.Platform:AccountPasswordRequired`, and the SPA asks for it. With a
  password and "Remember password" the link is written both ways. The answer is a five-minute ticket
  (`AccountSwitchTicket`, data protection with its own purpose) for `Account/SwitchAccount`; it carries the new
  password places (the old ones plus the target when a password was entered) and a nonce.
- **Redeem.** `Pages/Account/SwitchAccount` honours the ticket only for the auth cookie's user and tenant it names, not
  while impersonating, and once (the nonce is kept in the distributed cache for ten minutes). It checks the target again
  as the switch did, since up to five minutes may have passed: the tenant active, the same normalized e-mail address as
  the session user, active, not having to change its password, not locked out, allowed to sign in. It then signs it in with the claim,
  keeping the cookie's persistence, writes a security-log entry in the target tenant, and sends the browser to the SPA,
  which signs in again through the code flow, as after "Log in as" (`switchSession`). A refusal returns with
  `?impersonation=refused`, so the SPA restores the previous tenant and tells the user.
- **Remembered links.** `platform."UserAccountLinks"` (`UserAccountLink`): user and tenant, the linked user and
  tenant, both accounts' security stamps, the time. Host-owned like `UserSignInLookups`: written and read in the
  host database only (`HostUserAccountLinkStore`), unique per (user, linked tenant) with NULLS NOT DISTINCT. A password
  change of either account renews its stamp and so ends the link; "Stop remembering" in My settings deletes it both ways
  (`DELETE api/platform/my-accounts/remembered?tenantId=`). `UserAccountLinkCleanup` removes the links of a deleted
  user or tenant after its unit of work commits. Changing one's own phone number does not renew the stamp
  (`IdentityUser.SetPhoneNumber`, not the user manager's).

## Customer Success Manager

`CustomerSuccessManagerRoleDataSeedContributor` seeds the host role "Customer Success Manager" (`PlatformRoleNames`)
as a static role with `Platform.Tenants.Impersonation` on every host seed run; nothing in tenants. The menu's
"Log in to a tenant…" opens `TenantPickerDialog`: an `OpenTmsGrid` over `GET api/platform/impersonation/tenants`
(server-paged, the grid's search box filters by name), because the list can hold hundreds of tenants (AD-03). Next or a
double-click opens the shared `ImpersonateTenantDialog` (moved to `domains/platform/components`, since the Tenants app
and the shell both use it), and from there the existing flow of `docs/designs/tenants-and-editions.md` applies. The
menu item follows the permission, not the role name (rule 5), so the host admin, who has the permission too, sees it as
well.

## My settings, picture, password

- `GET/PUT api/platform/my-profile`: name, surname, phone number (ABP's column lengths), language (one of the
  product's languages or null). Users cannot change their own e-mail address: ABP's
  `Abp.Identity.User.IsEmailUpdateEnabled` defaults to false (`DisableSelfEmailUpdateSettingDefinitionProvider`), so
  ABP's `PUT api/account/my-profile` keeps it too; only an administrator changes it. The language is ABP's `Abp.Localization.DefaultLanguage` setting at user level. The
  SPA's `LocalizationProvider` renders in the user's language when one is set (else the browser's), keeps a copy in
  local storage (`opentms.userLanguage`) so the next start renders in it at once, and sends the culture as
  `Accept-Language`, so server messages match the screen. The menu applies the server's value whenever the profile
  loads, so the choice follows the user to every browser.
- `GET/POST/DELETE api/platform/my-profile/picture`: the user's own picture, with the rules of the Administration app
  (png, jpeg or webp, at most 2 MB).
- Change password uses ABP's `POST api/account/my-profile/change-password` (current and new password, the tenant's
  password rules). It renews the security stamp, which ends remembered switches to and from the account.

Every one of these needs a signed-in user only and acts on that user in the current tenant; no new permission.

## Domain impact

Platform only (horizontal: identity, tenancy, the shell). AD-03 (a database per tenant, 100–1,000 tenants) is why the
links are a host table and the tenant list is server-paged. AD-16 (the outbox) is not used: no fact another module
reacts to changes; the links are a host-owned hint like the sign-in lookup. None of the three flows of
`docs/architecture/02-flows.md` is extended. No PROPOSED or OPEN item is relied on. No status in `decisions.md` changes.

## Privacy and security (constitution rules 1 and 5)

- Only accounts of the signed-in user's own e-mail address are listed or entered; the lookup and the links hold ids,
  hashes and security stamps, no readable personal data.
- No password is stored anywhere; a remembered switch is bound to both accounts' current passwords through the stamp.
- The switch needs a fresh, single-use, purpose-bound ticket for the signed-in user; the auth server refuses one issued
  to another user or tenant. It is bound to the user, not to one browser session: another signed-in session of the same
  user could redeem it within its five minutes, which gains nothing that user could not do there.
- A known password or a remembered link opens only the user it was proven for, and only while the passwords are
  unchanged.
- **Residual (reported to the PO):** an administrator who changes a user's e-mail address, their own included, makes
  the menu list the tenants that address has accounts in (names only; entering one still needs its password).
- A wrong password in the switch counts towards that account's lockout, so the menu is no way around it.
- Logs carry user and tenant ids only.

## Manual

`docs/manual/gebruikersmenu.md` (new), with references from `aanmelden.md` and `beheer-tenants-en-edities.md`.
