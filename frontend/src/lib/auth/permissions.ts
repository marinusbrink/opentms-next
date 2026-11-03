import { useApplicationConfiguration } from "@/lib/abp/queries";

/* Permission and feature checks (UI-PERM-001, UI-PERM-006). Features never read
 * grantedPolicies or the feature values directly. A missing permission hides the
 * element; it never disables it (UI-PERM-002). */

export function useIsGranted(policy: string): boolean {
  const { data } = useApplicationConfiguration();
  const granted = data?.auth?.grantedPolicies as Record<string, boolean> | undefined;
  return granted?.[policy] === true;
}

/** For lists that check a permission per item (the app launcher): one subscription, a checker per policy. */
export function useIsGrantedChecker(): (policy: string) => boolean {
  const { data } = useApplicationConfiguration();
  const granted = data?.auth?.grantedPolicies as Record<string, boolean> | undefined;
  return (policy: string) => granted?.[policy] === true;
}

export function useIsFeatureEnabled(name: string): boolean {
  const { data } = useApplicationConfiguration();
  const values = data?.features?.values as Record<string, string | null> | undefined;
  return values?.[name] === "true";
}

/** A feature's state for a screen that is hidden behind it (UI-PERM-006): "loading" until the configuration is known, so
 *  the screen never flashes its unavailable state. */
export function useFeatureState(name: string): "loading" | "on" | "off" {
  const { data } = useApplicationConfiguration();
  if (data == null) return "loading";
  const values = data.features?.values as Record<string, string | null> | undefined;
  return values?.[name] === "true" ? "on" : "off";
}

/** True on the host side (no current tenant) once the configuration is known; false while loading. */
export function useIsHostSide(): boolean {
  const { data } = useApplicationConfiguration();
  return data != null && data.currentTenant?.id == null;
}

