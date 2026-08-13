import { useEffect, useState } from 'react'
import { authApi } from '../services/authApi.js'
import { sessionStore } from '../services/sessionStore.js'
import { setSessionChangedHandler } from '../services/apiClient.js'
import { AuthContext } from './authContext.js'

export function AuthProvider({ children }) {
  const [session, setSession] = useState(() => sessionStore.get())
  const [profile, setProfile] = useState(null)
  const [loading, setLoading] = useState(Boolean(session))

  useEffect(() => setSessionChangedHandler(next => { setSession(next); if (!next) setProfile(null) }), [])
  useEffect(() => {
    if (!sessionStore.get()) return
    let active = true
    Promise.all([authApi.me(), authApi.profile()]).then(([user, userProfile]) => {
      if (!active) return
      setSession(current => current ? { ...current, user } : current)
      setProfile(userProfile)
    }).catch(() => { sessionStore.clear(); if (active) setSession(null) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])

  const authenticate = async (mode, payload) => {
    const next = await authApi[mode](payload)
    sessionStore.set(next)
    setSession(next)
    setProfile(await authApi.profile())
  }
  const logout = async () => {
    try { if (session?.refreshToken) await authApi.logout(session.refreshToken) } finally {
      sessionStore.clear(); setSession(null); setProfile(null)
    }
  }
  const saveProfile = async payload => { const next = await authApi.updateProfile(payload); setProfile(next); return next }
  const saveAvatar = async avatar => { const next = await authApi.updateAvatar(avatar); setProfile(next); return next }
  const value = { session, user: session?.user, profile, loading, authenticate, logout, saveProfile, saveAvatar }
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
