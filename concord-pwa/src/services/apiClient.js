import { env } from '../config/env.js'
import { sessionStore } from './sessionStore.js'

export class ApiError extends Error {
  constructor(message, status = 0, details = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.details = details
  }
}

let refreshPromise
let onSessionChanged = () => {}

export function setSessionChangedHandler(handler) { onSessionChanged = handler }

export async function apiRequest(path, options = {}) {
  const { anonymous = false, retry = true, timeoutMs = env.requestTimeoutMs, ...fetchOptions } = options
  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), timeoutMs)
  const session = sessionStore.get()
  const headers = new Headers(fetchOptions.headers)
  if (!(fetchOptions.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  if (!anonymous && session?.accessToken) headers.set('Authorization', `Bearer ${session.accessToken}`)
  try {
    const response = await fetch(`${env.apiUrl}${path}`, { ...fetchOptions, headers, signal: controller.signal })
    if (response.status === 401 && !anonymous && retry && session?.refreshToken) {
      const refreshed = await refreshSession(session.refreshToken)
      if (refreshed) return apiRequest(path, { ...options, retry: false })
    }
    if (!response.ok) throw await createApiError(response)
    if (response.status === 204) return null
    return response.json()
  } catch (error) {
    if (error.name === 'AbortError') throw new ApiError('A solicitação excedeu o tempo limite.')
    if (error instanceof ApiError) throw error
    throw new ApiError('Não foi possível conectar à API.', 0, error)
  } finally { clearTimeout(timeout) }
}

async function refreshSession(refreshToken) {
  refreshPromise ??= fetch(`${env.apiUrl}/api/auth/refresh`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
  }).then(async response => {
    if (!response.ok) throw new Error('refresh failed')
    const session = await response.json()
    sessionStore.set(session)
    onSessionChanged(session)
    return session
  }).catch(() => {
    sessionStore.clear()
    onSessionChanged(null)
    return null
  }).finally(() => { refreshPromise = null })
  return refreshPromise
}

async function createApiError(response) {
  let details
  try { details = await response.json() } catch { details = null }
  const validation = details?.errors && Object.values(details.errors).flat().join(' ')
  const message = validation || details?.detail || details?.title || statusMessage(response.status)
  return new ApiError(message, response.status, details)
}

function statusMessage(status) {
  if (status === 401) return 'Sua sessão expirou. Entre novamente.'
  if (status === 403) return 'Você não possui permissão para esta ação.'
  if (status === 404) return 'O recurso solicitado não foi encontrado.'
  if (status === 409) return 'A operação conflita com o estado atual.'
  return 'Não foi possível concluir a operação.'
}
