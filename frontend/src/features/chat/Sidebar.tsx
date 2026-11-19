import { useQuery } from '@tanstack/react-query'
import { BookUser, MessageSquarePlus } from 'lucide-react'
import { useTranslation } from 'react-i18next'

import { api } from '../../api/client'
import type { ChatSummary } from '../../api/types'
import { useAuth } from '../auth/useAuth'
import { Avatar } from './Avatar'
import { ProfileMenu } from './ProfileMenu'
import { SystemEventText } from './SystemEventText'

type SidebarProps = {
  chats: ChatSummary[]
  activeChatId: string | null
  connectionState: 'connected' | 'reconnecting' | 'offline'
  onSelectChat: (chatId: string) => void
  onOpenNewChat: () => void
  onOpenContacts: () => void
}

export function Sidebar({
  chats,
  activeChatId,
  connectionState,
  onSelectChat,
  onOpenNewChat,
  onOpenContacts,
}: SidebarProps) {
  const { t } = useTranslation()
  const { user } = useAuth()

  const friends = useQuery({ queryKey: ['friends'], queryFn: api.friends, staleTime: 30_000 })
  const pendingRequests = friends.data?.incoming.length ?? 0

  return (
    <aside className="flex h-full w-[290px] shrink-0 flex-col border-r border-ink-200 bg-ink-50/60">
      <div className="flex items-center gap-2 px-3 pb-2 pt-3">
        <ProfileMenu />
      </div>

      <div className="flex-1 overflow-y-auto px-2 pb-2 scrollbar-thin">
        <p className="px-2 pb-1 text-[10px] font-semibold uppercase tracking-wider text-ink-400">
          {t('sidebar.chats')}
        </p>

        {chats.length === 0 ? (
          <div className="px-2 py-6 text-center">
            <p className="text-xs text-ink-500">{t('sidebar.emptyChats')}</p>
            <p className="mt-1 text-[11px] text-ink-400">{t('sidebar.emptyChatsHint')}</p>
          </div>
        ) : (
          <ul className="space-y-0.5">
            {chats.map((chat) => {
              const isSystem = chat.kind === 'System'

              return (
                <li key={chat.id}>
                  {isSystem ? (
                    <SystemChatListItem
                      chat={chat}
                      active={chat.id === activeChatId}
                      onClick={() => onSelectChat(chat.id)}
                    />
                  ) : (
                    <ChatListItem
                      chat={chat}
                      active={chat.id === activeChatId}
                      onClick={() => onSelectChat(chat.id)}
                    />
                  )}
                </li>
              )
            })}
          </ul>
        )}
      </div>

      <div className="flex items-center gap-1 border-t border-ink-200/70 px-3 py-2.5">
        <button
          type="button"
          onClick={onOpenNewChat}
          aria-label={t('sidebar.startChat')}
          title={t('sidebar.startChat')}
          className="flex h-8 items-center gap-1.5 rounded-lg bg-ink-900 px-2.5 text-[11px] font-medium text-white transition hover:bg-ink-800"
        >
          <MessageSquarePlus size={13} />
          {t('sidebar.startChat')}
        </button>

        <button
          type="button"
          onClick={onOpenContacts}
          aria-label={t('sidebar.contacts')}
          title={t('sidebar.contacts')}
          className="relative ml-auto flex h-8 items-center gap-1.5 rounded-lg px-2 text-[11px] font-medium text-ink-500 transition hover:bg-ink-100 hover:text-ink-900"
        >
          <BookUser size={14} />
          {t('sidebar.contacts')}
          {pendingRequests > 0 ? (
            <span className="absolute -right-0.5 -top-0.5 min-w-4 rounded-full bg-ink-900 px-1 text-center text-[10px] font-semibold leading-4 text-white">
              {pendingRequests > 9 ? '9+' : pendingRequests}
            </span>
          ) : null}
        </button>

        <span
          title={connectionState}
          className={`ml-1.5 h-1.5 w-1.5 shrink-0 rounded-full ${
            connectionState === 'connected'
              ? 'bg-emerald-500'
              : connectionState === 'reconnecting'
                ? 'bg-amber-500'
                : 'bg-ink-300'
          }`}
        />
      </div>
    </aside>
  )
}

function ChatListItem({ chat, active, onClick }: { chat: ChatSummary; active: boolean; onClick: () => void }) {
  const { t } = useTranslation()

  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex w-full items-start gap-2.5 rounded-lg px-2 py-2 text-left transition ${
        active ? 'bg-white shadow-sm ring-1 ring-ink-200' : 'hover:bg-ink-100/70'
      }`}
    >
      <Avatar name={chat.title} group={chat.isGroup} size={32} />

      <div className="min-w-0 flex-1">
        <div className="flex items-baseline gap-2">
          <span className={`truncate text-[13px] ${chat.unreadCount > 0 ? 'font-semibold' : 'font-medium'}`}>
            {chat.isGroup ? chat.title : `@${chat.title}`}
          </span>
          <span className="ml-auto shrink-0 text-[10px] text-ink-400">{formatListTime(chat.lastMessageAt, t)}</span>
        </div>

        <div className="mt-0.5 flex items-center gap-2">
          <span className="truncate text-[11px] text-ink-500">
            {chat.lastMessagePreview ?? (chat.isGroup ? t('chat.members', { count: chat.participants.length }) : '')}
          </span>
          {chat.unreadCount > 0 ? (
            <span className="ml-auto shrink-0 rounded-full bg-ink-900 px-1.5 text-[10px] font-semibold leading-4 text-white">
              {chat.unreadCount > 99 ? '99+' : chat.unreadCount}
            </span>
          ) : null}
        </div>
      </div>
    </button>
  )
}

/** The notifications feed reads as a conversation, but keeps its own muted identity. */
function SystemChatListItem({ chat, active, onClick }: { chat: ChatSummary; active: boolean; onClick: () => void }) {
  const { t } = useTranslation()

  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex w-full items-start gap-2.5 rounded-lg px-2 py-2 text-left transition ${
        active ? 'bg-white shadow-sm ring-1 ring-ink-200' : 'hover:bg-ink-100/70'
      }`}
    >
      <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full border border-ink-300 bg-white text-ink-600">
        <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M4 7h16v10H7l-3 3z" />
        </svg>
      </div>

      <div className="min-w-0 flex-1">
        <div className="flex items-baseline gap-2">
          <span className={`truncate text-[13px] ${chat.unreadCount > 0 ? 'font-semibold' : 'font-medium'}`}>
            {t('system.title')}
          </span>
          <span className="ml-auto shrink-0 text-[10px] text-ink-400">{formatListTime(chat.lastMessageAt, t)}</span>
        </div>

        <div className="mt-0.5 flex items-center gap-2">
          <span className="truncate text-[11px] text-ink-500">
            {chat.lastSystemEvent ? (
              <SystemEventText event={chat.lastSystemEvent} />
            ) : (
              t('system.subtitle')
            )}
          </span>
          {chat.unreadCount > 0 ? (
            <span className="ml-auto shrink-0 rounded-full bg-ink-900 px-1.5 text-[10px] font-semibold leading-4 text-white">
              {chat.unreadCount > 99 ? '99+' : chat.unreadCount}
            </span>
          ) : null}
        </div>
      </div>
    </button>
  )
}

function formatListTime(value: string, t: (key: string) => string) {
  const date = new Date(value)
  const now = new Date()

  if (date.toDateString() === now.toDateString()) {
    return date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
  }

  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)

  if (date.toDateString() === yesterday.toDateString()) {
    return t('chat.yesterday')
  }

  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}
