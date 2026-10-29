/** Two letters for an avatar without a picture: first name and surname, else the start of the user name. */
export function initialsOf(name?: string | null, surname?: string | null, userName?: string | null): string {
  const letters = [name?.trim()[0], surname?.trim()[0]].filter(Boolean).join("");
  return (letters || (userName ?? "").trim().slice(0, 2)).toUpperCase() || "?";
}
