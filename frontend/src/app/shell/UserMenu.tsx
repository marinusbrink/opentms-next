import { useEffect, useState } from "react";
import { useAuth } from "react-oidc-context";
import {
  ArrowRightLeft,
  Building2,
  Camera,
  ChevronDown,
  Copy,
  KeyRound,
  Languages,
  LockKeyhole,
  LogIn,
  LogOut,
  Settings,
  type LucideIcon,
} from "lucide-react";
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
import { useMyProfile, useMyProfilePictureUrl, useUpdateMyProfile } from "@/domains/platform/my-profile";
import { useApplicationConfiguration } from "@/lib/abp/queries";
import { getApiErrorCode, getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted, useIsHostSide } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";
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
  const { t, culture, setUserLanguage } = useL();
  const auth = useAuth();
  const { data: config } = useApplicationConfiguration();
  const { data: profile } = useMyProfile();
  const pictureUrl = useMyProfilePictureUrl(profile?.hasProfilePicture ?? false);
  const { data: accounts } = useMyAccounts();
  const switchAccount = useSwitchAccount();
  const updateProfile = useUpdateMyProfile();
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
      switchSession(url, account.tenantId ? { id: account.tenantId, name: account.tenantName ?? "" } : undefined, window.location.pathname);
    } catch (err) {
      /* A remembered switch ends when either password changes: then the password is asked after all. */
      if (getApiErrorCode(err) === ACCOUNT_PASSWORD_REQUIRED) setDialog({ kind: "switch", account });
      else toast.error(getApiErrorMessage(err, t));
    }
  };

  const languages = config?.localization?.languages ?? [];
  const chooseLanguage = async (language: string) => {
    if (!profile || language === culture || updateProfile.isPending) return;
    try {
      const saved = await updateProfile.mutateAsync({
        name: profile.name ?? null,
        surname: profile.surname ?? null,
        phoneNumber: profile.phoneNumber ?? null,
        language,
      });
      setUserLanguage(saved.language);
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    }
  };

  const copyTenantId = async (id: string) => {
    try {
      await navigator.clipboard.writeText(id);
      toast.success(t("UserMenu:TenantIdCopied"));
    } catch {
      /* Clipboard refused (permissions, insecure context): the id stays selectable. */
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
              className="group h-12 gap-2 rounded-none px-3 text-white hover:bg-white/15 hover:text-white data-[popup-open]:bg-white/15 data-[popup-open]:text-white aria-expanded:bg-white/15 aria-expanded:text-white"
            >
              <span className="hidden max-w-40 truncate text-xs font-medium sm:inline">{resolvedTenantName}</span>
              <Avatar className="size-8 ring-2 ring-white/30 transition-shadow group-hover:ring-white/60">
                {pictureUrl && <AvatarImage src={pictureUrl} alt="" />}
                <AvatarFallback className="bg-white/20 text-xs font-semibold text-white">{initials}</AvatarFallback>
              </Avatar>
              <ChevronDown className="hidden size-3.5 opacity-70 transition-transform group-data-[popup-open]:rotate-180 sm:block" />
            </Button>
          }
        />
        <DropdownMenuContent align="end" className="w-88 overflow-hidden rounded-xl p-0">
          {/* Identity: the brand colour is identity, not an action (UI-TOK-002). */}
          <div className="bg-gradient-to-br from-brand to-brand-hover px-4 pt-4 pb-3 text-brand-foreground">
            <div className="flex items-center gap-3">
              <button
                type="button"
                className="group/avatar relative shrink-0 rounded-full outline-none focus-visible:ring-2 focus-visible:ring-brand-foreground"
                aria-label={t("UserMenu:ChangeProfilePicture")}
                onClick={() => setDialog({ kind: "picture" })}
              >
                <Avatar className="size-14 ring-2 ring-brand-foreground/40">
                  {pictureUrl && <AvatarImage src={pictureUrl} alt="" />}
                  <AvatarFallback className="bg-brand-foreground/20 text-lg font-semibold text-brand-foreground">{initials}</AvatarFallback>
                </Avatar>
                <span className="absolute inset-0 flex items-center justify-center rounded-full bg-foreground/45 opacity-0 transition-opacity group-hover/avatar:opacity-100">
                  <Camera className="size-5 text-background" />
                </span>
              </button>
              <div className="min-w-0 flex-1">
                <p className="truncate text-title font-semibold">{fullName || userName}</p>
                {profile?.email && <p className="truncate text-xs opacity-80">{profile.email}</p>}
                <span className="mt-1.5 inline-flex max-w-full items-center gap-1 rounded-full bg-brand-foreground/15 px-2 py-0.5 text-xs font-medium">
                  <Building2 className="size-3 shrink-0" />
                  <span className="truncate">{resolvedTenantName}</span>
                </span>
              </div>
            </div>
            {/* The tenant's id, for support calls (PO 2026-10-29); copied in one click. The host has none. */}
            {config?.currentTenant?.id && (
              <div className="mt-3 flex items-center gap-2 rounded-md bg-brand-foreground/10 py-1 pr-1 pl-2 text-xs">
                <span className="shrink-0 opacity-80">{t("UserMenu:TenantId")}</span>
                <span className="min-w-0 flex-1 truncate font-mono tabular-nums select-all">{config.currentTenant.id}</span>
                <button
                  type="button"
                  className="shrink-0 rounded p-1 opacity-80 outline-none hover:bg-brand-foreground/15 hover:opacity-100 focus-visible:ring-2 focus-visible:ring-brand-foreground"
                  aria-label={t("UserMenu:CopyTenantId")}
                  title={t("UserMenu:CopyTenantId")}
                  onClick={() => void copyTenantId(config.currentTenant!.id!)}
                >
                  <Copy className="size-3.5" />
                </button>
              </div>
            )}
          </div>

          <div className="p-1.5">
            {(accounts?.length ?? 0) > 0 && (
              <>
                <DropdownMenuGroup>
                  <DropdownMenuLabel className="px-2 pt-1.5 text-label tracking-wide uppercase">{t("UserMenu:SwitchTo")}</DropdownMenuLabel>
                  {/* An address rarely has many accounts; a long list scrolls inside the menu. */}
                  <div className="max-h-56 overflow-y-auto">
                    {accounts!.map((account) => (
                      <DropdownMenuItem
                        key={account.tenantId ?? "host"}
                        className="gap-3 px-2 py-1.5"
                        disabled={switchAccount.isPending}
                        onClick={() => void switchTo(account)}
                      >
                        <span className="flex size-8 shrink-0 items-center justify-center rounded-md bg-brand-subtle text-xs font-semibold text-brand">
                          {monogramOf(accountName(account))}
                        </span>
                        <span className="flex min-w-0 flex-1 flex-col">
                          <span className="truncate font-medium">{accountName(account)}</span>
                          <span className="truncate text-label text-muted-foreground">
                            {account.needsPassword ? t("UserMenu:NeedsPassword") : t("UserMenu:SwitchesDirectly")}
                          </span>
                        </span>
                        {account.needsPassword ? (
                          <LockKeyhole className="text-muted-foreground" aria-label={t("UserMenu:NeedsPassword")} />
                        ) : (
                          <ArrowRightLeft className="text-muted-foreground opacity-0 transition-opacity group-focus/dropdown-menu-item:opacity-100" />
                        )}
                      </DropdownMenuItem>
                    ))}
                  </div>
                </DropdownMenuGroup>
                <DropdownMenuSeparator className="my-1.5" />
              </>
            )}

            {isHost && canImpersonate && !isImpersonating && (
              <>
                <MenuAction
                  icon={LogIn}
                  label={t("UserMenu:AllTenants")}
                  hint={t("UserMenu:AllTenantsHint")}
                  accent
                  onClick={() => setDialog({ kind: "tenants" })}
                />
                <DropdownMenuSeparator className="my-1.5" />
              </>
            )}

            <MenuAction icon={Settings} label={t("UserMenu:MySettings")} hint={t("UserMenu:MySettingsHint")} onClick={() => setDialog({ kind: "settings" })} />
            <MenuAction
              icon={Camera}
              label={t("UserMenu:ChangeProfilePicture")}
              hint={t("UserMenu:ChangeProfilePictureHint")}
              onClick={() => setDialog({ kind: "picture" })}
            />
            <MenuAction
              icon={KeyRound}
              label={t("UserMenu:ChangePassword")}
              hint={t("UserMenu:ChangePasswordHint")}
              onClick={() => setDialog({ kind: "password" })}
            />

            {languages.length > 1 && (
              <div className="mt-1 flex items-center gap-3 rounded-md px-2 py-1.5 text-sm">
                <span className="flex size-8 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
                  <Languages className="size-4" />
                </span>
                <span className="flex-1 font-medium">{t("UserMenu:Language")}</span>
                <div role="group" aria-label={t("UserMenu:Language")} className="flex rounded-full bg-muted p-0.5">
                  {languages.map((language) => {
                    const active = language.cultureName === culture;
                    return (
                      <button
                        key={language.cultureName}
                        type="button"
                        aria-pressed={active}
                        title={language.displayName ?? undefined}
                        disabled={!profile || updateProfile.isPending}
                        onClick={() => void chooseLanguage(language.cultureName ?? "")}
                        className={cn(
                          "rounded-full px-2.5 py-0.5 text-xs font-semibold uppercase transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-60",
                          active ? "bg-background text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground",
                        )}
                      >
                        {language.cultureName}
                      </button>
                    );
                  })}
                </div>
              </div>
            )}
          </div>

          <div className="border-t bg-muted/40 p-1.5">
            <DropdownMenuItem variant="destructive" className="gap-3 px-2 py-1.5" onClick={() => void auth.signoutRedirect()}>
              <span className="flex size-8 shrink-0 items-center justify-center rounded-md bg-tone-danger-bg">
                <LogOut className="size-4" />
              </span>
              <span className="font-medium">{t("Shell:SignOut")}</span>
            </DropdownMenuItem>
          </div>
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

/** One action row: an icon tile, the label and a one-line hint. */
function MenuAction({
  icon: Icon,
  label,
  hint,
  accent = false,
  onClick,
}: {
  icon: LucideIcon;
  label: string;
  hint: string;
  accent?: boolean;
  onClick: () => void;
}) {
  return (
    <DropdownMenuItem className="gap-3 px-2 py-1.5" onClick={onClick}>
      <span
        className={cn(
          "flex size-8 shrink-0 items-center justify-center rounded-md transition-colors",
          accent ? "bg-brand-subtle text-brand" : "bg-muted text-muted-foreground group-focus/dropdown-menu-item:text-foreground",
        )}
      >
        <Icon className="size-4" />
      </span>
      <span className="flex min-w-0 flex-col">
        <span className="truncate font-medium">{label}</span>
        <span className="truncate text-label text-muted-foreground">{hint}</span>
      </span>
    </DropdownMenuItem>
  );
}

/** Up to two letters for an organisation tile: the first letters of its first two words, else its first two letters. */
function monogramOf(name: string): string {
  const words = name.split(/[\s\-_.]+/).filter(Boolean);
  const letters = words.length > 1 ? words[0][0] + words[1][0] : name.slice(0, 2);
  return letters.toUpperCase();
}
