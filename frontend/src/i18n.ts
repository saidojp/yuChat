import { createInstance } from 'i18next'
import { initReactI18next } from 'react-i18next'

import en from './locales/en'
import ja from './locales/ja'

export const languages = [
  { code: 'en', label: 'English' },
  { code: 'ja', label: '日本語' },
] as const

export type LanguageCode = (typeof languages)[number]['code']

const storageKey = 'yuchat.language'

function detectLanguage(): LanguageCode {
  const stored = localStorage.getItem(storageKey)
  if (stored === 'en' || stored === 'ja') {
    return stored
  }

  // Default to Japanese only when the browser clearly prefers it.
  return navigator.language.toLowerCase().startsWith('ja') ? 'ja' : 'en'
}

const i18n = createInstance()

void i18n.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    ja: { translation: ja },
  },
  lng: detectLanguage(),
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
  returnNull: false,
})

export function setLanguage(code: LanguageCode) {
  localStorage.setItem(storageKey, code)
  void i18n.changeLanguage(code)
  document.documentElement.lang = code
}

document.documentElement.lang = i18n.language

export default i18n
