# Concord PWA

Cliente React/Vite do Concord integrado à API REST, ao `ChatHub`, ao `VoiceHub` e ao WebRTC.

## Estrutura

- `components/`: elementos visuais reutilizáveis, feedback e modais;
- `config/`: leitura e normalização de variáveis de ambiente;
- `contexts/`: estado global da sessão autenticada;
- `hooks/`: ciclo de vida das conexões de chat e voz;
- `pages/`: autenticação e workspace principal;
- `realtime/`: clientes SignalR e controle do mesh WebRTC;
- `services/`: sessão local, HTTP, refresh token e módulos da API;
- `assets/`: identidade visual oficial do Concord.

## Configuração

Copie `.env.example` para `.env.local`:

```env
VITE_API_URL=http://localhost:5187
VITE_REQUEST_TIMEOUT_MS=15000
VITE_WEBRTC_ICE_SERVERS=[]
```

`VITE_WEBRTC_ICE_SERVERS` recebe um array JSON de `RTCIceServer`. Em produção, configure
STUN/TURN administrados para o ambiente por meio do sistema de deploy. Não versione
credenciais TURN, tokens ou arquivos `.env.local`.

## Execução

```powershell
npm ci
npm run dev
```

A API deve estar acessível em `VITE_API_URL`. O backend aceita a origem padrão do Vite em
`http://localhost:5173`.

## Autenticação e REST

A PWA implementa cadastro, login, logout, restauração de sessão e rotação pelo refresh token.
As chamadas HTTP passam por `services/apiClient.js`, que centraliza JWT, timeout, falhas de
rede e respostas 401/403. O refresh token é enviado apenas aos endpoints de autenticação.

O workspace consome servidores, membros, convites, canais, perfil, mensagens, anexos e
contadores de não lidas. As permissões visuais são derivadas dos papéis `OWNER`, `ADMIN` e
`MEMBER` retornados pelo servidor; o backend permanece responsável pela autorização real.

## SignalR e WebRTC

O `ChatHub` entrega mensagens, presença e indicadores de digitação. O hook encerra grupos e
listeners ao trocar de canal ou desmontar o workspace e reentra nos canais após reconexão.

O `VoiceHub` mantém participantes, mute/deafened e encaminha SDP/ICE somente entre usuários
da mesma sessão. O áudio nunca passa pelo backend: cada par usa `RTCPeerConnection`. A PWA
solicita apenas microfone (`video: false`), reproduz streams remotos, controla volume por peer,
detecta fala localmente e libera tracks, analysers, elementos de áudio e peers ao sair.

## Teste manual com dois usuários

1. Inicie PostgreSQL, API e PWA.
2. Abra dois perfis de navegador e cadastre usuários distintos.
3. Crie um servidor/canal de voz e adicione o segundo usuário por convite.
4. Entre no mesmo canal nos dois perfis e permita o microfone.
5. Confirme áudio bidirecional, indicadores de fala e volume individual.
6. Valide mute/unmute, deafen/undeafen, saída e reconexão de rede.
7. Repita negando permissão e sem um dispositivo de entrada disponível.

Microfone/WebRTC exigem contexto seguro (`https`) fora de `localhost`.
