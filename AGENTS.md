# Nomi Academic — instruções para agentes

Branch principal: `master`. Stack: C# e .NET, seguindo `global.json` e os projetos existentes.

## Antes de alterar
- Leia README.md, CONTRIBUTING.md, docs relevantes e .github/workflows. Verifique branch e alterações locais; preserve o trabalho dos colegas.
- Trabalhe em uma branch curta a partir da principal atualizada. Não reutilize uma branch de bootstrap já integrada para uma nova entrega.
- Relacione o trabalho a uma HU ou TEC existente, com critérios de aceite e dependências. Não invente IDs nem links.
- Contrato aprovado: [TEC-003 v0.2](https://github.com/nomi-corporate/nomi-academic/blob/master/docs/TEC-003-contratos-api-eventos-v1.md). Preserve compatibilidade; registre decisões que mudem o contrato.
- Código, modelos e workflows são evidência do que existe. Um documento aprovado não comprova implementação, deploy ou teste em dispositivo.

## HUs, tarefas e PRs
- Cada tarefa tem um responsável; uma HU com entregas em vários repositórios tem um coordenador para acompanhar o conjunto.
- Descreva no PR a HU/TEC, o problema resolvido, a parte entregue, dependências, validação e limitações. Use links completos para referências entre repositórios.
- Para cartões em rascunho no GitHub Project, referencie o link do cartão. Não converta rascunhos automaticamente.
- Use `Refs`/link simples para entregas parciais. Reserve `Closes` para issues inteiramente atendidas; não feche a HU por concluir apenas uma tarefa.
- A HU só está concluída quando todos os critérios de aceite, integrações e verificações aplicáveis estiverem atendidos.
- Mantenedores designados: `gabrielwilliandev` e `Lucas200507`. Não integrar, autoaprovar, usar bypass ou alterar proteções/acessos sem autorização para essa ação. As regras efetivas são as do GitHub; sinalize divergências com docs antigos.

## Regras comuns de implementação
- Aplicar SOLID com simplicidade: casos de uso claros e adapters nas fronteiras externas; evitar padrões e abstrações sem necessidade.
- Serviços acessam somente seus próprios dados; integração por APIs/eventos versionados. Não compartilhar entidades de domínio nem consultar tabelas de outro serviço.
- O Assistant gera propostas; o usuário confirma e o Academic revalida/persiste. Conteúdo gerado por IA é entrada não confiável.
- Identidade vem de token validado. Validar permissão sobre cada recurso e grupo; não confiar em proprietário enviado pelo cliente.
- Seguir idempotência, ETag/If-Match, erros e datas/fusos da TEC-003. Repetições não devem duplicar efeitos; prazos sem horário não viram meia-noite implicitamente.
- Não versionar segredos, tokens, áudio pessoal, .env, local.properties ou chaves de assinatura. Não registrar tokens ou conteúdo pessoal em logs.
- Não adicionar infraestrutura paga, deploy, provedor de IA ou publicação de app implicitamente. AWS e provedores externos exigem configuração por ambiente e escopo definido.
- Wear OS é o alvo atual; Apple Watch é futuro. Não criar um microsserviço por dispositivo.

## Verificação e comunicação
- Execute verificações proporcionais à alteração e os checks aplicáveis do workflow. Mudanças apenas documentais exigem revisão de consistência, links e diff; não builds completos.
- Ao implementar comportamento, cubra casos relevantes de sucesso, validação, autorização e falha. Não simule persistência ou entrega real como sucesso de produção.
- Informe o que foi validado e o que não foi. Build, mocks, bundle e liveness não equivalem a fluxo completo ou execução em aparelho.
- Explique mudanças e limitações em português; preserve nomes de código e contratos em inglês conforme as convenções existentes.

## Arquitetura deste serviço
- `Api`: HTTP, autenticação e composição das dependências.
- `Application`: casos de uso e portas; depende de `Domain`.
- `Domain`: regras de negócio sem dependência de frameworks de persistência ou provedores.
- `Infrastructure`: adapters e persistência; depende de `Application`.
- `Api` referencia `Application` e `Infrastructure`. Não inverter essas dependências.
- Organizar por funcionalidade dentro das camadas; registrar dependências externas na composição da API.
- Propagar CancellationToken em I/O assíncrono e usar logs estruturados com correlação, sem dados sensíveis.
- O Academic é proprietário de matérias, professores, compromissos, agenda, anotações e grupos. Leia o contrato local em `docs/TEC-003-contratos-api-eventos-v1.md`.
- Persistência planejada: PostgreSQL e EF Core. Ao implementá-la, versionar migrations, revisar SQL e impacto nos dados; não aplicar migrations destrutivas automaticamente em ambientes compartilhados.
- Para alterações que gerem eventos, persistir estado e outbox na mesma transação. Publicação precisa tolerar repetição e consumidores precisam ser idempotentes.
- Cobrir isolamento entre usuários, permissões de grupo, concorrência e referências a recursos acessíveis nas HUs afetadas.
## Verificações locais
Execute na raiz do repositório:

```sh
dotnet restore Nomi.Academic.sln
dotnet build Nomi.Academic.sln -c Release --no-restore
dotnet publish src/Nomi.Academic.Api -c Release --no-build -o artifacts/api
dotnet run --project src/Nomi.Academic.Api
```

- Liveness local: `http://localhost:5101/health/live`. Validar HTTP 200; isso não comprova conexão com banco ou fila.
- Se alterar Dockerfile, dependências de execução ou composição do serviço: `docker build -t nomi-academic:dev .` e smoke test do contêiner.
- CI: `Build and smoke check` e `Container build`. Não renomear jobs sem coordenar os checks obrigatórios do GitHub.
- Executar os testes relevantes quando houver projetos de teste para o comportamento alterado; não declarar uma suíte inexistente como executada.
