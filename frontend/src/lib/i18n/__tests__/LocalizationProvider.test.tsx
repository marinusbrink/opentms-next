import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

/* The culture is the user's own language when chosen (docs/designs/63-user-menu.md), else the browser's. This browser
 * keeps a copy in localStorage ("opentms.userLanguage", only nl/en); setUserLanguage stores or removes it, updates the
 * culture, the texts asked for and the Accept-Language provider. Risk class: Medium. */

const localizationCultures: string[] = [];
vi.mock("@/lib/abp/queries", () => ({
  useLocalizationData: (culture: string) => {
    localizationCultures.push(culture);
    return { data: { resources: { OpenTms: { texts: { AppName: `OpenTMS-${culture}` } } } }, isLoading: false };
  },
}));

const setCultureProvider = vi.fn();
vi.mock("@/lib/api/client", () => ({
  setCultureProvider: (p: () => string | undefined) => setCultureProvider(p),
}));

import { LocalizationProvider, useL } from "@/lib/i18n/LocalizationProvider";

const KEY = "opentms.userLanguage";
let setLanguage: (language: string | null | undefined) => void = () => {};

function Probe() {
  const { culture, setUserLanguage, t } = useL();
  setLanguage = setUserLanguage;
  return (
    <div>
      <span data-testid="culture">{culture}</span>
      <span data-testid="title">{t("AppName")}</span>
    </div>
  );
}

const renderProvider = () =>
  render(
    <LocalizationProvider>
      <Probe />
    </LocalizationProvider>,
  );
const culture = () => screen.getByTestId("culture").textContent;
const currentProviderCulture = () => (setCultureProvider.mock.calls.at(-1)![0] as () => string | undefined)();
const browser = (language: string) => vi.spyOn(window.navigator, "language", "get").mockReturnValue(language);

beforeEach(() => {
  window.localStorage.clear();
  localizationCultures.length = 0;
  setCultureProvider.mockReset();
});
afterEach(() => vi.restoreAllMocks());

describe("LocalizationProvider culture", () => {
  it("uses the browser's language without a stored choice", () => {
    browser("nl-NL");
    renderProvider();
    expect(culture()).toBe("nl");
    expect(localizationCultures.at(-1)).toBe("nl");
  });

  it("falls back to English for a browser language other than Dutch", () => {
    browser("de-DE");
    renderProvider();
    expect(culture()).toBe("en");
  });

  it("a stored user language wins over the browser's", () => {
    browser("en-US");
    window.localStorage.setItem(KEY, "nl");
    renderProvider();
    expect(culture()).toBe("nl");
    expect(localizationCultures.at(-1)).toBe("nl");
    expect(document.documentElement.lang).toBe("nl");
  });

  it("ignores a stored value that is not a supported language", () => {
    browser("en-US");
    window.localStorage.setItem(KEY, "de");
    renderProvider();
    expect(culture()).toBe("en");
  });

  it("setUserLanguage stores the choice and switches culture, texts and Accept-Language", () => {
    browser("en-US");
    renderProvider();
    expect(currentProviderCulture()).toBe("en");

    act(() => setLanguage("nl"));

    expect(window.localStorage.getItem(KEY)).toBe("nl");
    expect(culture()).toBe("nl");
    expect(localizationCultures.at(-1)).toBe("nl");
    expect(screen.getByTestId("title")).toHaveTextContent("OpenTMS-nl");
    expect(currentProviderCulture()).toBe("nl");
    expect(document.documentElement.lang).toBe("nl");
  });

  it("setUserLanguage(null) removes the stored choice and goes back to the browser's language", () => {
    browser("en-US");
    window.localStorage.setItem(KEY, "nl");
    renderProvider();
    expect(culture()).toBe("nl");

    act(() => setLanguage(null));

    expect(window.localStorage.getItem(KEY)).toBeNull();
    expect(culture()).toBe("en");
    expect(currentProviderCulture()).toBe("en");
  });

  it("treats an unsupported language as no choice", () => {
    browser("nl-BE");
    window.localStorage.setItem(KEY, "en");
    renderProvider();

    act(() => setLanguage("fr"));

    expect(window.localStorage.getItem(KEY)).toBeNull();
    expect(culture()).toBe("nl");
  });

  it("still applies the language when storage is unavailable", () => {
    browser("en-US");
    renderProvider();
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("QuotaExceeded");
    });

    act(() => setLanguage("nl"));

    expect(culture()).toBe("nl");
  });
});
