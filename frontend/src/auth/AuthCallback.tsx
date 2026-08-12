import { useEffect } from "react";
import { useNavigate } from "@tanstack/react-router";
import { useAuth } from "react-oidc-context";
import { Button } from "@/components/ui/button";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { FullScreenSpinner } from "@/app/shell/FullScreenSpinner";

/* OIDC redirect target. react-oidc-context processes the code/state exchange
 * automatically; this view waits for the result and moves on. */
export function AuthCallback() {
  const auth = useAuth();
  const { t } = useL();
  const navigate = useNavigate();

  useEffect(() => {
    if (auth.isAuthenticated) {
      const returnTo = (auth.user?.state as { returnTo?: string } | undefined)?.returnTo;
      void navigate({ to: returnTo ?? "/dashboard" });
    }
  }, [auth.isAuthenticated, auth.user, navigate]);

  if (auth.error) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-4 bg-neutral-100">
        <p className="text-sm text-destructive">{auth.error.message}</p>
        <Button variant="outline" onClick={() => void navigate({ to: "/" })}>
          {t("Shell:SignIn")}
        </Button>
      </div>
    );
  }

  return <FullScreenSpinner />;
}
