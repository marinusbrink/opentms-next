import { Skeleton } from "@/components/ui/skeleton";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

interface OpenTmsGridFooterProps {
  totalCount: number | null;
  filteredCount: number | null;
  className?: string;
}

function formatNum(n: number, culture: string): string {
  return new Intl.NumberFormat(culture).format(n);
}

function fmt(template: string, ...values: string[]): string {
  return template.replace(/\{(\d+)\}/g, (_, i: string) => values[Number(i)] ?? "");
}

export function OpenTmsGridFooter({
  totalCount,
  filteredCount,
  className,
}: OpenTmsGridFooterProps) {
  const { t, culture } = useL();

  const isFiltered =
    filteredCount !== null && totalCount !== null && filteredCount < totalCount;

  return (
    <div
      className={cn(
        "flex items-center gap-4 border-t px-3 py-1.5 text-sm text-muted-foreground",
        className,
      )}
    >
      {totalCount === null ? (
        <Skeleton className="h-4 w-24" />
      ) : (
        <span>{fmt(t("Grid:Total"), formatNum(totalCount, culture))}</span>
      )}
      {isFiltered && filteredCount !== null && totalCount !== null && (
        <span>
          {fmt(
            t("Grid:Filtered"),
            formatNum(filteredCount, culture),
            formatNum(totalCount, culture),
          )}
        </span>
      )}
    </div>
  );
}
