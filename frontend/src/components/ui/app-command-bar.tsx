import { type LucideIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { useL } from "@/lib/i18n/LocalizationProvider";

export interface AppCommandBarCommand {
  id: string;
  labelKey: string;
  icon?: LucideIcon;
  variant?: "default" | "destructive";
  requiresSelection?: boolean;
  disabled?: boolean;
  onClick: () => void;
}

export interface AppCommandBarProps {
  commands: AppCommandBarCommand[];
  selectionCount?: number;
  className?: string;
}

export function AppCommandBar({ commands, selectionCount, className }: AppCommandBarProps) {
  const { t } = useL();

  const primaryCommands = commands.filter((c) => !c.requiresSelection);
  const selectionCommands = commands.filter((c) => c.requiresSelection);
  const hasSelection = (selectionCount ?? 0) > 0;

  return (
    <div
      className={cn(
        "mb-4 flex h-12 items-center gap-3 rounded-md border border-border bg-[#F8F9FA] px-4 dark:bg-card",
        className,
      )}
    >
      {primaryCommands.map((command) => {
        const Icon = command.icon;
        const isDisabled = command.disabled === true;
        return (
          <Button
            key={command.id}
            variant={command.variant ?? "default"}
            size="sm"
            disabled={isDisabled}
            aria-disabled={isDisabled}
            onClick={command.onClick}
          >
            {Icon && <Icon size={16} />}
            {t(command.labelKey)}
          </Button>
        );
      })}

      <div className="flex-1" />

      {hasSelection && (
        <span className="text-sm text-muted-foreground">
          {t("Administration:NSelected").replace("{0}", String(selectionCount))}
        </span>
      )}

      {selectionCommands.map((command) => {
        const isDisabled = command.disabled === true || !hasSelection;
        const Icon = command.icon;
        return (
          <Button
            key={command.id}
            variant={command.variant ?? "default"}
            size="sm"
            disabled={isDisabled}
            aria-disabled={isDisabled}
            onClick={command.onClick}
          >
            {Icon && <Icon size={16} />}
            {t(command.labelKey)}
          </Button>
        );
      })}
    </div>
  );
}
