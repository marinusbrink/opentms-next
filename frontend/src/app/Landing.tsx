import { useEffect } from "react";
import { useNavigate } from "@tanstack/react-router";
import { useAuth } from "react-oidc-context";
import { Button } from "@/components/ui/button";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { FullScreenSpinner } from "@/app/shell/FullScreenSpinner";

/* The root route: no welcome step. A signed-in user goes on to the dashboard, anyone else straight to the auth
 * server's sign-in page. Only an auth error stops here, with a button, so a failing sign-in cannot loop. */
export function Landing() {
  const auth = useAuth();
  const { t } = useL();
  const navigate = useNavigate();

  useEffect(() => {
    if (auth.isAuthenticated) {
      void navigate({ to: "/dashboard" });
    } else if (!auth.isLoading && !auth.activeNavigator && !auth.error) {
      void auth.signinRedirect();
    }
  }, [auth, navigate]);

  if (auth.error) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-4 bg-neutral-100">
        <p className="text-sm text-destructive">{auth.error.message}</p>
        <Button variant="outline" onClick={() => void auth.signinRedirect()}>
          {t("Shell:SignIn")}
        </Button>
      </div>
    );
  }

  return <FullScreenSpinner />;
}
