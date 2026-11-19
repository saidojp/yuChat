import { useEffect, useMemo, useRef } from 'react'
import { useTranslation } from 'react-i18next'

import type { Message } from '../../api/types'

type PendingMessage = {
  clientMessageId: string
  content: string
  createdAt: string
  status: 'sending' | 'failed'
}

function dayKey(value: string) {
  return new Date(value).toDateString()
}

export function MessageList({
  messages,
  pending,
  currentUserId,
  hasMore,
  loadingOlder,
  onLoadOlder,
}: {
  messages: Message[]
  pending: PendingMessage[]
  currentUserId: string
  hasMore: boolean
  loadingOlder: boolean
  onLoadOlder: () => void
}) {
  const { t, i18n } = useTranslation()
  const bottomRef = useRef<HTMLDivElement>(null)
  const containerRef = useRef<HTMLDivElement>(null)
  const previousCount = useRef(0)

  useEffect(() => {
    const shouldStickToBottom = messages.length !== previousCount.current || pending.length > 0
    previousCount.current = messages.length

    if (shouldStickToBottom) {
      bottomRef.current?.scrollIntoView({ block: 'end' })
    }
  }, [messages.length, pending.length])

  const groups = useMemo(() => {
    const result: { day: string; items: Message[] }[] = []

    for (const message of messages) {
      const key = dayKey(message.sentAt)
      const last = result[result.length - 1]

      if (last && last.day === key) {
        last.items.push(message)
      } else {
        result.push({ day: key, items: [message] })
      }
    }

    return result
  }, [messages])

  return (
    <div ref={containerRef} className="flex-1 overflow-y-auto px-5 py-4 scrollbar-thin">
      {hasMore ? (
        <div className="mb-4 flex justify-center">
          <button
            type="button"
            onClick={onLoadOlder}
            disabled={loadingOlder}
            className="rounded-full border border-ink-200 bg-white px-3 py-1 text-[11px] text-ink-500 transition hover:border-ink-300 hover:text-ink-800 disabled:opacity-60"
          >
            {loadingOlder ? t('common.loading') : t('chat.loadOlder')}
          </button>
        </div>
      ) : null}

      {messages.length === 0 && pending.length === 0 ? (
        <div className="flex h-full items-center justify-center">
          <p className="text-xs text-ink-400">{t('chat.emptyHistory')}</p>
        </div>
      ) : null}

      <div className="space-y-5">
        {groups.map((group) => (
          <div key={group.day} className="space-y-1">
            <div className="flex items-center gap-3 py-2">
              <span className="h-px flex-1 bg-ink-100" />
              <span className="text-[10px] font-medium uppercase tracking-wider text-ink-400">
                {formatDay(group.day, t, i18n.language)}
              </span>
              <span className="h-px flex-1 bg-ink-100" />
            </div>

            {group.items.map((message, index) => {
              const own = message.senderId === currentUserId
              const previous = group.items[index - 1]
              const showHeader = !previous || previous.senderId !== message.senderId

              return (
                <div key={message.id} className={`flex ${own ? 'justify-end' : 'justify-start'}`}>
                  <div className={`max-w-[78%] ${showHeader ? 'mt-2' : ''}`}>
                    {showHeader && !own ? (
                      <p className="mb-1 px-1 text-[10px] font-medium text-ink-400">@{message.senderHandle}</p>
                    ) : null}

                    <div
                      className={`whitespace-pre-wrap break-words rounded-2xl px-3 py-2 text-[13px] leading-relaxed ${
                        own ? 'bg-ink-900 text-white' : 'bg-ink-100 text-ink-900'
                      }`}
                    >
                      {message.content}
                    </div>

                    <p className={`mt-0.5 px-1 text-[10px] text-ink-400 ${own ? 'text-right' : ''}`}>
                      {formatTime(message.sentAt)}
                    </p>
                  </div>
                </div>
              )
            })}
          </div>
        ))}

        {pending.map((message) => (
          <div key={message.clientMessageId} className="flex justify-end">
            <div className="max-w-[78%]">
              <div className="whitespace-pre-wrap break-words rounded-2xl bg-ink-900/70 px-3 py-2 text-[13px] leading-relaxed text-white">
                {message.content}
              </div>
              <p className="mt-0.5 px-1 text-right text-[10px] text-ink-400">
                {message.status === 'failed' ? t('chat.failed') : t('chat.sending')}
              </p>
            </div>
          </div>
        ))}
      </div>

      <div className="h-4" />

      <div ref={bottomRef} />
    </div>
  )
}

function formatTime(value: string) {
  return new Date(value).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
}

function formatDay(day: string, t: (key: string) => string, language: string) {
  const date = new Date(day)
  const now = new Date()

  if (date.toDateString() === now.toDateString()) {
    return t('chat.today')
  }

  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)

  if (date.toDateString() === yesterday.toDateString()) {
    return t('chat.yesterday')
  }

  return date.toLocaleDateString(language === 'ja' ? 'ja-JP' : 'en-US', {
    month: 'short',
    day: 'numeric',
    year: date.getFullYear() === now.getFullYear() ? undefined : 'numeric',
  })
}
