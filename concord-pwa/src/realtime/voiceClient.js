import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { getMicrophoneStream, stopMediaStream } from '../services/mediaDevices.js'

export const VOICE_EVENTS = Object.freeze({
  userJoined: 'VoiceUserJoined',
  userLeft: 'VoiceUserLeft',
  userUpdated: 'VoiceUserUpdated',
  offerReceived: 'VoiceOfferReceived',
  answerReceived: 'VoiceAnswerReceived',
  iceCandidateReceived: 'VoiceIceCandidateReceived',
})

export const VOICE_METHODS = Object.freeze({
  join: 'JoinVoiceChannel',
  leave: 'LeaveVoiceChannel',
  setMute: 'SetMute',
  setDeafened: 'SetDeafened',
  sendOffer: 'SendOffer',
  sendAnswer: 'SendAnswer',
  sendIceCandidate: 'SendIceCandidate',
})

const RECONNECT_POLICY = {
  nextRetryDelayInMilliseconds: ({ previousRetryCount }) =>
    [0, 2_000, 5_000, 10_000][previousRetryCount] ?? 15_000,
}

export class VoiceClientError extends Error {
  constructor(code, message, cause) {
    super(message, { cause })
    this.name = 'VoiceClientError'
    this.code = code
  }
}

export function createVoiceClient({
  apiUrl,
  getAccessToken,
  rtcConfiguration = {},
  onStatusChange = () => {},
  onParticipantChange = () => {},
  onError = () => {},
} = {}) {
  let localStream
  let joinedChannelId
  let currentUserId
  let intentionalDisconnect = false
  let reconnecting = false
  let deafened = false
  let audioContext
  let localSpeakingMonitor
  let lifecycle = 0
  let disposed = false
  const peers = new Map()
  const participants = new Map()
  const participantVolumes = new Map()

  const connection = new HubConnectionBuilder()
    .withUrl(`${apiUrl.replace(/\/$/, '')}/hubs/voice`, {
      accessTokenFactory: getAccessToken,
      withCredentials: false,
    })
    .withAutomaticReconnect(RECONNECT_POLICY)
    .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning)
    .build()

  const emitParticipants = () => onParticipantChange([...participants.values()])

  function mergeParticipant(participant) {
    const current = participants.get(participant.userId)
    participants.set(participant.userId, {
      ...current,
      ...participant,
      isLocal: participant.userId === currentUserId,
      isSpeaking: current?.isSpeaking ?? false,
      volume: participantVolumes.get(participant.userId) ?? current?.volume ?? 1,
    })
    emitParticipants()
  }

  function startSpeakingMonitor(userId, stream) {
    const AudioContext = window.AudioContext || window.webkitAudioContext
    if (!AudioContext || !stream?.getAudioTracks().length) return () => {}
    audioContext ??= new AudioContext()
    void audioContext.resume()
    const analyser = audioContext.createAnalyser()
    analyser.fftSize = 512
    analyser.smoothingTimeConstant = .45
    const source = audioContext.createMediaStreamSource(stream)
    const samples = new Float32Array(analyser.fftSize)
    let animationFrame
    let lastSpeaking = false
    let speakingUntil = 0
    source.connect(analyser)

    const analyse = () => {
      analyser.getFloatTimeDomainData(samples)
      let sum = 0
      for (const sample of samples) sum += sample * sample
      const active = Math.sqrt(sum / samples.length) > .035
      if (active) speakingUntil = performance.now() + 180
      const speaking = performance.now() < speakingUntil
      if (speaking !== lastSpeaking && participants.has(userId)) {
        lastSpeaking = speaking
        participants.set(userId, { ...participants.get(userId), isSpeaking: speaking })
        emitParticipants()
      }
      animationFrame = requestAnimationFrame(analyse)
    }
    analyse()
    return () => {
      cancelAnimationFrame(animationFrame)
      source.disconnect()
      analyser.disconnect()
    }
  }

  function closePeer(userId) {
    const peer = peers.get(userId)
    if (!peer) return
    peer.pc.onicecandidate = null
    peer.pc.ontrack = null
    peer.pc.onconnectionstatechange = null
    clearTimeout(peer.recoveryTimer)
    peer.stopSpeakingMonitor?.()
    peer.pc.close()
    if (peer.audio) {
      peer.audio.pause()
      peer.audio.srcObject = null
      peer.audio.remove()
    }
    peers.delete(userId)
  }

  function closeAllPeers() {
    for (const userId of [...peers.keys()]) closePeer(userId)
  }

  function stopMicrophone() {
    stopMediaStream(localStream)
    localSpeakingMonitor?.()
    localSpeakingMonitor = undefined
    localStream = undefined
  }

  function createPeer(userId) {
    const existing = peers.get(userId)
    if (existing) return existing

    const pc = new RTCPeerConnection(rtcConfiguration)
    const peer = { pc, pendingCandidates: [], audio: null, stopSpeakingMonitor: null, recoveryTimer: null, recovering: false }
    peers.set(userId, peer)
    localStream?.getAudioTracks().forEach((track) => pc.addTrack(track, localStream))

    pc.onicecandidate = ({ candidate }) => {
      if (!candidate || connection.state !== HubConnectionState.Connected) return
      void connection.invoke(
        VOICE_METHODS.sendIceCandidate, userId, JSON.stringify(candidate.toJSON()))
        .catch((error) => onError(normalizeError(error)))
    }
    pc.ontrack = ({ streams, track }) => {
      if (!peer.audio) {
        peer.audio = document.createElement('audio')
        peer.audio.autoplay = true
        peer.audio.dataset.voiceUserId = userId
        peer.audio.hidden = true
        document.body.appendChild(peer.audio)
      }
      peer.audio.srcObject = streams[0] ?? new MediaStream([track])
      peer.audio.volume = participantVolumes.get(userId) ?? 1
      peer.audio.muted = deafened
      peer.stopSpeakingMonitor?.()
      peer.stopSpeakingMonitor = startSpeakingMonitor(userId, peer.audio.srcObject)
      void peer.audio.play().catch(() => {
        onError(new VoiceClientError(
          'AUTOPLAY_BLOCKED', 'O navegador bloqueou a reprodução automática do áudio remoto.'))
      })
    }
    pc.onconnectionstatechange = () => {
      if (pc.connectionState === 'failed') {
        void restartIce(userId).catch(handleAsyncError)
      }
      if (pc.connectionState === 'closed') closePeer(userId)
      if (pc.connectionState === 'connected') {
        clearTimeout(peer.recoveryTimer)
        peer.recoveryTimer = null
        peer.recovering = false
        onStatusChange('connected')
      }
      if (pc.connectionState === 'disconnected') {
        onStatusChange('peer-disconnected')
        clearTimeout(peer.recoveryTimer)
        peer.recoveryTimer = setTimeout(() => {
          if (pc.connectionState === 'disconnected') void restartIce(userId).catch(handleAsyncError)
        }, 3_000)
      }
    }
    return peer
  }

  async function flushCandidates(peer) {
    if (!peer.pc.remoteDescription) return
    for (const candidate of peer.pendingCandidates.splice(0))
      await peer.pc.addIceCandidate(candidate)
  }

  async function makeOffer(targetUserId) {
    if (!joinedChannelId || targetUserId === currentUserId) return
    const { pc } = createPeer(targetUserId)
    const offer = await pc.createOffer()
    await pc.setLocalDescription(offer)
    await connection.invoke(VOICE_METHODS.sendOffer, targetUserId, offer.sdp)
  }

  async function restartIce(targetUserId) {
    const peer = peers.get(targetUserId)
    if (!peer || peer.recovering || !joinedChannelId || connection.state !== HubConnectionState.Connected) return
    peer.recovering = true
    clearTimeout(peer.recoveryTimer)
    peer.recoveryTimer = null
    try {
      peer.pc.restartIce?.()
      const offer = await peer.pc.createOffer({ iceRestart: true })
      await peer.pc.setLocalDescription(offer)
      await connection.invoke(VOICE_METHODS.sendOffer, targetUserId, offer.sdp)
    } catch {
      closePeer(targetUserId)
      await makeOffer(targetUserId)
    }
  }

  const shouldInitiate = userId => currentUserId && currentUserId.localeCompare(userId) < 0

  connection.on(VOICE_EVENTS.userJoined, (participant) => {
    mergeParticipant(participant)
    if (participant.userId !== currentUserId && shouldInitiate(participant.userId))
      void makeOffer(participant.userId).catch(handleAsyncError)
  })
  connection.on(VOICE_EVENTS.userUpdated, (participant) => {
    mergeParticipant(participant)
  })
  connection.on(VOICE_EVENTS.userLeft, (participant) => {
    participants.delete(participant.userId)
    closePeer(participant.userId)
    emitParticipants()
  })
  connection.on(VOICE_EVENTS.offerReceived, async ({ senderUserId, channelId, sdp }) => {
    if (channelId !== joinedChannelId) return
    try {
      if (!participants.has(senderUserId)) {
        mergeParticipant({ userId: senderUserId, channelId })
      }
      const peer = createPeer(senderUserId)
      await peer.pc.setRemoteDescription({ type: 'offer', sdp })
      await flushCandidates(peer)
      const answer = await peer.pc.createAnswer()
      await peer.pc.setLocalDescription(answer)
      await connection.invoke(VOICE_METHODS.sendAnswer, senderUserId, answer.sdp)
    } catch (error) { handleAsyncError(error) }
  })
  connection.on(VOICE_EVENTS.answerReceived, async ({ senderUserId, channelId, sdp }) => {
    if (channelId !== joinedChannelId) return
    try {
      const peer = peers.get(senderUserId)
      if (!peer) return
      await peer.pc.setRemoteDescription({ type: 'answer', sdp })
      await flushCandidates(peer)
    } catch (error) { handleAsyncError(error) }
  })
  connection.on(VOICE_EVENTS.iceCandidateReceived, async ({ senderUserId, channelId, candidate }) => {
    if (channelId !== joinedChannelId) return
    try {
      const peer = createPeer(senderUserId)
      const iceCandidate = new RTCIceCandidate(JSON.parse(candidate))
      if (peer.pc.remoteDescription) await peer.pc.addIceCandidate(iceCandidate)
      else peer.pendingCandidates.push(iceCandidate)
    } catch (error) { handleAsyncError(error) }
  })

  connection.onreconnecting(() => {
    reconnecting = true
    closeAllPeers()
    onStatusChange('reconnecting')
  })
  connection.onreconnected(async () => {
    if (!joinedChannelId || !localStream) return
    try {
      participants.clear()
      const participant = await connection.invoke(VOICE_METHODS.join, joinedChannelId)
      mergeParticipant(participant)
      for (const userId of participants.keys())
        if (userId !== currentUserId && shouldInitiate(userId))
          await makeOffer(userId)
      reconnecting = false
      onStatusChange('connected')
    } catch (error) { handleAsyncError(error) }
  })
  connection.onclose((error) => {
    closeAllPeers()
    participants.clear()
    emitParticipants()
    if (!intentionalDisconnect) {
      joinedChannelId = undefined
      stopMicrophone()
      void audioContext?.close()
      audioContext = undefined
      onStatusChange('disconnected')
      if (error) onError(normalizeError(error))
    }
  })

  function handleAsyncError(error) {
    onError(normalizeError(error))
  }

  return {
    connection,
    get isJoined() { return Boolean(joinedChannelId) },
    async join(channelId) {
      if (disposed) throw new VoiceClientError('DISPOSED', 'A conexão de voz foi encerrada.')
      const operation = ++lifecycle
      if (!channelId) throw new VoiceClientError('CONFIGURATION', 'Canal de voz não configurado.')
      if (!navigator.mediaDevices?.getUserMedia)
        throw new VoiceClientError('NO_MEDIA_DEVICES', 'Este dispositivo ou navegador não oferece acesso ao microfone.')
      if (joinedChannelId === channelId && localStream) return

      const token = await getAccessToken()
      currentUserId = getUserIdFromToken(token)
      onStatusChange('requesting-microphone')
      try {
        const stream = await getMicrophoneStream()
        if (disposed || operation !== lifecycle) { stopMediaStream(stream); return }
        localStream = stream
      } catch (error) {
        throw new VoiceClientError(error.code || 'MICROPHONE_ERROR', error.message, error)
      }

      try {
        intentionalDisconnect = false
        if (connection.state === HubConnectionState.Disconnected) await connection.start()
        if (disposed || operation !== lifecycle) { stopMicrophone(); return }
        // Set before invoking so an offer triggered by VoiceUserJoined cannot race the hub completion.
        joinedChannelId = channelId
        const participant = await connection.invoke(VOICE_METHODS.join, channelId)
        mergeParticipant(participant)
        for (const userId of participants.keys())
          if (userId !== currentUserId && shouldInitiate(userId))
            await makeOffer(userId)
        localSpeakingMonitor = startSpeakingMonitor(currentUserId, localStream)
        onStatusChange('connected')
      } catch (error) {
        joinedChannelId = undefined
        stopMicrophone()
        throw normalizeError(error)
      }
    },
    async leave() {
      lifecycle += 1
      intentionalDisconnect = true
      try {
        if (connection.state === HubConnectionState.Connected && joinedChannelId)
          await connection.invoke(VOICE_METHODS.leave)
      } finally {
        joinedChannelId = undefined
        reconnecting = false
        closeAllPeers()
        stopMicrophone()
        void audioContext?.close()
        audioContext = undefined
        participants.clear()
        emitParticipants()
        if (connection.state !== HubConnectionState.Disconnected) await connection.stop()
        onStatusChange('idle')
      }
    },
    async setMuted(muted) {
      if (!joinedChannelId) return
      localStream?.getAudioTracks().forEach((track) => { track.enabled = !muted })
      try {
        await connection.invoke(VOICE_METHODS.setMute, muted)
      } catch (error) {
        localStream?.getAudioTracks().forEach((track) => { track.enabled = muted })
        throw error
      }
    },
    async setDeafened(value) {
      const previous = deafened
      deafened = value
      for (const { audio } of peers.values()) if (audio) audio.muted = value
      try {
        if (joinedChannelId) await connection.invoke(VOICE_METHODS.setDeafened, value)
      } catch (error) {
        deafened = previous
        for (const { audio } of peers.values()) if (audio) audio.muted = previous
        throw error
      }
    },
    setParticipantVolume(userId, volume) {
      const normalized = Math.max(0, Math.min(1, Number(volume)))
      participantVolumes.set(userId, normalized)
      const peer = peers.get(userId)
      if (peer?.audio) peer.audio.volume = normalized
      if (participants.has(userId)) {
        participants.set(userId, { ...participants.get(userId), volume: normalized })
        emitParticipants()
      }
    },
    async dispose() { disposed = true; await this.leave() },
    get reconnecting() { return reconnecting },
  }
}

function normalizeError(error) {
  return error instanceof VoiceClientError
    ? error
    : new VoiceClientError('VOICE_CONNECTION', 'Não foi possível manter a conexão de voz.', error)
}

function getUserIdFromToken(token) {
  try {
    const payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
    const claims = JSON.parse(atob(payload))
    if (!claims.sub) throw new Error('missing sub')
    return claims.sub
  } catch (error) {
    throw new VoiceClientError('INVALID_TOKEN', 'Token de acesso inválido.', error)
  }
}
