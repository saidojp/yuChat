export type UserProfile = {
  id: string
  login: string
  userHandle: string | null
  createdAt: string
  needsOnboarding: boolean
}

export type AuthResponse = {
  accessToken: string
  accessTokenExpiresAt: string
  user: UserProfile
}

export type RefreshResponse = {
  accessToken: string
  accessTokenExpiresAt: string
}

export type FriendshipState = 'None' | 'PendingSent' | 'PendingReceived' | 'Friends'

export type UserSearchResult = {
  id: string
  userHandle: string
  friendship: FriendshipState
  friendshipId: string | null
}

export type Friendship = {
  id: string
  user: UserSearchResult
  status: 'Pending' | 'Accepted' | 'Declined'
  isIncoming: boolean
  createdAt: string
}

export type FriendsOverview = {
  friends: Friendship[]
  incoming: Friendship[]
  outgoing: Friendship[]
}

export type ChatParticipant = {
  userId: string
  userHandle: string
}

export type ChatSummary = {
  id: string
  isGroup: boolean
  title: string
  participants: ChatParticipant[]
  lastMessagePreview: string | null
  lastMessageAt: string
  lastMessageSenderId: string | null
  unreadCount: number
}

export type ChatDetails = {
  id: string
  isGroup: boolean
  title: string
  createdById: string
  participants: ChatParticipant[]
  joinedAt: string
}

export type Message = {
  id: string
  chatRoomId: string
  senderId: string
  senderHandle: string
  content: string
  clientMessageId: string
  sentAt: string
}

export type MessagePage = {
  messages: Message[]
  hasMore: boolean
}

export type SendMessageAck = {
  chatRoomId: string
  id: string
  clientMessageId: string
  sentAt: string
}

export type ChatInvitation = {
  id: string
  chatRoomId: string
  chatTitle: string
  inviterHandle: string
  createdAt: string
}

export type GroupCreationResult = {
  chat: ChatSummary
  invitedHandles: string[]
}

export type NotificationItem = {
  id: string
  type: 'FriendAccepted' | 'AddedToGroup' | 'InvitationAccepted'
  referenceId: string
  actorHandle: string | null
  isRead: boolean
  createdAt: string
}

export type NotificationsPage = {
  items: NotificationItem[]
  unreadCount: number
}

export type TypingEvent = {
  chatRoomId: string
  userId: string
  userHandle: string
  isTyping: boolean
}

export type PresenceChanged = {
  userId: string
  isOnline: boolean
}

export type PresenceSnapshot = {
  onlineUserIds: string[]
}

export type MessageRead = {
  chatRoomId: string
  userId: string
  lastReadMessageId: string
  readAt: string
}
