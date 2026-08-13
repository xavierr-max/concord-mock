# Arquitetura do Concord

## Visão geral

O repositório contém uma SPA/PWA React e uma API ASP.NET Core. PostgreSQL armazena dados
duráveis; SignalR transporta eventos e signaling; áudio trafega diretamente entre navegadores
por WebRTC.

| Projeto | Responsabilidade | Tecnologias |
|---|---|---|
| `Concord.Api/` | REST, autenticação, persistência e hubs | ASP.NET Core 10, EF Core, Identity, SignalR, PostgreSQL |
| `Concord.Api.Tests/` | testes de endpoints, permissões e hubs | xUnit, WebApplicationFactory, EF InMemory |
| `concord-pwa/` | autenticação, workspace, chat e voz | React 19, Vite 8, SignalR Client, WebRTC |

## Backend

A API usa JWT de curta duração e refresh tokens rotativos armazenados somente como hash.
Controllers expõem autenticação, perfil, servidores/membros, convites, canais, mensagens,
anexos e contadores de não lidas. `ConcordDbContext` centraliza Identity e entidades de
domínio. Migrations versionam o schema PostgreSQL.

Anexos persistem apenas metadados. `IFileStorageService` usa disco local exclusivamente em
Development e exige um provedor externo em Production.

O `ChatHub` publica mensagens, presença e typing. O `VoiceHub` mantém sessões em memória,
sincroniza mute/deafened e encaminha Offer/Answer/ICE diretamente às conexões do destinatário.
Nenhum hub recebe ou retransmite mídia.

## Frontend

O frontend está organizado por responsabilidade:

```text
src/
  assets/       identidade visual
  components/   elementos reutilizáveis
  config/       ambiente
  contexts/     sessão global
  hooks/        ciclos de vida SignalR/voz
  pages/        autenticação e workspace
  realtime/     ChatHub, VoiceHub e WebRTC
  services/     HTTP, refresh e módulos REST
```

`apiClient` adiciona JWT, aplica timeout e executa um único refresh concorrente em respostas
401. A renderização protegida depende da sessão restaurada. Papéis retornados pelo backend
controlam somente a visibilidade de ações; autorização permanece no servidor.

O hook de chat entra e sai de grupos ao trocar de canal, limpa listeners ao desmontar e reentra
após reconexão. O cliente de voz mantém um peer por participante, enfileira ICE até existir uma
descrição remota, reproduz áudio em elementos dedicados e libera todos os recursos ao sair.
Mute apenas desabilita a track local; volume e detecção de fala são locais.

## Fluxo de dados

```text
React -> REST API -> Services -> EF Core -> PostgreSQL
React <-> ChatHub -> eventos de chat/presença/typing
React <-> VoiceHub -> estado e SDP/ICE
Navegador <------ WebRTC áudio ------> Navegador
```

## Configuração e segurança

Backend local usa .NET User Secrets para conexão e chave JWT. Frontend usa somente variáveis
`VITE_*` não secretas. Arquivos `.env`, tokens, credenciais TURN e conteúdo de uploads locais
não são versionados. Em produção, CORS, HTTPS, armazenamento externo e STUN/TURN devem ser
configurados pelo ambiente de deploy.

Consulte [Concord.Api/README.md](Concord.Api/README.md) e
[concord-pwa/README.md](concord-pwa/README.md) para contratos e execução local.
