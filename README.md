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

Liveness: `http://localhost:5101/health/live`. O endpoint confirma somente que o processo responde; conexões de banco e filas ainda serão implementadas.

## Estrutura

- `src/Nomi.Academic.Api`: HTTP e composição das dependências.
- `src/Nomi.Academic.Application`: casos de uso e portas de integração.
- `src/Nomi.Academic.Domain`: regras e modelos de domínio.
- `src/Nomi.Academic.Infrastructure`: adapters de persistência e provedores externos.

Ainda não há endpoints acadêmicos, autenticação Cognito, banco configurado ou envio de notificações. A base executa sem credenciais AWS. As próximas entregas adicionam as funcionalidades da TEC-003.

## Contêiner

```sh
docker build -t nomi-academic:dev .
docker run --rm -p 5101:8080 nomi-academic:dev
```

CI verifica compilação, publicação, liveness e construção da imagem. CD depende da infraestrutura e das permissões AWS.

## Documentação

- [Arquitetura](docs/architecture.md)
- [Entrega](docs/delivery.md)
- [Contribuição](CONTRIBUTING.md)
