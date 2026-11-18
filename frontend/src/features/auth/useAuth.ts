import { createContext, useContext } from 'react'

import type { UserProfile } from '../../api/types'

export type AuthState = {
  user: UserProfile | null
  status: 'loading' | 'ready' | 'anonymous'
}

export type AuthContextValue = AuthState & {
  login: (login: string, password: string) => Promise<UserProfile>
  register: (login: string, password: string) => Promise<UserProfile>
  logout: () => Promise<void>
  setUser: (user: UserProfile) => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth() {
  const context = useContext(AuthContext)

  if (!context) {
    throw new Error('useAuth must be used inside AuthProvider')
  }

  return context
}
