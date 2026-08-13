import { useCallback, useEffect, useRef, useState } from 'react'
import { env } from '../config/env.js'
import { concordApi } from '../services/concordApi.js'
import { sessionStore } from '../services/sessionStore.js'
import { createNotificationClient } from '../realtime/notificationClient.js'

export function useNotifications(onError) {
  const [items, setItems] = useState([])
  const [unreadCount, setUnreadCount] = useState(0)
  const errorRef = useRef(onError)
  useEffect(() => { errorRef.current = onError }, [onError])

  const refresh = useCallback(async () => {
    try {
      const [page, count] = await Promise.all([
        concordApi.notifications.list(), concordApi.notifications.unread(),
      ])
      setItems(page.items)
      setUnreadCount(count.unreadCount)
    } catch (error) { errorRef.current?.(error) }
  }, [])

  useEffect(() => {
    let active = true
    void concordApi.notifications.list().then(page => { if (active) setItems(page.items) })
      .catch(error => { if (active) errorRef.current?.(error) })
    void concordApi.notifications.unread().then(count => { if (active) setUnreadCount(count.unreadCount) })
      .catch(error => { if (active) errorRef.current?.(error) })
    const client = createNotificationClient({
      apiUrl: env.apiUrl,
      getAccessToken: async () => sessionStore.get()?.accessToken,
      handlers: {
        created: notification => {
          if (!active) return
          setItems(current => [notification, ...current.filter(item => item.id !== notification.id)])
          setUnreadCount(current => current + 1)
        },
        read: notification => {
          if (!active) return
          setItems(current => current.map(item => item.id === notification.id ? notification : item))
          setUnreadCount(current => Math.max(0, current - 1))
        },
        allRead: () => {
          if (!active) return
          setItems(current => current.map(item => ({ ...item, isRead: true })))
          setUnreadCount(0)
        },
      },
    })
    client.connect().catch(error => { if (active) errorRef.current?.(error) })
    return () => { active = false; void client.disconnect() }
  }, [refresh])

  return {
    items, unreadCount, refresh,
    async markRead(id) { await concordApi.notifications.markRead(id) },
    async markAllRead() { await concordApi.notifications.markAllRead() },
  }
}
