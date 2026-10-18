import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from './locales/en'
import fa from './locales/fa'

export type SupportedLanguage = 'fa-IR' | 'en'

export const defaultLanguage: SupportedLanguage = 'fa-IR'
export const languageStorageKey = 'hadron.erp.language'

export function normalizeLanguage(language?: string | null): SupportedLanguage {
  return language?.toLowerCase().startsWith('en') ? 'en' : 'fa-IR'
}

export function languageDirection(language?: string | null): 'rtl' | 'ltr' {
  return normalizeLanguage(language) === 'fa-IR' ? 'rtl' : 'ltr'
}

function savedLanguage(): SupportedLanguage {
  try {
    const saved = window.localStorage.getItem(languageStorageKey)
    return saved === 'en' || saved === 'fa-IR' ? saved : defaultLanguage
  } catch {
    return defaultLanguage
  }
}

function applyDocumentLanguage(language?: string | null) {
  const normalized = normalizeLanguage(language)
  document.documentElement.lang = normalized
  document.documentElement.dir = languageDirection(normalized)
}

void i18n.use(initReactI18next).init({
  resources: {
    'fa-IR': { translation: fa },
    en: { translation: en },
  },
  lng: savedLanguage(),
  fallbackLng: defaultLanguage,
  supportedLngs: ['fa-IR', 'en'],
  nonExplicitSupportedLngs: false,
  interpolation: { escapeValue: false },
  initAsync: false,
})

applyDocumentLanguage(i18n.resolvedLanguage ?? i18n.language)

i18n.on('languageChanged', (language) => {
  const normalized = normalizeLanguage(language)
  try {
    window.localStorage.setItem(languageStorageKey, normalized)
  } catch {
    // The language still changes for this session when storage is unavailable.
  }
  applyDocumentLanguage(normalized)
})

export default i18n
