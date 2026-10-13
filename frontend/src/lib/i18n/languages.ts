import { useMemo } from "react";

import { useApplicationConfiguration } from "@/lib/abp/queries";

export interface LanguageOption {
  cultureName: string;
  displayName: string;
}

/** The languages the platform is configured with (ABP application configuration): the choices for a
 * record's preferred language (design 43: relation and driver). Adding a language is a PO decision. */
export function useLanguages(): LanguageOption[] {
  const { data } = useApplicationConfiguration();
  const languages = data?.localization?.languages;
  return useMemo(
    () =>
      (languages ?? [])
        .map((l) => ({ cultureName: l.cultureName ?? "", displayName: l.displayName ?? l.cultureName ?? "" }))
        .filter((l) => l.cultureName.length > 0),
    [languages],
  );
}
