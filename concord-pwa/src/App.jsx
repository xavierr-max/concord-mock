import './App.css'
import { useAuth } from './hooks/useAuth.js'
import { AuthPage } from './pages/AuthPage.jsx'
import { WorkspacePage } from './pages/WorkspacePage.jsx'
import { Loading } from './components/Feedback.jsx'
import { LandingPage } from './pages/LandingPage.jsx'
import { useRoute } from './hooks/useRoute.js'

export default function App() {
  const { session, loading } = useAuth()
  const { path, navigate } = useRoute()
  if (loading) return <Loading label="Restaurando sua sessão…" />
  if (path === '/app') return session ? <WorkspacePage /> : <AuthPage navigate={navigate} />
  if (path === '/login') return session ? <LandingPage user={session.user} navigate={navigate} /> : <AuthPage navigate={navigate} />
  if (path === '/register') return session ? <LandingPage user={session.user} navigate={navigate} /> : <AuthPage initialMode="register" navigate={navigate} />
  return <LandingPage user={session?.user} navigate={navigate} />
}
