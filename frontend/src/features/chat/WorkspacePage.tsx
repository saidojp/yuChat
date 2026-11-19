import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router-dom'

import { api } from '../../api/client'
import { chatConnection } from '../../realtime/connection'
import { useAuth } from '../auth/useAuth'
import { ChatView } from './ChatView'
import { FriendsModal } from './FriendsModal'
import { GroupModal } from './GroupModal'
import { MembersModal } from './MembersModal'
import { NotificationsModal } from './NotificationsModal'
import { SearchModal } from './SearchModal'
import { Sidebar } from './Sidebar'

type OpenModal = 'search' | 'friends' | 'group' | 'notifications' | 'members' | null

export function WorkspacePage() {
  const { t } = useTranslation()
  const { user } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { chatId: routeChatId } = useParams()

  const [modal, setModal] = useState<OpenModal>(null)
  const [connectionState, setConnectionState] = useState<'connected' | 'reconnecting' | 'offline'>('offline')

  const chats = useQuery({ queryKey: ['chats'], queryFn: api.chats, refetchOnWindowFocus: false })
  const activeChat = chats.data?.find((chat) => chat.id === routeChatId) ?? null

  // A user who has not finished onboarding must not reach the workspace.
  useEffect(() => {
    if (user?.needsOnboarding) {
      navigate('/onboarding', { replace: true })
    }
  }, [user, navigate])

  useEffect(() => {
    const unsubscribeState = chatConnection.on('state', (state) => {
      setConnectionState(
        state === 'Connected' ? 'connected' : state === 'Disconnected' ? 'offline' : 'reconnecting',
      )
    })

    const onReconnected = () => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
      void queryClient.invalidateQueries({ queryKey: ['messages'] })
      void queryClient.invalidateQueries({ queryKey: ['notifications'] })
      void queryClient.invalidateQueries({ queryKey: ['invitations'] })
    }

    const unsubscribeReconnected = chatConnection.on('reconnected', onReconnected)
    const unsubscribeNotification = chatConnection.on('notification', () => {
      void queryClient.invalidateQueries({ queryKey: ['notifications'] })
    })
    const unsubscribeFriend = chatConnection.on('friendRequestUpdated', () => {
      void queryClient.invalidateQueries({ queryKey: ['friends'] })
    })
    const unsubscribeInvitation = chatConnection.on('invitationReceived', () => {
      void queryClient.invalidateQueries({ queryKey: ['invitations'] })
      void queryClient.invalidateQueries({ queryKey: ['notifications'] })
    })
    const unsubscribeChatCreated = chatConnection.on('chatCreated', (chat) => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
      // Subscribe this connection to the new room immediately: a freshly added group member must
      // receive typing/read events even before opening the chat.
      void chatConnection.joinChat(chat.id)
    })
    const unsubscribeChatUpdated = chatConnection.on('chatUpdated', (chat) => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
      void chatConnection.joinChat(chat.id)
    })

    void chatConnection.start()

    return () => {
      unsubscribeState()
      unsubscribeReconnected()
      unsubscribeNotification()
      unsubscribeFriend()
      unsubscribeInvitation()
      unsubscribeChatCreated()
      unsubscribeChatUpdated()
    }
  }, [queryClient])

  const openChat = (chatId: string) => {
    navigate(`/app/c/${chatId}`)
    setModal(null)
  }

  return (
    <div className="flex h-screen overflow-hidden bg-white">
      <div className={`${routeChatId ? 'hidden md:flex' : 'flex'} h-full`}>
        <Sidebar
          chats={chats.data ?? []}
          activeChatId={routeChatId ?? null}
          connectionState={connectionState}
          onSelectChat={openChat}
          onOpenSearch={() => setModal('search')}
          onOpenFriends={() => setModal('friends')}
          onOpenGroup={() => setModal('group')}
          onOpenNotifications={() => setModal('notifications')}
        />
      </div>

      <div className={`${routeChatId ? 'flex' : 'hidden md:flex'} min-w-0 flex-1`}>
        <ChatView
          chat={activeChat}
          onBack={() => navigate('/app')}
          onOpenMembers={() => setModal('members')}
        />
      </div>

      {connectionState === 'offline' ? (
        <div className="pointer-events-none fixed bottom-4 left-1/2 -translate-x-1/2 rounded-full bg-ink-900 px-3 py-1.5 text-[11px] text-white shadow-lg">
          {t('chat.reconnect')}
        </div>
      ) : null}

      {modal === 'search' ? <SearchModal onClose={() => setModal(null)} onChatStarted={openChat} /> : null}
      {modal === 'friends' ? <FriendsModal onClose={() => setModal(null)} onChatStarted={openChat} /> : null}
      {modal === 'group' ? <GroupModal onClose={() => setModal(null)} onCreated={openChat} /> : null}
      {modal === 'notifications' ? (
        <NotificationsModal onClose={() => setModal(null)} onChatOpened={openChat} />
      ) : null}
      {modal === 'members' && activeChat?.isGroup ? (
        <MembersModal chat={activeChat} onClose={() => setModal(null)} />
      ) : null}
    </div>
  )
}
