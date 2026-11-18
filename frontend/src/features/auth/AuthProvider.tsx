import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'

import { api, onAccessTokenChange, refreshSession, setAccessToken } from '../../api/client'
import type { UserProfile } from '../../api/types'
import { AuthContext, type AuthContextValue, type AuthState } from './useAuth'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ user: null, status: 'loading' })

  const bootstrap = useCallback(async () => {
    // The access token lives only in memory, so a page reload needs a refresh round-trip.
    const refreshed = await refreshSession()

    if (!refreshed) {
      setState({ user: null, status: 'anonymous' })
      return
    }

    try {
      const user = await api.me()
      setState({ user, status: 'ready' })
    } catch {
      setAccessToken(null)
      setState({ user: null, status: 'anonymous' })
    }
  }, [])

  useEffect(() => {
    void bootstrap()
  }, [bootstrap])

  useEffect(
    () =>
      onAccessTokenChange((token) => {
        if (!token) {
          setState((current) => (current.status === 'anonymous' ? current : { user: null, status: 'anonymous' }))
        }
      }),
    [],
  )

  const login = useCallback(async (loginValue: string, password: string) => {
    const result = await api.login(loginValue, password)
    setAccessToken(result.accessToken)
    setState({ user: result.user, status: 'ready' })
    return result.user
  }, [])

  const register = useCallback(async (loginValue: string, password: string) => {
    const result = await api.register(loginValue, password)
    setAccessToken(result.accessToken)
    setState({ user: result.user, status: 'ready' })
    return result.user
  }, [])

  const logout = useCallback(async () => {
    try {
      await api.logout()
    } finally {
      setAccessToken(null)
      setState({ user: null, status: 'anonymous' })
    }
  }, [])

  const setUser = useCallback((user: UserProfile) => setState({ user, status: 'ready' }), [])

  const value = useMemo<AuthContextValue>(
    () => ({ ...state, login, register, logout, setUser }),
    [state, login, register, logout, setUser],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
