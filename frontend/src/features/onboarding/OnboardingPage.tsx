import { useMutation } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useEffect, useState } from 'react'

import { api, ApiError } from '../../api/client'
import { LanguageSwitcher } from '../../components/LanguageSwitcher'
import { useAuth } from '../auth/useAuth'

export function OnboardingPage() {
  const { t } = useTranslation()
  const { user, setUser } = useAuth()
  const navigate = useNavigate()
  const [handle, setHandle] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (user && !user.needsOnboarding) {
      navigate('/app', { replace: true })
    }
  }, [user, navigate])

  const mutation = useMutation({
    mutationFn: (value: string) => api.setHandle(value),
    onSuccess: (updated) => {
      setUser(updated)
      navigate('/app', { replace: true })
    },
    onError: (caught) => {
      const code = caught instanceof ApiError ? caught.code : 'generic'
      setError(t(`errors.${code}`, { defaultValue: t('errors.generic') }))
    },
  })

  const isValid = /^[a-zA-Z0-9_]{4,24}$/.test(handle)

  return (
    <div className="flex min-h-screen items-center justify-center bg-ink-50 px-4">
      <div className="w-full max-w-sm rounded-xl border border-ink-200 bg-white p-6 shadow-sm">
        <div className="mb-4 flex items-center justify-between">
          <span className="text-sm font-semibold tracking-tight">{t('common.appName')}</span>
          <LanguageSwitcher compact />
        </div>
        <h1 className="text-lg font-semibold tracking-tight">{t('onboarding.title')}</h1>
        <p className="mt-2 text-xs leading-relaxed text-ink-500">{t('onboarding.description')}</p>

        <form
          className="mt-5 space-y-3"
          onSubmit={(event) => {
            event.preventDefault()
            setError(null)
            mutation.mutate(handle)
          }}
        >
          <div className="relative">
            <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-sm text-ink-400">@</span>
            <input
              value={handle}
              onChange={(event) => {
                setHandle(event.target.value.replace(/\s/g, ''))
                setError(null)
              }}
              maxLength={24}
              autoFocus
              autoCapitalize="none"
              spellCheck={false}
              placeholder={t('onboarding.placeholder')}
              className="w-full rounded-lg border border-ink-200 bg-white py-2 pl-7 pr-3 text-sm outline-none transition placeholder:text-ink-400 focus:border-ink-400"
            />
          </div>

          <div className="flex items-center justify-between text-[11px] text-ink-400">
            <span>{t('onboarding.hint')}</span>
            <span className={isValid ? 'text-emerald-600' : undefined}>
              {t('onboarding.characters', { count: handle.length })}
            </span>
          </div>

          {error ? (
            <p role="alert" className="rounded-lg bg-ink-100 px-3 py-2 text-xs text-ink-700">
              {error}
            </p>
          ) : null}

          <button
            type="submit"
            disabled={!isValid || mutation.isPending}
            className="w-full rounded-lg bg-ink-900 px-3 py-2 text-sm font-medium text-white transition hover:bg-ink-800 disabled:opacity-50"
          >
            {mutation.isPending ? t('common.loading') : t('onboarding.submit')}
          </button>
        </form>
      </div>
    </div>
  )
}
