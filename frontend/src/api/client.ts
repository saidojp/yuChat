export type ApiErrorBody = {
  title?: string
  detail?: string
  status?: number
  code?: string
  details?: unknown
}

export class ApiError extends Error {
  readonly status: number
  readonly code: string

  constructor(status: number, code: string, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
  }
}

/** In-memory access token: never persisted, so an XSS payload cannot read it from storage. */
let accessToken: string | null = null

export function setAccessToken(token: string | null) {
  accessToken = token
}

export function getAccessToken() {
  return accessToken
}

type TokenListener = (token: string | null) => void
const listeners = new Set<TokenListener>()

export function onAccessTokenChange(listener: TokenListener) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

function applyToken(token: string | null) {
  accessToken = token
  listeners.forEach((listener) => listener(token))
}

let refreshPromise: Promise<boolean> | null = null

/** Rotates the refresh cookie for a new access token. Concurrent callers share one request. */
export async function refreshSession(): Promise<boolean> {
  refreshPromise ??= (async () => {
    try {
      const response = await fetch('/api/auth/refresh', {
        method: 'POST',
        credentials: 'include',
        headers: { Accept: 'application/json' },
      })

      if (!response.ok) {
        applyToken(null)
        return false
      }

      const body = (await response.json()) as { accessToken: string }
      applyToken(body.accessToken)
      return true
    } catch {
      applyToken(null)
      return false
    } finally {
      refreshPromise = null
    }
  })()

  return refreshPromise
}

async function parseError(response: Response): Promise<ApiError> {
  let body: ApiErrorBody = {}

  try {
    body = (await response.json()) as ApiErrorBody
  } catch {
    // Non-JSON error bodies fall back to the status code.
  }

  return new ApiError(
    response.status,
    body.code ?? 'generic',
    body.detail ?? body.title ?? `Request failed with status ${response.status}`,
  )
}

type RequestOptions = {
  method?: string
  body?: unknown
  /** Skip the automatic refresh-and-retry cycle (used by the auth endpoints themselves). */
  skipAuthRetry?: boolean
  signal?: AbortSignal
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, skipAuthRetry = false, signal } = options

  const send = async (): Promise<Response> =>
    fetch(path, {
      method,
      credentials: 'include',
      signal,
      headers: {
        Accept: 'application/json',
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    })

  let response = await send()

  if (response.status === 401 && !skipAuthRetry) {
    const refreshed = await refreshSession()

    if (refreshed) {
      response = await send()
    }
  }

  if (!response.ok) {
    throw await parseError(response)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

export const api = {
  // auth
  register: (login: string, password: string) =>
    apiRequest<{ accessToken: string; accessTokenExpiresAt: string; user: import('./types').UserProfile }>(
      '/api/auth/register',
      { method: 'POST', body: { login, password }, skipAuthRetry: true },
    ),

  login: (login: string, password: string) =>
    apiRequest<{ accessToken: string; accessTokenExpiresAt: string; user: import('./types').UserProfile }>(
      '/api/auth/login',
      { method: 'POST', body: { login, password }, skipAuthRetry: true },
    ),

  logout: () => apiRequest<void>('/api/auth/logout', { method: 'POST', skipAuthRetry: true }),

  me: () => apiRequest<import('./types').UserProfile>('/api/users/me'),

  setHandle: (userHandle: string) =>
    apiRequest<import('./types').UserProfile>('/api/users/handle', { method: 'POST', body: { userHandle } }),

  searchUsers: (query: string, signal?: AbortSignal) =>
    apiRequest<import('./types').UserSearchResult[]>(`/api/users/search?query=${encodeURIComponent(query)}`, {
      signal,
    }),

  // friends
  friends: () => apiRequest<import('./types').FriendsOverview>('/api/friends'),

  sendFriendRequest: (userHandle: string) =>
    apiRequest<import('./types').Friendship>('/api/friends/request', { method: 'POST', body: { userHandle } }),

  respondFriendRequest: (friendshipId: string, action: 'accept' | 'decline') =>
    apiRequest<import('./types').Friendship>(`/api/friends/${friendshipId}/respond`, {
      method: 'POST',
      body: { action },
    }),

  removeFriend: (friendshipId: string) =>
    apiRequest<void>(`/api/friends/${friendshipId}`, { method: 'DELETE' }),

  // chats
  chats: () => apiRequest<import('./types').ChatSummary[]>('/api/chats'),

  chat: (chatId: string) => apiRequest<import('./types').ChatDetails>(`/api/chats/${chatId}`),

  startDirect: (targetUserId: string) =>
    apiRequest<import('./types').ChatSummary>('/api/chats/direct', { method: 'POST', body: { targetUserId } }),

  createGroup: (payload: {
    name: string
    friendIds: string[]
    additionalUsers: { userHandle: string; alsoSendFriendRequest: boolean }[]
  }) => apiRequest<import('./types').GroupCreationResult>('/api/chats/group', { method: 'POST', body: payload }),

  messages: (chatId: string, beforeSentAt?: string, limit = 30) => {
    const params = new URLSearchParams({ limit: String(limit) })
    if (beforeSentAt) params.set('beforeSentAt', beforeSentAt)
    return apiRequest<import('./types').MessagePage>(`/api/chats/${chatId}/messages?${params.toString()}`)
  },

  markRead: (chatId: string, lastReadMessageId: string) =>
    apiRequest<import('./types').MessageRead>(`/api/chats/${chatId}/read`, {
      method: 'POST',
      body: { lastReadMessageId },
    }),

  invitations: () => apiRequest<import('./types').ChatInvitation[]>('/api/chats/invitations'),

  respondInvitation: (invitationId: string, action: 'accept' | 'decline') =>
    apiRequest<import('./types').ChatSummary>(`/api/chats/invitations/${invitationId}/respond`, {
      method: 'POST',
      body: { action },
    }),
}
