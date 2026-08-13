# Arquitetura atual — Concord

## Escopo da análise

Este documento descreve o estado do repositório em 13 de agosto de 2026, antes da integração com dados persistentes. Nenhum componente visual ou comportamento do frontend foi modificado nesta etapa.

O repositório possui dois projetos independentes:

| Projeto | Papel atual | Tecnologia |
| --- | --- | --- |
| `concord-pwa/` | Interface web/PWA do Concord | React 19, Vite 8, JavaScript/JSX e CSS puro |
| `Concord.Api/` | Início do backend | ASP.NET Core em .NET 10 |

Apesar do nome `concord-pwa`, não há manifest, service worker ou configuração de instalação/offline no estado atual. A aplicação é uma SPA Vite.

## Estrutura de pastas

```text
Concord/
├── concord-pwa/
│   ├── public/                 # favicon e sprites SVG
│   ├── src/
│   │   ├── assets/             # imagens SVG e hero.png
│   │   ├── App.jsx             # toda a interface e estado da SPA
│   │   ├── App.css             # estilos da landing e da área logada
│   │   ├── index.css           # reset, tipografia e variáveis CSS
│   │   └── main.jsx            # bootstrap React
│   ├── index.html
│   ├── vite.config.js
│   └── package.json
├── Concord.Api/
│   ├── Program.cs              # configuração mínima e endpoint raiz
│   ├── Concord.Api.csproj      # SDK Web, target net10.0
│   ├── appsettings*.json       # logging e AllowedHosts
│   └── Properties/launchSettings.json
└── ARCHITECTURE.md
```

## Frontend

### Inicialização, framework e dependências

`src/main.jsx` monta `App` via `createRoot`, sob `StrictMode`. O projeto usa React 19.2, React DOM e Vite com o plugin oficial React. Não há React Router, biblioteca de estado, cliente HTTP, biblioteca de formulários, autenticação, testes ou TypeScript.

Os estilos ficam inteiramente em CSS global. `App.css` também contém os breakpoints responsivos; abaixo de 800px, as barras de servidores/canais viram painéis alternáveis.

### Componentes principais

Todos estão em `concord-pwa/src/App.jsx`:

| Componente | Responsabilidade |
| --- | --- |
| `Icon` | Renderiza ícones SVG internos por nome. |
| `Sentinel` | Renderiza a marca SVG do Concord. |
| `Landing` | Página institucional/marketing e prévia visual estática do app. |
| `AppView` | Interface principal de comunidade, canais, chat e membros. |
| `App` | Controla a alternância entre landing e aplicação. |

Não há uma pasta `components`, nem componentes reutilizáveis separados por domínio. A prévia dentro da landing (`mini-app`) é markup estático dentro de `Landing`, não uma instância de `AppView`.

### Páginas e navegação

Há duas visões lógicas, mas nenhuma rota de URL:

1. **Landing** (estado inicial): navegação institucional, hero, prévia e seções de conteúdo.
2. **Aplicação**: ambiente da comunidade “Núcleo Criativo”.

`App` mantém `view` em estado local. Os botões “Abrir o app” e “Começar agora” definem `view` como `app`; o ícone da barra de servidores chama `goHome` e retorna à landing. Links como Produto, Comunidades, Segurança, Sobre, Entrar e Conhecer o Concord não executam navegação. A troca de canais acontece somente no estado local e não atualiza a URL.

### Gerenciamento de estado

O único mecanismo é `useState`, dentro de `App` e `AppView`:

| Estado | Dono | Uso atual |
| --- | --- | --- |
| `view` | `App` | Landing ou interface da comunidade. |
| `channel` | `AppView` | Nome do canal selecionado e textos dependentes dele. |
| `text` | `AppView` | Campo do compositor. |
| `chat` | `AppView` | Lista de mensagens exibidas; recebe mensagens locais enviadas. |
| `mobile` | `AppView` | Visibilidade das barras laterais no mobile. |

O estado não é persistido, compartilhado por Context/store, sincronizado com a URL, nem recuperado de API. Uma mensagem enviada existe apenas até recarregar a página e é exibida em qualquer canal selecionado, pois `chat` não é indexado por canal.

### Domínios já representados na UI

**Servidores/comunidades.** A barra `server-rail` exibe a marca e quatro botões visuais: a comunidade atual, dois servidores identificados por `W` e `A`, e `+`. Apenas a marca retorna à landing; seleção, criação, listagem e dados dos servidores não existem.

**Canais.** `AppView` declara localmente quatro canais de texto (`visão-geral`, `projeto-alvorada`, `referências`, `em-andamento`) e uma sala de voz. O clique em um canal de texto só altera `channel`; os botões de criação e a sala de voz não têm ação. Nome, tópico e placeholder são parcialmente derivados de `channel`, porém a conversa continua a mesma.

**Mensagens.** `messages` contém três tuplas mockadas `[iniciais, nome, horário, conteúdo]`. O formulário acrescenta uma tupla do usuário “Você” ao estado local. Há um único anexo visual, condicionado à primeira mensagem; anexar, emoji, abrir download, paginação, edição, reação, exclusão e busca não foram implementados.

**Usuários e presença.** O perfil corrente é estático (“Alex Prado”, `AP`, online). O painel de membros tem cinco usuários online, três offline renderizados e contadores estáticos “18 membros”, “ONLINE — 5” e “OFFLINE — 13”. Há ainda uma indicação visual de três participantes na sala de voz. Não há perfis, autenticação, autorização ou presença em tempo real.

### Dados mockados e comportamento apenas visual

São mockados ou estáticos: comunidade atual, servidores, canais de texto e voz, tópico, membros, perfis, status, participantes de voz, mensagens iniciais, anexo, contagens, conteúdo da prévia da landing e todo o conteúdo institucional. Os controles de telefone, membros, busca, configurações, anexar, emoji e ações do anexo não estão conectados a lógica de negócio. A interface carrega fontes do Google Fonts diretamente no CSS.

### Interfaces e tipos existentes

Não existem interfaces, tipos TypeScript, modelos de domínio ou validação de payloads. O projeto é JavaScript puro. Atualmente, a única estrutura implícita para mensagens é a tupla de quatro posições; ela é frágil para integração e deve ser substituída, numa etapa futura, por objetos tipados/validados.

## Backend atual

`Concord.Api` usa `Microsoft.NET.Sdk.Web`, `net10.0`, nullable habilitado e implicit usings. `Program.cs` constrói a aplicação e expõe somente `GET /`, que devolve `Hello World!`. Não há controllers, MVC configurado, banco de dados, EF Core, autenticação, CORS, OpenAPI/Swagger, SignalR, DTOs ou camada de domínio. Portanto, ele é hoje uma Minimal API, não uma aplicação ASP.NET Core MVC estruturada.

## Proposta de integração com ASP.NET Core MVC

Manter o frontend como SPA React e usar `Concord.Api` como backend HTTP e de tempo real. Para desenvolvimento, Vite deve encaminhar `/api` e `/hubs` ao backend via proxy; em produção, o frontend pode ser servido por CDN/servidor web e o backend em domínio próprio, com CORS restrito aos domínios permitidos. A decisão de hospedar os arquivos Vite pelo ASP.NET é opcional e não muda o contrato da API.

### Organização sugerida do backend

```text
Concord.Api/
├── Controllers/                # API controllers versionados
├── Contracts/                  # requests/responses (DTOs)
├── Domain/                     # entidades e regras de negócio
├── Application/                # casos de uso/serviços
├── Infrastructure/             # EF Core, identidade, storage e integrações
├── Hubs/                       # SignalR para chat e presença
└── Program.cs                  # DI, MVC, auth, CORS, SignalR e middleware
```

Configuração futura central: `AddControllers`, `MapControllers`, autenticação por bearer/cookie conforme a estratégia escolhida, autorização por comunidade/canal, CORS explícito, tratamento padronizado de erros e OpenAPI em desenvolvimento. Para dados, adotar EF Core com migrações e um banco relacional; o provedor deve ser escolhido junto com requisitos de implantação.

### Recursos e contratos iniciais

| Recurso | Endpoints REST sugeridos | Consumidores atuais |
| --- | --- | --- |
| Sessão/usuário atual | `POST /api/auth/*`, `GET /api/users/me` | perfil da barra lateral e acesso ao app |
| Comunidades/servidores | `GET /api/communities`, `POST /api/communities`, `GET /api/communities/{id}` | `server-rail`, título da workspace |
| Membros | `GET /api/communities/{id}/members` | painel `members`, contadores e presença |
| Canais | `GET/POST /api/communities/{id}/channels`, `GET/PATCH/DELETE /api/channels/{id}` | sidebar de canais, cabeçalho e canal selecionado |
| Mensagens | `GET /api/channels/{id}/messages?cursor=...`, `POST /api/channels/{id}/messages` | `conversation` e `composer` |
| Anexos | `POST /api/uploads` e metadados em mensagens | botão de anexo e cartão de arquivo |
| Busca | `GET /api/search?communityId=...&query=...` | campo Buscar |

DTOs devem possuir IDs estáveis (preferencialmente UUID), timestamps UTC (`createdAt`), autor como objeto ou referência suficiente para renderização, e cursores para paginação. Modelos mínimos: `User`, `Community`, `Membership`, `Channel` (tipo texto/voz), `Message`, `Attachment` e `Presence`. O frontend deverá manter DTOs explicitamente tipados (idealmente migrando para TypeScript) ou validar JSON em runtime, em vez das tuplas atuais.

### Tempo real

Mensagens, presença e contagem/participantes de voz exigem atualização em tempo real. Um hub SignalR, por exemplo em `/hubs/community`, pode associar conexões aos grupos da comunidade e do canal. Eventos iniciais: `messageCreated`, `messageUpdated`, `messageDeleted`, `presenceChanged`, `channelCreated` e `channelUpdated`. O envio de mensagem deve continuar sendo autorizado e persistido pelo backend; SignalR atualiza os demais clientes após a confirmação, enquanto o cliente pode aplicar atualização otimista com rollback em erro.

### Pontos exatos a substituir no frontend, em etapa posterior

1. Em `App.jsx`, trocar `messages`, `avatars`, `channels` e arrays literais de membros/servidores por consultas aos recursos acima.
2. Trocar `view` por rotas reais, por exemplo `/`, `/login`, `/communities/:communityId/channels/:channelId`; preservar deep links e restauração da seleção.
3. Buscar sessão antes de renderizar a área autenticada e tratar loading, vazio, erro e acesso negado.
4. Fazer `chat` ser uma coleção por `channelId`, com carregamento paginado e atualização via SignalR; `send` deve chamar `POST` e não apenas `setChat`.
5. Derivar contadores e estado da sala de voz dos dados de membros/presença; a mídia de voz/vídeo requer desenho específico (por exemplo WebRTC, sinalização e TURN), além do CRUD de canal.
6. Conectar controles hoje inativos somente quando seus contratos estiverem definidos: login, criação de comunidade/canal, configurações, busca, anexos, emoji e chamada.

Esta abordagem permite introduzir o backend gradualmente, mantendo a UI atual intacta até cada domínio ter seu contrato e estados de carregamento/erro implementados.
