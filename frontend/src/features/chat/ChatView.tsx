import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Lock, Send, Users } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { api } from '../../api/client'
import type { ChatSummary, Message, MessageRead, PresenceChanged, TypingEvent } from '../../api/types'
import { chatConnection } from '../../realtime/connection'
import { useAuth } from '../auth/useAuth'
import { Avatar } from './Avatar'
import { MessageList } from './MessageList'

type PendingMessage = {
  clientMessageId: string
  content: string
  createdAt: string
  status: 'sending' | 'failed'
}

export function ChatView({
  chat,
  onBack,
  onOpenMembers,
}: {
  chat: ChatSummary | null
  onBack: () => void
  onOpenMembers: () => void
}) {
  const { t } = useTranslation()
  const { user } = useAuth()
  const queryClient = useQueryClient()

  const chatId = chat?.id ?? null

  const [draft, setDraft] = useState('')
  const [pending, setPending] = useState<PendingMessage[]>([])
  const [typingUsers, setTypingUsers] = useState<Map<string, { handle: string; until: number }>>(new Map())
  const [onlineUsers, setOnlineUsers] = useState<Set<string>>(new Set())
  const [loadingOlder, setLoadingOlder] = useState(false)

  const typingSentAt = useRef(0)
  const typingStopTimer = useRef<number | null>(null)
  const inputRef = useRef<HTMLTextAreaElement>(null)
  const scrollAnchor = useRef<HTMLDivElement>(null)

  const history = useQuery({
    queryKey: ['messages', chatId],
    queryFn: () => api.messages(chatId!),
    enabled: Boolean(chatId),
  })

  const messages = history.data?.messages ?? []
  const lastMessage = messages[messages.length - 1]

  // Clear local chat state when switching rooms.
  useEffect(() => {
    setPending([])
    setDraft('')
    setTypingUsers(new Map())

    if (chatId) {
      void chatConnection.joinChat(chatId)
      void chatConnection.onlineCoMembers().then((ids) => setOnlineUsers(new Set(ids)))
    }
  }, [chatId])

  // Read receipts go through the hub; the REST endpoint exists for clients without a live connection.
  useEffect(() => {
    if (!chatId || !lastMessage) return

    void chatConnection.markRead(chatId, lastMessage.id).then(() => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
    })
  }, [chatId, lastMessage, queryClient])

  // Realtime subscriptions scoped to the open chat.
  useEffect(() => {
    if (!chatId) return

    const typingTimers: number[] = []

    const receive = (event: Message) => {
      if (event.chatRoomId !== chatId) return

      queryClient.setQueryData<{ messages: Message[]; hasMore: boolean }>(['messages', chatId], (current) => {
        if (!current) {
          return { messages: [event], hasMore: false }
        }

        if (current.messages.some((message) => message.id === event.id)) {
          return current
        }

        return { ...current, messages: [...current.messages, event] }
      })

      setPending((current) => current.filter((message) => message.clientMessageId !== event.clientMessageId))
    }

    const typing = (event: TypingEvent) => {
      if (event.chatRoomId !== chatId) return

      setTypingUsers((current) => {
        const next = new Map(current)

        if (event.isTyping) {
          next.set(event.userId, { handle: event.userHandle, until: Date.now() + 5000 })
        } else {
          next.delete(event.userId)
        }

        return next
      })

      const timer = window.setTimeout(() => {
        setTypingUsers((current) => {
          const next = new Map(current)
          next.delete(event.userId)
          return next
        })
      }, 5000)

      typingTimers.push(timer)
    }

    const read = (event: MessageRead) => {
      if (event.chatRoomId === chatId) {
        void queryClient.invalidateQueries({ queryKey: ['messages', chatId] })
      }
    }

    const presenceSnapshot = (event: { onlineUserIds: string[] }) => {
      setOnlineUsers(new Set(event.onlineUserIds))
    }

    const presenceChanged = (event: PresenceChanged) => {
      setOnlineUsers((current) => {
        const next = new Set(current)
        if (event.isOnline) next.add(event.userId)
        else next.delete(event.userId)
        return next
      })
    }

    const offMessage = chatConnection.on('message', receive)
    const offTyping = chatConnection.on('typing', typing)
    const offRead = chatConnection.on('read', read)
    const offSnapshot = chatConnection.on('presenceSnapshot', presenceSnapshot)
    const offPresence = chatConnection.on('presence', presenceChanged)

    return () => {
      offMessage()
      offTyping()
      offRead()
      offSnapshot()
      offPresence()
      typingTimers.forEach((timer) => window.clearTimeout(timer))
    }
  }, [chatId, queryClient])

  const send = useCallback(async () => {
    const content = draft.trim()
    if (!content || !chatId) return

    const clientMessageId = crypto.randomUUID()
    const optimistic: PendingMessage = {
      clientMessageId,
      content,
      createdAt: new Date().toISOString(),
      status: 'sending',
    }

    setPending((current) => [...current, optimistic])
    setDraft('')

    try {
      const ack = await chatConnection.sendMessage(chatId, clientMessageId, content)

      queryClient.setQueryData<{ messages: Message[]; hasMore: boolean }>(['messages', chatId], (current) => {
        const base = current ?? { messages: [], hasMore: false }

        if (base.messages.some((message) => message.id === ack.id)) {
          return base
        }

        return {
          ...base,
          messages: [
            ...base.messages,
            {
              id: ack.id,
              chatRoomId: ack.chatRoomId,
              senderId: user!.id,
              senderHandle: user!.userHandle ?? user!.login,
              content,
              clientMessageId: ack.clientMessageId,
              sentAt: ack.sentAt,
            },
          ],
        }
      })

      setPending((current) => current.filter((message) => message.clientMessageId !== clientMessageId))
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
    } catch {
      setPending((current) =>
        current.map((message) =>
          message.clientMessageId === clientMessageId ? { ...message, status: 'failed' } : message,
        ),
      )
    }
  }, [draft, chatId, queryClient, user])

  const onDraftChange = (value: string) => {
    setDraft(value)

    if (!chatId) return

    const now = Date.now()
    if (now - typingSentAt.current > 2000) {
      typingSentAt.current = now
      void chatConnection.typing(chatId, true)
    }

    if (typingStopTimer.current) {
      window.clearTimeout(typingStopTimer.current)
    }

    typingStopTimer.current = window.setTimeout(() => {
      void chatConnection.typing(chatId, false)
    }, 2500)
  }

  const loadOlder = async () => {
    if (!chatId || messages.length === 0 || loadingOlder) return

    setLoadingOlder(true)
    const container = scrollAnchor.current?.parentElement
    const previousHeight = container?.scrollHeight ?? 0

    try {
      const page = await api.messages(chatId, messages[0].sentAt)

      queryClient.setQueryData<{ messages: Message[]; hasMore: boolean }>(['messages', chatId], (current) => ({
        messages: [...page.messages, ...(current?.messages ?? [])],
        hasMore: page.hasMore,
      }))

      window.requestAnimationFrame(() => {
        if (container) {
          container.scrollTop = container.scrollHeight - previousHeight
        }
      })
    } finally {
      setLoadingOlder(false)
    }
  }

  if (!chat) {
    return (
      <div className="flex h-full flex-1 flex-col items-center justify-center gap-2 bg-white">
        <p className="text-sm font-medium text-ink-500">{t('chat.selectChat')}</p>
        <p className="max-w-xs text-center text-xs text-ink-400">{t('chat.selectChatHint')}</p>
      </div>
    )
  }

  const peer = chat.isGroup ? null : (chat.participants.find((participant) => participant.userId !== user?.id) ?? null)

  // Expiry is driven by timers registered when the event arrives, so no clock read during render.
  const typingHandles = [...typingUsers.entries()]
    .filter(([userId]) => userId !== user?.id)
    .map(([, entry]) => entry.handle)

  const typingLabel =
    typingHandles.length === 0
      ? null
      : typingHandles.length === 1
        ? t('chat.typing', { handle: typingHandles[0] })
        : typingHandles.length === 2
          ? t('chat.typingTwo', { first: typingHandles[0], second: typingHandles[1] })
          : t('chat.typingSeveral', { count: typingHandles.length })

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

        <Avatar
          name={chat.title}
          group={chat.isGroup}
          size={34}
          online={peer ? onlineUsers.has(peer.userId) : undefined}
        />

        <div className="min-w-0">
          <h2 className="truncate text-sm font-semibold leading-tight">
            {chat.isGroup ? chat.title : `@${chat.title}`}
          </h2>

          <p className="truncate text-[11px] leading-tight text-ink-400">
            {chat.isGroup ? (
              <button type="button" onClick={onOpenMembers} className="inline-flex items-center gap-1 hover:text-ink-700">
                <Users size={10} />
                {t('chat.members', { count: chat.participants.length })}
              </button>
            ) : peer ? (
              onlineUsers.has(peer.userId) ? (
                <span className="text-emerald-600">{t('chat.online')}</span>
              ) : (
                t('chat.offline')
              )
            ) : null}
          </p>

          {/* Reserved height keeps the header from jumping when somebody starts or stops typing. */}
          <div className="h-3.5 truncate text-[10px] leading-3.5">
            {typingLabel ? (
              <span className="inline-flex items-center gap-1 text-ink-500">
                <span className="flex gap-0.5">
                  <Dot delay="0ms" />
                  <Dot delay="150ms" />
                  <Dot delay="300ms" />
                </span>
                {typingLabel}
              </span>
            ) : null}
          </div>
        </div>

        <div
          className="ml-auto inline-flex items-center gap-1 rounded-full border border-ink-200 px-2 py-1 text-[10px] text-ink-500"
          title={t('chat.encryptedTooltip')}
        >
          <Lock size={10} />
          {t('chat.encrypted')}
        </div>
      </header>

      <MessageList
        messages={messages}
        pending={pending}
        currentUserId={user?.id ?? ''}
        hasMore={history.data?.hasMore ?? false}
        loadingOlder={loadingOlder}
        onLoadOlder={() => void loadOlder()}
      />
      <div ref={scrollAnchor} />

      <div className="border-t border-ink-200 p-3">
        <form
          onSubmit={(event) => {
            event.preventDefault()
            void send()
          }}
          className="flex items-end gap-2"
        >
          <textarea
            ref={inputRef}
            value={draft}
            onChange={(event) => onDraftChange(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault()
                void send()
              }
            }}
            rows={1}
            maxLength={4000}
            placeholder={t('chat.messagePlaceholder')}
            className="max-h-32 min-h-[38px] flex-1 resize-none rounded-xl border border-ink-200 bg-white px-3 py-2 text-[13px] leading-relaxed outline-none transition placeholder:text-ink-400 focus:border-ink-400"
          />

          <button
            type="submit"
            disabled={draft.trim().length === 0}
            aria-label={t('chat.send')}
            className="flex h-[38px] w-[38px] items-center justify-center rounded-xl bg-ink-900 text-white transition hover:bg-ink-800 disabled:opacity-40"
          >
            <Send size={15} />
          </button>
        </form>
      </div>
    </section>
  )
}

function Dot({ delay }: { delay: string }) {
  return (
    <span
      className="h-1 w-1 animate-bounce rounded-full bg-ink-400"
      style={{ animationDelay: delay, animationDuration: '1s' }}
    />
  )
}

