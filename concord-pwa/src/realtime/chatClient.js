import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'

export const CHAT_EVENTS = Object.freeze({
  messageCreated: 'MessageCreated',
  messageUpdated: 'MessageUpdated',
  messageDeleted: 'MessageDeleted',
  userOnline: 'UserOnline',
  userOffline: 'UserOffline',
  userStatusChanged: 'UserStatusChanged',
  typingStarted: 'TypingStarted',
  typingStopped: 'TypingStopped',
})

export const CHAT_METHODS = Object.freeze({
  joinChannel: 'JoinChannel',
  leaveChannel: 'LeaveChannel',
  sendMessage: 'SendMessage',
  startTyping: 'StartTyping',
  stopTyping: 'StopTyping',
})

export function createChatClient({ apiUrl, getAccessToken, handlers = {} }) {
  const joinedChannels = new Set()
  const connection = new HubConnectionBuilder()
    .withUrl(`${apiUrl.replace(/\/$/, '')}/hubs/chat`, {
      accessTokenFactory: getAccessToken,
      withCredentials: false,
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()

  Object.values(CHAT_EVENTS).forEach((eventName) => {
    const handler = handlers[eventName]
    if (handler) connection.on(eventName, handler)
  })

  connection.onreconnected(async () => {
    await Promise.all([...joinedChannels].map((channelId) =>
      connection.invoke(CHAT_METHODS.joinChannel, channelId)))
  })

  return {
    connection,
    async connect() {
      if (connection.state === HubConnectionState.Disconnected) await connection.start()
    },
    async disconnect() {
      joinedChannels.clear()
      if (connection.state !== HubConnectionState.Disconnected) await connection.stop()
    },
    async joinChannel(channelId) {
      await connection.invoke(CHAT_METHODS.joinChannel, channelId)
      joinedChannels.add(channelId)
    },
    async leaveChannel(channelId) {
      await connection.invoke(CHAT_METHODS.leaveChannel, channelId)
      joinedChannels.delete(channelId)
    },
    sendMessage(channelId, content) {
      return connection.invoke(CHAT_METHODS.sendMessage, channelId, content)
    },
    createTypingController(channelId, options) {
      return createTypingController(connection, channelId, options)
    },
  }
}

export function createTypingController(connection, channelId, {
  throttleMs = 2_000,
  stopDebounceMs = 1_200,
} = {}) {
  let active = false
  let lastStartAt = 0
  let stopTimer

  const stop = async () => {
    clearTimeout(stopTimer)
    stopTimer = undefined
    if (!active || connection.state !== HubConnectionState.Connected) return
    active = false
    await connection.invoke(CHAT_METHODS.stopTyping, channelId)
  }

  return {
    input(content) {
      clearTimeout(stopTimer)
      if (!content.trim()) return stop()

      const now = Date.now()
      if (!active || now - lastStartAt >= throttleMs) {
        active = true
        lastStartAt = now
        void connection.invoke(CHAT_METHODS.startTyping, channelId)
      }
      stopTimer = setTimeout(() => void stop(), stopDebounceMs)
    },
    stop,
    dispose() {
      void stop()
    },
  }
}
