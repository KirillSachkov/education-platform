import { ru } from "./locales/ru";

type LocaleMap = Record<string, Record<string, string>>;

const LOCALE_MAP: LocaleMap = {
  ru,
  // Future: import { en } from "./locales/en";
};

const DEFAULT_LOCALE = "ru";

let currentLocale = DEFAULT_LOCALE;

export function setLocale(locale: string): void {
  if (LOCALE_MAP[locale]) {
    currentLocale = locale;
  }
}

export function getLocale(): string {
  return currentLocale;
}

/**
 * Translate an error code to a localized message.
 * Falls back to `fallback` if no translation exists for the current locale.
 */
export function t(code: string, fallback: string): string {
  return LOCALE_MAP[currentLocale]?.[code] ?? fallback;
}
