import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/lib/api/client";
import { useL } from "@/lib/i18n/LocalizationProvider";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { cn } from "@/lib/utils";

interface RoleMultiSelectProps {
  value: string[];
  onChange: (value: string[]) => void;
  disabled?: boolean;
  className?: string;
}

export function RoleMultiSelect({ value, onChange, disabled, className }: RoleMultiSelectProps) {
  const { t } = useL();

  const { data: rolesData, isLoading } = useQuery({
    queryKey: ["administration", "roles", "all"],
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/platform/administration/roles", {
        params: { query: { startRow: 0, endRow: 200 } },
      });
      if (error) throw error;
      return data?.rows ?? [];
    },
    staleTime: 5 * 60 * 1000,
  });

  const roles = rolesData ?? [];

  const toggle = (roleName: string) => {
    if (value.includes(roleName)) {
      onChange(value.filter((n) => n !== roleName));
    } else {
      onChange([...value, roleName]);
    }
  };

  const displayLabel =
    value.length > 0
      ? value.join(", ")
      : t("Administration:SelectRoles");

  return (
    <Popover>
      <PopoverTrigger
        disabled={disabled}
        className={cn(
          "h-8 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 py-1 text-left text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:opacity-50",
          !value.length && "text-muted-foreground",
          className,
        )}
      >
        {displayLabel}
      </PopoverTrigger>
      <PopoverContent className="w-72 p-1" align="start">
        {isLoading && (
          <span className="block px-2 py-1.5 text-sm text-muted-foreground">
            {t("Administration:Loading")}
          </span>
        )}
        {!isLoading && roles.length === 0 && (
          <span className="block px-2 py-1.5 text-sm text-muted-foreground">
            {t("Administration:NoRoles")}
          </span>
        )}
        {roles.map((role) => {
          const name = role.name ?? "";
          const checked = value.includes(name);
          return (
            <label
              key={role.id}
              className="flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-muted"
            >
              <input
                type="checkbox"
                checked={checked}
                onChange={() => toggle(name)}
                className="size-4 accent-primary"
              />
              {name}
            </label>
          );
        })}
      </PopoverContent>
    </Popover>
  );
}
