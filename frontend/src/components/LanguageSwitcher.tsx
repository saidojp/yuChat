import { useTranslation } from 'react-i18next'

import { languages, setLanguage, type LanguageCode } from '../i18n'

/**
 * Minimal language toggle: bare `en / jp` text, no chrome. The active language is emphasised,
 * the other stays reachable but quiet. Full names live in `title` for screen readers.
 */
export function LanguageSwitcher({ className = '' }: { className?: string }) {
  const { i18n } = useTranslation()
  const current = (i18n.language === 'ja' ? 'ja' : 'en') as LanguageCode

  return (
    <div className={`flex shrink-0 items-center gap-1 text-[10px] leading-none ${className}`}>
      {languages.map((language, index) => (
        <span key={language.code} className="flex items-center gap-1">
          {index > 0 ? <span className="text-ink-300">/</span> : null}
          <button
            type="button"
            onClick={() => setLanguage(language.code)}
            aria-pressed={current === language.code}
            title={language.label}
            className={`rounded transition-colors ${
              current === language.code
                ? 'font-semibold text-ink-900'
                : 'text-ink-400 hover:text-ink-700'
            }`}
          >
            {language.short}
          </button>
        </span>
      ))}
    </div>
  )
}
