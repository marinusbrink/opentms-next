/* Count texts with a singular form (browser-run finding 6: "1 consignment notes will be printed"). A count text has
 * two keys in each language: `<key>` for the plural ("{0} trips archived") and `<key>.One` for exactly one ("1 trip
 * archived"). nl and en both only distinguish one from other, so no CLDR plural rules are needed. A missing `.One`
 * key falls back to the plural text. {0} is the count; {1}… are the further arguments. */

export function countText(t: (key: string) => string, key: string, count: number, ...args: string[]): string {
  const singularKey = `${key}.One`;
  const singular = count === 1 ? t(singularKey) : null;
  const template = singular && singular !== singularKey ? singular : t(key);
  return [String(count), ...args].reduce((text, value, index) => text.replaceAll(`{${index}}`, value), template);
}
