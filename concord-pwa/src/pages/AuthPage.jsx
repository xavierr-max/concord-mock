import { useState } from 'react'
import { Brand } from '../components/Brand.jsx'
import { useAuth } from '../hooks/useAuth.js'

export function AuthPage() {
  const { authenticate } = useAuth()
  const [mode, setMode] = useState('login')
  const [form, setForm] = useState({ username: '', email: '', login: '', password: '' })
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const update = event => setForm(current => ({ ...current, [event.target.name]: event.target.value }))
  const submit = async event => {
    event.preventDefault(); setError(''); setLoading(true)
    try {
      const payload = mode === 'login'
        ? { login: form.login, password: form.password }
        : { username: form.username, email: form.email, password: form.password }
      await authenticate(mode, payload)
    } catch (reason) { setError(reason.message) } finally { setLoading(false) }
  }
  return <main className="auth-page">
    <section className="auth-branding"><Brand /><div><span className="eyebrow">COMUNICAÇÃO, REIMAGINADA</span><h1>Onde ideias ganham <em>conexão.</em></h1><p>Converse, colabore e esteja perto da sua comunidade.</p></div></section>
    <section className="auth-panel"><div className="auth-card"><Brand compact /><h2>{mode === 'login' ? 'Boas-vindas de volta' : 'Crie sua conta'}</h2><p>{mode === 'login' ? 'Entre para continuar no Concord.' : 'Comece uma nova comunidade.'}</p>
      <div className="auth-tabs"><button className={mode === 'login' ? 'active' : ''} onClick={() => setMode('login')}>Entrar</button><button className={mode === 'register' ? 'active' : ''} onClick={() => setMode('register')}>Cadastrar</button></div>
      <form onSubmit={submit}>{mode === 'register' && <><label>Username<input name="username" minLength="3" maxLength="32" required value={form.username} onChange={update} /></label><label>E-mail<input name="email" type="email" required value={form.email} onChange={update} /></label></>}<label>{mode === 'login' ? 'E-mail ou username' : 'Senha'}{mode === 'login' ? <input name="login" required value={form.login} onChange={update} /> : <input name="password" type="password" minLength="8" required value={form.password} onChange={update} />}</label>{mode === 'login' && <label>Senha<input name="password" type="password" required value={form.password} onChange={update} /></label>}{error && <div className="form-error">{error}</div>}<button className="primary-button" disabled={loading}>{loading ? 'Aguarde…' : mode === 'login' ? 'Entrar' : 'Criar conta'}</button></form>
    </div></section>
  </main>
}
