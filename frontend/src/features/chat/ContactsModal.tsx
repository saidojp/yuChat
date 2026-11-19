import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Trash2, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { api, ApiError } from '../../api/client'
import type { UserSearchResult } from '../../api/types'
import { Modal } from '../../components/Modal'

type Tab = 'friends' | 'incoming' | 'outgoing'

/**
 * Contacts = the former friends list merged with user search. Rows are clickable: opening a
 * conversation is the primary action, so there is no separate "Message" button.
 */
export function ContactsModal({
  onClose,
  onChatStarted,
}: {
  onClose: () => void
  onChatStarted: (chatId: string) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const [tab, setTab] = useState<Tab>('friends')
  const [query, setQuery] = useState('')
  const [debounced, setDebounced] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [confirmingId, setConfirmingId] = useState<string | null>(null)

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(query.trim()), 250)
    return () => window.clearTimeout(timer)
  }, [query])

  const searching = debounced.length >= 2

  const overview = useQuery({ queryKey: ['friends'], queryFn: api.friends })

  const results = useQuery({
    queryKey: ['user-search', debounced],
    queryFn: ({ signal }) => api.searchUsers(debounced, signal),
    enabled: searching,
    staleTime: 10_000,
  })

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['friends'] })
    void queryClient.invalidateQueries({ queryKey: ['user-search'] })
    void queryClient.invalidateQueries({ queryKey: ['chats'] })
  }

  const startChat = useMutation({
    mutationFn: (userId: string) => api.startDirect(userId),
    onSuccess: (chat) => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
      onChatStarted(chat.id)
      onClose()
    },
    onError: (caught) => setError(t(`errors.${caught instanceof ApiError ? caught.code : 'generic'}`, { defaultValue: t('errors.generic') })),
  })

  const respond = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'accept' | 'decline' }) =>
      api.respondFriendRequest(id, action),
    onSuccess: invalidate,
  })

  const addFriend = useMutation({
    mutationFn: (userHandle: string) => api.sendFriendRequest(userHandle),
    onSuccess: invalidate,
    onError: (caught) => setError(t(`errors.${caught instanceof ApiError ? caught.code : 'generic'}`, { defaultValue: t('errors.generic') })),
  })

  const removeFriend = useMutation({
    mutationFn: (id: string) => api.removeFriend(id),
    onSuccess: () => {
      setConfirmingId(null)
      invalidate()
    },
  })

  const data = overview.data
  const items =
    tab === 'friends' ? (data?.friends ?? []) : tab === 'incoming' ? (data?.incoming ?? []) : (data?.outgoing ?? [])

  const tabs: { key: Tab; label: string; count: number }[] = [
    { key: 'friends', label: t('contacts.friendsTab'), count: data?.friends.length ?? 0 },
    { key: 'incoming', label: t('contacts.incomingTab'), count: data?.incoming.length ?? 0 },
    { key: 'outgoing', label: t('contacts.outgoingTab'), count: data?.outgoing.length ?? 0 },
  ]

  return (
    <Modal title={t('contacts.title')} onClose={onClose}>
      <input
        value={query}
        onChange={(event) => {
          setQuery(event.target.value)
          setError(null)
        }}
        placeholder={t('contacts.searchPlaceholder')}
        className="w-full rounded-lg border border-ink-200 px-3 py-2 text-sm outline-none transition placeholder:text-ink-400 focus:border-ink-400"
      />

      {error ? <p className="mt-2 rounded-lg bg-ink-100 px-3 py-2 text-xs text-ink-700">{error}</p> : null}

      {searching ? (
        <div className="mt-3 space-y-0.5">
          {results.isFetching ? <p className="px-1 py-2 text-xs text-ink-400">{t('common.loading')}</p> : null}

          {!results.isFetching && results.data?.length === 0 ? (
            <p className="px-1 py-2 text-xs text-ink-400">{t('contacts.noResults')}</p>
          ) : null}

          {results.data?.map((user) => (
            <SearchResultRow
              key={user.id}
              user={user}
              busy={addFriend.isPending || startChat.isPending}
              onAdd={() => addFriend.mutate(user.userHandle)}
              onOpen={() => startChat.mutate(user.id)}
            />
          ))}
        </div>
      ) : (
        <>
          <div className="mb-3 mt-3 flex gap-1 rounded-lg bg-ink-100 p-0.5">
            {tabs.map((entry) => (
              <button
                key={entry.key}
                type="button"
                onClick={() => {
                  setTab(entry.key)
                  setConfirmingId(null)
                }}
                className={`flex-1 rounded-md px-2 py-1.5 text-[11px] font-medium transition ${
                  tab === entry.key ? 'bg-white text-ink-900 shadow-sm' : 'text-ink-500 hover:text-ink-800'
                }`}
              >
                {entry.label}
                {entry.count > 0 ? <span className="ml-1 text-ink-400">{entry.count}</span> : null}
              </button>
            ))}
          </div>

          {items.length === 0 ? <p className="py-6 text-center text-xs text-ink-400">{t('contacts.empty')}</p> : null}

          <ul className="space-y-0.5">
            {items.map((friendship) => {
              const isFriend = tab === 'friends'
              const confirming = confirmingId === friendship.id

              return (
                <li key={friendship.id}>
                  {isFriend ? (
                    <div className="flex items-center gap-1 rounded-lg pr-1 transition hover:bg-ink-50">
                      <button
                        type="button"
                        onClick={() => startChat.mutate(friendship.user.id)}
                        disabled={startChat.isPending}
                        className="flex min-w-0 flex-1 items-center gap-2 px-2 py-2 text-left disabled:opacity-60"
                      >
                        <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-ink-900 text-[11px] font-semibold text-white">
                          {friendship.user.userHandle.slice(0, 2).toUpperCase()}
                        </div>
                        <span className="truncate text-[13px] font-medium">@{friendship.user.userHandle}</span>
                      </button>

                      <button
                        type="button"
                        onClick={() =>
                          confirming ? removeFriend.mutate(friendship.id) : setConfirmingId(friendship.id)
                        }
                        onBlur={() => setConfirmingId(null)}
                        disabled={removeFriend.isPending}
                        title={confirming ? t('contacts.confirmRemove') : t('contacts.removeHint')}
                        aria-label={confirming ? t('contacts.confirmRemove') : t('contacts.remove')}
                        className={`flex h-7 shrink-0 items-center gap-1 rounded-md px-1.5 text-[10px] transition disabled:opacity-50 ${
                          confirming
                            ? 'bg-ink-900 text-white hover:bg-ink-800'
                            : 'text-ink-400 hover:bg-ink-100 hover:text-ink-700'
                        }`}
                      >
                        <Trash2 size={12} />
                        {confirming ? <span className="whitespace-nowrap">{t('contacts.confirmRemove')}</span> : null}
                      </button>
                    </div>
                  ) : (
                    <div className="flex items-center gap-2 rounded-lg px-2 py-2 hover:bg-ink-50">
                      <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-ink-900 text-[11px] font-semibold text-white">
                        {friendship.user.userHandle.slice(0, 2).toUpperCase()}
                      </div>

                      <div className="min-w-0 flex-1">
                        <p className="truncate text-[13px] font-medium">@{friendship.user.userHandle}</p>
                        {tab === 'outgoing' ? <p className="text-[11px] text-ink-400">{t('contacts.pending')}</p> : null}
                      </div>

                      {tab === 'incoming' ? (
                        <div className="flex gap-1">
                          <button
                            type="button"
                            onClick={() => respond.mutate({ id: friendship.id, action: 'accept' })}
                            disabled={respond.isPending}
                            aria-label={t('contacts.accept')}
                            className="rounded-lg bg-ink-900 p-1.5 text-white transition hover:bg-ink-800 disabled:opacity-50"
                          >
                            <Check size={12} />
                          </button>
                          <button
                            type="button"
                            onClick={() => respond.mutate({ id: friendship.id, action: 'decline' })}
                            disabled={respond.isPending}
                            aria-label={t('contacts.decline')}
                            className="rounded-lg border border-ink-200 p-1.5 transition hover:border-ink-300 disabled:opacity-50"
                          >
                            <X size={12} />
                          </button>
                        </div>
                      ) : null}
                    </div>
                  )}
                </li>
              )
            })}
          </ul>
        </>
      )}

      {!searching ? <p className="mt-3 px-1 text-[10px] text-ink-400">{t('contacts.hint')}</p> : null}
    </Modal>
  )
}

function SearchResultRow({
  user,
  busy,
  onAdd,
  onOpen,
}: {
  user: UserSearchResult
  busy: boolean
  onAdd: () => void
  onOpen: () => void
}) {
  const { t } = useTranslation()

  const label =
    user.friendship === 'Friends'
      ? t('search.friends')
      : user.friendship === 'PendingSent'
        ? t('search.requested')
        : user.friendship === 'PendingReceived'
          ? t('search.pendingReceived')
          : null

  return (
    <div className="flex items-center gap-2 rounded-lg px-2 py-2 hover:bg-ink-50">
      <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-ink-900 text-[11px] font-semibold text-white">
        {user.userHandle.slice(0, 2).toUpperCase()}
      </div>

      <div className="min-w-0 flex-1">
        <p className="truncate text-[13px] font-medium">@{user.userHandle}</p>
        {label ? <p className="text-[11px] text-ink-400">{label}</p> : null}
      </div>

      {user.friendship === 'Friends' ? (
        <button
          type="button"
          onClick={onOpen}
          disabled={busy}
          className="rounded-lg bg-ink-900 px-2.5 py-1.5 text-[11px] font-medium text-white transition hover:bg-ink-800 disabled:opacity-50"
        >
          {t('search.openChat')}
        </button>
      ) : user.friendship === 'None' ? (
        <button
          type="button"
          onClick={onAdd}
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
