import { useRef, useState, useEffect } from "react";
import { type LucideIcon, ChevronDown } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useApplicationConfiguration } from "@/lib/abp/queries";

export interface AppCommandBarCommand {
  id: string;
  labelKey: string;
  tooltipKey?: string;        // tooltip text (localization key); defaults to labelKey
  icon?: LucideIcon;
  isPrimary?: boolean;        // true → primary left-most action with distinct visual treatment
  variant?: "default" | "destructive";
  requiresSelection?: boolean;
  disabled?: boolean;
  disabledReasonKey?: string; // tooltip shown on disabled state (localization key)
  // Selection-count-aware label keys for selection-gated actions (flag-on path only).
  // The bar picks zero/one/many based on selectionCount; {0} in many is replaced with the count.
  selectionLabelKeys?: { zero: string; one: string; many: string };
  onClick: () => void;
}

export interface AppCommandBarProps {
  commands: AppCommandBarCommand[];
  selectionCount?: number;
  className?: string;
}

// ── Internal: new-style sub-components ───────────────────────────────────────

interface CmdProps {
  command: AppCommandBarCommand;
  t: (key: string) => string;
}

function PrimaryActionButton({ command, t }: CmdProps) {
  const Icon = command.icon;
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <button
            type="button"
            className="inline-flex items-center gap-1.5 m-[5px] bg-transparent px-3 py-1 text-sm text-brand hover:bg-brand/20 hover:underline focus-visible:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            onClick={command.onClick}
          />
        }
      >
        {Icon && <Icon size={16} />}
        <span className="max-w-[120px] truncate font-semibold">{t(command.labelKey)}</span>
      </TooltipTrigger>
      <TooltipContent side="bottom">
        {t(command.tooltipKey ?? command.labelKey)}
      </TooltipContent>
    </Tooltip>
  );
}

function SecondaryActionButton({ command, t }: CmdProps) {
  const Icon = command.icon;
  const isDisabled = command.disabled === true;
  const tooltipText = isDisabled
    ? t(command.disabledReasonKey ?? command.labelKey)
    : t(command.tooltipKey ?? command.labelKey);

  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <button
            type="button"
            className={cn(
              "inline-flex items-center gap-1.5 m-[5px] bg-transparent px-3 py-1 text-sm text-brand hover:bg-brand/20 hover:underline focus-visible:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
              isDisabled && "cursor-default opacity-50",
            )}
            aria-disabled={isDisabled || undefined}
            onClick={() => {
              if (!isDisabled) command.onClick();
            }}
          />
        }
      >
        {Icon && <Icon size={16} />}
        <span className="max-w-[120px] truncate">{t(command.labelKey)}</span>
      </TooltipTrigger>
      <TooltipContent side="bottom">{tooltipText}</TooltipContent>
    </Tooltip>
  );
}

interface OverflowMenuProps {
  actions: AppCommandBarCommand[];
  t: (key: string) => string;
}

function OverflowMenuButton({ actions, t }: OverflowMenuProps) {
  const triggerRef = useRef<HTMLButtonElement>(null);

  return (
    <DropdownMenu onOpenChange={(open) => { if (!open) triggerRef.current?.focus(); }}>
      <DropdownMenuTrigger
        render={
          <button
            ref={triggerRef}
            type="button"
            className="inline-flex items-center gap-1 m-[5px] bg-transparent px-3 py-1 text-sm text-brand hover:bg-brand/20 hover:underline focus-visible:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            aria-label={t("Shell:CommandBarMoreLabel")}
          />
        }
      >
        <span aria-hidden="true">…</span>
        <span>{t("Shell:CommandBarMore")}</span>
        <ChevronDown className="size-3" />
      </DropdownMenuTrigger>
      <DropdownMenuContent side="bottom" align="start">
        {actions.map((cmd) => {
          const Icon = cmd.icon;
          return (
            <DropdownMenuItem
              key={cmd.id}
              disabled={cmd.disabled}
              onClick={cmd.onClick}
            >
              {Icon && <Icon className="size-4" />}
              <span>{t(cmd.labelKey)}</span>
            </DropdownMenuItem>
          );
        })}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

// ── Internal: new-style bar (flag-on path) ────────────────────────────────────

interface NewStyleProps {
  commands: AppCommandBarCommand[];
  selectionCount?: number;
  className?: string;
  t: (key: string) => string;
}

function AppCommandBarNewStyle({ commands, selectionCount, className, t }: NewStyleProps) {
  const primaryCommand = commands.find((c) => !c.requiresSelection && c.isPrimary);
  const secondaryActions = commands.filter((c) => !c.requiresSelection && !c.isPrimary);
  const selectionCommands = commands.filter((c) => c.requiresSelection);
  const hasSelection = (selectionCount ?? 0) > 0;

  const containerRef = useRef<HTMLDivElement>(null);
  const measureRef = useRef<HTMLDivElement>(null);
  const [visibleCount, setVisibleCount] = useState(secondaryActions.length);

  useEffect(() => {
    const container = containerRef.current;
    const measure = measureRef.current;
    if (!container || !measure) return;

    const observer = new ResizeObserver((entries) => {
      const width = entries[0]?.contentRect.width ?? 0;
      const children = Array.from(measure.children) as HTMLElement[];

      if (width <= 0 || children.length === 0) {
        setVisibleCount(0);
        return;
      }

      let usedWidth = 0;
      let count = 0;

      for (let i = 0; i < children.length; i++) {
        // gap-1 = 4px between items (not before the first)
        const gap = i > 0 ? 4 : 0;
        const childWidth = children[i].offsetWidth + gap;
        if (usedWidth + childWidth <= width) {
          usedWidth += childWidth;
          count++;
        } else {
          break;
        }
      }

      setVisibleCount(count);
    });

    observer.observe(container);
    return () => observer.disconnect();
  }, [secondaryActions.length]);

  const visibleActions = secondaryActions.slice(0, visibleCount);
  const overflowActions = secondaryActions.slice(visibleCount);

  return (
    <TooltipProvider>
      <div
        className={cn(
          "flex h-10 items-center border-b-2 border-b-[#E5E5E5] dark:border-b-border bg-[#F8F9FA] dark:bg-background",
          className,
        )}
      >
        {/* Primary action */}
        {primaryCommand && <PrimaryActionButton command={primaryCommand} t={t} />}

        {/* Selection-gated actions — positioned next to primary, never in overflow */}
        {selectionCommands.map((command) => {
          const isDisabled = command.disabled === true || !hasSelection;
          const Icon = command.icon;
          const count = selectionCount ?? 0;
          let label: string;
          if (command.selectionLabelKeys) {
            if (count === 0) label = t(command.selectionLabelKeys.zero);
            else if (count === 1) label = t(command.selectionLabelKeys.one);
            else label = t(command.selectionLabelKeys.many).replace("{0}", String(count));
          } else {
            label = t(command.labelKey);
          }
          return (
            <button
              key={command.id}
              type="button"
              className={cn(
                "inline-flex items-center gap-1.5 m-[5px] bg-transparent px-3 py-1 text-sm hover:bg-brand/20 hover:underline focus-visible:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                command.variant === "destructive" ? "text-destructive" : "text-brand",
                isDisabled && "cursor-default opacity-50",
              )}
              aria-disabled={isDisabled || undefined}
              onClick={() => { if (!isDisabled) command.onClick(); }}
            >
              {Icon && <Icon size={16} />}
              {label}
            </button>
          );
        })}

        {/* Divider between primary/selection-gated group and secondary actions */}
        {(primaryCommand != null || selectionCommands.length > 0) && (
          <div className="mx-2 h-5 self-center border-r border-border" />
        )}

        {/* Secondary actions: flex-1 gives ResizeObserver the correct available width */}
        <div ref={containerRef} className="relative flex min-w-0 flex-1 gap-1">
          {/* Off-screen measurement div: always renders all secondary actions for width reads */}
          <div
            ref={measureRef}
            aria-hidden="true"
            className="pointer-events-none invisible absolute left-0 top-0 flex gap-1"
          >
            {secondaryActions.map((cmd) => (
              <SecondaryActionButton key={cmd.id} command={cmd} t={t} />
            ))}
          </div>

          {/* Visible secondary actions */}
          {visibleActions.map((cmd) => (
            <SecondaryActionButton key={cmd.id} command={cmd} t={t} />
          ))}

          {/* Overflow menu — only when at least one secondary action is collapsed */}
          {overflowActions.length > 0 && (
            <OverflowMenuButton actions={overflowActions} t={t} />
          )}
        </div>

        {/* Selection count badge */}
        {hasSelection && (
          <span className="mr-2 shrink-0 text-sm text-muted-foreground">
            {t("Administration:NSelected").replace("{0}", String(selectionCount))}
          </span>
        )}
      </div>
    </TooltipProvider>
  );
}

// ── Public component ──────────────────────────────────────────────────────────

export function AppCommandBar({ commands, selectionCount, className }: AppCommandBarProps) {
  const { t } = useL();
  const { data: appConfig } = useApplicationConfiguration();
  const isNewStyleEnabled = appConfig?.features?.values?.["UI.CommonToolbar"] === "true";

  const primaryCommands = commands.filter((c) => !c.requiresSelection);
  const selectionCommands = commands.filter((c) => c.requiresSelection);
  const hasSelection = (selectionCount ?? 0) > 0;

  // ── Flag-off path: original rendering, unchanged ──────────────────────────
  if (!isNewStyleEnabled) {
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

  // ── Flag-on path: Office-365 style ────────────────────────────────────────
  if (commands.length === 0) return null;

  return (
    <AppCommandBarNewStyle
      commands={commands}
      selectionCount={selectionCount}
      className={className}
      t={t}
    />
  );
}
