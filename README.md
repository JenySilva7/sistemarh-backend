# Sistema RH — Backend (.NET)

Backend em **ASP.NET Core Web API** para o app Flutter e o site do Sistema RH. Cuida de autenticação, registro de ponto e justificativas de ausência (já em uso pelo app), guardando tudo num banco **MySQL**.

Também já tem rotas completas (protegidas por perfil RH/Admin) pro lado administrativo/RH: departamentos, cargos, funcionários, extrato de banco de horas, recrutamento, avaliações, agenda e folha de pagamento — baseado no desenho de banco feito pela colega do grupo, revisado e incorporado aqui pra não manter dois bancos de dados separados pro mesmo projeto. Falta só a UI (site) pra usar essas rotas.

Este README foi escrito pensando em quem está começando com .NET — além de explicar "como rodar", explica **por que** cada coisa foi feita do jeito que foi.

---

## 1. O que é um backend, e por que ele existe

Até agora, o app Flutter só tinha a *interface* (as telas) — os dados (horário do ponto, ausências, etc.) viviam só na memória do celular e sumiam quando o app fechava.

Um **backend** é um programa que roda num servidor (aqui, na sua máquina, em `http://localhost:5289`) e:

1. Recebe pedidos do app (ou do site) pela internet, no formato de requisições HTTP.
2. Confere se o pedido é válido e se a pessoa tem permissão pra fazer aquilo.
3. Lê ou grava informações num banco de dados.
4. Devolve uma resposta (os dados pedidos, ou uma mensagem de erro).

Pense nele como um **garçom de restaurante**: o cliente (app/site) faz o pedido, o garçom (API) leva até a cozinha (banco de dados), confere se está tudo certo, e traz o prato pronto de volta — sem o cliente nunca entrar na cozinha diretamente.

---

## 2. Como o app/site conversam com essa API

Toda comunicação é feita por **HTTP** — os mesmos tipos de requisição que o navegador faz ao abrir um site:

| Verbo    | Uso                                  |
| -------- | ------------------------------------- |
| `GET`    | Buscar dados (ex.: listar ausências)  |
| `POST`   | Criar algo novo (ex.: fazer login, registrar ponto) |
| `PUT`    | Atualizar algo que já existe          |
| `DELETE` | Remover/cancelar algo                 |

Tanto o **app Flutter** quanto o **site React** chamam exatamente as mesmas rotas (ex.: `POST /api/auth/login`) — não importa a tecnologia de cada um, os dois "falam HTTP" e entendem JSON. É por isso que uma única API consegue atender os dois ao mesmo tempo.

---

## 3. Estrutura de pastas

```
SistemaRH.Backend/
├── SistemaRH.Backend.sln          # Arquivo de solução (agrupa os 2 projetos abaixo)
├── SistemaRH.Api/                 # O backend em si
│   ├── Controllers/               # Recebem a requisição HTTP e devolvem a resposta
│   ├── Services/                  # As regras de negócio "de verdade" ficam aqui
│   ├── Models/                    # As entidades (tabelas do banco)
│   ├── Dtos/                      # O que a API aceita receber / promete devolver
│   ├── Data/                      # Configuração do Entity Framework (acesso ao banco)
│   ├── Middleware/                # Código que roda "ao redor" de toda requisição
│   ├── Exceptions/                # Erros de regra de negócio (ex.: "não encontrado")
│   ├── Common/                    # Pequenos utilitários compartilhados
│   ├── Migrations/                # Histórico de alterações no esquema do banco
│   ├── Program.cs                 # Ponto de entrada — monta e liga tudo
│   ├── appsettings.json           # Configuração "padrão" (vai pro Git)
│   └── appsettings.Development.json  # Configuração só do seu ambiente local (não vai pro Git)
└── SistemaRH.Api.Tests/           # Testes automatizados
```

### Para que serve cada pasta

- **Controllers/** — são a "porta de entrada". Um Controller só faz 3 coisas: recebe a requisição, chama o Service certo, e devolve a resposta HTTP (200, 400, 404...). Ele **não** contém regra de negócio.
- **Services/** — é onde a lógica de verdade mora: "um e-mail só pode se cadastrar uma vez", "uma ausência só pode ser cancelada enquanto está 'Enviada'", etc. Separar isso do Controller facilita testar essas regras sem precisar simular uma requisição HTTP inteira (veja os testes em `SistemaRH.Api.Tests`).
- **Models/** — as classes que viram tabelas no banco. O "núcleo" usado pelo app (`Usuario`, `RegistroPonto`, `Ausencia`) e o lado administrativo/RH (`Departamento`, `Cargo`, `Funcionario`, `LancamentoBancoHoras`, `Recrutamento`, `Avaliacao`, `EventoAgenda`, `FolhaPagamento`).
- **Dtos/** (Data Transfer Objects) — "contratos" de entrada/saída da API. Por exemplo, o app nunca recebe o `SenhaHash` de volta — o `UsuarioDto` simplesmente não tem esse campo. Isso evita vazar informação sensível sem querer.
- **Data/** — o `AppDbContext`, que é a ponte entre as classes C# e as tabelas SQL de verdade.
- **Middleware/** — o `ExceptionMiddleware` captura qualquer erro que aconteça em qualquer rota e garante que a resposta de erro sempre tenha o mesmo formato, sem vazar detalhes internos.
- **Exceptions/** — classes de erro "esperado" (`NotFoundException`, `ForbiddenException`...) que o middleware sabe traduzir pro código HTTP certo.

---

## 4. Como rodar o projeto

### Pré-requisitos

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) ou superior (só é obrigatório se você for rodar a API direto com `dotnet run` — pulando a opção recomendada abaixo).
- [Podman](https://podman.io/) (ou Docker — os comandos são os mesmos, só trocando `podman` por `docker`).

### Rodando tudo (recomendado): MySQL + API, os dois em container

```bash
cd SistemaRH.Backend
podman compose up -d --build
```

Isso lê o `docker-compose.yml` da raiz do repositório e sobe **dois** containers: o MySQL 8.4 (`sistemarh-mysql`) e a própria API compilada e publicada dentro de um container Linux (`sistemarh-backend`, construído a partir do `Dockerfile` desta pasta), escutando em `http://localhost:5080`. Os dados do banco ficam num volume (`sistemarh-mysql-data`) que sobrevive a `podman compose down`.

> **Por que rodar a API em container também, e não só o banco?** No Windows, o **Smart App Control** (recurso de segurança do Windows 11) pode bloquear a execução de um `.dll`/`.exe` recém-compilado que ainda não tem "reputação" estabelecida — isso derruba o `dotnet run` direto com um erro tipo `Uma política de Controle de Aplicativo bloqueou este arquivo`, mesmo com o código 100% correto. Rodar dentro de um container Linux contorna esse bloqueio (o Smart App Control só se aplica a executáveis Windows), então é a forma mais confiável de rodar o projeto numa máquina com esse recurso ativado. Pra conferir se está ativo na sua máquina: `Get-MpComputerStatus | Select SmartAppControlState` no PowerShell — se vier `On`, prefira sempre essa opção via container.

Pra reconstruir a imagem depois de alterar o código (`podman compose up -d --build` de novo faz isso), ver logs (`podman logs sistemarh-backend -f`) ou derrubar tudo (`podman compose down`, os dados do banco continuam guardados no volume).

### Alternativa: rodando a API direto no Windows (só se o Smart App Control não for problema)

```bash
cd SistemaRH.Api
dotnet run
```

Nesse caso você ainda precisa do MySQL rodando separadamente (`podman compose up -d mysql`, ou só o container `sistemarh-mysql` isolado). Na primeira vez que você rodar, o próprio programa cria o banco `sistemarh` e aplica toda a estrutura de tabelas automaticamente — não precisa rodar nenhum comando nem script `.sql` na mão.

Por padrão, a API sobe em **http://localhost:5289** (ou na porta que você passar em `--urls`).

> Quer ver as tabelas visualmente, sem linha de comando? Instale um cliente MySQL como [DBeaver](https://dbeaver.io/) (gratuito) ou [HeidiSQL](https://www.heidisql.com/) e conecte em `localhost:3306`, usuário `root`, sem senha.

### Testando pelo Swagger

Com a API rodando, abra **http://localhost:5289/swagger** no navegador. Lá você vê todas as rotas documentadas e consegue testar cada uma sem precisar de Postman ou Insomnia:

1. Teste `POST /api/auth/register` pra criar um usuário.
2. Teste `POST /api/auth/login` — copie o `token` que vier na resposta.
3. Clique no botão **Authorize** (canto superior direito) e cole `Bearer SEU_TOKEN_AQUI`.
4. Agora todas as rotas protegidas (`/api/ponto`, `/api/ausencias`...) vão funcionar nos testes do Swagger também.

### Rodando os testes automatizados

```bash
dotnet test
```

### Conectando o app Flutter

- **Emulador Android**: o emulador não enxerga `localhost` como "o seu PC" — use **`http://10.0.2.2:5289`** como endereço base da API dentro do app.
- **Flutter Web / navegador**: use `http://localhost:5289` normalmente.
- **Celular físico** na mesma rede Wi-Fi: use o IP local do seu PC (ex.: `http://192.168.0.10:5289`) — e rode a API com `dotnet run --urls http://0.0.0.0:5289` pra ela aceitar conexões de fora da própria máquina.

> O código que faz essas chamadas HTTP dentro do app Flutter (telas de login, "Registrar ponto", "Justificar ausência") ainda precisa ser escrito — esse backend está pronto e testado, mas a integração do lado do Flutter é o próximo passo.

---

## 5. Como funciona o login

### Cadastro (`POST /api/auth/register`)

1. O `AuthController` recebe `{ nome, email, senha, cargo, departamento }`.
2. As anotações `[Required]`, `[EmailAddress]`, `[MinLength]` no `RegisterDto` já barram, automaticamente, campos vazios ou e-mail mal formatado — o ASP.NET Core devolve 400 sozinho, sem o Controller precisar checar nada manualmente.
3. O `AuthService` confere se já existe alguém com esse e-mail (se sim, erro 409 — Conflito).
4. A senha é transformada em **hash** (próxima seção) e só o hash é salvo.
5. Devolve um token JWT, já "logando" a pessoa automaticamente depois de criar a conta.

### Como a senha é guardada (nunca em texto puro!)

Usamos a classe `PasswordHasher<Usuario>`, do próprio ASP.NET Core. Pense em hash como um **liquidificador que não tem botão de "voltar"**: você bota a senha dentro, ele devolve uma "pasta" (o hash) — dá pra conferir se duas senhas batem comparando as pastas, mas é praticamente impossível reconstruir a senha original a partir da pasta.

```csharp
// No cadastro:
usuario.SenhaHash = _passwordHasher.HashPassword(usuario, dto.Senha);

// No login:
var resultado = _passwordHasher.VerifyHashedPassword(usuario, usuario.SenhaHash, dto.Senha);
```

Mesmo que alguém roube o banco de dados inteiro, não consegue descobrir a senha de ninguém — só o hash.

### Login (`POST /api/auth/login`)

1. Busca o usuário pelo e-mail.
2. Verifica a senha com `VerifyHashedPassword`.
3. **Importante**: se o e-mail não existe OU a senha está errada, a mensagem de erro é **a mesma** ("E-mail ou senha incorretos"). Se disséssemos "esse e-mail não existe" só quando o e-mail realmente não existe, alguém mal-intencionado conseguiria descobrir quais e-mails têm conta cadastrada, só testando um por um — isso se chama *user enumeration*, e é uma falha de segurança real.
4. Se tudo bater, devolve um token JWT.

### Autenticação por token (JWT)

Depois do login, o app recebe um **token** — uma string longa que funciona como um crachá temporário. Em toda requisição seguinte a uma rota protegida, o app manda esse crachá no cabeçalho:

```
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
```

O token:
- **Prova quem é o usuário**, sem precisar mandar e-mail/senha de novo a cada requisição.
- **Expira** depois de um tempo (configurável em `Jwt:ExpiracaoEmMinutos`, hoje 120 minutos) — depois disso, a pessoa precisa fazer login de novo.
- É **assinado digitalmente** com uma chave secreta que só o servidor conhece (`Jwt:Secret`). Se alguém tentar alterar o conteúdo do token (ex.: trocar o Id do usuário pra se passar por outra pessoa), a assinatura não bate mais, e a API rejeita o token automaticamente.

As rotas marcadas com `[Authorize]` (em `PontoController` e `AusenciasController`) só funcionam com um token válido. O `ControllerExtensions.UsuarioIdAutenticado()` lê o Id do usuário diretamente do token — **nunca** de um parâmetro que o cliente poderia manipular. É assim que garantimos que ninguém consegue ver o ponto ou as ausências de outra pessoa.

### Logout

Como o JWT não fica "guardado" em nenhum lugar no servidor (ele é *stateless*), fazer logout é simplesmente o **app apagar o token** que guardou localmente. Não existe uma rota de logout no backend, porque não há nada pro servidor "esquecer".

---

## 6. Como uma ausência é criada (exemplo de fluxo completo)

1. O app manda `POST /api/ausencias` com `{ tipo, data, diaInteiro, descricao }` e o token no cabeçalho.
2. O `[Authorize]` do Controller barra a requisição com 401 se não tiver token válido.
3. O `AusenciasController.Criar()` chama `_ausenciaService.CriarAsync(usuarioId, dto)` — o `usuarioId` vem do token, nunca do corpo da requisição.
4. O `AusenciaService` monta a entidade `Ausencia` com `Status = Enviada` e salva no banco via `_db.Ausencias.Add(...)` + `SaveChangesAsync()`.
5. A resposta é `201 Created`, com os dados da ausência recém-criada.

Pra **editar** ou **cancelar**, o `AusenciaService` sempre confere duas coisas antes de qualquer coisa:
- `ausencia.UsuarioId != usuarioId` → `403 Forbidden` (não é sua ausência).
- `ausencia.Status != Enviada` → `400 Bad Request` (já está em análise, não dá mais pra mexer).

---

## 7. Services, Controllers e DTOs — por que separar assim?

Imagine que, no futuro, você queira expor as mesmas regras de ausência também por um comando de linha, ou por uma automação agendada (sem passar por uma requisição HTTP). Se toda a lógica estivesse dentro do Controller, você teria que duplicá-la.

Com os **Services**, o Controller vira só "tradutor" entre HTTP e a regra de negócio:

```csharp
// Controller — só recebe, chama e devolve:
public async Task<ActionResult<AusenciaDto>> Criar(AusenciaCreateDto dto)
{
    var ausencia = await _ausenciaService.CriarAsync(this.UsuarioIdAutenticado(), dto);
    return CreatedAtAction(nameof(ObterPorId), new { id = ausencia.Id }, ausencia);
}
```

Os **DTOs** existem por dois motivos:
1. **Segurança** — o que entra (`AusenciaCreateDto`) e o que sai (`AusenciaDto`) são classes diferentes da entidade do banco (`Ausencia`), então dá pra controlar exatamente quais campos ficam visíveis/editáveis de fora.
2. **Validação** — as anotações (`[Required]`, `[MinLength]`...) ficam no DTO, não na entidade, porque as regras de "o que é um cadastro válido" podem ser diferentes de "o que é um registro válido no banco".

---

## 8. Como as validações funcionam

Existem **duas camadas** de validação, e as duas são necessárias:

1. **No DTO** (`[Required]`, `[EmailAddress]`, `[MinLength]`): o ASP.NET Core confere automaticamente antes mesmo do código do Controller rodar, graças ao atributo `[ApiController]`. Se algo estiver errado, devolve `400` sozinho, com uma lista dos campos problemáticos.
2. **No Service** (regras de negócio, como "e-mail já cadastrado" ou "ausência não pode mais ser editada"): são coisas que uma anotação simples não consegue expressar, porque dependem de consultar o banco ou do estado atual do registro.

**Por que não confiar só na validação do app?** Porque qualquer pessoa pode mandar uma requisição HTTP direto pra API, sem passar pelo app — usando o navegador, o Postman, ou um script. Se a única validação existisse no Flutter, bastaria pular o app pra burlar todas as regras. Por isso a API *sempre* valida de novo, mesmo que o app já tenha validado antes.

---

## 9. Como os erros são tratados

Toda exceção que sobe até o `ExceptionMiddleware` vira uma resposta JSON no formato:

```json
{ "mensagem": "Texto amigável explicando o que aconteceu" }
```

| Situação                                             | Status | Exceção lançada           |
| ----------------------------------------------------- | ------ | -------------------------- |
| Dados com formato inválido (campo obrigatório vazio)  | 400    | (automático, do `[ApiController]`) |
| Regra de negócio violada (senha errada, e-mail duplicado, ausência que não pode mais ser editada) | 400 / 409 | `BusinessRuleException` / `ConflictException` |
| Sem token, ou token inválido/expirado                | 401    | (automático, do middleware de autenticação) |
| Token válido, mas sem permissão pra esse recurso      | 403    | `ForbiddenException`       |
| Registro não encontrado                               | 404    | `NotFoundException`        |
| Erro inesperado (bug, falha de conexão)               | 500    | qualquer outra `Exception` |

Erros 500 têm o stack trace completo **só no log do servidor** (terminal/arquivo de log) — o usuário nunca vê detalhes internos, só uma mensagem genérica. Isso evita vazar informações que facilitariam um ataque (nomes de tabelas, versão do banco, caminhos de arquivo, etc).

---

## 10. Configuração — onde ficam os segredos

- `appsettings.json` — configuração "pública", pode ir pro Git. Tem a estrutura da configuração (ex.: `Jwt:Issuer`), mas **não** tem a chave secreta.
- `appsettings.Development.json` — tem `Jwt:Secret` com uma chave **só de desenvolvimento local**, claramente marcada como tal. Em projetos reais, mesmo esse arquivo normalmente não vai pro Git (use `dotnet user-secrets` pra isso), mas como esse secret não é usado em produção nenhuma, manter aqui facilita rodar o projeto na hora sem passo extra.
- `appsettings.Production.example.json` — mostra a "forma" da configuração de produção, sem nenhum segredo real. Pra usar de verdade, copie pra `appsettings.Production.json` (que já está no `.gitignore`) e preencha com valores reais, **ou** defina variáveis de ambiente (o ASP.NET Core também lê `Jwt__Secret`, `ConnectionStrings__Default`, etc. — o `__` duplo substitui os `:` do JSON).

### CORS — quem pode chamar a API pelo navegador

Configurado em `Cors:AllowedOrigins`, dentro do `appsettings.json`. Hoje libera:

- `http://localhost:5173` (o site React, `npm run dev`)
- `http://localhost:61800` (o app Flutter rodando como Flutter Web, se você usar esse modo pra testar)

Pra liberar um novo ambiente (ex.: o site publicado em produção), adicione a URL nessa lista.

**Importante**: CORS só existe pra proteger quem acessa a API **pelo navegador**. O app Flutter rodando como app nativo (Android/iOS instalado de verdade) não é afetado por CORS — só o Flutter Web é.

---

## 11. Checklist de testes manuais

Baseado nos 7 itens da lista "Testando o aplicativo". Esses testes foram feitos **antes** da integração do login no app, testando o **backend isoladamente** via Swagger — e cada item já foi executado durante o desenvolvimento (não é só teoria). O login em si (app ↔ API) já foi validado depois, ao vivo, no emulador Android.

| # | O que testar | Como testar | Resultado esperado | ✅ Testado? |
|---|---|---|---|---|
| 1 | Abre a tela inicial? *(adaptado: a API sobe corretamente?)* | `dotnet run` dentro de `SistemaRH.Api` | Mensagem "Now listening on: http://localhost:5289", sem exceções | ✅ Sim — ver seção 4 |
| 2 | Navega para tarefas e cadastro? *(adaptado: as rotas de cadastro/ausência/ponto respondem?)* | Chamar cada rota pelo Swagger | Cada rota responde com o status esperado (200/201) | ✅ Sim — testado via curl, ver abaixo |
| 3 | Impede cadastro vazio? | `POST /api/auth/register` com `{}` | `400`, com mensagem indicando os campos obrigatórios | ✅ Sim |
| 4 | Salva tarefa válida? *(adaptado: salva uma ausência/ponto válido?)* | `POST /api/ausencias` com dados válidos e token | `201`, e o registro aparece depois em `GET /api/ausencias` | ✅ Sim |
| 5 | Atualiza a listagem? | Criar, depois `GET /api/ausencias` | A lista reflete o item criado; depois de `DELETE`, ele some da lista | ✅ Sim |
| 6 | Permite voltar? *(não se aplica a uma API — é comportamento de navegação da interface)* | — | — | Não aplicável ao backend; validar no app Flutter |
| 7 | Funciona com teclado aberto? *(não se aplica a uma API — é comportamento visual do app)* | — | — | Não aplicável ao backend; validar no app Flutter |

> Os itens 6 e 7 são sobre comportamento de **interface** (navegação e teclado virtual), então não existe o que testar no backend — eles precisam ser validados depois, quando o app Flutter for integrado com essas rotas.

### Cenários de segurança testados manualmente (via curl, durante o desenvolvimento)

- [x] Login com credenciais válidas → `200`, devolve token.
- [x] Login com senha incorreta → `400`.
- [x] Login com usuário inexistente → `400` (mesma mensagem da senha incorreta).
- [x] Cadastro com e-mail duplicado → `409`.
- [x] Acessar `/api/auth/me` sem token → `401`.
- [x] Acessar `/api/auth/me` com token inválido → `401`.
- [x] Usuário A tentando ver uma ausência do usuário B → `403`.
- [x] Cancelar uma ausência que já está "Em análise" → `400`.

### O que fazer se um resultado for diferente do esperado

1. Confira o log do terminal onde a API está rodando — erros inesperados aparecem lá com o stack trace completo.
2. Confira se o token usado ainda não expirou (120 minutos por padrão).
3. Confira se o `Content-Type: application/json` foi enviado na requisição.
4. Se nada disso resolver, é sinal de um bug de verdade — abra uma issue ou registre o que aconteceu pra investigar com calma.

---

## 12. Rotas da API

Base: `http://localhost:5289`

### Autenticação

#### `POST /api/auth/register` — pública

Cria uma conta nova.

```json
// Requisição
{ "nome": "Ana Silva", "email": "ana@empresa.com", "senha": "senha123", "cargo": "Dev", "departamento": "TI" }
```
```json
// 201 Created
{ "token": "eyJ...", "usuario": { "id": 1, "nome": "Ana Silva", "email": "ana@empresa.com", "cargo": "Dev", "departamento": "TI" } }
```
Erros: `400` (campos inválidos), `409` (e-mail já existe).

#### `POST /api/auth/login` — pública

```json
// Requisição
{ "email": "ana@empresa.com", "senha": "senha123" }
```
```json
// 200 OK — mesma forma do register
{ "token": "eyJ...", "usuario": { ... } }
```
Erros: `400` (e-mail/senha incorretos, ou campos vazios; ou `"Seu acesso foi desativado. Procure o RH."` se a conta está vinculada a um funcionário desligado).

#### `GET /api/auth/me` — 🔒 autenticada

Devolve os dados do usuário dono do token.
```json
// 200 OK
{ "id": 1, "nome": "Ana Silva", "email": "ana@empresa.com", "cargo": "Dev", "departamento": "TI",
  "saldoBancoHorasMinutos": 90, "totalSolicitacoesAusencia": 2,
  "funcionarioId": 5, "dataAdmissao": "2024-01-01T00:00:00", "gerenteNome": "Roberto" }
```
Quando a conta está vinculada a um funcionário (`funcionarioId` preenchido), `cargo`, `departamento`, `dataAdmissao`, `gerenteNome` e o saldo vêm do cadastro do RH. Sem vínculo, `funcionarioId`, `dataAdmissao` e `gerenteNome` vêm `null`.
Erros: `401` (sem token / token inválido ou expirado), `403` (funcionário vinculado foi desligado).

> Instantes (`registradoEm`, `criadoEm`) sempre saem em UTC, com `Z`. Datas de calendário (`data`, `dataAdmissao`, `dataRegistro`...) saem sem fuso, de propósito.

### Ponto

#### `GET /api/ponto/hoje` — 🔒 autenticada

Lista as marcações de ponto de hoje do usuário autenticado.
```json
// 200 OK
[ { "id": 1, "tipo": 0, "registradoEm": "2026-10-31T08:02:00Z" } ]
```
`tipo`: `0=Entrada, 1=SaidaAlmoco, 2=VoltaAlmoco, 3=Saida`.

#### `POST /api/ponto` — 🔒 autenticada

Registra a próxima marcação pendente do dia (a API decide qual é, na ordem Entrada → Saída almoço → Volta almoço → Saída).
```json
// 201 Created
{ "registro": { "id": 1, "tipo": 0, "registradoEm": "..." }, "proximo": 1 }
```
`proximo: null` quando a jornada do dia já foi concluída.
Erros: `400` (todas as marcações do dia já foram feitas).

### Ausências

Todas exigem autenticação (🔒) e só enxergam/alteram ausências do próprio usuário.

#### `GET /api/ausencias`

Lista as ausências do usuário autenticado, mais recentes primeiro.

#### `GET /api/ausencias/{id}`

Consulta uma ausência específica. Erros: `403` (não é sua), `404` (não existe).

#### `POST /api/ausencias`

```json
// Requisição
{ "tipo": 0, "data": "2026-11-10T00:00:00Z", "diaInteiro": true, "descricao": "Consulta médica" }
```
`tipo`: `0=AtestadoMedico, 1=FaltaJustificada, 2=EsquecimentoDePonto, 3=Outro`.
```json
// 201 Created
{ "id": 1, "tipo": 0, "data": "...", "diaInteiro": true, "descricao": "...", "status": 0, "criadoEm": "..." }
```
`status`: `0=Enviada, 1=EmAnalise, 2=Aprovada, 3=Recusada`.
Erros: `400` (campos inválidos).

#### `PUT /api/ausencias/{id}`

Mesmo corpo do `POST`. Só funciona enquanto `status == Enviada`.
Erros: `400` (já não pode mais editar), `403` (não é sua), `404` (não existe).

#### `DELETE /api/ausencias/{id}`

Cancela (remove) a ausência. Só funciona enquanto `status == Enviada`. Devolve `204 No Content`.
Erros: `400`, `403`, `404` — mesmos casos do `PUT`.

### Lado administrativo/RH

Essas rotas existem pro site (ainda sem UI pra elas — ver seção 13). A maioria exige o perfil **RH** ou **Admin** no token (`🔒 RhOuAdmin`); departamentos e cargos só exigem estar logado pra listar (🔒), mas também pedem RH/Admin pra criar/editar.

> **Como funciona a policy `RhOuAdmin`**: o token JWT carrega uma claim `"perfil"` (`Funcionario`, `Rh` ou `Admin`) — ver `Usuario.Perfil`. Toda conta criada por `POST /api/auth/register` nasce com `Funcionario` (não dá pra se autopromover por essa rota, de propósito). **Hoje não existe nenhum endpoint pra promover alguém a RH/Admin** — isso só é feito editando a coluna `Perfil` direto no banco (`UPDATE Usuarios SET Perfil = 1 WHERE Email = '...'`, onde `1 = Rh` e `2 = Admin`). É uma limitação conhecida, não um bug — criar essa rota de gerenciamento é um próximo passo.

#### Departamentos — `GET/POST/PUT /api/departamentos`

`GET` 🔒, `POST`/`PUT` 🔒 RhOuAdmin. Corpo do `POST`/`PUT`: `{ "nome": "...", "descricao": "..." }`. Erros: `400`, `404` (PUT em id inexistente), `409` (nome duplicado).

#### Cargos — `GET/POST/PUT /api/cargos`

Mesmo padrão dos departamentos. Corpo: `{ "nome": "...", "salarioBase": 5000, "descricao": "..." }`.

#### Funcionários — `GET/POST/PUT /api/funcionarios`, `POST /api/funcionarios/{id}/desligar`, `POST /api/funcionarios/{id}/acesso-app` — 🔒 RhOuAdmin

```json
// POST /api/funcionarios
{ "nome": "Ana Souza", "cpf": "111.111.111-11", "email": "ana@empresa.com", "dataAdmissao": "2024-01-01", "departamentoId": 1, "cargoId": 1, "gerenteId": null, "senhaAcessoApp": "senha123" }
```
`GET /api/funcionarios` aceita `?status=0` (0=Ativo, 1=Desligado) e `?departamentoId=` como filtros opcionais.
`POST /{id}/desligar` aceita `{ "dataDesligamento": "..." }` (opcional — usa a data atual se omitido).
Erros: `404` (departamento/cargo/funcionário/gestor não existe), `409` (CPF ou e-mail duplicado), `400` (já está desligado, ou tentando ser gestor de si mesmo).

Todo funcionário devolvido traz `possuiAcessoApp` (`true` se já existe uma conta de login vinculada a ele).

**Acesso ao app** — `POST /{id}/acesso-app` com `{ "senha": "..." }` (devolve `200` com o funcionário atualizado), ou `senhaAcessoApp` (opcional) direto no `POST /api/funcionarios`:
- se já existe conta vinculada a esse funcionário, só troca a senha (serve pra "redefinir senha");
- senão, se existe conta com o mesmo e-mail, vincula essa conta e **sobrescreve a senha** pela definida pelo RH (o perfil da conta é mantido);
- senão, cria uma conta nova (perfil `Funcionario`) com o e-mail do funcionário.

Erros: `404` (funcionário não existe), `400` (`"Não é possível dar acesso ao app a um colaborador desligado."`, `"Cadastre um e-mail para esse colaborador antes de criar o acesso ao app."`, `"A senha de acesso ao app precisa ter pelo menos 6 caracteres."`), `409` (`"Esse e-mail já está vinculado ao acesso de outro colaborador."`). No `POST /api/funcionarios`, essas validações rodam antes de gravar — se falharem, nada é criado. O vínculo nunca é feito automaticamente no login/cadastro do app.

`gerenteId` (opcional) é o `Id` de outro funcionário já cadastrado — é essa ligação que monta o **Organograma** no site: quem não tem `gerenteId` aparece no topo; quem tem, aparece "pendurado" embaixo do gestor escolhido. Cada funcionário também devolve `gerenteNome` (preenchido pela API) pra facilitar exibição sem precisar de outra consulta.

#### Banco de horas — `GET/POST /api/funcionarios/{id}/banco-horas` — 🔒 RhOuAdmin

`GET` devolve o extrato completo: `{ "saldoMinutos": 90, "lancamentos": [ { "id": 1, "dataRegistro": "...", "tipo": 0, "quantidadeMinutos": 120, "descricao": "..." } ] }` (`tipo`: `0=Credito, 1=Debito`).
`POST` registra um lançamento novo: `{ "dataRegistro": "2026-08-10", "tipo": 0, "quantidadeMinutos": 120, "descricao": "Hora extra" }`.

#### Recrutamento — `GET /api/recrutamento`, `POST /api/recrutamento`, `PUT /api/recrutamento/{id}/status` — 🔒 RhOuAdmin

`GET` aceita `?status=` opcional (`0=EmProcesso, 1=Entrevista, 2=Aprovado, 3=Rejeitado`). `PUT /{id}/status`: `{ "status": 2, "observacoes": "..." }`.

#### Avaliações — `GET/POST /api/avaliacoes?funcionarioId=` — 🔒 RhOuAdmin

`funcionarioId` é obrigatório na query string nos dois verbos. Corpo do `POST`: `{ "nota": 9.5, "comentario": "...", "dataAvaliacao": "2026-07-05" }` (nota entre 0 e 10).

#### Agenda — `GET/POST/DELETE /api/agenda` — 🔒 RhOuAdmin

`GET` aceita `?de=` e `?ate=` (intervalo de datas, opcionais). Corpo do `POST`: `{ "titulo": "...", "dataEvento": "...", "tipo": 0, "funcionarioId": 1 }` (`funcionarioId` opcional; `tipo`: `0=Reuniao, 1=Entrevista, 2=Avaliacao, 3=Outro`).

#### Folha de pagamento — `GET/POST /api/folha-pagamento?funcionarioId=` — 🔒 RhOuAdmin

Corpo do `POST`: `{ "competencia": "2026-08", "salario": 4000, "bonus": 400, "descontos": 300 }` — o `salarioLiquido` é calculado pelo backend (nunca é enviado). Erros: `409` (já existe folha dessa competência pra esse funcionário).

#### Acompanhamento do RH — `/api/rh` — 🔒 RhOuAdmin

`GET /api/rh/ausencias?status=` (status opcional, `0..3`) — justificativas de **todos** os colaboradores, mais recentes primeiro:
```json
[ { "id": 1, "tipo": 0, "data": "2026-11-10T00:00:00", "diaInteiro": true, "descricao": "...", "status": 0, "criadoEm": "2026-11-13T15:50:51Z",
    "usuarioId": 3, "usuarioNome": "Ana", "usuarioEmail": "ana@empresa.com", "funcionarioId": 5 } ]
```
`PUT /api/rh/ausencias/{id}/status` com `{ "status": 2 }` — devolve `200` com o mesmo formato acima. Só aceita `1=EmAnalise`, `2=Aprovada` ou `3=Recusada`.
Erros: `400` (`"Status inválido."`, ou `"Essa justificativa já foi concluída e não pode mais ser alterada."` se já está Aprovada/Recusada), `404` (`"Justificativa de ausência não encontrada."`).

`GET /api/rh/ponto?data=AAAA-MM-DD` (opcional; padrão = hoje) — marcações de todos no dia, no horário de Brasília, ordenadas por nome e depois horário:
```json
[ { "id": 1, "tipo": 0, "registradoEm": "2026-11-13T11:02:00Z", "usuarioId": 3, "usuarioNome": "Ana", "usuarioEmail": "ana@empresa.com" } ]
```
Erros: `400` (`"Data inválida. Use o formato AAAA-MM-DD."`).

---

## 13. Resumo do que foi criado

| Arquivo/Pasta | O que é |
|---|---|
| `Models/Usuario.cs`, `RegistroPonto.cs`, `Ausencia.cs` | As tabelas usadas pelo app (login, ponto, ausências) |
| `Models/Departamento.cs`, `Cargo.cs`, `Funcionario.cs`, `LancamentoBancoHoras.cs`, `Recrutamento.cs`, `Avaliacao.cs`, `EventoAgenda.cs`, `FolhaPagamento.cs` | Tabelas do lado administrativo/RH |
| `Data/AppDbContext.cs` | Configuração do Entity Framework Core |
| `Migrations/` | Histórico de criação das tabelas (gerado por `dotnet ef migrations add`) |
| `Dtos/AuthDtos.cs`, `AusenciaDtos.cs`, `PontoDtos.cs` | Formato de entrada/saída de cada rota do app |
| `Dtos/DepartamentoDtos.cs`, `CargoDtos.cs`, `FuncionarioDtos.cs`, `BancoHorasDtos.cs`, `RecrutamentoDtos.cs`, `AvaliacaoDtos.cs`, `AgendaDtos.cs`, `FolhaPagamentoDtos.cs` | Formato de entrada/saída das rotas administrativas/RH |
| `Services/AuthService.cs` | Regras de cadastro, login e hash de senha (já unifica os dois "banco de horas" — ver item 5 antigo, resolvido) |
| `Services/JwtTokenService.cs` | Geração do token JWT (inclui a claim `perfil`, usada pela policy `RhOuAdmin`) |
| `Services/PontoService.cs` | Regra do "qual é a próxima marcação do dia" |
| `Services/AusenciaService.cs` | CRUD de ausências, com validação de dono e de status |
| `Services/DepartamentoService.cs`, `CargoService.cs`, `FuncionarioService.cs`, `LancamentoBancoHorasService.cs`, `RecrutamentoService.cs`, `AvaliacaoService.cs`, `EventoAgendaService.cs`, `FolhaPagamentoService.cs` | Regras de negócio do lado administrativo/RH (`FuncionarioService` também cria/vincula o acesso ao app) |
| `Services/RhService.cs` | Visão do RH sobre ausências (com aprovação) e ponto de todos os colaboradores |
| `Controllers/AuthController.cs`, `PontoController.cs`, `AusenciasController.cs` | As rotas HTTP usadas pelo app |
| `Controllers/DepartamentosController.cs`, `CargosController.cs`, `FuncionariosController.cs`, `RecrutamentoController.cs`, `AvaliacoesController.cs`, `AgendaController.cs`, `FolhaPagamentoController.cs`, `RhController.cs` | As rotas HTTP administrativas/RH (🔒 RhOuAdmin) |
| `Middleware/ExceptionMiddleware.cs` | Tratamento global de erros |
| `Exceptions/ApiExceptions.cs` | Tipos de erro de negócio (404/403/409/400) |
| `Common/ControllerExtensions.cs` | Helper pra ler o usuário autenticado do token |
| `Common/HorarioBrasilia.cs` | Conta do "dia de hoje" em Brasília (UTC-3), usada pelo ponto |
| `Program.cs` | Liga tudo: banco, autenticação (+ policy `RhOuAdmin`), CORS, Swagger, middleware |
| `appsettings*.json` | Configurações (desenvolvimento / exemplo de produção) |
| `SistemaRH.Api.Tests/` | Testes automatizados (unitários + integração ponta-a-ponta) |

### O que ainda falta (próximos passos)

1. ~~Integrar o app Flutter~~ ✅ feito — o app já tem tela de login de verdade e consome `/api/auth/login`, mostrando nome, cargo/departamento e saldo do banco de horas reais. Ponto e ausências ainda não foram ligados às telas correspondentes do app.
2. **Integrar o site React** com a API — tanto o login quanto as rotas administrativas/RH do item 4 (feito) ainda não têm nenhuma tela usando elas.
3. ~~Fluxo de **aprovação de ausências pelo RH**~~ ✅ feito — `GET /api/rh/ausencias` e `PUT /api/rh/ausencias/{id}/status` (ver seção 12).
4. ~~Criar rotas/serviços pro lado administrativo~~ ✅ feito — `Departamento`, `Cargo`, `Funcionario`, `LancamentoBancoHoras`, `Recrutamento`, `Avaliacao`, `EventoAgenda` e `FolhaPagamento` já têm Controller/Service completos, documentados na seção 12 e visíveis no Swagger, protegidos pela policy `RhOuAdmin`. **Ainda falta a UI** — isso é trabalho do site React.
5. ~~Unificar os dois "banco de horas"~~ ✅ feito — `AuthService` agora calcula o saldo a partir do extrato (`LancamentoBancoHoras`) sempre que a conta (`Usuario`) tem um `FuncionarioId` vinculado; senão, cai de volta no campo achatado `SaldoBancoHorasMinutos` (caso das contas demo que nunca foram ligadas a um `Funcionario`).
6. **Criar uma rota de gerenciamento de usuários** — ligar um `Usuario` a um `Funcionario` já é feito pelo RH (`POST /api/funcionarios/{id}/acesso-app`), mas ainda não existe jeito pela API de promover uma conta pra `Rh`/`Admin` — só editando o banco direto.
