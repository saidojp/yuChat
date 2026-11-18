import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, X } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'

import { api } from '../../api/client'
import { Modal } from '../../components/Modal'

type Tab = 'friends' | 'incoming' | 'outgoing'

export function FriendsModal({
  onClose,
  onChatStarted,
}: {
  onClose: () => void
  onChatStarted: (chatId: string) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [tab, setTab] = useState<Tab>('friends')

  const overview = useQuery({ queryKey: ['friends'], queryFn: api.friends })

  const respond = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'accept' | 'decline' }) =>
      api.respondFriendRequest(id, action),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['friends'] })
      void queryClient.invalidateQueries({ queryKey: ['notifications'] })
    },
  })

  const startChat = useMutation({
    mutationFn: (userId: string) => api.startDirect(userId),
    onSuccess: (chat) => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
      onChatStarted(chat.id)
      onClose()
    },
  })

  const data = overview.data
  const items = tab === 'friends' ? (data?.friends ?? []) : tab === 'incoming' ? (data?.incoming ?? []) : (data?.outgoing ?? [])

  const tabs: { key: Tab; label: string; count: number }[] = [
    { key: 'friends', label: t('friends.friendsTab'), count: data?.friends.length ?? 0 },
    { key: 'incoming', label: t('friends.incomingTab'), count: data?.incoming.length ?? 0 },
    { key: 'outgoing', label: t('friends.outgoingTab'), count: data?.outgoing.length ?? 0 },
  ]

  return (
    <Modal title={t('friends.title')} onClose={onClose}>
      <div className="mb-3 flex gap-1 rounded-lg bg-ink-100 p-0.5">
        {tabs.map((entry) => (
          <button
            key={entry.key}
            type="button"
            onClick={() => setTab(entry.key)}
            className={`flex-1 rounded-md px-2 py-1.5 text-[11px] font-medium transition ${
              tab === entry.key ? 'bg-white text-ink-900 shadow-sm' : 'text-ink-500 hover:text-ink-800'
            }`}
          >
            {entry.label}
            {entry.count > 0 ? <span className="ml-1 text-ink-400">{entry.count}</span> : null}
          </button>
        ))}
      </div>

      {items.length === 0 ? <p className="py-6 text-center text-xs text-ink-400">{t('friends.empty')}</p> : null}

      <ul className="space-y-1">
        {items.map((friendship) => (
          <li key={friendship.id} className="flex items-center gap-2 rounded-lg px-2 py-2 hover:bg-ink-50">
            <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-ink-900 text-[11px] font-semibold text-white">
              {friendship.user.userHandle.slice(0, 2).toUpperCase()}
            </div>

            <div className="min-w-0 flex-1">
              <p className="truncate text-[13px] font-medium">@{friendship.user.userHandle}</p>
              {tab === 'outgoing' ? <p className="text-[11px] text-ink-400">{t('friends.pending')}</p> : null}
            </div>

            {tab === 'friends' ? (
              <button
                type="button"
                onClick={() => startChat.mutate(friendship.user.id)}
                disabled={startChat.isPending}
                className="rounded-lg bg-ink-900 px-2.5 py-1.5 text-[11px] font-medium text-white transition hover:bg-ink-800 disabled:opacity-50"
              >
                {t('search.openChat')}
              </button>
            ) : tab === 'incoming' ? (
              <div className="flex gap-1">
                <button
                  type="button"
                  onClick={() => respond.mutate({ id: friendship.id, action: 'accept' })}
                  disabled={respond.isPending}
                  aria-label={t('friends.accept')}
                  className="rounded-lg bg-ink-900 p-1.5 text-white transition hover:bg-ink-800 disabled:opacity-50"
                >
                  <Check size={12} />
                </button>
                <button
                  type="button"
                  onClick={() => respond.mutate({ id: friendship.id, action: 'decline' })}
                  disabled={respond.isPending}
                  aria-label={t('friends.decline')}
                  className="rounded-lg border border-ink-200 p-1.5 transition hover:border-ink-300 disabled:opacity-50"
                >
                  <X size={12} />
                </button>
              </div>
            ) : null}
          </li>
        ))}
      </ul>
    </Modal>
  )
}
