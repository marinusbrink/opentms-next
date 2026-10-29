import { useState } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { AvatarPicker } from "@/components/ui/avatar-picker";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import {
  useDeleteMyProfilePicture,
  useMyProfile,
  useMyProfilePictureUrl,
  useSetMyProfilePicture,
} from "@/domains/platform/my-profile";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { initialsOf } from "./initials";

interface ChangeProfilePictureDialogProps {
  open: boolean;
  onClose: () => void;
}

/* "Change profile picture" (docs/designs/63-user-menu.md): the same picture field as the Administration app's user
 * dialog; the change is staged and saved with the primary. */
export function ChangeProfilePictureDialog({ open, onClose }: ChangeProfilePictureDialogProps) {
  const { t } = useL();
  const { data: profile } = useMyProfile();
  const currentUrl = useMyProfilePictureUrl(profile?.hasProfilePicture ?? false);
  const setPicture = useSetMyProfilePicture();
  const deletePicture = useDeleteMyProfilePicture();
  const [pending, setPending] = useState<File | null | undefined>(undefined);
  const [error, setError] = useState<string | null>(null);
  const isBusy = setPicture.isPending || deletePicture.isPending;

  const save = async () => {
    if (pending === undefined || isBusy) return;
    setError(null);
    try {
      if (pending === null) await deletePicture.mutateAsync();
      else await setPicture.mutateAsync(pending);
      toast.success(t("UserMenu:ProfilePictureSaved"));
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
      size="sm"
      title={t("UserMenu:ChangeProfilePicture")}
      isBusy={isBusy}
      isDirty={pending !== undefined}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={isBusy}>
            {t("Cancel")}
          </Button>
          <Button onClick={() => void save()} disabled={pending === undefined || isBusy}>
            {t("Save")}
          </Button>
        </>
      }
    >
      <div className="flex flex-col items-center gap-3">
        {error && (
          <Alert variant="destructive" className="w-full">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <AvatarPicker
          currentUrl={currentUrl}
          pending={pending}
          onChange={setPending}
          initials={initialsOf(profile?.name, profile?.surname, profile?.userName)}
          label={t("UserMenu:ProfilePicture")}
          disabled={isBusy}
        />
        <p className="text-center text-label text-muted-foreground">{t("UserMenu:ProfilePictureHint")}</p>
      </div>
    </EntityDialog>
  );
}
