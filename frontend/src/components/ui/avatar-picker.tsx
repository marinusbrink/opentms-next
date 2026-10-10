import { useEffect, useRef, useState, type DragEvent } from "react";
import { Trash2, Upload } from "lucide-react";

import { ContextMenu, ContextMenuContent, ContextMenuItem, ContextMenuSeparator, ContextMenuTrigger } from "@/components/ui/context-menu";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* A picture field for a record (profile picture): the current image or an initials
 * placeholder. No buttons: drop an image on it, click it to choose one, or right-click for
 * Change picture / Remove picture. The value is what the form stages: `null` means "remove",
 * a File means "replace on Save". The full drag-and-drop upload control with progress is
 * component-map GAP-05; this is the small avatar case. */
export interface AvatarPickerProps {
  /** The stored picture as an object URL, or null when there is none. */
  currentUrl: string | null;
  /** The staged change: undefined = unchanged, null = remove, File = replace. */
  pending: File | null | undefined;
  onChange: (pending: File | null | undefined) => void;
  /** Initials for the placeholder, e.g. "MB". */
  initials: string;
  label: string;
  accept?: string;
  disabled?: boolean;
  className?: string;
}

const ACCEPTED_TYPES = ["image/png", "image/jpeg", "image/webp"];

export function AvatarPicker({
  currentUrl,
  pending,
  onChange,
  initials,
  label,
  accept = ACCEPTED_TYPES.join(","),
  disabled = false,
  className,
}: AvatarPickerProps) {
  const { t } = useL();
  const inputRef = useRef<HTMLInputElement>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [dragOver, setDragOver] = useState(false);

  useEffect(() => {
    if (!(pending instanceof File)) {
      setPreviewUrl(null);
      return;
    }
    const url = URL.createObjectURL(pending);
    setPreviewUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [pending]);

  const shownUrl = pending === null ? null : (previewUrl ?? currentUrl);
  const hasPicture = shownUrl != null;
  const accepted = accept.split(",").map((type) => type.trim());

  const choose = (file: File | undefined) => {
    if (!file || disabled || !accepted.includes(file.type)) return;
    onChange(file);
  };
  /* Removing a stored picture stages "remove"; dropping a staged file just goes back to the stored one. */
  const remove = () => onChange(currentUrl ? null : undefined);

  const onDrop = (e: DragEvent<HTMLElement>) => {
    e.preventDefault();
    setDragOver(false);
    choose(e.dataTransfer.files?.[0]);
  };

  return (
    <ContextMenu>
      <ContextMenuTrigger
        render={
          <button
            type="button"
            aria-label={label}
            title={t("Administration:PictureDropHint")}
            disabled={disabled}
            onClick={() => inputRef.current?.click()}
            onDragOver={(e) => {
              /* Only a file drag lights the target up; dragged text or images from the page do not. */
              if (disabled || !Array.from(e.dataTransfer.types).includes("Files")) return;
              e.preventDefault();
              setDragOver(true);
            }}
            onDragLeave={() => setDragOver(false)}
            onDragEnd={() => setDragOver(false)}
            onMouseLeave={() => setDragOver(false)}
            onDrop={onDrop}
            data-slot="avatar-picker"
            data-drag-over={dragOver || undefined}
            className={cn(
              "relative flex size-28 shrink-0 items-center justify-center overflow-hidden rounded-full border bg-muted text-xl font-semibold text-muted-foreground transition-colors outline-none",
              "focus-visible:ring-3 focus-visible:ring-ring/50 enabled:cursor-pointer enabled:hover:border-brand",
              dragOver && "border-brand bg-brand-subtle",
              className,
            )}
          />
        }
      >
        {shownUrl ? (
          <img src={shownUrl} alt="" className="size-full object-cover" />
        ) : (
          <span aria-hidden="true">{initials}</span>
        )}
        {dragOver && (
          <span className="absolute inset-0 flex items-center justify-center bg-brand-subtle/80 text-brand" aria-hidden="true">
            <Upload className="size-8" />
          </span>
        )}
      </ContextMenuTrigger>

      <input
        ref={inputRef}
        type="file"
        accept={accept}
        className="sr-only"
        tabIndex={-1}
        aria-label={t("Administration:ChangePicture")}
        disabled={disabled}
        onChange={(e) => {
          choose(e.target.files?.[0]);
          e.target.value = "";
        }}
      />

      {/* A disabled control has no menu: nothing can change. */}
      {!disabled && (
      <ContextMenuContent>
        <ContextMenuItem onClick={() => inputRef.current?.click()}>
          <Upload />
          {t("Administration:ChangePicture")}
        </ContextMenuItem>
        {hasPicture && (
          <>
            <ContextMenuSeparator />
            <ContextMenuItem variant="destructive" onClick={remove}>
              <Trash2 />
              {t("Administration:RemovePicture")}
            </ContextMenuItem>
          </>
        )}
      </ContextMenuContent>
      )}
    </ContextMenu>
  );
}
