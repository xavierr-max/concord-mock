export const env = Object.freeze({
  apiUrl: (import.meta.env.VITE_API_URL || 'http://localhost:5187').replace(/\/$/, ''),
  requestTimeoutMs: Number(import.meta.env.VITE_REQUEST_TIMEOUT_MS || 15_000),
  iceServers: parseIceServers(import.meta.env.VITE_WEBRTC_ICE_SERVERS),
})

function parseIceServers(value) {
  if (!value) return []
  try { return JSON.parse(value) }
  catch { return [] }
}
