import { useEffect, useState } from 'react'

export function useRoute() {
  const [path, setPath] = useState(() => window.location.pathname)
  useEffect(() => {
    const update = () => setPath(window.location.pathname)
    window.addEventListener('popstate', update)
    return () => window.removeEventListener('popstate', update)
  }, [])
  const navigate = next => {
    if (next === window.location.pathname) return
    window.history.pushState({}, '', next)
    setPath(next)
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }
  return { path, navigate }
}
