import { useEffect, useState, type FormEvent } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useForgetAccount, useMyAccounts } from "@/domains/platform/my-accounts";
import { useMyProfile, useUpdateMyProfile } from "@/domains/platform/my-profile";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface MySettingsDialogProps {
  open: boolean;
  onClose: () => void;
}

/** The Select's value for "no language of my own": the browser's language applies. */
const BROWSER = "browser";

/* "My settings" (PO 2026-10-29, docs/designs/63-user-menu.md): the user's first name, surname, phone number and
 * language; the e-mail address is the administrator's and only shown. The language is stored on the server and applies
 * at once. Below, the organisations whose password is remembered, each with "Stop remembering". */
export function MySettingsDialog({ open, onClose }: MySettingsDialogProps) {
  const { t, setUserLanguage } = useL();
  const { data: profile, isLoading } = useMyProfile(open);
  const { data: accounts } = useMyAccounts(open);
  const update = useUpdateMyProfile();
  const forget = useForgetAccount();
  const [name, setName] = useState("");
  const [surname, setSurname] = useState("");
  const [phoneNumber, setPhoneNumber] = useState("");
  const [language, setLanguage] = useState(BROWSER);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open && profile) {
      setName(profile.name ?? "");
      setSurname(profile.surname ?? "");
      setPhoneNumber(profile.phoneNumber ?? "");
      setLanguage(profile.language ?? BROWSER);
      setError(null);
    }
  }, [open, profile]);

  const isDirty =
    !!profile &&
    (name !== (profile.name ?? "") ||
      surname !== (profile.surname ?? "") ||
      phoneNumber !== (profile.phoneNumber ?? "") ||
      language !== (profile.language ?? BROWSER));

  const languageItems = [
    { value: BROWSER, label: t("UserMenu:LanguageBrowser") },
    { value: "nl", label: "Nederlands" },
    { value: "en", label: "English" },
  ];

  const remembered = (accounts ?? []).filter((a) => a.isRemembered);

  const save = async (e?: FormEvent) => {
    e?.preventDefault();
    if (update.isPending) return;
    setError(null);
    try {
      const saved = await update.mutateAsync({
        name: name.trim() || null,
        surname: surname.trim() || null,
        phoneNumber: phoneNumber.trim() || null,
        language: language === BROWSER ? null : language,
      });
      setUserLanguage(saved.language);
      toast.success(t("UserMenu:SettingsSaved"));
      onClose();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open={open}
      onOpenChange={(isOpen) => {
        if (!isOpen) onClose();
      }}
      variant="action"
      size="md"
      title={t("UserMenu:MySettings")}
      isBusy={update.isPending || isLoading}
      isDirty={isDirty}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={update.isPending}>
            {t("Cancel")}
          </Button>
          <Button onClick={() => void save()} disabled={!profile || update.isPending}>
            {t("Save")}
          </Button>
        </>
      }
    >
      <form onSubmit={(e) => void save(e)} className="flex flex-col gap-4" noValidate>
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <FormSection title={t("UserMenu:MySettings")}>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <FormField label={t("UserMenu:Name")} htmlFor="ms-name">
              <Input id="ms-name" maxLength={64} value={name} onChange={(e) => setName(e.target.value)} autoFocus />
            </FormField>
            <FormField label={t("UserMenu:Surname")} htmlFor="ms-surname">
              <Input id="ms-surname" maxLength={64} value={surname} onChange={(e) => setSurname(e.target.value)} />
            </FormField>
            <FormField label={t("UserMenu:PhoneNumber")} htmlFor="ms-phone">
              <Input
                id="ms-phone"
                type="tel"
                maxLength={16}
                value={phoneNumber}
                onChange={(e) => setPhoneNumber(e.target.value)}
              />
            </FormField>
            <FormField label={t("UserMenu:Language")} htmlFor="ms-language" floating="always">
              <Select items={languageItems} value={language} onValueChange={(next) => setLanguage((next as string | null) ?? BROWSER)}>
                <SelectTrigger id="ms-language" className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {languageItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <FormField label={t("UserMenu:Email")} htmlFor="ms-email" floating="always" className="sm:col-span-2">
              <Input id="ms-email" value={profile?.email ?? ""} readOnly disabled />
            </FormField>
          </div>
        </FormSection>
        {remembered.length > 0 && (
          <FormSection title={t("UserMenu:Remembered")}>
            <ul className="flex flex-col divide-y rounded-lg border">
              {remembered.map((account) => (
                <li key={account.tenantId ?? "host"} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
                  <span className="truncate">{account.tenantName ?? t("Shell:TenantHost")}</span>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={forget.isPending}
                    onClick={() => void forget.mutateAsync(account.tenantId ?? null).catch((err) => setError(getApiErrorMessage(err, t)))}
                  >
                    {t("UserMenu:Forget")}
                  </Button>
                </li>
              ))}
            </ul>
          </FormSection>
        )}
        <button type="submit" hidden />
      </form>
    </EntityDialog>
  );
}
