import { useEffect, useRef, useState } from "react";
import { Check, Copy } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";

export interface CopyButtonProps {
  /** The text that goes to the clipboard. */
  value: string;
  /** The tooltip and accessible name, already translated ("Copy client ID"). */
  label: string;
  className?: string;
}

/* An inline copy action (UI-ACT-006/007: the `Copy` icon, ghost icon-sm, with a tooltip and an aria-label): the
 * value goes to the clipboard and the icon turns into a check for a moment. A refused clipboard changes nothing; the
 * value next to the button stays selectable as text. No toast: the user sees the check (UI-FB-003). */
export function CopyButton({ value, label, className }: CopyButtonProps) {
  const [copied, setCopied] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const text = label;

  useEffect(() => () => clearTimeout(timer.current), []);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      clearTimeout(timer.current);
      timer.current = setTimeout(() => setCopied(false), 1500);
    } catch {
      /* Clipboard access can be denied; the value stays selectable as text. */
    }
  };

  return (
    <TooltipProvider>
      <Tooltip>
        <TooltipTrigger render={<Button type="button" variant="ghost" size="icon-sm" className={className} onClick={() => void copy()} aria-label={text} />}>
          {copied ? <Check className="text-tone-success" /> : <Copy />}
        </TooltipTrigger>
        <TooltipContent>{text}</TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}
