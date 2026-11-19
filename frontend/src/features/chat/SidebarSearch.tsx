import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Search, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { api } from '../../api/client'
import type { ChatSearchResult, UserSearchResult } from '../../api/types'
import { Avatar } from './Avatar'

/**
 * Telegram-style search bar: one input, results replace the chat list while a query is active.
 * Searches people (any handle) and conversations (group names, or the peer of your direct chats).
 */
export function SidebarSearch({
  onOpenChat,
  onClose,
}: {
  onOpenChat: (chatId: string) => void
  onClose: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [query, setQuery] = useState('')
  const [debounced, setDebounced] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(query.trim()), 200)
    return () => window.clearTimeout(timer)
  }, [query])

  const active = debounced.length >= 2

  const results = useQuery({
    queryKey: ['global-search', debounced],
    queryFn: ({ signal }) => api.search(debounced, signal),
    enabled: active,
    staleTime: 10_000,
    // Keep the previous hits on screen while the next query runs: the sidebar should never
    // collapse into a spinner between keystrokes.
    placeholderData: keepPreviousData,
  })

  const startChat = useMutation({
    mutationFn: (userId: string) => api.startDirect(userId),
    onSuccess: (chat) => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
      onOpenChat(chat.id)
      onClose()
    },
  })

  const addFriend = useMutation({
    mutationFn: (userHandle: string) => api.sendFriendRequest(userHandle),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['friends'] })
      void queryClient.invalidateQueries({ queryKey: ['global-search'] })
    },
  })

  const openChat = (chatId: string) => {
    onOpenChat(chatId)
    onClose()
  }

  const chats = results.data?.chats ?? []
  const users = results.data?.users ?? []
  const nothing = active && !results.isFetching && chats.length === 0 && users.length === 0

  return (
    <div className="px-2 pb-2">
      <div className="flex h-8 items-center gap-1.5 rounded-full bg-ink-100 px-2.5 transition focus-within:bg-white focus-within:ring-1 focus-within:ring-ink-200">
        <Search size={13} className="shrink-0 text-ink-400" />
        <input
          ref={inputRef}
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              if (query) setQuery('')
              else onClose()
            }
          }}
          placeholder={t('search.barPlaceholder')}
          className="min-w-0 flex-1 bg-transparent text-[12px] outline-none placeholder:text-ink-400"
        />
        {query ? (
          <button
            type="button"
            onClick={() => setQuery('')}
            aria-label={t('search.clear')}
            className="shrink-0 rounded-full p-0.5 text-ink-400 transition hover:text-ink-800"
          >
            <X size={12} />
          </button>
        ) : null}
      </div>

      {active ? (
        <div className="mt-2">
          {results.isFetching && chats.length === 0 && users.length === 0 ? (
            <p className="px-2 py-3 text-[11px] text-ink-400">{t('common.loading')}</p>
          ) : null}

          {nothing ? <p className="px-2 py-3 text-[11px] text-ink-400">{t('search.noResults')}</p> : null}

          {chats.length > 0 ? (
            <section className="mb-2">
              <p className="px-2 pb-1 text-[10px] font-semibold uppercase tracking-wider text-ink-400">
                {t('search.chatsSection')}
              </p>
              <ul className="space-y-0.5">
                {chats.map((chat) => (
                  <li key={chat.id}>
                    <ChatResultRow chat={chat} onClick={() => openChat(chat.id)} />
                  </li>
                ))}
              </ul>
            </section>
          ) : null}

          {users.length > 0 ? (
            <section>
              <p className="px-2 pb-1 text-[10px] font-semibold uppercase tracking-wider text-ink-400">
                {t('search.peopleSection')}
              </p>
              <ul className="space-y-0.5">
                {users.map((user) => (
                  <li key={user.id}>
                    <UserResultRow
                      user={user}
                      busy={startChat.isPending || addFriend.isPending}
                      onOpen={() => startChat.mutate(user.id)}
                      onAdd={() => addFriend.mutate(user.userHandle)}
                    />
                  </li>
                ))}
              </ul>
            </section>
          ) : null}
        </div>
      ) : (
        <p className="mt-2 px-2 text-[10px] text-ink-400">{t('search.hint')}</p>
      )}
    </div>
  )
}

function ChatResultRow({ chat, onClick }: { chat: ChatSearchResult; onClick: () => void }) {
  const { t } = useTranslation()

  return (
    <button
      type="button"
      onClick={onClick}
      className="flex w-full items-center gap-2.5 rounded-lg px-2 py-1.5 text-left transition hover:bg-ink-100/70"
    >
      <Avatar name={chat.title} group={chat.isGroup} size={26} />

      <div className="min-w-0 flex-1">
        <p className="truncate text-[12.5px] font-medium">{chat.isGroup ? chat.title : `@${chat.title}`}</p>
        {chat.isGroup ? (
          <p className="truncate text-[10.5px] text-ink-400">
            {t('chat.members', { count: chat.participants.length })}
          </p>
        ) : null}
      </div>
    </button>
  )
}

function UserResultRow({
  user,
  busy,
  onOpen,
  onAdd,
}: {
  user: UserSearchResult
  busy: boolean
  onOpen: () => void
  onAdd: () => void
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
    <div className="flex items-center gap-2 rounded-lg px-2 py-1.5 transition hover:bg-ink-100/70">
      <div className="flex h-[26px] w-[26px] shrink-0 items-center justify-center rounded-full bg-ink-900 text-[10px] font-semibold text-white">
        {user.userHandle.slice(0, 2).toUpperCase()}
      </div>

      <button
        type="button"
        onClick={user.friendship === 'Friends' ? onOpen : undefined}
        disabled={user.friendship !== 'Friends' || busy}
        className="min-w-0 flex-1 text-left"
      >
        <p className="truncate text-[12.5px] font-medium">@{user.userHandle}</p>
        {label ? <p className="truncate text-[10.5px] text-ink-400">{label}</p> : null}
      </button>

      {user.friendship === 'None' ? (
        <button
          type="button"
          onClick={onAdd}
          disabled={busy}
          className="shrink-0 rounded-md border border-ink-200 px-2 py-1 text-[10.5px] font-medium transition hover:border-ink-300 disabled:opacity-50"
        >
          {t('search.addFriend')}
        </button>
      ) : null}
    </div>
  )
}
