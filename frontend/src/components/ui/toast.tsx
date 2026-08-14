import { createPortal } from "react-dom";

import { cn } from "@/lib/utils";

interface ToastProps {
  message: string;
  className?: string;
}

export function Toast({ message, className }: ToastProps) {
  return createPortal(
    <div
      role="alert"
      aria-live="assertive"
      aria-atomic="true"
      className={cn(
        "fixed bottom-4 right-4 z-50 max-w-sm rounded-lg border bg-background px-4 py-3 text-sm shadow-lg",
        className,
      )}
    >
      {message}
    </div>,
    document.body,
  );
}
