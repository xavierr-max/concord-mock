import { useState } from 'react'
import './App.css'

const Icon = ({ name, size = 18 }) => {
  const paths = {
    bolt: <><path d="m13 2-9 12h7l-1 8 9-12h-7l1-8Z"/></>,
    hash: <><path d="M4 9h16M3 15h16M10 3 8 21M16 3l-2 18"/></>,
    volume: <><path d="M11 5 6 9H3v6h3l5 4V5Z"/><path d="M15.5 9.5a4 4 0 0 1 0 5M18 7a8 8 0 0 1 0 10"/></>,
    send: <path d="m22 2-7 20-4-9-9-4 20-7Z"/>,
    plus: <><path d="M12 5v14M5 12h14"/></>,
    search: <><circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/></>,
    users: <><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/></>,
    phone: <><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.8 19.8 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.12 4.18 2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.12.9.33 1.78.62 2.64a2 2 0 0 1-.45 2.11L8 9.73a16 16 0 0 0 6 6l1.26-1.26a2 2 0 0 1 2.11-.45c.86.29 1.74.5 2.64.62A2 2 0 0 1 22 16.92Z"/></>,
    settings: <><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .34 1.88l.06.06-2.12 2.12-.06-.06a1.7 1.7 0 0 0-1.88-.34 1.7 1.7 0 0 0-1 1.55V20.3h-3v-.09a1.7 1.7 0 0 0-1-1.55 1.7 1.7 0 0 0-1.88.34l-.06.06-2.12-2.12.06-.06A1.7 1.7 0 0 0 7.08 15a1.7 1.7 0 0 0-1.55-1H5.4v-3h.13a1.7 1.7 0 0 0 1.55-1 1.7 1.7 0 0 0-.34-1.88l-.06-.06L8.8 5.94l.06.06a1.7 1.7 0 0 0 1.88.34 1.7 1.7 0 0 0 1-1.55V4.7h3v.09a1.7 1.7 0 0 0 1 1.55 1.7 1.7 0 0 0 1.88-.34l.06-.06 2.12 2.12-.06.06A1.7 1.7 0 0 0 19.4 10a1.7 1.7 0 0 0 1.55 1h.13v3h-.13a1.7 1.7 0 0 0-1.55 1Z"/></>,
    menu: <><path d="M4 6h16M4 12h16M4 18h16"/></>,
  }
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">{paths[name]}</svg>
}

// Marca original do Concord: uma sentinela angular com olhar hostil.
const Sentinel = ({ size = 28, className = '' }) => <svg className={className} width={size} height={size} viewBox="0 0 64 64" aria-label="Símbolo Concord" role="img"><defs><linearGradient id="sentinelRed" x1="0" y1="0" x2="1" y2="1"><stop stopColor="#ff7272"/><stop offset=".42" stopColor="#ff2635"/><stop offset="1" stopColor="#9e101c"/></linearGradient></defs><path fill="url(#sentinelRed)" d="M9 21 16 9l7 8c6-5 19-5 25 0l7-8 7 12-5 28-12-7H19L7 49 2 21l7 0Z"/><path fill="#10070a" d="m13 28 15 5-10 8c-5-2-7-6-5-13Zm38 0-15 5 10 8c5-2 7-6 5-13ZM24 45h16l-3 5H27l-3-5Z"/><path fill="#ffd0d0" opacity=".55" d="m11 22 6-10 4 5c-4 1-7 3-10 5Z"/></svg>

const avatars = ['AL','MR','DV','KA','JS']
const messages = [
  ['AL','Aurora Lima','09:41','O protótipo do novo painel está pronto. Deixei o fluxo de criação bem mais direto.'],
  ['MR','Marcos Rocha','09:43','Ficou excelente. O contraste da interface está bem mais confortável agora.'],
  ['DV','Davi Valença','09:46','Vou validar os estados no mobile e retorno com os ajustes.'],
]

function Landing({ openApp }) {
  return <div className="landing">
    <nav className="marketing-nav"><div className="brand"><span className="brand-mark"><Sentinel size={22}/></span> concord</div><div className="nav-links"><a>Produto</a><a>Comunidades</a><a>Segurança</a><a>Sobre</a></div><div className="nav-actions"><button className="login">Entrar</button><button className="primary small" onClick={openApp}>Abrir o app <span>→</span></button></div></nav>
    <main>
      <section className="hero-section">
        <div className="eyebrow"><span></span> COMUNICAÇÃO, REIMAGINADA</div>
        <h1>Onde ideias ganham<br/><em>conexão.</em></h1>
        <p>Uma casa digital para sua comunidade criar, conversar e avançar — sem ruído, sem limites.</p>
        <div className="hero-actions"><button className="primary" onClick={openApp}>Começar agora <span>→</span></button><button className="secondary">Conhecer o Concord <span>↗</span></button></div>
        <div className="hero-sentinel"><Sentinel size={280}/></div><div className="ambient-particles"><i/><i/><i/><i/><i/><i/></div><div className="hero-orb orb-one"></div><div className="hero-orb orb-two"></div>
      </section>
      <section className="preview-wrap"><div className="mini-app"><div className="mini-rail"><div className="mini-logo"><Sentinel size={29}/></div><i></i><i></i><i></i><b>+</b></div><div className="mini-channels"><strong>NÚCLEO CRIATIVO</strong><p className="active"><Icon name="hash" size={14}/> projeto-alvorada</p><p><Icon name="hash" size={14}/> referências</p><strong>VOZ</strong><p><Icon name="volume" size={14}/> Sala de criação</p></div><div className="mini-chat"><header><Icon name="hash" size={18}/><b>projeto-alvorada</b><span>Um lugar para construir juntos.</span></header><div className="mini-chat-inner"><div className="chat-welcome"><div className="spark"><Sentinel size={27}/></div><h3>Seu próximo capítulo começa aqui.</h3><p>O canal foi criado. Compartilhe uma ideia para começar.</p></div><div className="bubble-line"><b>Marina Torres</b><p>Acabei de organizar as novas direções visuais.</p></div><div className="bubble-line red"><b>Você</b><p>Incrível. Vamos transformar isso em algo memorável.</p></div></div><div className="mini-input">Escreva uma mensagem... <span>⌘ ↵</span></div></div></div></section>
      <section className="feature-grid"><div><span className="number">01</span><h2>Espaços que<br/>pensam com você.</h2></div><div className="feature-copy"><p>Organize cada conversa em comunidades, canais e salas vivas. Tudo o que importa, exatamente onde sua equipe espera encontrar.</p><a>Explorar espaços <span>→</span></a></div></section>
      <section className="statement"><div className="eyebrow"><span></span> FEITO PARA ESTAR JUNTO</div><h2>Presença não é estar online.<br/><em>É se sentir por perto.</em></h2><p>Converse por texto, voz ou vídeo. Entre e saia no seu ritmo.</p></section>
    </main>
  </div>
}

function AppView({ goHome }) {
 const [channel, setChannel] = useState('projeto-alvorada'); const [text,setText] = useState(''); const [chat,setChat] = useState(messages); const [mobile,setMobile] = useState(false)
 const send = (e) => { e.preventDefault(); if(!text.trim()) return; setChat([...chat,['EU','Você','agora',text.trim()]]); setText('') }
 const channels = ['visão-geral','projeto-alvorada','referências','em-andamento'];
 return <div className="app-shell">
  <aside className={'server-rail '+(mobile?'show':'')}><button className="rail-brand" onClick={goHome}><Sentinel size={31}/></button><div className="rail-line"/><button className="server current"><Sentinel size={29}/></button><button className="server cyan">W</button><button className="server purple">A</button><button className="server gold">+</button></aside>
  <aside className={'channel-sidebar '+(mobile?'show':'')}><div className="workspace-title">Núcleo Criativo <span>⌄</span></div><div className="side-scroll"><div className="side-label">CANAIS DE TEXTO <button>+</button></div>{channels.map(c=><button key={c} onClick={()=>{setChannel(c);setMobile(false)}} className={'channel '+(channel===c?'selected':'')}><Icon name="hash" size={16}/>{c}</button>)}<div className="side-label voice-label">CANAIS DE VOZ <button>+</button></div><button className="channel"><Icon name="volume" size={16}/>Sala de criação <span className="voice-count">3</span></button><div className="voice-live"><div className="avatars"><span className="avatar a1">ML</span><span className="avatar a2">DV</span><span className="avatar a3">KA</span></div><span>3 pessoas conversando</span><button><Icon name="volume" size={14}/></button></div></div><div className="profile-bar"><div className="avatar me">AP</div><div><b>Alex Prado</b><small>online</small></div><button title="Configurações"><Icon name="settings" size={17}/></button></div></aside>
  <main className="chat-area"><header className="chat-header"><button className="mobile-menu" onClick={()=>setMobile(!mobile)}><Icon name="menu"/></button><Icon name="hash" size={20}/><b>{channel}</b><span className="topic">Colabore e acompanhe os próximos passos.</span><div className="header-actions"><button><Icon name="phone"/></button><button><Icon name="users"/></button><div className="search"><Icon name="search" size={15}/><span>Buscar</span><kbd>⌘ K</kbd></div></div></header><div className="conversation"><div className="welcome"><div className="welcome-icon"><Icon name="hash" size={30}/></div><h1>Boas-vindas a <span>#{channel}</span></h1><p>Este é o começo deste canal. Transforme conversas em progresso.</p></div><div className="divider"><span>HOJE</span></div>{chat.map((m,i)=><div className="message" key={i}><div className={'avatar message-avatar '+(['AL','MR','DV','EU'].includes(m[0])?m[0].toLowerCase():'')}>{m[0]}</div><div><div className="message-meta"><b>{m[1]}</b><small>{m[2]}</small></div><p>{m[3]}</p>{i===0&&<div className="attachment"><div className="attachment-shape"></div><div><b>Direção visual — V2.fig</b><span>12,4 MB · Documento de design</span></div><button>↗</button></div>}</div></div>)}</div><form className="composer" onSubmit={send}><button type="button" className="attach"><Icon name="plus" size={18}/></button><input value={text} onChange={e=>setText(e.target.value)} placeholder={'Enviar mensagem para #'+channel}/><button type="button">☺</button><button type="submit" className="send"><Icon name="send" size={17}/></button></form></main>
  <aside className="members"><header><Icon name="users" size={18}/> 18 membros</header><div className="member-section"><span>ONLINE — 5</span>{['Aurora Lima','Marcos Rocha','Davi Valença','Karla Nunes','João Silva'].map((n,i)=><div className="member" key={n}><div className={'avatar '+['al','mr','dv','ka','js'][i]}>{avatars[i]}</div><div><b>{n}</b><small>{i===0?'Criando uma nova visão':i===1?'Em foco':'online'}</small></div>{i===0&&<i></i>}</div>)}</div><div className="member-section offline"><span>OFFLINE — 13</span>{['Iris Campos','Pedro Tavares','Bia Moura'].map((n,i)=><div className="member" key={n}><div className="avatar muted">{['IC','PT','BM'][i]}</div><div><b>{n}</b></div></div>)}</div></aside>
 </div>
}

export default function App(){ const [view,setView]=useState('landing'); return view==='landing'?<Landing openApp={()=>setView('app')}/>:<AppView goHome={()=>setView('landing')}/> }
