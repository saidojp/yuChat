import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Mail, UserPlus, Users, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'

import { api } from '../../api/client'
import { Modal } from '../../components/Modal'

export function NotificationsModal({
  onClose,
  onChatOpened,
}: {
  onClose: () => void
  onChatOpened: (chatId: string) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const notifications = useQuery({ queryKey: ['notifications'], queryFn: api.notifications })
  const friends = useQuery({ queryKey: ['friends'], queryFn: api.friends })
  const invitations = useQuery({ queryKey: ['invitations'], queryFn: api.invitations })

  const invalidateAll = () => {
    void queryClient.invalidateQueries({ queryKey: ['notifications'] })
    void queryClient.invalidateQueries({ queryKey: ['friends'] })
    void queryClient.invalidateQueries({ queryKey: ['invitations'] })
    void queryClient.invalidateQueries({ queryKey: ['chats'] })
  }

  const respondFriend = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'accept' | 'decline' }) => api.respondFriendRequest(id, action),
    onSuccess: invalidateAll,
  })

  const respondInvitation = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'accept' | 'decline' }) => api.respondInvitation(id, action),
    onSuccess: (chat) => {
      invalidateAll()
      if (chat) onChatOpened(chat.id)
    },
  })

  const markRead = useMutation({
    mutationFn: () => api.markAllNotificationsRead(),
    onSuccess: invalidateAll,
  })

  const unread = notifications.data?.unreadCount ?? 0
  const pendingFriendRequests = friends.data?.incoming ?? []
  const pendingInvitations = invitations.data ?? []
  const informational = notifications.data?.items ?? []

  const empty = pendingFriendRequests.length === 0 && pendingInvitations.length === 0 && informational.length === 0

  return (
    <Modal
      title={t('notifications.title')}
      onClose={onClose}
      footer={
        informational.length > 0 ? (
          <button
            type="button"
            onClick={() => markRead.mutate()}
            disabled={unread === 0 || markRead.isPending}
            className="w-full rounded-lg border border-ink-200 px-3 py-2 text-xs font-medium transition hover:border-ink-300 disabled:opacity-40"
          >
            {t('notifications.markAllRead')}
          </button>
        ) : undefined
      }
    >
      {empty ? <p className="py-6 text-center text-xs text-ink-400">{t('notifications.empty')}</p> : null}

      {pendingInvitations.length > 0 ? (
        <section className="mb-3">
          <h3 className="mb-1.5 text-[10px] font-semibold uppercase tracking-wider text-ink-400">{t('chat.invitation')}</h3>
          <ul className="space-y-1">
            {pendingInvitations.map((invitation) => (
              <li key={invitation.id} className="rounded-lg border border-ink-200 px-2.5 py-2">
                <div className="flex items-start gap-2">
                  <Users size={13} className="mt-0.5 shrink-0 text-ink-400" />
                  <p className="flex-1 text-[12px] leading-snug">
                    {t('chat.invitationFrom', { handle: invitation.inviterHandle, chat: invitation.chatTitle })}
                  </p>
                </div>

                <div className="mt-1.5 flex gap-1.5 pl-5">
                  <button
                    type="button"
                    onClick={() => respondInvitation.mutate({ id: invitation.id, action: 'accept' })}
                    disabled={respondInvitation.isPending}
                    className="inline-flex items-center gap-1 rounded-lg bg-ink-900 px-2 py-1 text-[11px] font-medium text-white transition hover:bg-ink-800 disabled:opacity-50"
                  >
                    <Check size={10} />
                    {t('chat.join')}
                  </button>
                  <button
                    type="button"
                    onClick={() => respondInvitation.mutate({ id: invitation.id, action: 'decline' })}
                    disabled={respondInvitation.isPending}
                    className="inline-flex items-center gap-1 rounded-lg border border-ink-200 px-2 py-1 text-[11px] transition hover:border-ink-300 disabled:opacity-50"
                  >
                    <X size={10} />
                    {t('chat.decline')}
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {pendingFriendRequests.length > 0 ? (
        <section className="mb-3">
          <h3 className="mb-1.5 text-[10px] font-semibold uppercase tracking-wider text-ink-400">
            {t('friends.incomingTab')}
          </h3>
          <ul className="space-y-1">
            {pendingFriendRequests.map((friendship) => (
              <li key={friendship.id} className="rounded-lg border border-ink-200 px-2.5 py-2">
                <div className="flex items-start gap-2">
                  <UserPlus size={13} className="mt-0.5 shrink-0 text-ink-400" />
                  <p className="flex-1 text-[12px] leading-snug">
                    @{friendship.user.userHandle} — {t('search.pendingReceived')}
                  </p>
                </div>

                <div className="mt-1.5 flex gap-1.5 pl-5">
                  <button
                    type="button"
                    onClick={() => respondFriend.mutate({ id: friendship.id, action: 'accept' })}
                    disabled={respondFriend.isPending}
                    className="inline-flex items-center gap-1 rounded-lg bg-ink-900 px-2 py-1 text-[11px] font-medium text-white transition hover:bg-ink-800 disabled:opacity-50"
                  >
                    <Check size={10} />
                    {t('friends.accept')}
                  </button>
                  <button
                    type="button"
                    onClick={() => respondFriend.mutate({ id: friendship.id, action: 'decline' })}
                    disabled={respondFriend.isPending}
                    className="inline-flex items-center gap-1 rounded-lg border border-ink-200 px-2 py-1 text-[11px] transition hover:border-ink-300 disabled:opacity-50"
                  >
                    <X size={10} />
                    {t('friends.decline')}
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {informational.length > 0 ? (
        <section>
          <h3 className="mb-1.5 text-[10px] font-semibold uppercase tracking-wider text-ink-400">
            {t('notifications.title')}
          </h3>
          <ul className="space-y-1">
            {informational.map((item) => (
              <li
                key={item.id}
                className={`flex items-start gap-2 rounded-lg px-2.5 py-2 ${
                  item.isRead ? 'text-ink-500' : 'bg-ink-50 text-ink-900'
                }`}
              >
                <Mail size={13} className="mt-0.5 shrink-0 text-ink-400" />
                <p className="text-[12px] leading-snug">
                  {item.type === 'FriendAccepted'
                    ? t('notifications.friendAccepted', { handle: item.actorHandle ?? '?' })
                    : item.type === 'AddedToGroup'
                      ? t('notifications.addedToGroup', { handle: item.actorHandle ?? '?' })
                      : t('notifications.invitationAccepted', { handle: item.actorHandle ?? '?' })}
                </p>
                <span className="ml-auto shrink-0 text-[10px] text-ink-400">
                  {new Date(item.createdAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
                </span>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </Modal>
  )
}
