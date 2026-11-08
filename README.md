# Nomi Academic

Matérias, professores, compromissos, anotações, grupos e suas permissões. Publica eventos de criação, alteração e exclusão de compromissos.

## Stack acordada

C#, ASP.NET Core, Entity Framework Core e PostgreSQL.

Infraestrutura AWS; autenticação Amazon Cognito; arquivos no S3. Cada serviço é implantado independentemente e controla seus próprios dados.


## Executar localmente

Instale o SDK .NET 10 compatível com `global.json`.

```sh
dotnet restore Nomi.Academic.sln
dotnet build Nomi.Academic.sln
dotnet run --project src/Nomi.Academic.Api
```

Liveness: `http://localhost:5101/health/live` confirma que o processo responde. Readiness em `/health/ready` confirma conexão PostgreSQL quando configurada, ou retorna 503 quando o banco está ausente/indisponível.

Para executar com PostgreSQL local, em PowerShell 7:

```powershell
pwsh -NoProfile -File ./scripts/Start-LocalDatabase.ps1
pwsh -NoProfile -File ./scripts/Run-LocalAcademic.ps1
```

Consulte o [passo a passo do banco local](docs/local-database.md), incluindo permissões, credenciais locais, migrations e validação de persistência.

## Estrutura

- `src/Nomi.Academic.Api`: HTTP e composição das dependências.
- `src/Nomi.Academic.Application`: casos de uso e portas de integração.
- `src/Nomi.Academic.Domain`: regras e modelos de domínio.
- `src/Nomi.Academic.Infrastructure`: adapters de persistência e provedores externos.

O banco local e a conexão EF Core estão preparados. Tabelas/migrations de negócio, endpoints acadêmicos, autenticação Cognito e envio de notificações entram nas HUs correspondentes. A base executa sem credenciais AWS; sem configuração de banco, liveness continua disponível e readiness retorna 503.

## Contêiner

```sh
docker build -t nomi-academic:dev .
docker run --rm -p 5101:8080 nomi-academic:dev
```

CI verifica compilação, publicação, liveness e construção da imagem. Após os checks da master, o workflow publica uma imagem validada no ECR usando OIDC e registra seu digest. Configure as três variáveis descritas em [Entrega](docs/delivery.md). A implantação na EC2 ainda não está implementada.

## Documentação

- [Operação dos três serviços na EC2 com Compose](infra/aws/dev/README.md)
- [Arquitetura](docs/architecture.md)
- [Entrega](docs/delivery.md)
- [Contribuição](CONTRIBUTING.md)
