import './App.css'
import { useAuth } from './hooks/useAuth.js'
import { AuthPage } from './pages/AuthPage.jsx'
import { WorkspacePage } from './pages/WorkspacePage.jsx'
import { Loading } from './components/Feedback.jsx'

export default function App() {
  const { session, loading } = useAuth()
  if (loading) return <Loading label="Restaurando sua sessão…" />
  return session ? <WorkspacePage /> : <AuthPage />
}
