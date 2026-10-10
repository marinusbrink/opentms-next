import { clsx, type ClassValue } from "clsx"
import { extendTailwindMerge } from "tailwind-merge"

/* tailwind-merge only dedupes utilities it knows. The dialog width tiers of UI-DLG-003 are
 * custom `--container-*` tokens (opentms-theme.css), so without this registration a
 * `sm:max-w-dialog-md` passed by EntityDialog would not replace the base `sm:max-w-sm` of
 * the shadcn DialogContent, and every editor would stay 384px wide. */
const twMerge = extendTailwindMerge({
  extend: {
    theme: {
      container: ["dialog-confirm", "dialog-sm", "dialog-md", "dialog-lg"],
    },
  },
})

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
