import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router-dom'

import { api } from '../../api/client'
import { chatConnection } from '../../realtime/connection'
import { useAuth } from '../auth/useAuth'
import { ChatView } from './ChatView'
import { ContactsModal } from './ContactsModal'
import { MembersModal } from './MembersModal'
import { NewChatModal } from './NewChatModal'
import { Sidebar } from './Sidebar'
import { SystemChatView } from './SystemChatView'

type OpenModal = 'contacts' | 'newChat' | 'members' | null

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
  const isSystemChat = activeChat?.kind === 'System'

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
      void queryClient.invalidateQueries({ queryKey: ['friends'] })
      void queryClient.invalidateQueries({ queryKey: ['invitations'] })
    }

    const unsubscribeReconnected = chatConnection.on('reconnected', onReconnected)

    // Every push that changes a chat or a relationship refreshes the cached lists. The realtime
    // contract deliberately stays generic so new event types need no extra plumbing here.
    const refreshChats = () => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
    }
    const refreshRelationships = () => {
      void queryClient.invalidateQueries({ queryKey: ['friends'] })
      void queryClient.invalidateQueries({ queryKey: ['invitations'] })
      refreshChats()
    }

    const unsubscribeMessage = chatConnection.on('message', refreshChats)
    const unsubscribeFriend = chatConnection.on('friendRequestUpdated', refreshRelationships)
    const unsubscribeInvitation = chatConnection.on('invitationReceived', refreshRelationships)
    const unsubscribeChatCreated = chatConnection.on('chatCreated', (chat) => {
      refreshChats()
      // Subscribe this connection to the new room immediately: a freshly added group member must
      // receive typing/read events even before opening the chat.
      void chatConnection.joinChat(chat.id)
    })
    const unsubscribeChatUpdated = chatConnection.on('chatUpdated', (chat) => {
      refreshChats()
      void chatConnection.joinChat(chat.id)
    })

    void chatConnection.start()

    return () => {
      unsubscribeState()
      unsubscribeReconnected()
      unsubscribeMessage()
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

  const chatIsOpen = Boolean(routeChatId)

  return (
    <div className="flex h-screen overflow-hidden bg-white">
      <div className={`${chatIsOpen ? 'hidden md:flex' : 'flex'} h-full`}>
        <Sidebar
          chats={chats.data ?? []}
          activeChatId={routeChatId ?? null}
          connectionState={connectionState}
          onSelectChat={openChat}
          onOpenNewChat={() => setModal('newChat')}
          onOpenContacts={() => setModal('contacts')}
        />
      </div>

      <div className={`${chatIsOpen ? 'flex' : 'hidden md:flex'} min-w-0 flex-1`}>
        {isSystemChat && activeChat ? (
          <SystemChatView chat={activeChat} onBack={() => navigate('/app')} onOpenChat={openChat} />
        ) : (
          <ChatView
            chat={activeChat}
            onBack={() => navigate('/app')}
            onOpenMembers={() => setModal('members')}
          />
        )}
      </div>

      {connectionState === 'offline' ? (
        <div className="pointer-events-none fixed bottom-4 left-1/2 -translate-x-1/2 rounded-full bg-ink-900 px-3 py-1.5 text-[11px] text-white shadow-lg">
          {t('chat.reconnect')}
        </div>
      ) : null}

      {modal === 'newChat' ? <NewChatModal onClose={() => setModal(null)} onChatStarted={openChat} /> : null}
      {modal === 'contacts' ? <ContactsModal onClose={() => setModal(null)} onChatStarted={openChat} /> : null}
      {modal === 'members' && activeChat?.isGroup ? (
        <MembersModal chat={activeChat} onClose={() => setModal(null)} />
      ) : null}
    </div>
  )
}
