import * as signalR from '@microsoft/signalr'

import { getAccessToken, refreshSession } from '../api/client'
import type {
  ChatSummary,
  Message,
  MessageRead,
  PresenceChanged,
  PresenceSnapshot,
  SendMessageAck,
  TypingEvent,
} from '../api/types'

type EventMap = {
  message: Message
  read: MessageRead
  typing: TypingEvent
  presence: PresenceChanged
  presenceSnapshot: PresenceSnapshot
  chatCreated: ChatSummary
  chatUpdated: ChatSummary
  notification: unknown
  friendRequestUpdated: unknown
  invitationReceived: unknown
  state: signalR.HubConnectionState
  reconnected: void
}

type Listener<K extends keyof EventMap> = (payload: EventMap[K]) => void

const serverEvents: Record<string, keyof EventMap> = {
  ReceiveMessage: 'message',
  MessageRead: 'read',
  TypingChanged: 'typing',
  PresenceChanged: 'presence',
  PresenceSnapshot: 'presenceSnapshot',
  ChatCreated: 'chatCreated',
  ChatUpdated: 'chatUpdated',
  Notification: 'notification',
  FriendRequestUpdated: 'friendRequestUpdated',
  InvitationReceived: 'invitationReceived',
  InvitationUpdated: 'invitationReceived',
}

const reconnectedEvent = 'reconnected'

/** Thin typed wrapper over the SignalR connection: subscribers receive decoded payloads. */
export class ChatConnection {
  private connection: signalR.HubConnection | null = null
  private startPromise: Promise<void> | null = null
  private listeners = new Map<string, Set<(payload: unknown) => void>>()

  isConnected() {
    return this.connection?.state === signalR.HubConnectionState.Connected
  }

  get state() {
    return this.connection?.state ?? signalR.HubConnectionState.Disconnected
  }

  on<K extends keyof EventMap>(event: K, listener: Listener<K>) {
    const key = event as string
    const bucket = this.listeners.get(key) ?? new Set()
    bucket.add(listener as (payload: unknown) => void)
    this.listeners.set(key, bucket)

    return () => {
      bucket.delete(listener as (payload: unknown) => void)
    }
  }

  private emit<K extends keyof EventMap>(event: K, payload: EventMap[K]) {
    this.listeners.get(event as string)?.forEach((listener) => listener(payload))
  }

  async start() {
    if (this.connection) {
      await this.ensureStarted()
      // A subscriber registered after the initial connect still needs the current state,
      // otherwise the UI believes the connection is down.
      this.emit('state', this.state)
      return
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/chat', { accessTokenFactory: () => getAccessToken() ?? '' })
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 20000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    for (const [serverEvent, localEvent] of Object.entries(serverEvents)) {
      connection.on(serverEvent, (payload: unknown) => {
        this.emit(localEvent, payload as EventMap[typeof localEvent])
      })
    }

    connection.onreconnecting(() => this.emit('state', connection.state))
    connection.onreconnected(() => {
      this.emit('state', connection.state)
      this.emit(reconnectedEvent, undefined)
    })
    connection.onclose(() => this.emit('state', connection.state))

    this.connection = connection

    await this.ensureStarted()
  }

  private async ensureStarted() {
    const connection = this.connection
    if (!connection || connection.state === signalR.HubConnectionState.Connected) return

    this.startPromise ??= (async () => {
      // The hub handshake fails with 401 when the in-memory access token expired.
      if (!getAccessToken()) {
        await refreshSession()
      }

      try {
        await connection.start()
      } catch {
        await refreshSession()

        if (connection.state === signalR.HubConnectionState.Disconnected) {
          await connection.start()
        }
      }

      this.emit('state', connection.state)
    })().finally(() => {
      this.startPromise = null
    })

    return this.startPromise
  }

  async stop() {
    const connection = this.connection
    this.connection = null

    if (connection) {
      await connection.stop()
    }
  }

  async onlineCoMembers(): Promise<string[]> {
    if (!this.isConnected()) return []
    return this.connection!.invoke<string[]>('GetOnlineCoMembers')
  }

  async joinChat(chatId: string) {
    if (!this.isConnected()) return
    await this.connection!.invoke('JoinChat', chatId)
  }

  async sendMessage(chatId: string, clientMessageId: string, content: string): Promise<SendMessageAck> {
    if (!this.isConnected()) {
      await this.ensureStarted()
    }

    return this.connection!.invoke<SendMessageAck>('SendMessage', chatId, clientMessageId, content)
  }

  async typing(chatId: string, isTyping: boolean) {
    if (!this.isConnected()) return
    await this.connection!.invoke('Typing', chatId, isTyping)
  }

  async markRead(chatId: string, lastReadMessageId: string) {
    if (!this.isConnected()) return
    await this.connection!.invoke('MarkRead', chatId, lastReadMessageId)
  }
}

export const chatConnection = new ChatConnection()
