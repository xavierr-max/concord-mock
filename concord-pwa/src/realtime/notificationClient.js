import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'

export const NOTIFICATION_EVENTS = Object.freeze({
  created: 'NotificationCreated',
  read: 'NotificationRead',
  allRead: 'AllNotificationsRead',
})

export function createNotificationClient({ apiUrl, getAccessToken, handlers = {} }) {
  let startPromise
  let disposed = false
  const connection = new HubConnectionBuilder()
    .withUrl(`${apiUrl.replace(/\/$/, '')}/hubs/notifications`, {
      accessTokenFactory: getAccessToken,
      withCredentials: false,
    })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: ({ previousRetryCount }) =>
        [0, 2_000, 5_000, 10_000][previousRetryCount] ?? 15_000,
    })
    .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning)
    .build()

  Object.entries(NOTIFICATION_EVENTS).forEach(([key, event]) => {
    if (handlers[key]) connection.on(event, handlers[key])
  })

  return {
    async connect() {
      if (disposed || connection.state === HubConnectionState.Connected) return
      startPromise ??= connection.start().finally(() => { startPromise = undefined })
      await startPromise
    },
    async disconnect() {
      disposed = true
      if (startPromise) try { await startPromise } catch { /* reported by connect */ }
      if (connection.state !== HubConnectionState.Disconnected) await connection.stop()
    },
  }
}
