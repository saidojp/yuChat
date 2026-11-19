import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Check, Inbox, X } from 'lucide-react'
import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'

import { api } from '../../api/client'
import type { ChatSummary, Message } from '../../api/types'
import { SystemEventText } from './SystemEventText'

/**
 * The notifications conversation: a real chat room whose messages are system events.
 * Actionable cards (friend requests, group invitations) stay actionable only while the
 * corresponding row is still pending, so answering elsewhere instantly disables them.
 */
export function SystemChatView({ chat, onBack, onOpenChat }: { chat: ChatSummary; onBack: () => void; onOpenChat: (chatId: string) => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const bottomRef = useRef<HTMLDivElement>(null)

  const history = useQuery({
    queryKey: ['messages', chat.id],
    queryFn: () => api.messages(chat.id),
  })

  const friends = useQuery({ queryKey: ['friends'], queryFn: api.friends })
  const invitations = useQuery({ queryKey: ['invitations'], queryFn: api.invitations })

  const messages = history.data?.messages ?? []
  const pendingFriendRequests = new Map((friends.data?.incoming ?? []).map((item) => [item.id, item]))
  const pendingInvitations = new Map((invitations.data ?? []).map((item) => [item.id, item]))

  // Opening the feed is the equivalent of reading the chat.
  useEffect(() => {
    if (messages.length === 0) return

    const last = messages[messages.length - 1]
    void api.markRead(chat.id, last.id).then(() => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
    })
    // Only the newest event matters for the read watermark.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chat.id, messages.length])

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: 'end' })
  }, [messages.length])

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['friends'] })
    void queryClient.invalidateQueries({ queryKey: ['invitations'] })
    void queryClient.invalidateQueries({ queryKey: ['chats'] })
  }

  const respondFriend = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'accept' | 'decline' }) => api.respondFriendRequest(id, action),
    onSuccess: invalidate,
  })

  const respondInvitation = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'accept' | 'decline' }) => api.respondInvitation(id, action),
    onSuccess: (summary) => {
      invalidate()
      if (summary) onOpenChat(summary.id)
    },
  })

  return (
    <section className="flex h-full min-w-0 flex-1 flex-col bg-white">
      <header className="flex items-center gap-3 border-b border-ink-200 px-4 py-3">
        <button
          type="button"
          onClick={onBack}
          className="rounded-md p-1 text-ink-500 transition hover:bg-ink-100 md:hidden"
          aria-label={t('common.back')}
        >
          <ArrowLeft size={16} />
        </button>

        <div className="flex h-[34px] w-[34px] shrink-0 items-center justify-center rounded-full border border-ink-300 text-ink-600">
          <Inbox size={15} />
        </div>

        <div className="min-w-0">
          <h2 className="truncate text-sm font-semibold leading-tight">{t('system.title')}</h2>
          <p className="truncate text-[11px] leading-tight text-ink-400">{t('system.subtitle')}</p>
        </div>
      </header>

      <div className="flex-1 overflow-y-auto px-4 py-4 scrollbar-thin">
        {messages.length === 0 ? (
          <div className="flex h-full items-center justify-center">
            <p className="text-xs text-ink-400">{t('system.empty')}</p>
          </div>
        ) : (
          <ul className="space-y-2">
            {messages.map((message) => (
              <FeedItem
                key={message.id}
                message={message}
                pendingFriendRequest={message.systemEvent?.referenceId ? pendingFriendRequests.get(message.systemEvent.referenceId) : undefined}
                pendingInvitation={message.systemEvent?.referenceId ? pendingInvitations.get(message.systemEvent.referenceId) : undefined}
                onAcceptFriend={(id) => respondFriend.mutate({ id, action: 'accept' })}
                onDeclineFriend={(id) => respondFriend.mutate({ id, action: 'decline' })}
                onAcceptInvitation={(id) => respondInvitation.mutate({ id, action: 'accept' })}
                onDeclineInvitation={(id) => respondInvitation.mutate({ id, action: 'decline' })}
                onOpenChat={onOpenChat}
                busy={respondFriend.isPending || respondInvitation.isPending}
              />
            ))}
          </ul>
        )}

        <div ref={bottomRef} />
      </div>
    </section>
  )
}

function FeedItem({
  message,
  pendingFriendRequest,
  pendingInvitation,
  onAcceptFriend,
  onDeclineFriend,
  onAcceptInvitation,
  onDeclineInvitation,
  onOpenChat,
  busy,
}: {
  message: Message
  pendingFriendRequest?: { id: string; user: { userHandle: string } }
  pendingInvitation?: { id: string; chatTitle: string }
  onAcceptFriend: (id: string) => void
  onDeclineFriend: (id: string) => void
  onAcceptInvitation: (id: string) => void
  onDeclineInvitation: (id: string) => void
  onOpenChat: (chatId: string) => void
  busy: boolean
}) {
  const { t } = useTranslation()
  const event = message.systemEvent

  if (!event) return null

  const handle = event.actorHandle ?? '?'
  const initials = handle.slice(0, 2).toUpperCase()
  const time = new Date(message.sentAt).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })

  return (
    <li className="flex items-start gap-2.5">
      <div className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-ink-900 text-[10px] font-semibold text-white">
        {initials}
      </div>

      <div className="min-w-0 flex-1">
        <div className="rounded-xl rounded-tl-sm bg-ink-50 px-3 py-2">
          <p className="text-[12.5px] leading-snug text-ink-900">
            <SystemEventText event={event} />
          </p>

          {pendingFriendRequest ? (
            <div className="mt-2 flex gap-1.5">
              <button
                type="button"
                onClick={() => onAcceptFriend(pendingFriendRequest.id)}
                disabled={busy}
                className="inline-flex items-center gap-1 rounded-lg bg-ink-900 px-2 py-1 text-[11px] font-medium text-white transition hover:bg-ink-800 disabled:opacity-50"
              >
                <Check size={10} />
                {t('contacts.accept')}
              </button>
              <button
                type="button"
                onClick={() => onDeclineFriend(pendingFriendRequest.id)}
                disabled={busy}
                className="inline-flex items-center gap-1 rounded-lg border border-ink-200 bg-white px-2 py-1 text-[11px] transition hover:border-ink-300 disabled:opacity-50"
              >
                <X size={10} />
                {t('contacts.decline')}
              </button>
            </div>
          ) : null}

          {pendingInvitation ? (
            <div className="mt-2 flex gap-1.5">
              <button
                type="button"
                onClick={() => onAcceptInvitation(pendingInvitation.id)}
                disabled={busy}
                className="inline-flex items-center gap-1 rounded-lg bg-ink-900 px-2 py-1 text-[11px] font-medium text-white transition hover:bg-ink-800 disabled:opacity-50"
              >
                <Check size={10} />
                {t('system.join')}
              </button>
              <button
                type="button"
                onClick={() => onDeclineInvitation(pendingInvitation.id)}
                disabled={busy}
                className="inline-flex items-center gap-1 rounded-lg border border-ink-200 bg-white px-2 py-1 text-[11px] transition hover:border-ink-300 disabled:opacity-50"
              >
                <X size={10} />
                {t('system.decline')}
              </button>
            </div>
          ) : null}
        </div>

        <p className="mt-0.5 px-1 text-[10px] text-ink-400">{time}</p>
      </div>
    </li>
  )
}
