import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import { useLocalizationData } from "@/lib/abp/queries";
import { setCultureProvider } from "@/lib/api/client";

/* Localization comes from ABP's application-localization endpoint — the same
 * resources the backend uses. There are NO hardcoded user-facing strings in this
 * frontend, shell and app names included: everything renders through t().
 * Unknown keys render as the key itself, which makes missing translations
 * visible instead of silent.
 * The culture is the user's own language (My settings, docs/designs/63-user-menu.md) when they chose one, else the
 * browser's. The choice is stored on the server; this browser keeps a copy so the next start renders in it at once. */

interface LocalizationContextValue {
  t: (key: string) => string;
  culture: string;
  isLoading: boolean;
  /** Applies the user's own language ("nl", "en"), or the browser's when null, and remembers it in this browser. */
  setUserLanguage: (language: string | null | undefined) => void;
}

const LocalizationContext = createContext<LocalizationContextValue | undefined>(undefined);

const USER_LANGUAGE_KEY = "opentms.userLanguage";
const CULTURES = ["nl", "en"];

function browserCulture(): string {
  return navigator.language?.toLowerCase().startsWith("nl") ? "nl" : "en";
}

function storedUserLanguage(): string | null {
  try {
    const value = window.localStorage.getItem(USER_LANGUAGE_KEY);
    return value && CULTURES.includes(value) ? value : null;
  } catch {
    return null;
  }
}

export function LocalizationProvider({ children }: { children: ReactNode }) {
  const [culture, setCulture] = useState(() => storedUserLanguage() ?? browserCulture());
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

  const setUserLanguage = useCallback((language: string | null | undefined) => {
    const chosen = language && CULTURES.includes(language) ? language : null;
    try {
      if (chosen) window.localStorage.setItem(USER_LANGUAGE_KEY, chosen);
      else window.localStorage.removeItem(USER_LANGUAGE_KEY);
    } catch {
      /* Storage unavailable: the language still applies to this page. */
    }
    setCulture(chosen ?? browserCulture());
  }, []);

  /* API calls carry the same culture, so server messages match the screen. */
  useEffect(() => {
    setCultureProvider(() => culture);
  }, [culture]);

  useEffect(() => {
    if (!isLoading) {
      document.title = t("AppName");
      document.documentElement.lang = culture;
    }
  }, [isLoading, t, culture]);

  return (
    <LocalizationContext.Provider value={{ t, culture, isLoading, setUserLanguage }}>
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
