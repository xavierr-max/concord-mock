import { useEffect, useRef, useState } from 'react'
import { createVoiceClient } from '../realtime/voiceClient.js'
import { env } from '../config/env.js'
import { sessionStore } from '../services/sessionStore.js'

export function useVoice(onError) {
  const clientRef = useRef()
  const errorHandler = useRef(onError)
  const [status, setStatus] = useState('idle')
  const [participants, setParticipants] = useState([])
  const [muted, setMutedState] = useState(false)
  const [deafened, setDeafenedState] = useState(false)

  useEffect(() => {
    const client = createVoiceClient({
      apiUrl: env.apiUrl,
      getAccessToken: async () => sessionStore.get()?.accessToken,
      rtcConfiguration: { iceServers: env.iceServers },
      onStatusChange: setStatus,
      onParticipantChange: setParticipants,
      onError: error => errorHandler.current?.(error),
    })
    clientRef.current = client
    return () => { clientRef.current = null; void client.leave() }
  }, [])

  return {
    status, participants, muted, deafened,
    join: channelId => clientRef.current?.join(channelId),
    async leave() { await clientRef.current?.leave(); setMutedState(false); setDeafenedState(false) },
    async setMuted(value) { await clientRef.current?.setMuted(value); setMutedState(value) },
    async setDeafened(value) { await clientRef.current?.setDeafened(value); setDeafenedState(value) },
    setVolume: (userId, value) => clientRef.current?.setParticipantVolume(userId, value),
    isJoined: ['connected', 'reconnecting', 'peer-disconnected'].includes(status),
  }
}
