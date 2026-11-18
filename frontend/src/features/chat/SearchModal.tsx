import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { MessageSquare } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { api, ApiError } from '../../api/client'
import type { UserSearchResult } from '../../api/types'
import { Modal } from '../../components/Modal'

export function SearchModal({
  onClose,
  onChatStarted,
}: {
  onClose: () => void
  onChatStarted: (chatId: string) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [query, setQuery] = useState('')
  const [debounced, setDebounced] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(query.trim()), 250)
    return () => window.clearTimeout(timer)
  }, [query])

  const results = useQuery({
    queryKey: ['user-search', debounced],
    queryFn: ({ signal }) => api.searchUsers(debounced, signal),
    enabled: debounced.length >= 2,
    staleTime: 10_000,
  })

  const friend = useMutation({
    mutationFn: (userHandle: string) => api.sendFriendRequest(userHandle),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['friends'] })
      void queryClient.invalidateQueries({ queryKey: ['user-search'] })
    },
    onError: (caught) => {
      const code = caught instanceof ApiError ? caught.code : 'generic'
      setError(t(`errors.${code}`, { defaultValue: t('errors.generic') }))
    },
  })

  const startChat = useMutation({
    mutationFn: (userId: string) => api.startDirect(userId),
    onSuccess: (chat) => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
      onChatStarted(chat.id)
      onClose()
    },
    onError: (caught) => {
      const code = caught instanceof ApiError ? caught.code : 'generic'
      setError(t(`errors.${code}`, { defaultValue: t('errors.generic') }))
    },
  })

  return (
    <Modal title={t('search.title')} onClose={onClose}>
      <input
        value={query}
        onChange={(event) => setQuery(event.target.value)}
        autoFocus
        placeholder={t('search.placeholder')}
        className="w-full rounded-lg border border-ink-200 px-3 py-2 text-sm outline-none transition placeholder:text-ink-400 focus:border-ink-400"
      />

      {debounced.length < 2 ? <p className="mt-2 text-[11px] text-ink-400">{t('search.hint')}</p> : null}

      {error ? <p className="mt-2 rounded-lg bg-ink-100 px-3 py-2 text-xs text-ink-700">{error}</p> : null}

      <div className="mt-3 space-y-1">
        {results.isFetching ? <p className="px-1 py-2 text-xs text-ink-400">{t('common.loading')}</p> : null}

        {debounced.length >= 2 && !results.isFetching && results.data?.length === 0 ? (
          <p className="px-1 py-2 text-xs text-ink-400">{t('search.noResults')}</p>
        ) : null}

        {results.data?.map((user) => (
          <UserRow
            key={user.id}
            user={user}
            onAddFriend={() => friend.mutate(user.userHandle)}
            onOpenChat={() => startChat.mutate(user.id)}
            busy={friend.isPending || startChat.isPending}
          />
        ))}
      </div>
    </Modal>
  )
}

function UserRow({
  user,
  onAddFriend,
  onOpenChat,
  busy,
}: {
  user: UserSearchResult
  onAddFriend: () => void
  onOpenChat: () => void
  busy: boolean
}) {
  const { t } = useTranslation()

  return (
    <div className="flex items-center gap-2 rounded-lg px-2 py-2 hover:bg-ink-50">
      <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-ink-900 text-[11px] font-semibold text-white">
        {user.userHandle.slice(0, 2).toUpperCase()}
      </div>

      <div className="min-w-0 flex-1">
        <p className="truncate text-[13px] font-medium">@{user.userHandle}</p>
        <p className="text-[11px] text-ink-400">
          {user.friendship === 'Friends'
            ? t('search.friends')
            : user.friendship === 'PendingSent'
              ? t('search.requested')
              : user.friendship === 'PendingReceived'
                ? t('search.pendingReceived')
                : null}
        </p>
      </div>

      {user.friendship === 'Friends' ? (
        <button
          type="button"
          onClick={onOpenChat}
          disabled={busy}
          className="inline-flex items-center gap-1 rounded-lg bg-ink-900 px-2.5 py-1.5 text-[11px] font-medium text-white transition hover:bg-ink-800 disabled:opacity-50"
        >
          <MessageSquare size={11} />
          {t('search.openChat')}
        </button>
      ) : user.friendship === 'None' ? (
        <button
          type="button"
          onClick={onAddFriend}
          disabled={busy}
          className="rounded-lg border border-ink-200 px-2.5 py-1.5 text-[11px] font-medium transition hover:border-ink-300 disabled:opacity-50"
        >
          {t('search.addFriend')}
        </button>
      ) : (
        <span className="rounded-lg border border-ink-200 px-2.5 py-1.5 text-[11px] text-ink-400">
          {t('search.requested')}
        </span>
      )}
    </div>
  )
}
