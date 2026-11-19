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

1. Teste `POST /api/auth/login` com uma conta que já existe — copie o `token` que vier na resposta. (Criar contas pelo `POST /api/auth/register` agora exige um token de **Admin**; colaborador ganha acesso pelo RH, em `POST /api/funcionarios/{id}/acesso-app`.)
2. Clique no botão **Authorize** (canto superior direito) e cole `Bearer SEU_TOKEN_AQUI`.
3. Agora todas as rotas protegidas (`/api/ponto`, `/api/ausencias`...) vão funcionar nos testes do Swagger também.

### Rodando os testes automatizados

```bash
dotnet test
```

Com o Smart App Control ligado (ver acima), o Windows bloqueia o `.dll` recém-compilado dos testes. Nesse caso, rode dentro do container do SDK (de dentro da pasta `SistemaRH.Backend`):

```bash
podman run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test
```

Os testes usam o banco InMemory, que não tem transação nem `SELECT ... FOR UPDATE`; esses passos só rodam no MySQL (`Database.IsRelational()`).

### Conectando o app Flutter

- **Emulador Android**: o emulador não enxerga `localhost` como "o seu PC" — use **`http://10.0.2.2:5289`** como endereço base da API dentro do app.
- **Flutter Web / navegador**: use `http://localhost:5289` normalmente.
- **Celular físico** na mesma rede Wi-Fi: use o IP local do seu PC (ex.: `http://192.168.0.10:5289`) — e rode a API com `dotnet run --urls http://0.0.0.0:5289` pra ela aceitar conexões de fora da própria máquina.

> O código que faz essas chamadas HTTP dentro do app Flutter (telas de login, "Registrar ponto", "Justificar ausência") ainda precisa ser escrito — esse backend está pronto e testado, mas a integração do lado do Flutter é o próximo passo.

---

## 5. Como funciona o login

### Cadastro (`POST /api/auth/register`) — só Admin

O cadastro era público: qualquer pessoa criava uma conta, batia ponto, mandava justificativa e lia o salário base dos cargos — e, cadastrando-se antes com o e-mail de um futuro colaborador, "virava" o acesso dele quando o RH liberasse o app. Agora:

1. Só um token de **Admin** chama essa rota (policy `Admin`); sem token é `401`, com outro perfil é `403`.
2. O `AuthController` lê só `{ nome, email, senha, perfil? }` (`perfil`: `0=Funcionario` (padrão), `1=Rh`, `2=Admin`). Qualquer outro campo (`saldoBancoHorasMinutos`, `cargo`, `departamento`...) é ignorado.
3. O `AuthService` valida nome, e-mail e senha (ver seção 8), confere se já existe alguém com esse e-mail (`409`) e grava só o **hash** da senha (próxima seção).
4. Devolve `201` com a conta criada (`{ id, nome, email, perfil, funcionarioId, funcionarioNome, funcionarioDesligado }`) — **sem token**: quem vai usar a conta faz login.

Colaborador **não** ganha acesso por aqui: o RH cria o acesso dele em `POST /api/funcionarios/{id}/acesso-app`.

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

1. E-mail com caractere fora do ASCII (acento) ou de controle é recusado na hora, com a mensagem genérica — a coluna do banco ignora acento, e "zédalima@" entrava na conta de "zedalima@".
2. **Limite de tentativas** (`LimiteTentativasLogin`), com duas contagens:
   - por **e-mail + IP**: 5 falhas em 15 minutos bloqueiam aquele e-mail **a partir daquele IP** por 15 minutos (quem está em outro IP continua entrando — antes um anônimo deixava o Admin de fora pra sempre mandando 5 senhas erradas a cada 15 minutos);
   - por **IP**: 20 falhas em 15 minutos, somando todos os e-mails, bloqueiam o IP por 15 minutos (freia quem testa uma senha comum em muitos e-mails).

   Bloqueado, responde `429` com `"Muitas tentativas de login. Tente novamente em N minuto(s)."` e o cabeçalho `Retry-After`, até pra senha certa. Um login certo zera as falhas daquele e-mail, mas **nunca** desfaz um bloqueio que já está valendo.

   **Tentativas ao mesmo tempo.** Cada tentativa é **reservada antes** de conferir a senha, e quem não cabe **espera a vez** (até 10 s) em vez de levar 429:
   - o mesmo e-mail, do mesmo IP, confere **uma senha por vez**. 20 senhas erradas em paralelo continuam parando em 5 (a 6ª já encontra o bloqueio), e 8 logins **certos** em paralelo entram todos — antes, as tentativas em andamento contavam como falha e 3 dos 8 recebiam "tente em 15 minutos" sem bloqueio nenhum;
   - do mesmo IP, as tentativas em andamento mais as falhas nunca passam de 20: a 21ª espera uma terminar (pessoas diferentes atrás do mesmo NAT entram, só um pouco depois).

   O `429` só sai com **bloqueio de verdade** ou, se a fila não andar em 10 s, com `"Muitas tentativas de login. Aguarde alguns segundos e tente de novo."` e `Retry-After: 2`.

   **Memória.** A contagem fica em memória (a API roda numa instância só), com teto de 100 mil entradas (cada uma é um e-mail+IP ou um IP). Uma entrada só é apagada quando não guarda mais nada (sem falha na janela, sem bloqueio, sem tentativa em andamento), e apagar uma dessas é igual a ela nunca ter existido. Se o teto encher de entradas **ativas**, a escolha é **falhar fechado**: uma chave **nova** recebe `429` (`Retry-After: 60`), e as chaves que já existem continuam contando normalmente. Encher a memória nunca apaga um bloqueio nem zera uma contagem. Com IP bloqueado, a chave e-mail+IP nem é criada. Antes, com o `MemoryCache` cheio, a contagem de uma chave nova deixava de ser guardada e as entradas antigas (inclusive bloqueios) podiam ser descartadas.

   **De onde vem o IP.** Do `X-Forwarded-For` (`UseForwardedHeaders`, com a lista de proxies conhecidos limpa porque o proxy da Railway não tem IP fixo). Só o **último** valor da lista é usado (`ForwardLimit = 1`). Isso depende de o proxy da Railway **acrescentar o IP real no fim** do `X-Forwarded-For` (e não repassar o cabeçalho que o cliente mandou sem mexer):
   - assim, o último valor é sempre o que o proxy viu, e o que o cliente escreveu fica antes e é ignorado;
   - se a API for acessada **sem passar pelo proxy**, o cabeçalho é do cliente, e trocá-lo a cada tentativa burla o limite por IP. Por isso a porta da API não pode ficar exposta direto;
   - se um dia houver **mais de um proxy** na frente, ajuste `Proxy:ForwardLimit` na configuração. Senão todo mundo parece vir do IP do proxy do meio, e o limite de 20 por IP vira global.
3. Busca o usuário pelo e-mail e verifica a senha com `VerifyHashedPassword`. Se o e-mail **não existe**, a senha é conferida mesmo assim contra um hash fictício — a resposta leva o mesmo tempo nos dois casos (antes, 20 ms contra 60 ms denunciavam quais e-mails tinham conta).
4. **Importante**: se o e-mail não existe OU a senha está errada, a mensagem de erro é **a mesma** ("E-mail ou senha incorretos."). Se disséssemos "esse e-mail não existe" só quando o e-mail realmente não existe, alguém mal-intencionado conseguiria descobrir quais e-mails têm conta cadastrada, só testando um por um — isso se chama *user enumeration*, e é uma falha de segurança real.
5. Se tudo bater (e o colaborador ligado não estiver desligado), devolve um token JWT.

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

### Versão do token (sessão que cai na hora)

Assinatura e validade não bastam: um token roubado, ou o de alguém que acabou de ser desligado, continuava funcionando por até 120 minutos. Por isso cada conta tem `Usuario.TokenVersao`, que vai no JWT como a claim `tv`. **A cada requisição autenticada** (`JwtBearerEvents.OnTokenValidated` → `Services/ValidacaoDeSessao.cs`) a API confere no banco:

- se a conta ainda existe;
- se o colaborador ligado a ela **não** está desligado (senão `401` com `"Seu acesso foi desativado. Procure o RH."`);
- se o `tv` do token é a versão atual (senão `401` com `"Sua sessão foi encerrada. Entre novamente."`).

A `TokenVersao` sobe — e todos os tokens antigos daquela conta param de valer — no **logout**, ao **definir/redefinir a senha do app**, ao **trocar a própria senha**, ao **desligar** o colaborador, ao **mudar o perfil** e ao **mudar o e-mail de login**. Tokens emitidos antes dessa mudança não têm `tv` e são recusados: depois da atualização, todo mundo entra de novo uma vez.

### Logout (`POST /api/auth/logout`)

Exige estar logado e responde `204`. Sobe a `TokenVersao` da conta, então o token usado **e os de todas as outras abas/aparelhos** deixam de valer na hora. O app/site também apaga o token que guardou.

### Troca da própria senha (`PUT /api/auth/senha`)

A própria pessoa troca a senha, confirmando a atual: `{ "senhaAtual": "...", "novaSenha": "..." }`.
- A senha atual errada responde `400` `"Senha atual incorreta."` e **conta no mesmo limite do login** (e-mail+IP e IP). Sem isso, quem pegasse um celular com a sessão aberta testava senhas aqui à vontade. Depois de 5, vem `429`, tanto aqui quanto no login daquele IP, com o mesmo `Retry-After`:
  - aqui, `"Muitas tentativas com a senha atual incorreta. Tente novamente em N minuto(s)."`;
  - no login, `"Muitas tentativas de login. Tente novamente em N minuto(s)."`.
- A nova senha segue as regras da seção 8 e precisa ser diferente da atual (`"A nova senha precisa ser diferente da atual."`).
- A troca sobe a `TokenVersao`, o que derruba as outras sessões, e devolve um **token novo** `{ "token": "..." }` pra esta continuar.
- A linha da conta é travada (`FOR UPDATE`) e relida antes de conferir a senha. Uma promoção ao mesmo tempo, que troca a senha pela provisória, ou espera esta troca terminar e sobrescreve a senha, ou termina antes, e aí a senha atual digitada (a antiga) deixa de conferir.

### Promoção de perfil e senha provisória

A senha de uma conta de colaborador é escolhida pelo **RH** (no cadastro ou no "Criar acesso ao app"). Se o Admin promovesse essa conta a Admin, o RH continuava sabendo a senha e entrava como Admin.

Por isso, quando o perfil **sobe** (Funcionário → RH, Funcionário → Admin, RH → Admin), o `PUT /api/usuarios/{id}/perfil` exige `senhaProvisoria`, definida pelo Admin:
- sem ela, a resposta é `400` `"Defina uma senha provisória para a conta promovida."`;
- ela segue as regras de senha, **substitui a senha atual** e sobe a `TokenVersao`;
- a pessoa entra com a provisória e troca pela dela em `PUT /api/auth/senha`.

Rebaixar não pede senha nem mexe na senha.

---

## 6. Como uma ausência é criada (exemplo de fluxo completo)

1. O app manda `POST /api/ausencias` com `{ tipo, data, diaInteiro, descricao }` e o token no cabeçalho.
2. O `[Authorize]` do Controller barra a requisição com 401 se não tiver token válido.
3. O `AusenciasController.Criar()` chama `_ausenciaService.CriarAsync(usuarioId, dto)` — o `usuarioId` vem do token, nunca do corpo da requisição.
4. O `AusenciaService` monta a entidade `Ausencia` com `Status = Enviada` e salva no banco via `_db.Ausencias.Add(...)` + `SaveChangesAsync()`.
5. A resposta é `201 Created`, com os dados da ausência recém-criada.

Pra **consultar**, **editar** ou **cancelar**, o `AusenciaService` sempre confere, antes de qualquer coisa:
- ausência de outra pessoa → `404 Not Found`, **igual** a uma que não existe (um `403` revelaria que aquele id existe);
- `versao` enviada diferente da atual → `409 Conflict` (alguém — o RH, por exemplo — mexeu nela depois que a tela carregou);
- `ausencia.Status != Enviada` → `400 Bad Request` (já está em análise, não dá mais pra mexer).

Ao criar, a mesma pessoa com o **mesmo tipo e a mesma data** ainda Enviada/Em análise recebe `409` (envio repetido). A conta do usuário é travada no banco durante a gravação (`SELECT ... FOR UPDATE`), então dois envios simultâneos não passam juntos pela checagem.

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

### Regras centralizadas (`Common/`)

As regras de formato ficam em helpers únicos, usados por todos os serviços (o painel e o app validam igual, pra mostrar o erro antes):

| Helper | Regra |
|---|---|
| `Textos.Validar` / `ValidarOpcional` / `ValidarNomePessoa` + `RegraTexto` (`Textos.Limpar`) | **Campos de uma linha** (nome, assunto, departamento, cargo, título, vaga): espaços repetidos (inclusive NBSP e os outros `\p{Zs}`) viram um só, as pontas são aparadas, e **qualquer** caractere de controle ou invisível é recusado — inclusive `\n`, tab, zero-width (U+200B) e RTL override (U+202E). **Campos de texto longo** (`MultiLinha`: descrição da ausência, motivo do ajuste, mensagem e resposta ao RH, comentário, observações, descrição de evento), nesta ordem: `\r\n` e `\r` viram `\n`; espaços repetidos viram um só; **cada linha é aparada** (uma linha só de espaços fica vazia); quebras seguidas são reduzidas a no máximo 2 (`\n{3,}` → `\n\n`); as pontas do texto são aparadas. Sem aparar as linhas antes, `"\n \n \n"` passava como 3 quebras. Os outros controles/invisíveis (inclusive tab) continuam recusados. Mínimo e máximo contados **depois** da limpeza, em caracteres de verdade (code points: um emoji como 👍 conta 1, igual ao painel, ao app e ao `varchar(N)` do MySQL). O **máximo** conta todos os code points, porque é o que ocupa a coluna. O **mínimo** não conta as partes de emoji (U+FE0E, U+FE0F, U+200D, U+20E3, tons U+1F3FB–U+1F3FF e tags U+E0020–U+E007F). Espaço e quebra de linha entre palavras contam. Exemplos: "👍🏽👍🏽" tem 4 code points e 2 caracteres, então não atinge o mínimo de 3; "❤️❤️❤️" tem 6 e 3, e atinge. |
| Caracteres recusados (textos e senha) | Categorias Unicode **Cc** (controle; nos textos longos, só `\n` é aceito), **Cf** (formatação: U+200B–U+200F, U+202A–U+202E, U+2060–U+2064, U+FEFF, U+00AD, ZWJ U+200D...), **Zl** (U+2028), **Zp** (U+2029), surrogate solto, **Cn** (code point sem caractere atribuído: U+2065, U+FFF0–U+FFF8, U+E0080–U+E00FF, U+E01F0–U+E0FFF..., que não aparecem na tela e davam nomes "gêmeos"; exceção: a faixa U+1F000–U+1FAFF, onde ficam os emoji novos que a tabela do .NET 8 ainda não conhece), **não-caracteres** (U+FDD0–U+FDEF e todo U+xFFFE/U+xFFFF), e estes "brancos" que não são Cc/Cf (`Textos.EhInvisivelExtra`): U+034F, U+115F, U+1160, U+3164, U+FFA0, U+17B4, U+17B5, U+2800, U+180B–U+180F, U+FE00–U+FE0F e U+E0100–U+E01EF. |
| Emoji (`Textos.Limpar` → `SequenciaDeEmoji`, só nos **textos longos** e no **assunto** da mensagem ao RH) | As partes de emoji só são aceitas **dentro de uma sequência de emoji de verdade**. **Base de emoji** (`Textos.EhBaseDeEmoji`): U+1F000–U+1FAFF (menos os tons de pele), U+2600–U+27BF, U+2300–U+23FF, U+2B00–U+2BFF, U+2190–U+21FF, U+2900–U+297F, U+3030, U+303D, U+3297, U+3299, U+00A9, U+00AE, U+203C, U+2049, U+2122, U+2139, U+24C2, U+25AA, U+25AB, U+25B6, U+25C0 e U+25FB–U+25FE. **Base de keycap**: dígito, `#` e `*`. Regras: **seletor** (U+FE0E/U+FE0F) só depois de uma base de emoji ou de keycap, no máximo 1 por base; **keycap** (U+20E3) só depois de dígito/`#`/`*` (com ou sem seletor); **tom de pele** (U+1F3FB–U+1F3FF) só depois de uma base de emoji, no máximo 1 por base; **ZWJ** (U+200D) só **entre** duas bases de emoji (antes dele pode vir o seletor/tom da primeira; depois, logo a próxima base), no máximo **10 por sequência**; **tags** (U+E0020–U+E007E) só logo depois de 🏴 (U+1F3F4), no máximo **8**, terminando em U+E007F. Passam "❤️", "👍🏽", "👨‍👩‍👧", "🏳️‍🌈", "🏴‍☠️", "1️⃣", "#️⃣", "©️", "↩️", "▶️", "👩🏽‍💻" e as bandeiras com tags. São recusados: "a" + U+FE0F, dois seletores ou dois tons na mesma base, ZWJ entre letras, tom depois de letra ou de dígito, tags depois de letra (texto escondido), tags sem U+E007F e mais de 8 tags. O texto precisa ter pelo menos 1 caractere visível de verdade. **Nome de pessoa, departamento, cargo, e-mail e senha** seguem a regra estrita: nenhuma dessas partes é aceita. |
| Letras do nome | Contadas por caractere de verdade (code point, `Rune.IsLetter`): "𝓐𝓷𝓪 𝓢𝓲𝓵𝓿𝓪" tem 8 letras. |
| Tamanhos | Nome de pessoa 3–120 (com pelo menos 2 letras); departamento/cargo 2–100; motivo 3–500; descrição da ausência 3–2000; assunto 3–100; mensagem/resposta 3–2000. As colunas do banco têm o mesmo limite. |
| `Emails` | Só ASCII visível, no máximo 254, sem espaços, gravado em minúsculas. Parte local (antes do `@`, até 64): letras, dígitos e `` . ! # $ % & ' * + / = ? ^ _ ` { | } ~ - ``, sem ponto no começo, no fim nem `..` (então `o'neil@empresa.com` é aceito). Domínio: rótulos de 1 a 63 caracteres (letras, dígitos e hífen fora das pontas) e TLD só de letras, de 2 a 63. |
| `Telefones` | 10 ou 11 dígitos, DDD de 11 a 99 e, com 11 dígitos, o terceiro é 9. Gravado formatado: `(11) 98765-4321`. Letras, `+` e dígitos de outros alfabetos são recusados. |
| `Cpf` | Só dígitos ASCII (com ou sem máscara) e dígito verificador válido; gravado como `000.000.000-00`. |
| `Senhas` | Senha (do app, provisória, nova): 6 a 64 caracteres **de verdade** (code points), não pode ser só espaços, não pode começar/terminar com espaço e **não pode ter emoji** (base ou parte de emoji, como acima) **nem nenhum** dos caracteres recusados acima, em qualquer posição: `"A senha não pode conter emoji nem caracteres invisíveis."` (6 × U+200B parecia uma senha vazia; NUL e BOM passavam; o mesmo coração chega como "❤" de um teclado e "❤️" de outro, e a pessoa não entrava pelo outro aparelho). |
| `Dinheiro` | No máximo 2 casas (mais que isso dá 400, sem arredondar em silêncio). Salário R$ 1,00–1.000.000,00; vale R$ 0,00–10.000,00; folha R$ 0–1.000.000 e líquido ≥ 0. Novo valor igual ao atual dá 400. Número tem que vir como número no JSON (`JsonNumberHandling.Strict`): `"5.000"` (texto) dá 400 em vez de virar R$ 5,00. |
| `Datas` | Nascimento: 01/01/1900 até hoje − 14 anos. Admissão: obrigatória, de nascimento + 14 anos até hoje + 90 dias. Vigência: de **admissão até max(hoje, admissão)** — data futura dá `"Ajustes com data futura ainda não são suportados."`; pra quem ainda vai ser admitido (admissão futura), a vigência é a própria data de admissão (`"Para quem ainda vai ser admitido, a vigência é a própria data de admissão (dd/mm/aaaa)."`). Desligamento: de **admissão até max(hoje, admissão)**; sem data, vale hoje — ou a própria admissão, se ela é futura. Assim quem desistiu antes de começar pode ser desligado na data de admissão (`"Para quem ainda vai ser admitido, o desligamento é na própria data de admissão (dd/mm/aaaa)."` pra uma data depois dela). Ausência: hoje ± 365 dias. Avaliação: da admissão até hoje. Agenda: hoje − 1 ano até hoje + 2 anos. |
| `DataCalendarioJsonConverter` | Datas de calendário são lidas **como foram escritas**: `"2026-11-17T00:00:00+14:00"` é 17/11 (antes virava 16/11 no fuso do servidor). |
| `DataHoraBrasiliaJsonConverter` | Data e hora da agenda (`dataEvento`): com fuso ou `Z`, é convertida pro horário de Brasília (`"2026-12-01T02:00:00Z"` → `2026-11-30T23:00:00`); sem fuso, já é de Brasília. É gravada e devolvida sem fuso, igual no POST e no GET. |
| `Enums` | Tipo de ausência, tipo de evento, status etc.: `Enum.IsDefined`, senão 400 (antes `tipo: 99` era gravado). `perfil` e `perfilAtual` inválidos (`9`, `-1`, `"0"`, `"Admin"`) dão `"Perfil inválido."`. Todo status inválido — no corpo (`0` onde não cabe, `99`, `-1`, `"abc"`, `null`) ou na query (`?status=9`, `?status=abc`) — responde a mesma mensagem: `"Status inválido."`. |

Os campos obrigatórios de número/data dos DTOs são anuláveis com `[Required]` — sem isso, um campo esquecido virava `0` ou `01/01/0001` em silêncio.

---

## 9. Como os erros são tratados

Toda exceção que sobe até o `ExceptionMiddleware` vira uma resposta JSON no formato:

```json
{ "mensagem": "Texto amigável explicando o que aconteceu" }
```

| Situação                                             | Status | Exceção lançada           |
| ----------------------------------------------------- | ------ | -------------------------- |
| Dados com formato inválido, corpo vazio, JSON quebrado, valor de query que não é do tipo certo | 400    | (automático, do `[ApiController]`, traduzido por `MensagensValidacao`) |
| Regra de negócio violada (senha errada, ausência que não pode mais ser editada) | 400 | `BusinessRuleException` |
| Conflito: e-mail/CPF duplicado, versão desatualizada, já respondida | 409 | `ConflictException` |
| Duas requisições disputando o mesmo registro: `DbUpdateConcurrencyException` (versão) → `"Este registro foi alterado por outra pessoa. Atualize a tela e tente de novo."`; deadlock (MySQL 1213), espera de trava (1205) ou trava global que não saiu em 10 s → `"Outra operação estava em andamento ao mesmo tempo. Tente de novo."`; entrada duplicada (1062) | 409 | (traduzido no `ExceptionMiddleware` via `ErrosBanco`) |
| Sem token, token inválido/expirado, sessão encerrada ou colaborador desligado | 401 | (middleware de autenticação + `UseStatusCodePages`) |
| Token válido, mas sem permissão pra esse recurso      | 403    | `ForbiddenException` / policy |
| Registro ou rota não encontrada                       | 404    | `NotFoundException` / `UseStatusCodePages` |
| Corpo acima do limite do Kestrel (30 MB)              | 413    | `BadHttpRequestException` → `"Requisição grande demais."` (antes virava 500) |
| Content-Type que não é JSON                           | 415    | (`UseStatusCodePages`) |
| Muitas tentativas de login                            | 429    | `TooManyRequestsException` (com `Retry-After`) |
| Erro inesperado (bug, falha de conexão)               | 500    | qualquer outra `Exception` |
| Cliente desistiu da requisição (o painel cancela os GETs ao sair da tela, conexão caiu) | 499 (só no log) | qualquer exceção com `RequestAborted` cancelado: registrada como **Information** (`"Requisição cancelada pelo cliente..."`, sem stack trace), sem escrever resposta. Na conferência do token (`ValidacaoDeSessao`), o cancelamento encerra a autenticação sem resultado (`NoResult`) e também é registrado como Information, então não aparece mais `fail: JwtBearerHandler[3] … OperationCanceledException`. As consultas ao banco não recebem o `RequestAborted`, de propósito: são curtas, e com o token do cliente o EF registrava `fail: Microsoft.EntityFrameworkCore.Database.Connection[20004]` a cada tela fechada. Agora esse `fail` do EF só aparece numa falha de conexão de verdade. |

**Toda** resposta de erro sai no formato `{ "mensagem": "..." }` em português — inclusive 401, 403, 404, 405, 413 e 415, que antes saíam sem corpo ou em ProblemDetails em inglês (`UseStatusCodePages` em `Program.cs`). As mensagens de binding nunca repetem o valor enviado pelo cliente (`?departamentoId=1;DROP...` vira `"Departamento está em formato inválido."`). JSON truncado ou malformado (erro de sintaxe) responde `"O corpo da requisição não é um JSON válido."`, sem apontar campo; valor do tipo errado num campo responde `"<Campo> está em formato inválido."`.

**Concorrência** nunca vira 500. Como funciona (ver `AppDbContext`, seção "Transação e trava"):

- As transações são **READ COMMITTED**: no REPEATABLE READ padrão do MySQL, a primeira leitura comum congelava uma "foto" do banco, e o que a transação lia depois de esperar uma trava vinha dessa foto velha (um desligamento não via o subordinado que outra requisição acabara de pôr sob o gestor).
- Regra de uso: **travar primeiro, ler depois** (`SELECT ... FOR UPDATE` nas linhas envolvidas; `FOR SHARE` no gestor ao cadastrar alguém sob ele).
- **Trava global de hierarquia** (`GET_LOCK('sistemarh_hierarquia:<database>', 10)`): toda mudança de hierarquia — cadastro com gestor, edição com gestor, promoção e desligamento — passa uma de cada vez. Travar só as linhas não bastava: com A→B e C→D gravados, "B→C" e "D→A" ao mesmo tempo travavam pares disjuntos e fechavam o ciclo A→B→C→D→A.
- **Trava global de perfis** (`GET_LOCK('sistemarh_perfis:<database>', 10)`): passam uma de cada vez a troca de perfil e tudo o que mexe na conta ligada a um colaborador e depende do perfil dela:
  - o "Criar acesso ao app" (senha);
  - a edição do colaborador (o e-mail de login);
  - o desligamento (o 403 para conta RH/Admin e a regra do último Admin).

  Essas rotas também travam a **linha da conta** (`SELECT … FROM Usuarios WHERE FuncionarioId = … FOR UPDATE`) antes de ler o perfil dela. Antes só travavam a linha do colaborador: com uma promoção a Admin em andamento, o RH lia a conta como Funcionário, passava no 403 e gravava a senha ou o e-mail dele na conta já promovida.
- **Ordem das travas, igual em todas as rotas:** globais (hierarquia, depois perfis) → linhas de `Funcionarios` (por Id) → linha da conta (`Usuarios`). Nenhuma rota trava a conta e depois um colaborador. As globais são liberadas depois do commit. Se não saírem em 10 s, a resposta é 409 `"Outra operação estava em andamento ao mesmo tempo. Tente de novo."`.
- Ajustes de salário/vale travam a linha do funcionário; o ponto, a ausência e a mensagem ao RH travam a conta do usuário.
- No banco InMemory dos testes esses passos são pulados (`Database.IsRelational()`); as corridas foram conferidas no MySQL de verdade (rajadas de desligar × cadastrar/editar/promover, promoção × edição cruzada e o ciclo de 4 em paralelo: nenhum ativo sob gestor desligado, nenhum ciclo).

Erros 500 têm o stack trace completo **só no log do servidor** (terminal/arquivo de log) — o usuário nunca vê detalhes internos, só uma mensagem genérica. Isso evita vazar informações que facilitariam um ataque (nomes de tabelas, versão do banco, caminhos de arquivo, etc).

Toda resposta também leva `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` e `Referrer-Policy: no-referrer`, e o Kestrel não envia mais o cabeçalho `Server`.

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
| 3 | Impede cadastro vazio? | `POST /api/auth/register` com `{}` e token de Admin | `400`, com mensagem indicando o campo obrigatório | ✅ Sim |
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
- [x] Cadastro sem token → `401`; com token de Funcionário ou RH → `403`.
- [x] Acessar `/api/auth/me` sem token → `401`.
- [x] Acessar `/api/auth/me` com token inválido → `401`.
- [x] Token antigo depois de logout, troca de senha, troca de perfil/e-mail ou desligamento → `401`.
- [x] 6ª tentativa de login depois de 5 senhas erradas (mesmo e-mail e IP) → `429`; 20 em paralelo → só 5 conferem a senha; 20 falhas de um IP em e-mails diferentes → `429` pra esse IP.
- [x] 8 logins **certos** em paralelo (mesma conta e IP) → 8 × `200`, nenhum `429`.
- [x] RH cadastra o colaborador com a senha do app, o Admin promove a conta a Admin (com `senhaProvisoria`) e o RH tenta entrar com a senha que escolheu → `400`; sem `senhaProvisoria` a promoção dá `400`.
- [x] `PUT /api/auth/senha` com a senha atual errada → `400` `"Senha atual incorreta."`; depois de 5 → `429` (também no login daquele IP).
- [x] Usuário A tentando ver uma ausência do usuário B → `404` (igual a uma inexistente).
- [x] Cancelar uma ausência que já está "Em análise" → `400`.
- [x] 10 "registrar ponto" ou 10 ajustes de salário simultâneos → nenhum `500`, nenhuma duplicata (conferido no MySQL de verdade).

### O que fazer se um resultado for diferente do esperado

1. Confira o log do terminal onde a API está rodando — erros inesperados aparecem lá com o stack trace completo.
2. Confira se o token usado ainda não expirou (120 minutos por padrão).
3. Confira se o `Content-Type: application/json` foi enviado na requisição.
4. Se nada disso resolver, é sinal de um bug de verdade — abra uma issue ou registre o que aconteceu pra investigar com calma.

---

## 12. Rotas da API

Base: `http://localhost:5289`

### Autenticação

#### `POST /api/auth/register` — 🔒 Admin

Cria uma conta de acesso (ver seção 5). Só lê `nome`, `email`, `senha` e `perfil` (opcional, `0=Funcionario` padrão, `1=Rh`, `2=Admin`); o resto é ignorado.

```json
// Requisição
{ "nome": "Ana Silva", "email": "ana@empresa.com", "senha": "senha123", "perfil": 1 }
```
```json
// 201 Created — a conta criada, sem token
{ "id": 9, "nome": "Ana Silva", "email": "ana@empresa.com", "perfil": 1, "funcionarioId": null, "funcionarioNome": null, "funcionarioDesligado": false }
```
Erros: `400` (campos inválidos), `401` (sem token), `403` (não é Admin), `409` (e-mail já existe).

#### `POST /api/auth/login` — pública

```json
// Requisição
{ "email": "ana@empresa.com", "senha": "senha123" }
```
```json
// 200 OK
{ "token": "eyJ...", "usuario": { ... } }
```
Erros: `400` (`"E-mail ou senha incorretos."` — também pra e-mail com acento ou caractere de controle; ou `"Seu acesso foi desativado. Procure o RH."` se a conta está vinculada a um funcionário desligado), `429` (`"Muitas tentativas de login. Tente novamente em N minuto(s)."` depois de 5 falhas no mesmo e-mail a partir do mesmo IP, ou 20 falhas do mesmo IP somando todos os e-mails, em 15 minutos; o bloqueio dura 15 minutos. Se a fila de tentativas ao mesmo tempo não andar em 10 s: `"Muitas tentativas de login. Aguarde alguns segundos e tente de novo."`, `Retry-After: 2`).

#### `POST /api/auth/logout` — 🔒 autenticada

Sem corpo. Responde `204` e invalida **todos** os tokens da conta (ver "Versão do token", seção 5).

#### `PUT /api/auth/senha` — 🔒 autenticada

Troca a própria senha (ver seção 5).

```json
{ "senhaAtual": "senha123", "novaSenha": "minhaSenhaNova1" }
```

Resposta (`200`): `{ "token": "..." }`. O token usado e os de outros aparelhos deixam de valer, e o app passa a usar esse token novo.
Erros: `400` (`"Informe a senha atual."`, `"Senha atual incorreta."`, `"A nova senha precisa ser diferente da atual."`, regras de senha da seção 8), `401`, `429` (a senha atual errada conta no limite do login: `"Muitas tentativas com a senha atual incorreta. Tente novamente em N minuto(s)."`, com `Retry-After`).

#### `GET /api/auth/me` — 🔒 autenticada

Devolve os dados do usuário dono do token.
```json
// 200 OK
{ "id": 1, "nome": "Ana Silva", "email": "ana@empresa.com", "cargo": "Dev", "departamento": "TI",
  "saldoBancoHorasMinutos": 90, "totalSolicitacoesAusencia": 2,
  "funcionarioId": 5, "dataAdmissao": "2024-01-01T00:00:00", "gerenteNome": "Roberto" }
```
Quando a conta está vinculada a um funcionário (`funcionarioId` preenchido), `cargo`, `departamento`, `dataAdmissao`, `gerenteNome` e o saldo vêm do cadastro do RH. Sem vínculo, `funcionarioId`, `dataAdmissao` e `gerenteNome` vêm `null`.
Erros: `401` (sem token, token inválido/expirado, sessão encerrada ou funcionário vinculado desligado — com a `mensagem` de cada caso).

> Instantes (`registradoEm`, `criadoEm`, `respondidaEm`) sempre saem em UTC, com `Z`. Datas de calendário (`data`, `dataAdmissao`, `dataRegistro`...) saem sem fuso, de propósito.

### Dados do próprio usuário (`/api/me`) — 🔒 autenticada (qualquer perfil)

O colaborador é sempre o ligado à conta do token (não há Id na URL).

- `GET /api/me/cadastro` — o `FuncionarioDto` do colaborador ligado à conta (CPF, telefone, salário, vale...). `404` com `"Sua conta não está ligada a um cadastro de colaborador."` se não houver vínculo.
- `GET /api/me/ajustes-remuneracao` — o histórico de remuneração da própria pessoa, no formato de `GET /api/funcionarios/{id}/ajustes-remuneracao`.
- `GET /api/me/holerites` — as folhas de pagamento da própria pessoa (mesmo formato de `/api/folha-pagamento`), da competência mais recente pra mais antiga.

As duas listas devolvem `[]` pra conta sem vínculo.

### Mensagens ao RH ("Falar com o RH")

- `POST /api/mensagens-rh` — 🔒 conta ligada a um colaborador ativo. Corpo `{ "assunto": "...", "mensagem": "..." }` (assunto de 3 a 100 caracteres, uma linha; mensagem de 3 a 2000, aceita quebra de linha). Devolve `201` com a mensagem. Conta sem colaborador: `403`. A mesma mensagem (mesmo assunto e texto, depois da limpeza) ainda Aberta e enviada há menos de 2 minutos dá `409` `"Você acabou de enviar essa mesma mensagem. Aguarde a resposta do RH."` (a conta é travada durante o envio, então uma rajada de envios iguais grava uma só).
- `GET /api/mensagens-rh/minhas` — 🔒 as mensagens da própria pessoa, das mais novas pras mais antigas, com a resposta do RH quando houver.
- `GET /api/rh/mensagens?status=` — 🔒 RhOuAdmin. `status`: `0=Aberta`, `1=Respondida` (opcional).
- `PUT /api/rh/mensagens/{id}/resposta` — 🔒 RhOuAdmin. Corpo `{ "resposta": "...", "versao": 0 }` (3 a 2000 caracteres). Já respondida ou com versão diferente: `409`.

```json
// MensagemRhDto
{ "id": 1, "usuarioId": 8, "usuarioNome": "Zé", "assunto": "Férias", "mensagem": "Posso tirar férias em janeiro?",
  "status": 1, "resposta": "Pode sim.", "respondidaEm": "2026-11-18T15:00:00Z", "responsavelNome": "Equipe RH",
  "criadoEm": "2026-11-18T14:00:00Z", "versao": 1 }
```

### Contas de acesso e perfis (`/api/usuarios`)

- `GET /api/usuarios` — 🔒 RhOuAdmin. `[{ "id", "nome", "email", "perfil", "funcionarioId", "funcionarioNome", "funcionarioDesligado" }]` (`perfil` como número).
- `PUT /api/usuarios/{id}/perfil` — 🔒 Admin. Corpo `{ "perfil": 1, "perfilAtual": 0, "senhaProvisoria": "..." }`; devolve o item atualizado. Quando o perfil **sobe** (Funcionário → RH, Funcionário → Admin, RH → Admin), `senhaProvisoria` é obrigatória (`400` `"Defina uma senha provisória para a conta promovida."`), segue as regras de senha e substitui a senha atual (ver seção 5). Ao rebaixar, ela é ignorada. `perfilAtual` inválido (`9`, `"0"`...) dá `400` `"Perfil inválido."`. `perfilAtual` (opcional, o painel sempre envia) é o perfil que a tela estava mostrando: se outro Admin mudou antes, a resposta é `409` `"Este registro foi alterado por outra pessoa. Atualize a tela e tente de novo."`. Ninguém muda o próprio perfil (`400`), o sistema nunca fica sem Admin **ativo** (`400`; um Admin cujo colaborador foi desligado não conta) e os tokens da conta alterada deixam de valer. As trocas passam uma de cada vez (trava global de perfis) e, dentro da trava, o perfil de quem pede é relido do banco: quem acabou de ser rebaixado por outro Admin recebe `403` `"Somente um administrador pode alterar perfis."` mesmo com o token antigo.

### Concorrência (versão otimista)

`FuncionarioDto`, as ausências (lado do colaborador e do RH) e as mensagens ao RH trazem `versao`, que sobe a cada alteração. As rotas de escrita aceitam `versao` (o painel sempre envia): `PUT /api/funcionarios/{id}`, `PUT /{id}/salario`, `PUT /{id}/vale-alimentacao`, `POST /{id}/promocao`, `POST /{id}/desligar`, `PUT /api/rh/ausencias/{id}/status`, `PUT /api/rh/mensagens/{id}/resposta`, `PUT /api/ausencias/{id}` (no corpo ou em `?versao=`) e `DELETE /api/ausencias/{id}?versao=`. Se a versão não for a atual, ou se outra requisição gravar antes, a resposta é `409` com `"Este registro foi alterado por outra pessoa. Atualize a tela e tente de novo."`.

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
Erros: `400` (todas as marcações do dia já foram feitas, ou `"Aguarde 1 minuto entre registros de ponto."`). Cada registro trava a conta do usuário no banco: toques simultâneos são atendidos um de cada vez e nunca gravam marcações repetidas.

### Ausências

Todas exigem autenticação (🔒) e só enxergam/alteram ausências do próprio usuário.

#### `GET /api/ausencias`

Lista as ausências do usuário autenticado, mais recentes primeiro.

#### `GET /api/ausencias/{id}`

Consulta uma ausência específica. Erros: `404` (não existe **ou** não é sua — a resposta é a mesma, pra não revelar quais ids existem).

#### `POST /api/ausencias`

```json
// Requisição
{ "tipo": 0, "data": "2026-11-10", "diaInteiro": true, "descricao": "Consulta médica" }
```
`tipo` (obrigatório): `0=AtestadoMedico, 1=FaltaJustificada, 2=EsquecimentoDePonto, 3=Outro`. `data` (obrigatória) entre hoje − 365 e hoje + 365 dias, lida como data de calendário. `descricao` de 3 a 2000 caracteres (aceita quebra de linha).
```json
// 201 Created
{ "id": 1, "tipo": 0, "data": "2026-11-10T00:00:00", "diaInteiro": true, "descricao": "...", "status": 0, "criadoEm": "...", "versao": 0 }
```
`status`: `0=Enviada, 1=EmAnalise, 2=Aprovada, 3=Recusada`.
Erros: `400` (campos inválidos, tipo fora da lista), `409` (mesmo tipo e mesma data ainda Enviada/Em análise).

#### `PUT /api/ausencias/{id}`

Mesmo corpo do `POST`, mais `versao` (ou `?versao=`). Só funciona enquanto `status == Enviada`.
Erros: `400` (já não pode mais editar), `404` (não existe ou não é sua), `409` (versão desatualizada ou duplicada).

#### `DELETE /api/ausencias/{id}?versao=`

Cancela (remove) a ausência. Só funciona enquanto `status == Enviada`. Devolve `204 No Content`.
Erros: `400`, `404`, `409` — mesmos casos do `PUT`.

### Lado administrativo/RH

Essas rotas existem pro site. A maioria exige o perfil **RH** ou **Admin** no token (`🔒 RhOuAdmin`); departamentos só exigem estar logado pra listar (🔒), mas pedem RH/Admin pra criar/editar. **Cargos** pedem RH/Admin até pra listar, porque trazem o salário base.

> **Como funcionam as policies**: o token JWT carrega uma claim `"perfil"` (`Funcionario`, `Rh` ou `Admin`) — ver `Usuario.Perfil`. `RhOuAdmin` aceita os dois últimos; `Admin` só o último. O perfil de uma conta é trocado por um Admin em `PUT /api/usuarios/{id}/perfil` (ver acima).

#### Departamentos — `GET/POST/PUT /api/departamentos`

`GET` 🔒, `POST`/`PUT` 🔒 RhOuAdmin. Corpo do `POST`/`PUT`: `{ "nome": "...", "descricao": "..." }` (nome de 2 a 100, descrição até 500). Erros: `400`, `404` (PUT em id inexistente), `409` (nome duplicado, inclusive dois cadastros iguais ao mesmo tempo).

#### Cargos — `GET/POST/PUT /api/cargos` — 🔒 RhOuAdmin (inclusive o `GET`)

Corpo: `{ "nome": "...", "salarioBase": 5000, "descricao": "..." }` (salário base de R$ 0,00 a R$ 1.000.000,00, com 2 casas).

#### Funcionários — `GET/POST/PUT /api/funcionarios`, `POST /api/funcionarios/{id}/desligar`, `POST /api/funcionarios/{id}/acesso-app` — 🔒 RhOuAdmin

```json
// POST /api/funcionarios
{ "nome": "Ana Souza", "cpf": "111.111.111-11", "email": "ana@empresa.com", "dataAdmissao": "2024-01-01", "departamentoId": 1, "cargoId": 1, "gerenteId": null, "senhaAcessoApp": "senha123" }
```
`GET /api/funcionarios` aceita `?status=0` (0=Ativo, 1=Desligado) e `?departamentoId=` como filtros opcionais.
Validações do cadastro/edição: ver a tabela da seção 8 (nome, CPF, e-mail, telefone gravado formatado, nascimento, admissão obrigatória). `departamentoId` e `cargoId` faltando dão `400` (`"Selecione o departamento."`/`"Selecione o cargo."`), não mais `404`.
Erros: `404` (departamento/cargo/funcionário/gestor não existe), `409` (CPF ou e-mail duplicado, inclusive dois cadastros iguais ao mesmo tempo; versão desatualizada), `400` (dados inválidos, gestor inválido).

**Gestor** (`gerenteId` no cadastro/edição, `novoGerenteId` na promoção): precisa existir (`404`), estar **ativo**, não ser a própria pessoa e não estar **abaixo** dela na hierarquia (a API sobe a cadeia de gestores; A gestor de B e B gestor de A é recusado como ciclo). Gestor de outro departamento é permitido (a Diretoria gerencia os outros departamentos). Toda mudança de hierarquia passa pela trava global de hierarquia (seção 9), então edições em paralelo não fecham ciclos (nem de 4 pessoas) e ninguém fica ativo sob um gestor que estava sendo desligado. Na promoção, `novoGerenteId` informado é **sempre** validado, mesmo que seja o gestor atual.

**E-mail e login do app**: se o colaborador tem acesso ao app, trocar o e-mail no `PUT /api/funcionarios/{id}` troca também o e-mail de login (a conta antiga para de valer: a `TokenVersao` sobe). O novo e-mail não pode ser de outra conta (`409 "Já existe uma conta de acesso com este e-mail."`) e não dá pra apagar o e-mail de quem tem acesso (`400`). O nome da conta acompanha o nome do cadastro. Se a conta ligada tem perfil **RH ou Admin**, só um **Admin** pode trocar o e-mail de login (`403` `"Somente um administrador pode alterar o acesso de contas RH/Admin."`); o RH continua editando os outros dados.

**Desligar** — `POST /{id}/desligar`, corpo **opcional** `{ "dataDesligamento": "AAAA-MM-DD", "versao": 3 }` (sem data, vale hoje; a data vai da admissão até hoje — ou, pra quem tem admissão futura, é a própria data de admissão, que é o padrão sem data). Na **mesma transação**, os subordinados diretos ativos passam pro gestor da pessoa desligada (se ele estiver ativo) ou ficam sem gestor, e o token do app dela para de valer. Desligar quem já está desligado dá `400`. Se a conta ligada ao colaborador tem perfil **RH ou Admin**, só um **Admin** pode desligar (`403` `"Somente um administrador pode desligar um colaborador com conta RH/Admin."`), e nunca o colaborador do **último Admin ativo** (`400` `"Não é possível desligar o último administrador ativo do sistema."`).
```json
// 200 OK
{ "funcionario": { ...FuncionarioDto... }, "subordinadosRealocados": 2, "novoGestorNome": "Ana Silva" }
```
`novoGestorNome` é `null` quando a equipe ficou sem gestor (ou não havia equipe).

Todo funcionário devolvido traz `possuiAcessoApp` (`true` se já existe uma conta de login vinculada a ele) e `versao`.

**Acesso ao app** — `POST /{id}/acesso-app` com `{ "senha": "..." }` (devolve `200` com o funcionário atualizado), ou `senhaAcessoApp` (opcional) direto no `POST /api/funcionarios`:
- se já existe conta vinculada a esse funcionário, só troca a senha (serve pra "redefinir senha") e **derruba as sessões abertas** (a `TokenVersao` sobe). Se essa conta tem perfil **RH ou Admin**, só um **Admin** pode redefinir a senha (`403` `"Somente um administrador pode alterar o acesso de contas RH/Admin."`) — antes um RH redefinia a senha de uma conta Admin ligada a um colaborador e entrava como Admin;
- se existe uma conta com o mesmo e-mail que **não** é deste colaborador, responde `409` com `"Já existe uma conta de acesso com este e-mail. Fale com o administrador."` — a conta **não é mais ligada automaticamente** (era assim que um intruso cadastrado antes virava o acesso do colaborador);
- senão, cria uma conta nova (perfil `Funcionario`) com o e-mail do funcionário.

A senha segue as regras da seção 8 (6 a 64 caracteres reais, sem ser só espaços nem começar/terminar com espaço).
Erros: `404` (funcionário não existe), `400` (`"Não é possível dar acesso ao app a um colaborador desligado."`, `"Cadastre um e-mail para esse colaborador antes de criar o acesso ao app."`, `"A senha de acesso ao app precisa ter pelo menos 6 caracteres."`...), `409` (conta existente com o e-mail). No `POST /api/funcionarios`, essas validações rodam antes de gravar — se falharem, nada é criado.

`gerenteId` (opcional) é o `Id` de outro funcionário já cadastrado — é essa ligação que monta o **Organograma** no site: quem não tem `gerenteId` aparece no topo; quem tem, aparece "pendurado" embaixo do gestor escolhido. Cada funcionário também devolve `gerenteNome` (preenchido pela API) pra facilitar exibição sem precisar de outra consulta.

Todo funcionário devolvido traz também `salario` e `valeAlimentacao` (valores individuais dele). No `POST /api/funcionarios`, os dois são opcionais: `"salario": 5500.00` (R$ 1,00 a R$ 1.000.000,00) e `"valeAlimentacao": 700.00` (R$ 0,00 a R$ 10.000,00), sempre com até 2 casas. Sem `salario`, usa o `salarioBase` do cargo escolhido; sem `valeAlimentacao`, usa o padrão de `600.00`. O `PUT /api/funcionarios/{id}` não mexe nesses valores — eles só mudam pelas rotas de remuneração abaixo.

#### Remuneração — `PUT /api/funcionarios/{id}/salario`, `PUT /{id}/vale-alimentacao`, `POST /{id}/promocao`, `GET /{id}/ajustes-remuneracao` — 🔒 RhOuAdmin

```json
// PUT /api/funcionarios/{id}/salario
{ "novoSalario": 5500.00, "dataVigencia": "2026-11-01", "motivo": "Reajuste anual", "versao": 3 }

// PUT /api/funcionarios/{id}/vale-alimentacao
{ "novoValor": 700.00, "dataVigencia": "2026-11-01", "motivo": "Novo acordo coletivo", "versao": 4 }

// POST /api/funcionarios/{id}/promocao — novo cargo e/ou departamento (e o salário)
{ "novoCargoId": 10, "novoDepartamentoId": 2, "novoSalario": 9000.00, "dataVigencia": "2026-11-01", "motivo": "Promoção a coordenador", "novoGerenteId": null, "versao": 5 }
```
Salário e vale devolvem `200` com o funcionário atualizado (mesmo formato do `GET /api/funcionarios/{id}`); a promoção devolve `{ "funcionario": { ... }, "subordinadosRealocados": 0 }`. O novo valor vale na hora; por isso a `dataVigencia` (data de calendário, `AAAA-MM-DD`) vai da admissão até **hoje** — data futura dá `400` (`"Ajustes com data futura ainda não são suportados."`). Pra quem ainda vai ser admitido (admissão futura), a vigência é a própria data de admissão: a janela é `[admissão, max(hoje, admissão)]`. Cada operação grava uma linha no histórico, com o usuário logado como responsável. Cada ajuste trava a linha do funcionário até gravar: dois ajustes ao mesmo tempo são feitos um depois do outro (o "valor anterior" do histórico fica sempre certo) ou, se a tela mandou uma `versao` velha, o segundo recebe `409`.

**Regras de valor**: salário de R$ 1,00 a R$ 1.000.000,00, vale de R$ 0,00 a R$ 10.000,00, sempre com até 2 casas decimais; novo valor igual ao atual dá `400` (`"O novo valor é igual ao atual."`). Motivo de 3 a 500 caracteres.

**Promoção**: precisa mudar o cargo e/ou o departamento. Sem isso: mudar só o salário dá `400 "Para alterar só o salário, use Ajuste Salarial."`; nada mudar, ou mudar só o gestor, dá `400 "A promoção precisa mudar o cargo ou o departamento."` (pra trocar só o gestor, edite o cadastro); e uma promoção repetida com a `versao` velha da tela dá `409`. O novo salário não pode ser menor que o atual (`400 "Promoção não pode reduzir o salário."`); `novoGerenteId` é opcional e, quando vem, é sempre validado como o `gerenteId` (inclusive se for o gestor atual); `null` mantém o atual. Se o departamento muda, os subordinados diretos que ficam no departamento antigo passam pro **gestor anterior** da pessoa promovida, na mesma transação.

`GET /{id}/ajustes-remuneracao` — histórico do funcionário, mais recentes primeiro (`tipo`: `0=Salario, 1=ValeAlimentacao, 2=Promocao`; em `1`, os valores são do vale; nos outros, do salário; os nomes de cargo/departamento só vêm preenchidos numa promoção):
```json
[ { "id": 3, "tipo": 2, "valorAnterior": 5500.00, "valorNovo": 9000.00,
    "cargoAnteriorNome": "Desenvolvedor", "cargoNovoNome": "Coordenador",
    "departamentoAnteriorNome": "Tecnologia da Informação", "departamentoNovoNome": "Tecnologia da Informação",
    "dataVigencia": "2026-12-01T00:00:00", "motivo": "Promoção a coordenador", "responsavelNome": "Equipe RH",
    "criadoEm": "2026-11-17T17:30:00Z" } ]
```
Erros: `404` (`"Funcionário não encontrado."`, e na promoção `"Cargo informado não existe."` / `"Departamento informado não existe."`), `400` (`"Não é possível ajustar a remuneração de um colaborador desligado."`, `"O salário precisa estar entre R$ 1,00 e R$ 1.000.000,00."`, `"O vale alimentação precisa estar entre R$ 0,00 e R$ 10.000,00."`, `"Informe o salário com no máximo 2 casas decimais."`, `"O novo valor é igual ao atual."`, `"Informe a data de vigência."`, `"Ajustes com data futura ainda não são suportados."`, `"Informe o motivo do ajuste."`, `"O motivo precisa ter pelo menos 3 caracteres."`, e na promoção as duas regras acima), `409` (versão desatualizada).

> A migration `AddSalarioValeAlimentacaoEHistorico` preenche os funcionários que já existiam com `salario` = `salarioBase` do cargo e `valeAlimentacao` = `600.00`.
>
> A migration `SessaoConcorrenciaMensagensRhETamanhos` cria `Usuarios.TokenVersao`, `Funcionarios.Versao`, `Ausencias.Versao` e a tabela `MensagensRh`, e troca as colunas de texto `LONGTEXT` por `VARCHAR` com o tamanho máximo das regras da seção 8. Antes de diminuir uma coluna, ela corta o que já estiver maior que o novo limite (só textos absurdos, de antes das validações); nome de departamento/cargo cortado termina com ` #Id` pra continuar único.

#### Banco de horas — `GET/POST /api/funcionarios/{id}/banco-horas` — 🔒 RhOuAdmin

`GET` devolve o extrato completo: `{ "saldoMinutos": 90, "lancamentos": [ { "id": 1, "dataRegistro": "...", "tipo": 0, "quantidadeMinutos": 120, "descricao": "..." } ] }` (`tipo`: `0=Credito, 1=Debito`).
`POST` registra um lançamento novo: `{ "dataRegistro": "2026-08-10", "tipo": 0, "quantidadeMinutos": 120, "descricao": "Hora extra" }` (data obrigatória, da admissão até hoje + 1 ano; tipo da lista; de 1 a 100000 minutos).

#### Recrutamento — `GET /api/recrutamento`, `POST /api/recrutamento`, `PUT /api/recrutamento/{id}/status` — 🔒 RhOuAdmin

`GET` aceita `?status=` opcional (`0=EmProcesso, 1=Entrevista, 2=Aprovado, 3=Rejeitado`). `PUT /{id}/status`: `{ "status": 2, "observacoes": "..." }`.

#### Avaliações — `GET/POST /api/avaliacoes?funcionarioId=` — 🔒 RhOuAdmin

`funcionarioId` é obrigatório na query string nos dois verbos. Corpo do `POST`: `{ "nota": 9.5, "comentario": "...", "dataAvaliacao": "2026-07-05" }` (nota obrigatória, de 0 a 10 com no máximo 1 casa decimal; data obrigatória, da admissão até hoje; comentário até 2000 caracteres).

#### Agenda — `GET/POST/DELETE /api/agenda` — 🔒 RhOuAdmin

`GET` aceita `?de=` e `?ate=` (intervalo de datas, opcionais). Corpo do `POST`: `{ "titulo": "...", "dataEvento": "...", "tipo": 0, "funcionarioId": 1 }` (`dataEvento` obrigatória, de 1 ano atrás até 2 anos à frente, sempre no horário de Brasília e sem fuso — com fuso ou `Z`, é convertida pra Brasília; a resposta do POST e do GET sai igual; título de 2 a 100; `funcionarioId` opcional; `tipo` opcional, padrão `3`, e precisa ser da lista: `0=Reuniao, 1=Entrevista, 2=Avaliacao, 3=Outro`).

#### Folha de pagamento — `GET/POST /api/folha-pagamento?funcionarioId=` — 🔒 RhOuAdmin

Corpo do `POST`: `{ "competencia": "2026-08", "salario": 4000, "bonus": 400, "descontos": 300 }` — o `salarioLiquido` é calculado pelo backend (nunca é enviado). Valores de R$ 0 a R$ 1.000.000 com até 2 casas, líquido nunca negativo e competência do mês da admissão até o mês que vem. Erros: `400` (faixas acima), `409` (já existe folha dessa competência pra esse funcionário, inclusive dois lançamentos ao mesmo tempo).

#### Acompanhamento do RH — `/api/rh` — 🔒 RhOuAdmin

`GET /api/rh/ausencias?status=` (status opcional, `0..3`) — justificativas de **todos** os colaboradores, mais recentes primeiro:
```json
[ { "id": 1, "tipo": 0, "data": "2026-11-10T00:00:00", "diaInteiro": true, "descricao": "...", "status": 0, "criadoEm": "2026-11-13T15:50:51Z",
    "usuarioId": 3, "usuarioNome": "Ana", "usuarioEmail": "ana@empresa.com", "funcionarioId": 5 } ]
```
`PUT /api/rh/ausencias/{id}/status` com `{ "status": 2, "versao": 0 }` — devolve `200` com o mesmo formato acima (cada item traz `versao`). Só aceita `1=EmAnalise`, `2=Aprovada` ou `3=Recusada`, e só decide o que ainda está Enviada ou Em análise.
Erros: `400` (`"Status inválido."`, ou `"Essa justificativa já foi concluída e não pode mais ser alterada."` se já está Aprovada/Recusada), `404` (`"Justificativa de ausência não encontrada."`), `409` (o colaborador editou/cancelou ou outra aba decidiu depois que a tela carregou).

`GET /api/rh/ponto?data=AAAA-MM-DD` (opcional; padrão = hoje) — marcações de todos no dia, no horário de Brasília, ordenadas por nome e depois horário:
```json
[ { "id": 1, "tipo": 0, "registradoEm": "2026-11-13T11:02:00Z", "usuarioId": 3, "usuarioNome": "Ana", "usuarioEmail": "ana@empresa.com" } ]
```
Erros: `400` (`"Data inválida. Use o formato AAAA-MM-DD."`, ou `"Data fora do período permitido..."` fora de 01/01/2000 até hoje + 1 ano — antes `9999-12-31` dava 500).

`GET /api/rh/mensagens` e `PUT /api/rh/mensagens/{id}/resposta` — ver "Mensagens ao RH", no começo desta seção.

---

## 13. Resumo do que foi criado

| Arquivo/Pasta | O que é |
|---|---|
| `Models/Usuario.cs`, `RegistroPonto.cs`, `Ausencia.cs` | As tabelas usadas pelo app (login, ponto, ausências) |
| `Models/Departamento.cs`, `Cargo.cs`, `Funcionario.cs`, `LancamentoBancoHoras.cs`, `Recrutamento.cs`, `Avaliacao.cs`, `EventoAgenda.cs`, `FolhaPagamento.cs`, `AjusteRemuneracao.cs` | Tabelas do lado administrativo/RH (`AjusteRemuneracao` é o histórico de salário, vale alimentação e promoções) |
| `Data/AppDbContext.cs` | Configuração do Entity Framework Core |
| `Migrations/` | Histórico de criação das tabelas (gerado por `dotnet ef migrations add`) |
| `Dtos/AuthDtos.cs`, `AusenciaDtos.cs`, `PontoDtos.cs` | Formato de entrada/saída de cada rota do app |
| `Dtos/DepartamentoDtos.cs`, `CargoDtos.cs`, `FuncionarioDtos.cs`, `BancoHorasDtos.cs`, `RecrutamentoDtos.cs`, `AvaliacaoDtos.cs`, `AgendaDtos.cs`, `FolhaPagamentoDtos.cs` | Formato de entrada/saída das rotas administrativas/RH |
| `Services/AuthService.cs` | Regras de cadastro, login e hash de senha (já unifica os dois "banco de horas" — ver item 5 antigo, resolvido) |
| `Services/JwtTokenService.cs` | Geração do token JWT (inclui a claim `perfil`, usada pela policy `RhOuAdmin`) |
| `Services/ValidacaoDeSessao.cs` | Conferência de cada token no banco (conta existe, colaborador não desligado, versão `tv` atual); requisição cancelada pelo cliente não vira erro no log |
| `Services/PontoService.cs` | Regra do "qual é a próxima marcação do dia" |
| `Services/AusenciaService.cs` | CRUD de ausências, com validação de dono e de status |
| `Services/DepartamentoService.cs`, `CargoService.cs`, `FuncionarioService.cs`, `LancamentoBancoHorasService.cs`, `RecrutamentoService.cs`, `AvaliacaoService.cs`, `EventoAgendaService.cs`, `FolhaPagamentoService.cs` | Regras de negócio do lado administrativo/RH (`FuncionarioService` também cria/vincula o acesso ao app e faz os ajustes de remuneração) |
| `Services/RhService.cs` | Visão do RH sobre ausências (com aprovação) e ponto de todos os colaboradores |
| `Services/UsuarioService.cs`, `MeService.cs`, `MensagemRhService.cs` | Contas e perfis (tela de Permissões), dados da própria pessoa (`/api/me`) e o "Falar com o RH" |
| `Services/LimiteTentativasLogin.cs` | Limite de tentativas de login: 5 falhas por e-mail+IP e 20 por IP, em 15 minutos |
| `Models/MensagemRh.cs`, `Dtos/MensagemRhDtos.cs` | Mensagens do app ao RH |
| `Controllers/AuthController.cs`, `PontoController.cs`, `AusenciasController.cs`, `MeController.cs`, `MensagensRhController.cs` | As rotas HTTP usadas pelo app (e o "Meus Dados" do painel) |
| `Controllers/DepartamentosController.cs`, `CargosController.cs`, `FuncionariosController.cs`, `RecrutamentoController.cs`, `AvaliacoesController.cs`, `AgendaController.cs`, `FolhaPagamentoController.cs`, `RhController.cs`, `UsuariosController.cs` | As rotas HTTP administrativas/RH (🔒 RhOuAdmin / Admin) |
| `Middleware/ExceptionMiddleware.cs` | Tratamento global de erros (inclusive deadlock/duplicata/versão do MySQL → 409) |
| `Exceptions/ApiExceptions.cs` | Tipos de erro de negócio (400/403/404/409/429) |
| `Common/ControllerExtensions.cs` | Helper pra ler o usuário autenticado do token |
| `Common/HorarioBrasilia.cs` | Conta do "dia de hoje" em Brasília (UTC-3), usada pelo ponto |
| `Common/Textos.cs`, `Contato.cs` (e-mail, telefone, senha), `Cpf.cs`, `Datas.cs`, `Valores.cs` (dinheiro, enums), `Concorrencia.cs`, `MensagensValidacao.cs` | Regras de validação centralizadas (ver seção 8), versão otimista e tradução dos erros do banco e do framework |
| `Program.cs` | Liga tudo: banco, autenticação (+ conferência da versão do token, policies `RhOuAdmin` e `Admin`), CORS, cabeçalhos de segurança, erros `{ "mensagem" }`, Swagger, middleware |
| `appsettings*.json` | Configurações (desenvolvimento / exemplo de produção) |
| `SistemaRH.Api.Tests/` | Testes automatizados (unitários + integração ponta-a-ponta) |

### O que ainda falta (próximos passos)

1. ~~Integrar o app Flutter~~ ✅ feito — o app já tem tela de login de verdade e consome `/api/auth/login`, mostrando nome, cargo/departamento e saldo do banco de horas reais. Ponto e ausências ainda não foram ligados às telas correspondentes do app.
2. **Integrar o site React** com a API — tanto o login quanto as rotas administrativas/RH do item 4 (feito) ainda não têm nenhuma tela usando elas.
3. ~~Fluxo de **aprovação de ausências pelo RH**~~ ✅ feito — `GET /api/rh/ausencias` e `PUT /api/rh/ausencias/{id}/status` (ver seção 12).
4. ~~Criar rotas/serviços pro lado administrativo~~ ✅ feito — `Departamento`, `Cargo`, `Funcionario`, `LancamentoBancoHoras`, `Recrutamento`, `Avaliacao`, `EventoAgenda` e `FolhaPagamento` já têm Controller/Service completos, documentados na seção 12 e visíveis no Swagger, protegidos pela policy `RhOuAdmin`. **Ainda falta a UI** — isso é trabalho do site React.
5. ~~Unificar os dois "banco de horas"~~ ✅ feito — `AuthService` agora calcula o saldo a partir do extrato (`LancamentoBancoHoras`) sempre que a conta (`Usuario`) tem um `FuncionarioId` vinculado; senão, cai de volta no campo achatado `SaldoBancoHorasMinutos` (caso das contas demo que nunca foram ligadas a um `Funcionario`).
6. ~~Criar uma rota de gerenciamento de usuários~~ ✅ feito — `GET /api/usuarios` e `PUT /api/usuarios/{id}/perfil` (só Admin). Ligar um `Usuario` a um `Funcionario` continua sendo feito pelo RH (`POST /api/funcionarios/{id}/acesso-app`).
7. **Reativar colaborador / revogar acesso sem desligar** — ainda não existem rotas pra isso.
