import { useTranslation } from 'react-i18next'
import { Navigate, useNavigate } from 'react-router-dom'
import { useState, type FormEvent } from 'react'

import { ApiError } from '../../api/client'
import { LanguageSwitcher } from '../../components/LanguageSwitcher'
import { useAuth } from './useAuth'

export function AuthPage() {
  const { t } = useTranslation()
  const { login, register, status } = useAuth()
  const navigate = useNavigate()

  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [loginValue, setLoginValue] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  if (status === 'ready') {
    return <Navigate to="/app" replace />
  }

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    setSubmitting(true)

    try {
      const user = mode === 'login' ? await login(loginValue, password) : await register(loginValue, password)
      navigate(user.needsOnboarding ? '/onboarding' : '/app', { replace: true })
    } catch (caught) {
      const code = caught instanceof ApiError ? caught.code : 'generic'
      setError(t(code, { defaultValue: t('errors.generic') }))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="flex min-h-screen flex-col bg-ink-50">
      <header className="flex items-center justify-between px-6 py-5">
        <span className="text-sm font-semibold tracking-tight">{t('common.appName')}</span>
        <LanguageSwitcher />
      </header>

      <main className="flex flex-1 items-center justify-center px-4 pb-20">
        <div className="w-full max-w-sm">
          <div className="mb-8 text-center">
            <h1 className="text-2xl font-semibold tracking-tight">{t('auth.tagline')}</h1>
            <p className="mt-2 text-sm text-ink-500">{t('auth.subtitle')}</p>
          </div>

          <div className="rounded-xl border border-ink-200 bg-white p-1 shadow-sm">
            <div className="grid grid-cols-2 gap-1">
              {(['login', 'register'] as const).map((tab) => (
                <button
                  key={tab}
                  type="button"
                  onClick={() => {
                    setMode(tab)
                    setError(null)
                  }}
                  className={`rounded-lg px-3 py-2 text-sm font-medium transition ${
                    mode === tab ? 'bg-ink-900 text-white' : 'text-ink-600 hover:bg-ink-100'
                  }`}
                >
                  {tab === 'login' ? t('auth.loginTab') : t('auth.registerTab')}
                </button>
              ))}
            </div>

            <form onSubmit={onSubmit} className="space-y-3 p-4">
              <label className="block">
                <span className="mb-1.5 block text-xs font-medium text-ink-600">{t('auth.login')}</span>
                <input
                  value={loginValue}
                  onChange={(event) => setLoginValue(event.target.value)}
                  placeholder={t('auth.loginPlaceholder')}
                  autoComplete="username"
                  autoCapitalize="none"
                  spellCheck={false}
                  required
                  className="w-full rounded-lg border border-ink-200 bg-white px-3 py-2 text-sm outline-none transition placeholder:text-ink-400 focus:border-ink-400"
                />
              </label>

              <label className="block">
                <span className="mb-1.5 block text-xs font-medium text-ink-600">{t('auth.password')}</span>
                <input
                  type="password"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  placeholder={t('auth.passwordPlaceholder')}
                  autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
                  required
                  minLength={8}
                  className="w-full rounded-lg border border-ink-200 bg-white px-3 py-2 text-sm outline-none transition placeholder:text-ink-400 focus:border-ink-400"
                />
              </label>

              {error ? (
                <p role="alert" className="rounded-lg bg-ink-100 px-3 py-2 text-xs text-ink-700">
                  {error}
                </p>
              ) : null}

              <button
                type="submit"
                disabled={submitting}
                className="w-full rounded-lg bg-ink-900 px-3 py-2 text-sm font-medium text-white transition hover:bg-ink-800 disabled:opacity-60"
              >
                {submitting ? t('common.loading') : mode === 'login' ? t('auth.submitLogin') : t('auth.submitRegister')}
              </button>
            </form>
          </div>

          <p className="mt-4 text-center text-xs text-ink-500">
            {mode === 'login' ? t('auth.noAccount') : t('auth.haveAccount')}{' '}
            <button
              type="button"
              onClick={() => {
                setMode(mode === 'login' ? 'register' : 'login')
                setError(null)
              }}
              className="font-medium text-ink-900 underline-offset-2 hover:underline"
            >
              {mode === 'login' ? t('auth.registerTab') : t('auth.loginTab')}
            </button>
          </p>
        </div>
      </main>
    </div>
  )
}
