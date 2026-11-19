import { useTranslation } from 'react-i18next'

import type { SystemEventPayload } from '../../api/types'

/**
 * Renders a system event in the reader's language. The server stores only the event type and
 * references, so switching language re-renders the whole history correctly.
 */
export function SystemEventText({ event }: { event: SystemEventPayload }) {
  const { t } = useTranslation()
  const handle = event.actorHandle ?? '?'
  const chat = event.referenceName ?? t('system.title')

  switch (event.event) {
    case 'FriendRequestReceived':
      return <>{t('system.friendRequestReceived', { handle })}</>
    case 'FriendRequestAccepted':
      return <>{t('system.friendRequestAccepted', { handle })}</>
    case 'FriendRequestDeclined':
      return <>{t('system.friendRequestDeclined', { handle })}</>
    case 'FriendRequestCancelled':
      return <>{t('system.friendRequestCancelled', { handle })}</>
    case 'ChatInvitationReceived':
      return <>{t('system.chatInvitationReceived', { handle, chat })}</>
    case 'ChatInvitationAccepted':
      return <>{t('system.chatInvitationAccepted', { handle, chat })}</>
    case 'ChatInvitationDeclined':
      return <>{t('system.chatInvitationDeclined', { handle, chat })}</>
    case 'AddedToGroup':
      return <>{t('system.addedToGroup', { handle, chat })}</>
    case 'FriendRemoved':
      return <>{t('system.friendRemoved', { handle })}</>
    default:
      return null
  }
}
