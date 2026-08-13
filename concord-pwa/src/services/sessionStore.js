const KEY = 'concord.session'

export const sessionStore = {
  get() {
    try { return JSON.parse(localStorage.getItem(KEY)) }
    catch { return null }
  },
  set(session) { localStorage.setItem(KEY, JSON.stringify(session)) },
  clear() { localStorage.removeItem(KEY) },
}
