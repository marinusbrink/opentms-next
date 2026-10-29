import { useEffect, useState } from "react";
import { useAuth } from "react-oidc-context";
import { Building2, Camera, KeyRound, LockKeyhole, LogIn, LogOut, Settings } from "lucide-react";
import { toast } from "sonner";

import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { switchSession } from "@/auth/session-switch";
import { ImpersonateTenantDialog } from "@/domains/platform/components/ImpersonateTenantDialog";
import type { ImpersonationTenantRow } from "@/domains/platform/impersonation";
import { ACCOUNT_PASSWORD_REQUIRED, useMyAccounts, useSwitchAccount, type MyAccount } from "@/domains/platform/my-accounts";
import { useMyProfile, useMyProfilePictureUrl } from "@/domains/platform/my-profile";
import { useApplicationConfiguration } from "@/lib/abp/queries";
import { getApiErrorCode, getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted, useIsHostSide } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { ChangePasswordDialog } from "./user-menu/ChangePasswordDialog";
import { ChangeProfilePictureDialog } from "./user-menu/ChangeProfilePictureDialog";
import { initialsOf } from "./user-menu/initials";
import { MySettingsDialog } from "./user-menu/MySettingsDialog";
import { SwitchAccountDialog } from "./user-menu/SwitchAccountDialog";
import { TenantPickerDialog } from "./user-menu/TenantPickerDialog";

type OpenDialog =
  | { kind: "settings" }
  | { kind: "picture" }
  | { kind: "password" }
  | { kind: "switch"; account: MyAccount }
  | { kind: "tenants" }
  | { kind: "impersonate"; tenant: ImpersonationTenantRow }
  | null;

/* The user menu, right side of the app bar (PO 2026-10-29, docs/designs/63-user-menu.md): the user's picture and name,
 * the organisation ABP actually resolved (from the application configuration, not from what the client stored), the
 * other organisations the user's e-mail address has an account in, and for a host user with the Impersonation
 * permission (the Customer Success Manager role) "Log in to a tenant…"; then My settings, Change profile picture,
 * Change password and Sign out. */
export function UserMenu() {
  const { t, setUserLanguage } = useL();
  const auth = useAuth();
  const { data: config } = useApplicationConfiguration();
  const { data: profile } = useMyProfile();
  const pictureUrl = useMyProfilePictureUrl(profile?.hasProfilePicture ?? false);
  const { data: accounts } = useMyAccounts();
  const switchAccount = useSwitchAccount();
  const isHost = useIsHostSide();
  const canImpersonate = useIsGranted("Platform.Tenants.Impersonation");
  const [dialog, setDialog] = useState<OpenDialog>(null);

  /* The language chosen in My settings is stored on the server; apply it when this browser does not know it yet. */
  const profileLanguage = profile?.language;
  const profileLoaded = profile != null;
  useEffect(() => {
    if (profileLoaded) setUserLanguage(profileLanguage);
  }, [profileLoaded, profileLanguage, setUserLanguage]);

  const userName =
    (auth.user?.profile.preferred_username as string | undefined) ?? (auth.user?.profile.name as string | undefined) ?? "";
  const fullName = [profile?.name, profile?.surname].filter(Boolean).join(" ");
  const resolvedTenantName = config?.currentTenant?.name ?? t("Shell:TenantHost");
  const isImpersonating = !!config?.currentUser?.impersonatorUserId;
  const initials = initialsOf(profile?.name, profile?.surname, profile?.userName ?? userName);
  const accountName = (account: MyAccount) => account.tenantName ?? t("Shell:TenantHost");

  const switchTo = async (account: MyAccount) => {
    if (account.needsPassword) {
      setDialog({ kind: "switch", account });
      return;
    }
    try {
      const url = await switchAccount.mutateAsync({ tenantId: account.tenantId ?? null });
      switchSession(url, account.tenantId ? { id: account.tenantId, name: account.tenantName ?? "" } : undefined);
    } catch (err) {
      /* A remembered switch ends when either password changes: then the password is asked after all. */
      if (getApiErrorCode(err) === ACCOUNT_PASSWORD_REQUIRED) setDialog({ kind: "switch", account });
      else toast.error(getApiErrorMessage(err, t));
    }
  };

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger
          render={
            <Button
              variant="ghost"
              aria-label={t("Shell:SignedInAs")}
              className="h-12 gap-2 rounded-none px-3 text-white hover:bg-white/15 hover:text-white"
            >
              <span className="hidden text-xs sm:inline">{resolvedTenantName}</span>
              <Avatar className="size-7">
                {pictureUrl && <AvatarImage src={pictureUrl} alt="" />}
                <AvatarFallback className="bg-white/20 text-xs text-white">{initials}</AvatarFallback>
              </Avatar>
            </Button>
          }
        />
        <DropdownMenuContent align="end" className="w-80">
          <div className="flex items-center gap-3 px-2 py-2">
            <Avatar className="size-12">
              {pictureUrl && <AvatarImage src={pictureUrl} alt="" />}
              <AvatarFallback className="text-sm">{initials}</AvatarFallback>
            </Avatar>
            <div className="min-w-0">
              <p className="truncate text-sm font-medium">{fullName || userName}</p>
              {profile?.email && <p className="truncate text-xs text-muted-foreground">{profile.email}</p>}
              <p className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
                <Building2 className="size-3" />
                {t("Shell:Tenant")}: {resolvedTenantName}
              </p>
            </div>
          </div>

          {(accounts?.length ?? 0) > 0 && (
            <>
              <DropdownMenuSeparator />
              <DropdownMenuGroup>
                <DropdownMenuLabel>{t("UserMenu:SwitchTo")}</DropdownMenuLabel>
                {/* An address rarely has many accounts; a long list scrolls inside the menu. */}
                <div className="max-h-64 overflow-y-auto">
                  {accounts!.map((account) => (
                    <DropdownMenuItem
                      key={account.tenantId ?? "host"}
                      disabled={switchAccount.isPending}
                      onClick={() => void switchTo(account)}
                    >
                      <Building2 />
                      <span className="min-w-0 flex-1 truncate">{accountName(account)}</span>
                      {account.needsPassword && (
                        <LockKeyhole className="text-muted-foreground" aria-label={t("UserMenu:NeedsPassword")} />
                      )}
                    </DropdownMenuItem>
                  ))}
                </div>
              </DropdownMenuGroup>
            </>
          )}

          {isHost && canImpersonate && !isImpersonating && (
            <>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={() => setDialog({ kind: "tenants" })}>
                <LogIn />
                {t("UserMenu:AllTenants")}
              </DropdownMenuItem>
            </>
          )}

          <DropdownMenuSeparator />
          <DropdownMenuItem onClick={() => setDialog({ kind: "settings" })}>
            <Settings />
            {t("UserMenu:MySettings")}
          </DropdownMenuItem>
          <DropdownMenuItem onClick={() => setDialog({ kind: "picture" })}>
            <Camera />
            {t("UserMenu:ChangeProfilePicture")}
          </DropdownMenuItem>
          <DropdownMenuItem onClick={() => setDialog({ kind: "password" })}>
            <KeyRound />
            {t("UserMenu:ChangePassword")}
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem onClick={() => void auth.signoutRedirect()}>
            <LogOut />
            {t("Shell:SignOut")}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      {dialog?.kind === "settings" && <MySettingsDialog open onClose={() => setDialog(null)} />}
      {dialog?.kind === "picture" && <ChangeProfilePictureDialog open onClose={() => setDialog(null)} />}
      {dialog?.kind === "password" && <ChangePasswordDialog open onClose={() => setDialog(null)} />}
      {dialog?.kind === "switch" && (
        <SwitchAccountDialog account={dialog.account} name={accountName(dialog.account)} open onClose={() => setDialog(null)} />
      )}
      {dialog?.kind === "tenants" && (
        <TenantPickerDialog open onClose={() => setDialog(null)} onPick={(tenant) => setDialog({ kind: "impersonate", tenant })} />
      )}
      {dialog?.kind === "impersonate" && (
        <ImpersonateTenantDialog tenant={dialog.tenant} open onClose={() => setDialog(null)} />
      )}
    </>
  );
}
