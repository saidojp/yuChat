import { useQuery } from '@tanstack/react-query'
import { Bell, LogOut, MessageSquarePlus, UserPlus, UsersRound } from 'lucide-react'
import { useTranslation } from 'react-i18next'

import { api } from '../../api/client'
import type { ChatSummary } from '../../api/types'
import { LanguageSwitcher } from '../../components/LanguageSwitcher'
import { useAuth } from '../auth/useAuth'
import { Avatar } from './Avatar'

type SidebarProps = {
  chats: ChatSummary[]
  activeChatId: string | null
  connectionState: 'connected' | 'reconnecting' | 'offline'
  onSelectChat: (chatId: string) => void
  onOpenSearch: () => void
  onOpenFriends: () => void
  onOpenGroup: () => void
  onOpenNotifications: () => void
}

export function Sidebar({
  chats,
  activeChatId,
  connectionState,
  onSelectChat,
  onOpenSearch,
  onOpenFriends,
  onOpenGroup,
  onOpenNotifications,
}: SidebarProps) {
  const { t } = useTranslation()
  const { user, logout } = useAuth()

  const notifications = useQuery({ queryKey: ['notifications'], queryFn: api.notifications, staleTime: 30_000 })
  const friends = useQuery({ queryKey: ['friends'], queryFn: api.friends, staleTime: 30_000 })
  const invitations = useQuery({ queryKey: ['invitations'], queryFn: api.invitations, staleTime: 30_000 })

  const pendingCount =
    (notifications.data?.unreadCount ?? 0) + (friends.data?.incoming.length ?? 0) + (invitations.data?.length ?? 0)

  const actionButton =
    'relative flex h-9 w-9 items-center justify-center rounded-lg text-ink-500 transition hover:bg-ink-100 hover:text-ink-900'

  return (
    <aside className="flex h-full w-[290px] shrink-0 flex-col border-r border-ink-200 bg-ink-50/60">
      <div className="flex items-center gap-2 px-4 pb-3 pt-4">
        <div className="flex min-w-0 flex-1 items-center gap-2">
          <Avatar name={user?.userHandle ?? user?.login ?? '?'} size={28} />
          <div className="min-w-0">
            <p className="truncate text-sm font-medium leading-tight">@{user?.userHandle}</p>
            <p className="truncate text-[11px] leading-tight text-ink-400">{user?.login}</p>
          </div>
        </div>

        <button type="button" onClick={() => void logout()} aria-label={t('sidebar.logout')} className={actionButton}>
          <LogOut size={15} />
        </button>
      </div>

      <div className="flex items-center gap-1 px-3 pb-3">
        <button
          type="button"
          onClick={onOpenSearch}
          aria-label={t('sidebar.newChat')}
          title={t('sidebar.newChat')}
          className="relative flex h-9 w-9 items-center justify-center rounded-lg bg-ink-900 text-white transition hover:bg-ink-800"
        >
          <MessageSquarePlus size={16} />
        </button>
        <button type="button" onClick={onOpenFriends} aria-label={t('sidebar.friends')} className={actionButton}>
          <UserPlus size={16} />
          {friends.data && friends.data.incoming.length > 0 ? (
            <span className="absolute right-1 top-1 h-1.5 w-1.5 rounded-full bg-ink-900" />
          ) : null}
        </button>
        <button type="button" onClick={onOpenGroup} aria-label={t('sidebar.createGroup')} className={actionButton}>
          <UsersRound size={16} />
        </button>
        <button
          type="button"
          onClick={onOpenNotifications}
          aria-label={t('sidebar.notifications')}
          className={actionButton}
        >
          <Bell size={16} />
          {pendingCount > 0 ? (
            <span className="absolute -right-0.5 -top-0.5 min-w-4 rounded-full bg-ink-900 px-1 text-center text-[10px] font-semibold leading-4 text-white">
              {pendingCount > 9 ? '9+' : pendingCount}
            </span>
          ) : null}
        </button>

        <div className="ml-auto flex items-center gap-2">
          <span
            title={connectionState === 'connected' ? 'Connected' : connectionState}
            className={`h-1.5 w-1.5 rounded-full ${
              connectionState === 'connected'
                ? 'bg-emerald-500'
                : connectionState === 'reconnecting'
                  ? 'bg-amber-500'
                  : 'bg-ink-300'
            }`}
          />
          <LanguageSwitcher />
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-2 pb-4 scrollbar-thin">
        <p className="px-2 pb-1 text-[10px] font-semibold uppercase tracking-wider text-ink-400">
          {t('sidebar.chats')}
        </p>

        {chats.length === 0 ? (
          <div className="px-2 py-6 text-center">
            <p className="text-xs text-ink-500">{t('sidebar.emptyChats')}</p>
            <p className="mt-1 text-[11px] text-ink-400">{t('sidebar.emptyChatsHint')}</p>
            <button
              type="button"
              onClick={onOpenSearch}
              className="mt-3 inline-flex items-center gap-1 rounded-lg bg-ink-900 px-2.5 py-1.5 text-[11px] font-medium text-white transition hover:bg-ink-800"
            >
              <MessageSquarePlus size={11} />
              {t('sidebar.newChat')}
            </button>
          </div>
        ) : (
          <ul className="space-y-0.5">
            {chats.map((chat) => (
              <li key={chat.id}>
                <ChatListItem chat={chat} active={chat.id === activeChatId} onClick={() => onSelectChat(chat.id)} />
              </li>
            ))}
          </ul>
        )}
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
            {chat.lastMessagePreview ?? (chat.isGroup ? t('chat.members', { count: chat.participants.length }) : '\u00a0')}
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

  const sameDay = date.toDateString() === now.toDateString()

  if (sameDay) {
    return date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
  }

  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)

  if (date.toDateString() === yesterday.toDateString()) {
    return t('chat.yesterday')
  }

  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}
