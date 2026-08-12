import { createContext, useCallback, useContext, useEffect, type ReactNode } from "react";
import { useLocalizationData } from "@/lib/abp/queries";

/* Localization comes from ABP's application-localization endpoint — the same
 * resources the backend uses. There are NO hardcoded user-facing strings in this
 * frontend, shell and app names included: everything renders through t().
 * Unknown keys render as the key itself, which makes missing translations
 * visible instead of silent. */

interface LocalizationContextValue {
  t: (key: string) => string;
  culture: string;
  isLoading: boolean;
}

const LocalizationContext = createContext<LocalizationContextValue | undefined>(undefined);

function detectCulture(): string {
  return navigator.language?.toLowerCase().startsWith("nl") ? "nl" : "en";
}

export function LocalizationProvider({ children }: { children: ReactNode }) {
  const culture = detectCulture();
  const { data, isLoading } = useLocalizationData(culture);

  const t = useCallback(
    (key: string): string => {
      const resources = data?.resources ?? {};
      // Product resource first, then any other resource that defines the key.
      const own = resources["OpenTms"]?.texts?.[key];
      if (own != null) {
        return own;
      }
      for (const resource of Object.values(resources)) {
        const text = resource?.texts?.[key];
        if (text != null) {
          return text;
        }
      }
      return key;
    },
    [data],
  );

  useEffect(() => {
    if (!isLoading) {
      document.title = t("AppName");
      document.documentElement.lang = culture;
    }
  }, [isLoading, t, culture]);

  return (
    <LocalizationContext.Provider value={{ t, culture, isLoading }}>
      {children}
    </LocalizationContext.Provider>
  );
}

export function useL(): LocalizationContextValue {
  const context = useContext(LocalizationContext);
  if (!context) {
    throw new Error("useL must be used within LocalizationProvider");
  }
  return context;
}
