import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'

import { LanguageSwitcher } from './components/LanguageSwitcher'
import { AuthPage } from './features/auth/AuthPage'
import { AuthProvider } from './features/auth/AuthProvider'
import { useAuth } from './features/auth/useAuth'
import { WorkspacePage } from './features/chat/WorkspacePage'
import { OnboardingPage } from './features/onboarding/OnboardingPage'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (failureCount, error) => {
        const status = (error as { status?: number }).status
        if (status === 401 || status === 403 || status === 404) return false
        return failureCount < 2
      },
      staleTime: 10_000,
    },
  },
})

function RequireAuth({ children }: { children: React.ReactNode }) {
  const { status } = useAuth()

  if (status === 'loading') {
    return <SplashScreen />
  }

  if (status === 'anonymous') {
    return <Navigate to="/login" replace />
  }

  return <>{children}</>
}

function RedirectIfAuthed({ children }: { children: React.ReactNode }) {
  const { status } = useAuth()

  if (status === 'loading') {
    return <SplashScreen />
  }

  if (status === 'ready') {
    return <Navigate to="/app" replace />
  }

  return <>{children}</>
}

function SplashScreen() {
  return (
    <div className="flex h-screen items-center justify-center bg-ink-50">
      <div className="flex items-center gap-2 text-xs text-ink-400">
        <span className="h-3 w-3 animate-spin rounded-full border border-ink-300 border-t-ink-700" />
        <LanguageSwitcher compact />
      </div>
    </div>
  )
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <Routes>
            <Route
              path="/login"
              element={
                <RedirectIfAuthed>
                  <AuthPage />
                </RedirectIfAuthed>
              }
            />

            <Route
              path="/onboarding"
              element={
                <RequireAuth>
                  <OnboardingPage />
                </RequireAuth>
              }
            />

            <Route
              path="/app"
              element={
                <RequireAuth>
                  <WorkspacePage />
                </RequireAuth>
              }
            />

            <Route
              path="/app/c/:chatId"
              element={
                <RequireAuth>
                  <WorkspacePage />
                </RequireAuth>
              }
            />

            <Route path="*" element={<Navigate to="/app" replace />} />
          </Routes>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
