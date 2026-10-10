/* Neutral loading state (no text: localization itself may still be loading). */
export function FullScreenSpinner() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-neutral-100" role="status">
      <div className="size-8 animate-spin rounded-full border-2 border-brand border-t-transparent" />
    </div>
  );
}
