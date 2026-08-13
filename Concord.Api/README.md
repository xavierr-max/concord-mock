# Concord API

## Desenvolvimento

1. Copie `.env.example` para `../.env` caso queira alterar as credenciais padrão.
2. Execute `docker compose up -d` na raiz do repositório para iniciar o PostgreSQL.
3. Configure opcionalmente `ConnectionStrings__ConcordDatabase`; ela sobrepõe `appsettings.json`.
4. Execute `dotnet ef database update` e `dotnet run`.

Em Development, o Swagger está em `/swagger` e a saúde da aplicação em `/health`.

## Configuração

`appsettings.json` define a conexão de desenvolvimento, origens da PWA permitidas por CORS e valores JWT locais. Em produção, a chave JWT é obrigatoriamente fornecida por ambiente: `Jwt__SigningKey` (mínimo de 32 caracteres). Configure também `Jwt__Issuer`, `Jwt__Audience`, `ConnectionStrings__ConcordDatabase` e `Cors__AllowedOrigins__0` conforme o ambiente.

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
