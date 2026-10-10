import { Skeleton } from "@/components/ui/skeleton";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

interface OpenTmsGridFooterProps {
  totalCount: number | null;
  filteredCount: number | null;
  className?: string;
}

function fmt(template: string, ...values: string[]): string {
  return template.replace(/\{(\d+)\}/g, (_, i: string) => values[Number(i)] ?? "");
}

export function OpenTmsGridFooter({
  totalCount,
  filteredCount,
  className,
}: OpenTmsGridFooterProps) {
  const { t } = useL();
  const f = useFormat();

  const isFiltered =
    filteredCount !== null && totalCount !== null && filteredCount < totalCount;

  return (
    <div
      className={cn(
        "flex items-center gap-4 border-t bg-surface-page px-3 py-1.5 text-label text-muted-foreground tabular-nums",
        className,
      )}
    >
      {totalCount === null ? (
        <Skeleton className="h-4 w-24" />
      ) : (
        <span>{fmt(t("Grid:Total"), f.number(totalCount, { decimals: 0 }))}</span>
      )}
      {isFiltered && filteredCount !== null && totalCount !== null && (
        <span>
          {fmt(
            t("Grid:Filtered"),
            f.number(filteredCount, { decimals: 0 }),
            f.number(totalCount, { decimals: 0 }),
          )}
        </span>
      )}
    </div>
  );
}
