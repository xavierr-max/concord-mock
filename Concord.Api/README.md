# Concord API

## Desenvolvimento

1. Copie `.env.example` para `../.env` caso queira alterar as credenciais padrão.
2. Execute `docker compose up -d` na raiz do repositório para iniciar o PostgreSQL.
3. Configure opcionalmente `ConnectionStrings__ConcordDatabase`; ela sobrepõe `appsettings.json`.
4. Execute `dotnet ef database update` e `dotnet run`.

Em Development, o Swagger está em `/swagger` e a saúde da aplicação em `/health`.

## Configuração

`appsettings.json` contém apenas configurações não sensíveis. Em desenvolvimento, a conexão e a chave JWT devem ser configuradas com .NET User Secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:ConcordDatabase" "<connection-string-local>"
dotnet user-secrets set "Jwt:SigningKey" "<chave-aleatoria-com-ao-menos-32-caracteres>"
```

Em produção, use um secret manager ou variáveis de ambiente para `Jwt__SigningKey` e `ConnectionStrings__ConcordDatabase`. Configure também `Jwt__Issuer`, `Jwt__Audience` e `Cors__AllowedOrigins__0` conforme o ambiente.

## Autenticação

Os endpoints são `POST /api/auth/register`, `login`, `refresh`, `logout` e `GET /api/auth/me`. A API retorna somente DTOs; senhas são processadas pelo ASP.NET Core Identity e refresh tokens são armazenados exclusivamente como hash. Para executar migrations: `dotnet tool run dotnet-ef database update`.

## Perfil do usuário

Com um access token Bearer válido, estão disponíveis:

- `GET /api/users/me`: perfil do usuário autenticado;
- `PUT /api/users/me`: altera `username`, `displayName` e `bio`;
- `PUT /api/users/me/avatar`: altera a URL do avatar;
- `GET /api/users/{id}`: perfil público de um usuário.

Os DTOs de perfil nunca expõem e-mail, senha/hash, telefone, tokens ou campos internos do Identity. `Id` e `CreatedAt` são somente leitura. Username duplicado retorna HTTP `409 Conflict`. Os contratos e códigos de resposta também estão disponíveis no Swagger.

## Servidores

Os endpoints autenticados `POST/GET /api/servers`, `GET/PUT/DELETE /api/servers/{id}` e
`POST /api/servers/{id}/members` / `DELETE /api/servers/{id}/members/{userId}` gerenciam
servidores e membros. O criador entra automaticamente como `OWNER`. Dados internos são
visíveis apenas aos membros; atualização e exclusão do servidor exigem o OWNER. Um membro
pode sair removendo a si mesmo, e o OWNER pode remover outros membros.

## Convites

Membros podem criar convites com validade e limite opcional de usos em
`POST /api/servers/{serverId}/invites`. Convites podem ser consultados, aceitos e removidos
por código em `/api/invites/{code}`. Códigos são aleatórios, criptograficamente seguros e
URL-safe. Convites expirados retornam `410 Gone`; limite atingido ou usuário já membro
retornam `409 Conflict`. O criador ou o OWNER pode excluir um convite.

## Canais

Os canais de texto e voz compartilham a entidade `Channel` e são diferenciados por `Type`.
Membros podem consultar `GET /api/servers/{serverId}/channels`; o OWNER pode criar canais
nesse recurso e editar ou excluir em `/api/channels/{id}`. A listagem é ordenada por
`Position`, que aceita valores entre 0 e 10.000. Nomes vazios são rejeitados.

## Permissões por servidor

As permissões são derivadas do papel em `ServerMember`, sem bitmask. `OWNER` possui todas
as permissões e mantém exclusividade sobre alteração/exclusão do servidor. `ADMIN` herda
as capacidades básicas e pode gerenciar canais e convites e moderar membros comuns.
`MEMBER` pode visualizar canais, enviar mensagens e entrar em canais de voz. As verificações
ficam centralizadas em `IServerAuthorizationService`.

## Mensagens

Membros podem enviar mensagens em canais de texto por `POST /api/channels/{channelId}/messages`.
O histórico em `GET /api/channels/{channelId}/messages` exige `page` e `pageSize` (máximo 100),
e retorna dados resumidos do autor. Autor, ADMIN ou OWNER podem editar e excluir. Exclusões
são lógicas: a mensagem permanece no histórico com `IsDeleted = true` e conteúdo oculto.

### Mensagens em tempo real

O SignalR `ChatHub` está disponível em `/hubs/chat` e exige o mesmo JWT da API. Clientes
WebSocket/SSE podem enviar o token por `access_token`; esse parâmetro é aceito somente na
rota do hub. Os métodos disponíveis são:

- `JoinChannel(channelId)`: valida acesso e adiciona a conexão ao grupo do canal;
- `LeaveChannel(channelId)`: remove a conexão do grupo;
- `SendMessage(channelId, content)`: valida, persiste e publica a mensagem.

Os eventos recebidos são `MessageCreated`, `MessageUpdated` e `MessageDeleted`, todos com
um `MessageResponse`. No evento de exclusão, `content` é `null` e `isDeleted` é `true`.
Criações pelo hub e mutações pela API REST publicam os mesmos eventos. O histórico continua
exclusivamente no REST; não há polling de mensagens.

### Anexos

O autor pode anexar um arquivo a uma mensagem ativa com
`POST /api/messages/{messageId}/attachments`, usando `multipart/form-data` e o campo `file`.
O binário não é armazenado no PostgreSQL; somente nome, tipo, tamanho, URL e data são
persistidos. Em Development, `FileStorage:Provider=Local` usa `wwwroot/uploads`. Tamanho,
Content-Types e extensões permitidas são definidos na seção `FileStorage`.

Em Production, o padrão é `FileStorage:Provider=External`. Registre uma implementação de
`IFileStorageService` para o provedor escolhido e configure sua URL pública. O serviço local
não é habilitado em produção.

### Presença em tempo real

Ao conectar ao `ChatHub`, o usuário é marcado como `Online` somente na memória e sua conexão
entra nos grupos dos servidores dos quais é membro. Após a última conexão cair, o estado muda
para `Offline` depois de 5 segundos (`Presence:DisconnectGracePeriod`); uma reconexão dentro
desse intervalo cancela a mudança. Múltiplas abas/conexões são contabilizadas separadamente.

Membros conectados aos mesmos servidores recebem `UserOnline`, `UserOffline` e
`UserStatusChanged`, com `userId`, `username`, `avatar`, `status` e `changedAt`. Nenhuma
mudança de presença é gravada no PostgreSQL e Redis não é utilizado nesta etapa.

### Indicador de digitação

Após `JoinChannel`, clientes podem chamar `StartTyping(channelId)` e `StopTyping(channelId)`.
Os demais participantes do grupo recebem `TypingStarted` e `TypingStopped` com `channelId`,
`userId`, `username`, `avatar` e `occurredAt`; o emissor não recebe o próprio evento. O hub
elimina chamadas consecutivas duplicadas e envia `TypingStopped` ao sair ou desconectar.
Esses eventos existem somente em memória e nunca são persistidos.

### Canais de voz

O `VoiceHub` autenticado está disponível em `/hubs/voice`. Ele mantém `VoiceSession` e
`VoiceParticipant` somente em memória e oferece `JoinVoiceChannel(channelId)`,
`LeaveVoiceChannel()`, `SetMute(muted)` e `SetDeafened(deafened)`. Apenas membros podem
entrar, e o canal precisa existir com `Type = Voice`.

Os eventos `VoiceUserJoined`, `VoiceUserLeft` e `VoiceUserUpdated` transportam somente
`userId`, `channelId`, `joinedAt`, `muted` e `deafened`. Áudio não passa pelo backend;
o hub mantém apenas o estado necessário para uma futura camada de signaling WebRTC.

Para signaling WebRTC, participantes da mesma sessão podem chamar
`SendOffer(targetUserId, sdp)`, `SendAnswer(targetUserId, sdp)` e
`SendIceCandidate(targetUserId, candidate)`. O destinatário recebe, respectivamente,
`VoiceOfferReceived`, `VoiceAnswerReceived` e `VoiceIceCandidateReceived`, sempre com
`senderUserId` e `channelId`. Os sinais são encaminhados somente às conexões do destinatário,
não são enviados à sala inteira e nunca são persistidos. Os logs incluem apenas tipo do
sinal e identificadores, sem registrar SDP ou ICE candidates.

A PWA disponibiliza `src/realtime/chatClient.js`, com os contratos de métodos/eventos,
reentrada automática nos canais após reconexão e um controlador de digitação que aplica
throttle de 2 segundos e debounce de 1,2 segundo por padrão.
