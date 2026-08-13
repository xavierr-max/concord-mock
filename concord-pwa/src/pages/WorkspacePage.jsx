/* eslint-disable react-hooks/set-state-in-effect, react-hooks/exhaustive-deps */
import { useEffect, useRef, useState } from 'react'
import { useAuth } from '../hooks/useAuth.js'
import { concordApi } from '../services/concordApi.js'
import { env } from '../config/env.js'
import { useChat } from '../hooks/useChat.js'
import { useVoice } from '../hooks/useVoice.js'
import { Brand } from '../components/Brand.jsx'
import { Avatar } from '../components/Avatar.jsx'
import { Empty, Loading, Toast } from '../components/Feedback.jsx'
import { Modal } from '../components/Modal.jsx'
import { MicrophoneTest } from '../components/MicrophoneTest.jsx'

export function WorkspacePage() {
  const { user, profile, logout, saveProfile, saveAvatar } = useAuth()
  const [servers, setServers] = useState([])
  const [server, setServer] = useState(null)
  const [channels, setChannels] = useState([])
  const [channel, setChannel] = useState(null)
  const [messages, setMessages] = useState([])
  const [pageInfo, setPageInfo] = useState(null)
  const [unread, setUnread] = useState({})
  const [presence, setPresence] = useState({})
  const [typing, setTyping] = useState({})
  const [composer, setComposer] = useState('')
  const [loading, setLoading] = useState(true)
  const [loadingMessages, setLoadingMessages] = useState(false)
  const [toast, setToast] = useState('')
  const [modal, setModal] = useState(null)
  const [mobilePanel, setMobilePanel] = useState(false)
  const typingController = useRef()
  const fileInput = useRef()
  const activeChannelId = channel?.type === 'Text' ? channel.id : null

  const showError = error => setToast(error?.message || 'Não foi possível concluir a operação.')
  const chat = useChat({
    channelId: activeChannelId,
    onError: showError,
    onMessage: (_, message) => {
      if (message.channelId === activeChannelId) {
        setMessages(current => upsertMessage(current, message))
        if (message.author.id !== user.id) void concordApi.channels.markRead(message.channelId)
      } else if (message.author.id !== user.id) {
        setUnread(current => ({ ...current, [message.channelId]: (current[message.channelId] || 0) + 1 }))
      }
    },
    onPresence: update => setPresence(current => ({ ...current, [update.userId]: update.status })),
    onTyping: (started, update) => setTyping(current => {
      const next = { ...current }
      const users = { ...(next[update.channelId] || {}) }
      if (started) users[update.userId] = update.username
      else delete users[update.userId]
      next[update.channelId] = users
      return next
    }),
  })
  const voice = useVoice(showError)

  const currentMember = server?.members.find(member => member.userId === user.id)
  const isOwner = currentMember?.role === 'OWNER'
  const canManage = isOwner || currentMember?.role === 'ADMIN'

  useEffect(() => { void loadServers() }, [])
  useEffect(() => { if (server) void loadChannels(server.id); else { setChannels([]); setChannel(null) } }, [server?.id])
  useEffect(() => {
    typingController.current?.dispose()
    typingController.current = activeChannelId ? chat.createTypingController() : null
    if (activeChannelId) void loadMessages(activeChannelId)
    else setMessages([])
    return () => typingController.current?.dispose()
  }, [activeChannelId])

  async function loadServers(selectId) {
    setLoading(true)
    try {
      const list = await concordApi.servers.list()
      setServers(list)
      const selected = list.find(item => item.id === (selectId || server?.id)) || list[0] || null
      setServer(selected)
    } catch (error) { showError(error) } finally { setLoading(false) }
  }
  async function loadChannels(serverId) {
    try {
      const list = await concordApi.channels.list(serverId)
      setChannels(list)
      setChannel(current => list.find(item => item.id === current?.id) || list.find(item => item.type === 'Text') || list[0] || null)
      const counts = await Promise.all(list.filter(item => item.type === 'Text').map(async item => [item.id, (await concordApi.channels.unread(item.id)).unreadCount]))
      setUnread(Object.fromEntries(counts))
    } catch (error) { showError(error) }
  }
  async function loadMessages(channelId, page = 1) {
    setLoadingMessages(true)
    try {
      const result = await concordApi.messages.list(channelId, page, 50)
      setMessages(current => page === 1 ? result.items : mergeMessages(current, result.items))
      setPageInfo(result)
      await concordApi.channels.markRead(channelId)
      setUnread(current => ({ ...current, [channelId]: 0 }))
    } catch (error) { showError(error) } finally { setLoadingMessages(false) }
  }
  async function submitMessage(event) {
    event.preventDefault()
    const content = composer.trim()
    if (!content || !activeChannelId) return
    setComposer(''); typingController.current?.stop()
    try { await chat.sendMessage(content) } catch (error) { setComposer(content); showError(error) }
  }
  async function attachFile(event) {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file || !activeChannelId) return
    try {
      const created = await concordApi.messages.send(activeChannelId, file.name)
      await concordApi.messages.attach(created.id, file)
      await loadMessages(activeChannelId)
    } catch (error) { showError(error) }
  }
  async function selectChannel(next) {
    setMobilePanel(false)
    if (next.type === 'Voice') {
      try {
        if (voice.isJoined) await voice.leave()
        await voice.join(next.id)
      } catch (error) { showError(error) }
      return
    }
    setChannel(next)
  }
  async function saveEntity(kind, values) {
    try {
      if (kind === 'server') {
        const created = await concordApi.servers.create({ name: values.name, icon: values.icon || null })
        await loadServers(created.id)
      } else if (kind === 'server-edit') {
        await concordApi.servers.update(server.id, { name: values.name, icon: values.icon || null }); await loadServers(server.id)
      } else if (kind === 'channel') {
        await concordApi.channels.create(server.id, { name: values.name, type: values.type, position: channels.length }); await loadChannels(server.id)
      } else if (kind === 'channel-edit') {
        await concordApi.channels.update(channel.id, { name: values.name, type: channel.type, position: Number(values.position) }); await loadChannels(server.id)
      } else if (kind === 'invite-create') {
        const invite = await concordApi.invites.create(server.id, { expiresAt: new Date(Date.now() + Number(values.hours) * 3600000).toISOString(), maxUses: values.maxUses ? Number(values.maxUses) : null })
        setModal({ kind: 'invite-result', invite })
        return
      } else if (kind === 'invite-accept') {
        await concordApi.invites.accept(values.code.trim()); await loadServers();
      } else if (kind === 'profile') {
        await saveProfile({ username: values.username, displayName: values.displayName || null, bio: values.bio || null })
        if (values.avatar && values.avatar !== profile?.avatar) await saveAvatar(values.avatar)
      }
      setModal(null)
    } catch (error) { showError(error) }
  }
  async function confirmDelete() {
    try {
      if (modal.target === 'server') { await concordApi.servers.remove(server.id); setModal(null); await loadServers() }
      if (modal.target === 'channel') { await concordApi.channels.remove(channel.id); setModal(null); await loadChannels(server.id) }
      if (modal.target === 'leave') { await concordApi.servers.leaveOrRemoveMember(server.id, user.id); setModal(null); await loadServers() }
    } catch (error) { showError(error) }
  }
  async function removeMember(member) {
    try {
      await concordApi.servers.leaveOrRemoveMember(server.id, member.userId)
      const updated = await concordApi.servers.get(server.id)
      setServer(updated)
      setServers(current => current.map(item => item.id === updated.id ? updated : item))
    } catch (error) { showError(error) }
  }

  if (loading) return <Loading label="Abrindo o Concord…" />
  if (!servers.length) return <><Onboarding user={user} onCreate={() => setModal({ kind: 'server' })} onJoin={() => setModal({ kind: 'invite-accept' })} onLogout={logout} />{modal && <EntityModal modal={modal} onClose={() => setModal(null)} onSave={saveEntity} />}</>

  const sortedMessages = [...messages].sort((a, b) => new Date(a.createdAt) - new Date(b.createdAt))
  const typingNames = Object.values(typing[activeChannelId] || {}).filter((_, index) => index < 3)
  return <div className="workspace">
    <nav className="server-rail"><Brand compact />{servers.map(item => <button key={item.id} className={item.id === server.id ? 'active' : ''} onClick={() => setServer(item)} title={item.name}>{item.icon ? <img src={item.icon} alt="" /> : item.name.slice(0, 2).toUpperCase()}</button>)}<button onClick={() => setModal({ kind: 'server' })} title="Criar servidor">＋</button><button onClick={() => setModal({ kind: 'invite-accept' })} title="Aceitar convite">↗</button></nav>
    <aside className={`channel-sidebar ${mobilePanel ? 'mobile-open' : ''}`}><header><strong>{server.name}</strong><button onClick={() => setModal({ kind: 'server-menu' })}>•••</button></header><div className="channel-list"><ChannelGroup label="CANAIS DE TEXTO" canManage={canManage} onAdd={() => setModal({ kind: 'channel', type: 'Text' })}>{channels.filter(item => item.type === 'Text').map(item => <ChannelButton key={item.id} item={item} selected={channel?.id === item.id} unread={unread[item.id]} onClick={() => selectChannel(item)} />)}</ChannelGroup><ChannelGroup label="CANAIS DE VOZ" canManage={canManage} onAdd={() => setModal({ kind: 'channel', type: 'Voice' })}>{channels.filter(item => item.type === 'Voice').map(item => <ChannelButton key={item.id} item={item} selected={voice.participants.some(p => p.channelId === item.id && p.isLocal)} onClick={() => selectChannel(item)} />)}{voice.isJoined && <VoicePanel voice={voice} members={server.members} />}</ChannelGroup></div><button className="profile-bar" onClick={() => setModal({ kind: 'profile' })}><Avatar user={{ ...user, ...profile }} /><span><strong>{profile?.displayName || user.username}</strong><small>{profile?.status || user.status}</small></span><b>⚙</b></button></aside>
    <main className="chat-panel"><header className="chat-header"><button className="mobile-toggle" onClick={() => setMobilePanel(value => !value)}>☰</button><span className="channel-symbol">#</span><strong>{channel?.name || 'Selecione um canal'}</strong><span>{channel?.type === 'Text' ? 'Converse com sua comunidade.' : 'Canal de voz'}</span>{canManage && channel && <button onClick={() => setModal({ kind: 'channel-menu' })}>Configurar</button>}</header>{!channel ? <Empty title="Nenhum canal disponível" detail="Crie um canal para começar." /> : loadingMessages ? <Loading label="Carregando mensagens…" /> : <><section className="messages">{pageInfo?.totalPages > 1 && <button className="load-more" onClick={() => loadMessages(activeChannelId, Math.min(pageInfo.page + 1, pageInfo.totalPages))}>Carregar mensagens anteriores</button>}{!sortedMessages.length && <Empty title={`Bem-vindo a #${channel.name}`} detail="Esta é a primeira página deste canal." />}{sortedMessages.map(message => <Message key={message.id} message={message} currentUser={user} canModerate={canManage} onEdit={() => setModal({ kind: 'message-edit', message })} onDelete={() => setModal({ kind: 'message-delete', message })} />)}</section>{typingNames.length > 0 && <div className="typing-indicator"><i /><i /><i /> {typingNames.join(', ')} {typingNames.length === 1 ? 'está' : 'estão'} digitando</div>}<form className="composer" onSubmit={submitMessage}><input ref={fileInput} hidden type="file" onChange={attachFile} /><button type="button" onClick={() => fileInput.current?.click()}>＋</button><input value={composer} maxLength="2000" onChange={event => { setComposer(event.target.value); typingController.current?.input(event.target.value) }} placeholder={`Mensagem para #${channel.name}`} /><button className="send-button">Enviar</button></form></>}</main>
    <aside className="member-sidebar"><header>{server.members.length} membros</header>{server.members.map(member => <div className="member-row" key={member.userId}><Avatar user={member} /><span><strong>{member.username}</strong><small>{member.role} · {presence[member.userId] || (member.userId === user.id ? user.status : 'Offline')}</small></span>{canManage && member.role === 'MEMBER' && member.userId !== user.id && <button title="Remover membro" onClick={() => removeMember(member)}>×</button>}</div>)}</aside>
    {modal && <WorkspaceModal modal={modal} server={server} channel={channel} profile={profile} user={user} isOwner={isOwner} canManage={canManage} onClose={() => setModal(null)} onSave={saveEntity} onConfirm={confirmDelete} onLogout={logout} setModal={setModal} />}
    <Toast message={toast} onClose={() => setToast('')} />
  </div>
}

function ChannelGroup({ label, canManage, onAdd, children }) { return <section className="channel-group"><header><span>{label}</span>{canManage && <button onClick={onAdd}>＋</button>}</header>{children}</section> }
function ChannelButton({ item, selected, unread = 0, onClick }) { return <button className={`channel-button ${selected ? 'active' : ''} ${unread ? 'has-unread' : ''}`} onClick={onClick}><span>{item.type === 'Voice' ? '◖))' : '#'}</span>{item.name}{unread > 0 && <b>{unread > 99 ? '99+' : unread}</b>}</button> }
function Message({ message, currentUser, canModerate, onEdit, onDelete }) { return <article className={`message ${message.isDeleted ? 'deleted' : ''}`}><Avatar user={message.author} /><div><header><strong>{message.author.username}</strong><time>{new Date(message.createdAt).toLocaleString('pt-BR')}</time>{!message.isDeleted && (message.author.id === currentUser.id || canModerate) && <span className="message-actions">{message.author.id === currentUser.id && <button onClick={onEdit}>Editar</button>}<button onClick={onDelete}>Excluir</button></span>}</header><p>{message.isDeleted ? 'Mensagem removida' : message.content}</p>{message.attachments?.map(file => <a className="attachment-card" key={file.id} href={absoluteUrl(file.url)} target="_blank" rel="noreferrer"><span>↗</span><div><strong>{file.fileName}</strong><small>{file.contentType} · {formatBytes(file.fileSize)}</small></div></a>)}</div></article> }

function VoicePanel({ voice, members }) { return <div className="voice-panel"><div className="voice-status"><span className="live-dot" />{voice.status === 'reconnecting' ? 'Reconectando…' : 'Voz conectada'}</div>{voice.participants.map(participant => { const member = members.find(item => item.userId === participant.userId) || { username: participant.userId.slice(0, 8) }; return <div className="voice-user" key={participant.userId}><Avatar user={member} speaking={participant.isSpeaking && !participant.muted} /><span>{participant.isLocal ? 'Você' : member.username}</span>{participant.muted && <b title="Mutado">⌁</b>}{participant.deafened && <b title="Áudio desativado">◉</b>}{!participant.isLocal && <input type="range" min="0" max="1" step=".05" value={participant.volume ?? 1} onChange={event => voice.setVolume(participant.userId, event.target.value)} />}</div>})}<div className="voice-controls"><button className={voice.muted ? 'danger' : ''} onClick={() => voice.setMuted(!voice.muted)}>{voice.muted ? 'Ativar mic' : 'Mutar'}</button><button className={voice.deafened ? 'danger' : ''} onClick={() => voice.setDeafened(!voice.deafened)}>{voice.deafened ? 'Ativar áudio' : 'Desativar áudio'}</button><button className="hangup" onClick={voice.leave}>Sair</button></div></div> }

function Onboarding({ user, onCreate, onJoin, onLogout }) { return <main className="onboarding"><Brand /><h1>Olá, {user.username}.</h1><p>Crie uma comunidade ou aceite um convite para começar.</p><div><button className="primary-button" onClick={onCreate}>Criar servidor</button><button className="secondary-button" onClick={onJoin}>Aceitar convite</button></div><button className="link-button" onClick={onLogout}>Sair da conta</button></main> }

function EntityModal({ modal, onClose, onSave }) { const [values, setValues] = useState({ name: '', icon: '', code: '', type: modal.type || 'Text' }); return <Modal title={modal.kind === 'server' ? 'Novo servidor' : 'Aceitar convite'} onClose={onClose} actions={<button className="primary-button" onClick={() => onSave(modal.kind, values)}>Continuar</button>}><FormFields kind={modal.kind} values={values} setValues={setValues} /></Modal> }

function WorkspaceModal({ modal, server, channel, profile, user, isOwner, canManage, onClose, onSave, onConfirm, onLogout, setModal }) {
  const defaults = modal.kind === 'profile' ? { username: profile?.username || user.username, displayName: profile?.displayName || '', bio: profile?.bio || '', avatar: profile?.avatar || '' } : modal.kind.includes('server') ? { name: server.name, icon: server.icon || '' } : modal.kind.includes('channel') ? { name: channel?.name || '', type: modal.type || channel?.type || 'Text', position: channel?.position ?? 0 } : modal.kind === 'message-edit' ? { content: modal.message.content } : { hours: '24', maxUses: '' }
  const [values, setValues] = useState(defaults)
  if (modal.kind === 'server-menu') return <Modal title="Configurações do servidor" onClose={onClose}><div className="action-list">{isOwner && <><button onClick={() => setModal({ kind: 'server-edit' })}>Editar servidor</button><button className="danger-text" onClick={() => setModal({ kind: 'confirm', target: 'server' })}>Excluir servidor</button></>}<button onClick={() => setModal({ kind: 'invite-create' })}>Criar convite</button>{!isOwner && <button className="danger-text" onClick={() => setModal({ kind: 'confirm', target: 'leave' })}>Sair do servidor</button>}<button onClick={onLogout}>Sair da conta</button></div></Modal>
  if (modal.kind === 'microphone-test') return <Modal title="Testar microfone" onClose={onClose}><MicrophoneTest /></Modal>
  if (modal.kind === 'channel-menu') return <Modal title="Configurações do canal" onClose={onClose}><div className="action-list"><button onClick={() => setModal({ kind: 'channel-edit' })}>Editar canal</button><button className="danger-text" onClick={() => setModal({ kind: 'confirm', target: 'channel' })}>Excluir canal</button></div></Modal>
  if (modal.kind === 'confirm' || modal.kind === 'message-delete') return <Modal title="Confirmar ação" onClose={onClose} actions={<button className="danger-button" onClick={modal.kind === 'message-delete' ? async () => { try { await concordApi.messages.remove(modal.message.id) } finally { onClose() } } : onConfirm}>Confirmar</button>}><p>Esta ação não poderá ser desfeita.</p></Modal>
  if (modal.kind === 'invite-result') return <Modal title="Convite criado" onClose={onClose}><label>Código do convite<input readOnly value={modal.invite.code} onFocus={event => event.target.select()} /></label><p className="muted-copy">Expira em {new Date(modal.invite.expiresAt).toLocaleString('pt-BR')}.</p><button className="danger-button" onClick={async () => { await concordApi.invites.remove(modal.invite.code); onClose() }}>Revogar convite</button></Modal>
  if (modal.kind === 'message-edit') return <Modal title="Editar mensagem" onClose={onClose} actions={<button className="primary-button" onClick={async () => { await concordApi.messages.update(modal.message.id, values.content); onClose() }}>Salvar</button>}><label>Mensagem<textarea value={values.content} onChange={event => setValues({ content: event.target.value })} /></label></Modal>
  const kind = modal.kind
  return <Modal title={modalTitle(kind)} onClose={onClose} actions={<button className="primary-button" onClick={() => onSave(kind, values)}>Salvar</button>}><FormFields kind={kind} values={values} setValues={setValues} canManage={canManage} />{kind === 'profile' && <button className="secondary-button audio-settings-button" onClick={() => setModal({ kind: 'microphone-test' })}>Testar microfone</button>}</Modal>
}

function FormFields({ kind, values, setValues }) { const update = event => setValues(current => ({ ...current, [event.target.name]: event.target.value })); if (kind === 'invite-accept') return <label>Código<input name="code" required value={values.code || ''} onChange={update} /></label>; if (kind === 'invite-create') return <><label>Validade em horas<input name="hours" type="number" min="1" value={values.hours} onChange={update} /></label><label>Máximo de usos (opcional)<input name="maxUses" type="number" min="1" value={values.maxUses} onChange={update} /></label></>; if (kind === 'profile') return <><label>Username<input name="username" minLength="3" value={values.username} onChange={update} /></label><label>Nome de exibição<input name="displayName" value={values.displayName} onChange={update} /></label><label>Avatar (URL)<input name="avatar" type="url" value={values.avatar} onChange={update} /></label><label>Bio<textarea name="bio" value={values.bio} onChange={update} /></label></>; return <><label>Nome<input name="name" required value={values.name || ''} onChange={update} /></label>{kind === 'server' || kind === 'server-edit' ? <label>Ícone (URL opcional)<input name="icon" type="url" value={values.icon || ''} onChange={update} /></label> : <>{kind === 'channel' && <label>Tipo<select name="type" value={values.type} onChange={update}><option value="Text">Texto</option><option value="Voice">Voz</option></select></label>}{kind === 'channel-edit' && <label>Posição<input name="position" type="number" min="0" max="10000" value={values.position} onChange={update} /></label>}</>}</> }

function modalTitle(kind) { return ({ server: 'Novo servidor', 'server-edit': 'Editar servidor', channel: 'Novo canal', 'channel-edit': 'Editar canal', 'invite-create': 'Criar convite', 'invite-accept': 'Aceitar convite', profile: 'Seu perfil' })[kind] || 'Concord' }
function upsertMessage(messages, next) { const exists = messages.some(item => item.id === next.id); return exists ? messages.map(item => item.id === next.id ? next : item) : [...messages, next] }
function mergeMessages(current, next) { return [...new Map([...current, ...next].map(message => [message.id, message])).values()] }
function absoluteUrl(url) { return url?.startsWith('/') ? `${env.apiUrl}${url}` : url }
function formatBytes(bytes) { if (bytes < 1024) return `${bytes} B`; if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`; return `${(bytes / 1048576).toFixed(1)} MB` }
