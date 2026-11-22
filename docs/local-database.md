# PostgreSQL local — passo a passo

Esta entrega prepara persistência e APIs de matérias/professores ligadas às HU-106–HU-111. As histórias permanecem parciais: a validação depende do serviço Nomi Identity, da migration aplicada no ambiente e do fluxo Mobile completo. A infraestrutura local não exige conta ou recurso AWS.

## Como as peças se conectam

`Academic → EF Core → Npgsql → PostgreSQL no Docker → volume persistente`.

O `compose.yaml` descreve o banco. O EF Core é a camada de persistência da aplicação; Npgsql é o provedor PostgreSQL. O volume guarda os dados fora do ciclo de vida do contêiner. Volume não é backup: perda do volume ou da máquina ainda pode destruir dados.

| Item | Configuração local |
| --- | --- |
| Servidor | PostgreSQL 18, imagem oficial Alpine fixada por digest |
| Endereço | `127.0.0.1:55432`; acesso apenas neste computador |
| Banco | `nomi_academic` |
| Dados da aplicação | Schema `academic` |
| Histórico do EF Core | Schema `migrations` |
| Aplicação | Usuário `nomi_academic_app`, leitura/gravação de dados |
| Migrations | Usuário `nomi_academic_migrator`, proprietário do banco e schemas |
| Administração inicial | `nomi_admin`, usado pelo bootstrap do contêiner |
| Credenciais | Geradas em `.env`, ignorado pelo Git |

Somente o Academic tem um banco preparado nesta etapa. Assistant e Notifications receberão bancos/identidades próprios quando precisarem persistir dados; não utilizarão o banco Academic. Cada desenvolvedor possui sua própria cópia local.

## 1. Iniciar o PostgreSQL

Requisitos: Docker Desktop/Engine iniciado, Docker Compose, PowerShell 7 e SDK .NET conforme `global.json`. Os comandos abaixo são executados na raiz de `nomi-academic`.

```powershell
pwsh -NoProfile -File ./scripts/Start-LocalDatabase.ps1
```

Na primeira execução, o script gera três senhas diferentes, aleatórias, em `.env`. Depois executa Compose e espera o banco aceitar conexões. Ele preserva um `.env` existente.

A imagem executa `infra/local/postgres/initialize.sh` somente quando o volume está vazio: cria usuários, banco, schemas e permissões. Em execuções seguintes, o volume existente é reutilizado. O script não apaga nem recria seus dados.

O exemplo `.env.example` documenta as chaves, mas seus placeholders não são credenciais utilizáveis. Os scripts locais aceitam senhas de 64 caracteres hexadecimais; o gerador já produz esse formato. Em Linux/macOS, o script restringe a leitura do arquivo ao proprietário; em Windows, valem as permissões do seu perfil/pasta. `.env` é texto local, não um cofre de produção.

Se a porta estiver ocupada, altere apenas `NOMI_POSTGRES_PORT` em `.env` e execute o script novamente. Alterar senhas nesse arquivo não altera automaticamente usuários de um volume já inicializado: isso exige rotação SQL coordenada. Não excluir o volume para corrigir senha ou porta sem avaliar seus dados.

## 2. Iniciar o Academic conectado

```powershell
pwsh -NoProfile -File ./scripts/Run-LocalAcademic.ps1
```

O script lê a senha local da aplicação e fornece `ConnectionStrings__Academic` somente ao processo de execução. Não copia senha para appsettings ou arquivos versionados. Ele mantém a API em `http://127.0.0.1:5101` até você encerrar com Ctrl+C.

Em outro terminal:

```powershell
Invoke-WebRequest http://127.0.0.1:5101/health/live
Invoke-WebRequest http://127.0.0.1:5101/health/ready
```

- `/health/live`: HTTP 200 significa que o processo responde, independentemente do banco.
- `/health/ready`: HTTP 200 significa conexão EF Core/PostgreSQL funcionando; configuração ausente ou banco indisponível retorna HTTP 503.

Readiness verifica conexão, não tabelas, migrations aplicadas, autorização de usuário final ou funcionamento de uma HU. A API oferece `POST/GET /api/v1/subjects`, `GET/PATCH /api/v1/subjects/{id}`, `PUT /api/v1/subjects/{id}/archive`, `PUT /api/v1/subjects/{id}/professors` e CRUD de `/api/v1/professors`. As rotas consultam Nomi Identity com o Bearer token recebido e só consultam registros do usuário autenticado. Alterações exigem `If-Match`; os POSTs de matérias e professores exigem `Idempotency-Key`, com replay da resposta original e retenção proposta de 24 horas. O endpoint móvel exige API ativa, Identity acessível e migrations aplicadas; isso não comprova o fluxo completo das HUs.

## 3. Entender e usar migrations

Migration é um arquivo de código versionado que descreve uma mudança na estrutura do banco. As migrations atuais criam `academic.subjects`, `academic.professors`, o vínculo de professores e matérias com chaves compostas por usuário e as respostas persistentes de idempotência. Elas devem ser revisadas e aplicadas explicitamente com o usuário migrador; a API não aplica migrations automaticamente.

A base contém DbContext, provedor e ferramenta EF Core fixada por versão. As migrations ainda precisam ser revisadas e aplicadas em cada ambiente; não são executadas automaticamente pela API.

Conferir contexto/provedor/banco:

```powershell
pwsh -NoProfile -File ./scripts/Invoke-LocalMigrations.ps1 -Action Info
pwsh -NoProfile -File ./scripts/Invoke-LocalMigrations.ps1 -Action List
```

Para gerar e revisar o SQL das migrations existentes:

```powershell
pwsh -NoProfile -File ./scripts/Invoke-LocalMigrations.ps1 -Action Script
# Revisar .local/migrations.sql antes de aplicar no seu banco.
pwsh -NoProfile -File ./scripts/Invoke-LocalMigrations.ps1 -Action Update
```

Novos modelos exigem migration versionada em `src/Nomi.Academic.Infrastructure/Persistence/Migrations` e revisão do SQL antes da aplicação. O helper não aplica migrations automaticamente na inicialização da API.

O helper usa a identidade de migrations e restaura a variável de ambiente ao terminar. O processo normal da API recebe apenas a identidade de aplicação, sem DDL e sem acesso ao schema de histórico.

## 4. Validar o ambiente

```powershell
pwsh -NoProfile -File ./scripts/Test-LocalDatabase.ps1 -Restart
```

O teste cria uma tabela de prova isolada com nome aleatório e a remove ao terminar. Verifica leitura, inserção, alteração e exclusão pela identidade da aplicação; nega criação de tabela; confere acesso aos schemas e privilégios; inicia a API e verifica readiness. Com `-Restart`, para o PostgreSQL, verifica live=200/ready=503, reinicia e confirma reconexão e preservação do registro.

O teste interrompe somente este banco local. Evite executá-lo com `-Restart` durante outra atividade que dependa do seu PostgreSQL. Sem esse parâmetro, ele não reinicia o banco. A instância de API usada na validação é temporária e encerrada pelo próprio script.

O job `Database integration` executa essas verificações em banco descartável do runner, independente do banco dos desenvolvedores.

## 5. Encerrar e retomar

```powershell
docker compose stop postgres
# Para retomar, execute Start-LocalDatabase.ps1 novamente.
```

Parar o contêiner preserva o volume. `docker compose down` também preserva volumes nomeados por padrão, mas `down --volumes` os apaga. Não usar a remoção de volumes como rotina de desenvolvimento.

Este Compose não é a configuração de produção. AWS exige rede, TLS, backup/restauração, segredos e identidades próprios, além da revisão de orçamento e infraestrutura da TEC-004.

## Fontes

- [Imagem oficial PostgreSQL e inicialização/volume](https://hub.docker.com/_/postgres)
- [Provedor Npgsql para EF Core](https://www.npgsql.org/efcore/)
- [Aplicação e revisão de migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
