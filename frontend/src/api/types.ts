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

export type SystemEventName =
  | 'FriendRequestReceived'
  | 'FriendRequestAccepted'
  | 'FriendRequestDeclined'
  | 'FriendRequestCancelled'
  | 'ChatInvitationReceived'
  | 'ChatInvitationAccepted'
  | 'ChatInvitationDeclined'
  | 'AddedToGroup'
  | 'FriendRemoved'

export type SystemEventPayload = {
  event: SystemEventName
  actorId: string | null
  actorHandle: string | null
  referenceId: string | null
  referenceName: string | null
}

export type ChatKind = 'Direct' | 'Group' | 'System'

export type ChatSummary = {
  id: string
  kind: ChatKind
  isGroup: boolean
  title: string
  participants: ChatParticipant[]
  lastMessagePreview: string | null
  lastMessageAt: string
  lastMessageSenderId: string | null
  unreadCount: number
  lastSystemEvent: SystemEventPayload | null
}

export type ChatSearchResult = {
  id: string
  kind: ChatKind
  isGroup: boolean
  title: string
  participants: ChatParticipant[]
  unreadCount: number
}

export type SearchResponse = {
  chats: ChatSearchResult[]
  users: UserSearchResult[]
}

export type ChatDetails = {
  id: string
  kind: ChatKind
  isGroup: boolean
  title: string
  createdById: string
  participants: ChatParticipant[]
  joinedAt: string
}

export type Message = {
  id: string
  chatRoomId: string
  kind: 'User' | 'System'
  senderId: string | null
  senderHandle: string | null
  content: string | null
  clientMessageId: string
  sentAt: string
  systemEvent: SystemEventPayload | null
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
