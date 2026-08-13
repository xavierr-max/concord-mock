import { useEffect, useRef } from 'react'
import { createChatClient, CHAT_EVENTS } from '../realtime/chatClient.js'
import { env } from '../config/env.js'
import { sessionStore } from '../services/sessionStore.js'

export function useChat({ channelId, onMessage, onPresence, onTyping, onError }) {
  const clientRef = useRef()
  const channelRef = useRef()
  const callbacks = useRef({ onMessage, onPresence, onTyping, onError })
  useEffect(() => { callbacks.current = { onMessage, onPresence, onTyping, onError } },
    [onMessage, onPresence, onTyping, onError])

  useEffect(() => {
    let active = true
    const handlers = {
      [CHAT_EVENTS.messageCreated]: message => callbacks.current.onMessage?.('created', message),
      [CHAT_EVENTS.messageUpdated]: message => callbacks.current.onMessage?.('updated', message),
      [CHAT_EVENTS.messageDeleted]: message => callbacks.current.onMessage?.('deleted', message),
      [CHAT_EVENTS.userOnline]: update => callbacks.current.onPresence?.(update),
      [CHAT_EVENTS.userOffline]: update => callbacks.current.onPresence?.(update),
      [CHAT_EVENTS.userStatusChanged]: update => callbacks.current.onPresence?.(update),
      [CHAT_EVENTS.typingStarted]: update => callbacks.current.onTyping?.(true, update),
      [CHAT_EVENTS.typingStopped]: update => callbacks.current.onTyping?.(false, update),
    }
    const client = createChatClient({
      apiUrl: env.apiUrl,
      getAccessToken: async () => sessionStore.get()?.accessToken,
      handlers,
    })
    clientRef.current = client
    client.connect().catch(error => { if (active) callbacks.current.onError?.(error) })
    return () => { active = false; clientRef.current = null; void client.disconnect() }
  }, [])

  useEffect(() => {
    const client = clientRef.current
    if (!client) return
    let cancelled = false
    const changeChannel = async () => {
      try {
        if (channelRef.current) await client.leaveChannel(channelRef.current)
        channelRef.current = channelId
        if (channelId && !cancelled) await client.joinChannel(channelId)
      } catch (error) { if (!cancelled) callbacks.current.onError?.(error) }
    }
    void changeChannel()
    return () => { cancelled = true }
  }, [channelId])

  return {
    sendMessage: content => clientRef.current?.sendMessage(channelId, content),
    createTypingController: options => clientRef.current?.createTypingController(channelId, options),
  }
}
