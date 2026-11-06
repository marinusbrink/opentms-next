import { useRef, useState, useEffect, type ReactNode } from "react";
import { type LucideIcon, ChevronDown } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
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

/** An entry of a command's menu (a command with `items` opens a menu instead of acting itself). */
export interface AppCommandBarMenuItem {
  id: string;
  labelKey: string;
  icon?: LucideIcon;
  disabled?: boolean;
  onClick: () => void;
}

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
  // Selection-count-aware label keys for selection-gated actions.
  // The bar picks zero/one/many based on selectionCount; {0} in many is replaced with the count.
  selectionLabelKeys?: { zero: string; one: string; many: string };
  /** The command opens a menu with these entries (e.g. the Planboard's Add pane); `onClick` is then not called. */
  items?: AppCommandBarMenuItem[];
  onClick: () => void;
}

export interface AppCommandBarProps {
  commands: AppCommandBarCommand[];
  selectionCount?: number;
  className?: string;
  /** Controls at the bar's right end, e.g. a bespoke screen's filters (design 70: the Planboard's range and units). */
  trailing?: ReactNode;
}

// ── Internal: sub-components ──────────────────────────────────────────────────

interface CmdProps {
  command: AppCommandBarCommand;
  t: (key: string) => string;
}

function CommandMenuButton({ command, t, primary }: CmdProps & { primary: boolean }) {
  const Icon = command.icon;
  const isDisabled = command.disabled === true;
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <button
            type="button"
            disabled={isDisabled}
            className={cn(
              "inline-flex items-center gap-1.5 m-[5px] bg-transparent px-3 py-1 text-sm text-brand hover:bg-brand/20 hover:underline focus-visible:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
              isDisabled && "cursor-default opacity-50",
            )}
          />
        }
      >
        {Icon && <Icon size={16} />}
        <span className={cn("whitespace-nowrap", primary && "font-semibold")}>{t(command.labelKey)}</span>
        <ChevronDown className="size-3" />
      </DropdownMenuTrigger>
      <DropdownMenuContent side="bottom" align="start">
        {command.items!.map((item) => {
          const ItemIcon = item.icon;
          return (
            <DropdownMenuItem key={item.id} disabled={item.disabled} onClick={item.onClick}>
              {ItemIcon && <ItemIcon className="size-4" />}
              <span>{t(item.labelKey)}</span>
            </DropdownMenuItem>
          );
        })}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function PrimaryActionButton({ command, t }: CmdProps) {
  if (command.items) return <CommandMenuButton command={command} t={t} primary />;
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
        <span className="whitespace-nowrap font-semibold">{t(command.labelKey)}</span>
      </TooltipTrigger>
      <TooltipContent side="bottom">{tooltipText}</TooltipContent>
    </Tooltip>
  );
}

function SecondaryActionButton({ command, t }: CmdProps) {
  if (command.items) return <CommandMenuButton command={command} t={t} primary={false} />;
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
        <span className="whitespace-nowrap">{t(command.labelKey)}</span>
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
          if (cmd.items) {
            return (
              <DropdownMenuSub key={cmd.id}>
                <DropdownMenuSubTrigger disabled={cmd.disabled}>
                  {Icon && <Icon className="size-4" />}
                  <span>{t(cmd.labelKey)}</span>
                </DropdownMenuSubTrigger>
                <DropdownMenuSubContent>
                  {cmd.items.map((item) => {
                    const ItemIcon = item.icon;
                    return (
                      <DropdownMenuItem key={item.id} disabled={item.disabled} onClick={item.onClick}>
                        {ItemIcon && <ItemIcon className="size-4" />}
                        <span>{t(item.labelKey)}</span>
                      </DropdownMenuItem>
                    );
                  })}
                </DropdownMenuSubContent>
              </DropdownMenuSub>
            );
          }
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

// ── Internal: bar ─────────────────────────────────────────────────────────────

interface NewStyleProps {
  commands: AppCommandBarCommand[];
  selectionCount?: number;
  className?: string;
  trailing?: ReactNode;
  t: (key: string) => string;
}

function AppCommandBarNewStyle({ commands, selectionCount, className, trailing, t }: NewStyleProps) {
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
          "flex h-10 items-center border-b-2 border-b-border bg-surface-page",
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
          const button = (
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
          // UI-PERM-005: a command disabled for a reason says why, as primary and secondary commands do.
          if (!command.disabled || !command.disabledReasonKey) return button;
          return (
            <Tooltip key={command.id}>
              <TooltipTrigger render={button} />
              <TooltipContent side="bottom">{t(command.disabledReasonKey)}</TooltipContent>
            </Tooltip>
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

        {trailing && <div className="flex shrink-0 items-center gap-2 pr-2">{trailing}</div>}

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

export function AppCommandBar({ commands, selectionCount, className, trailing }: AppCommandBarProps) {
  const { t } = useL();

  if (commands.length === 0 && trailing == null) return null;

  return (
    <AppCommandBarNewStyle
      commands={commands}
      selectionCount={selectionCount}
      className={className}
      trailing={trailing}
      t={t}
    />
  );
}
