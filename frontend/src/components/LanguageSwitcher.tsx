import { useTranslation } from 'react-i18next'

import { languages, setLanguage, type LanguageCode } from '../i18n'

export function LanguageSwitcher({ compact = false }: { compact?: boolean }) {
  const { i18n } = useTranslation()
  const current = (i18n.language === 'ja' ? 'ja' : 'en') as LanguageCode

  return (
    <div
      className={`inline-flex shrink-0 items-center rounded-lg border border-ink-200 bg-white p-0.5 ${compact ? 'text-[10px]' : 'text-xs'}`}
    >
      {languages.map((language) => (
        <button
          key={language.code}
          type="button"
          onClick={() => setLanguage(language.code)}
          aria-pressed={current === language.code}
          className={`whitespace-nowrap rounded-md font-medium transition ${
            compact ? 'px-1.5 py-0.5' : 'px-2 py-1'
          } ${current === language.code ? 'bg-ink-900 text-white' : 'text-ink-500 hover:text-ink-900'}`}
        >
          {language.label}
        </button>
      ))}
    </div>
  )
}
