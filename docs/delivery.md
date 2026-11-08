# Integração e entrega — nomi-academic

## Escopo e rastreabilidade

Esta entrega estrutura CI e publicacao de imagens no ECR como parte da [TEC-007 — CI/CD](https://github.com/orgs/nomi-corporate/projects/1/views/1?pane=issue&itemId=292993590). Ela não conclui o CD nem a TEC inteira.

Branch principal: `master`. Cada alteração entra por branch e PR associado à HU/TEC, com responsável, parte entregue, dependências e evidências. Uma HU que atravessa repositórios só termina após validar a entrega completa.

Os mantenedores designados são `gabrielwilliandev` e `Lucas200507`. Autorizações excepcionais e proteções são configuradas no GitHub; este documento não as altera. Conferir as regras efetivas antes de integrar. Não falsificar checks nem desabilitar proteções para fazer o pipeline passar.

## Disparos e segurança

- PRs destinados a `master`, pushes em `master` e execução manual em Actions. A execução manual passa a aparecer quando o workflow estiver na branch principal.
- Não há filtro por caminho: uma alteração documental também publica os checks obrigatórios, evitando estados Expected indefinidos.
- Novos commits cancelam execuções anteriores do mesmo PR. Execuções já iniciadas da principal não são canceladas automaticamente; a fila de concurrency pode substituir execuções ainda pendentes.
- Token com `contents: read`; checkout sem persistir credenciais. Somente `Publish image to ECR`, restrito a master do repositorio original, recebe `id-token: write` e credenciais AWS temporarias. PRs nao publicam imagens nem acessam a AWS.
- Actions fixadas por SHA. Para atualizar, conferir a origem e o commit da versão desejada, atualizar os pins e executar os checks.
- O job `Workflow lint` valida todos os YAMLs com actionlint 1.7.12, cujo binário é conferido por SHA-256.
- Não usar `pull_request_target` para executar código de PR. Runners são hospedados no GitHub; não executar contribuições externas em máquina pessoal com segredos.

## Pipeline atual

Workflow `Service CI`, jobs:
- `Build and smoke check`: restaura, compila Release e publica a API, executa os projetos de teste disponíveis em `tests/**/*.csproj` e exige HTTP 200 com corpo `Healthy` em `/health/live`.
- `Container build`: constrói a imagem, inicia um contêiner, valida o mesmo liveness. A exportação da imagem é opcional em execução manual.
- `Workflow lint`: valida o próprio workflow.
- `Publish image to ECR`: apos todos os checks, publica a imagem validada no registry privado; nao implanta na EC2.
- `Database integration`: sobe PostgreSQL isolado, verifica permissões, conexão EF Core, readiness, indisponibilidade, reconexão e persistência após reinício. Confere também a ferramenta de migrations. Detalhes em [banco local](local-database.md).

Checks de proteção existentes mantêm seus nomes: `Build and smoke check` e `Container build`. Recomenda-se tornar `Workflow lint` obrigatório também; adicionar essa exigência é uma configuração separada do GitHub.

Ainda não há projetos de teste. O resumo informa essa ausência; passar em build/liveness não significa que regras de negócio foram testadas. Novos projetos de teste devem ficar sob `tests/`, ser incluídos na solução e verificar comportamento relevante.

## Pacotes gerados

- API publicada em arquivo `nomi-academic-api.tar.gz`, com build.json, retenção de 7 dias.
- Imagem Docker `nomi-academic:<SHA>` em `nomi-academic-image.tar.gz`, acompanhada de SHA256SUMS e ID local da imagem. Gerada somente em execução manual com `export_image=true`; retenção de 1 dia. PRs e pushes não armazenam essa imagem para reduzir uso de armazenamento.
- Resultados TRX, quando existirem, por 7 dias. Não gerar relatório vazio para simular teste.

Para inspecionar uma imagem, baixar o artefato, conferir `sha256sum --check SHA256SUMS`, executar `docker load --input nomi-academic-image.tar.gz` e usar a tag de commit registrada. O ID local não é o digest de um registry.

Para baixar a imagem, abrir Actions → Service CI → Run workflow e marcar Exportar imagem Docker. Usar a branch principal para um pacote de referência confiável. O caminho de exportação foi exercitado nos PRs de estruturação antes de se tornar uma opção manual.

O pacote de API e a imagem são construídos por jobs distintos a partir do mesmo SHA. A futura implantação deve usar a imagem testada, sem reconstruí-la. Liveness comprova processo respondendo; readiness de banco/filas entra quando essas dependências existirem.

## Identidade e artefatos

Os nomes incluem o SHA testado e a tentativa da execução. Em PR, `github.sha` identifica o merge de teste do GitHub; não confundir esse valor com o último commit da branch de origem. `build.json` registra repositório, SHA, execução e tentativa.

Artefatos servem para inspeção e reaproveitamento técnico. Um artefato de PR não é uma release confiável para implantação. Para promover, usar uma execução bem-sucedida de push da branch principal, conferir origem, SHA e ambiente. A retenção curta não é armazenamento permanente de releases.

A indisponibilidade do upload de um artefato obrigatório falha o job, mesmo que o build tenha passado. Se atingir a cota de artefatos, revisar armazenamento e retenção; não mascarar a falha.

## CD AWS — próxima etapa

A publicacao no ECR esta implementada no workflow. A autenticacao OIDC e o push reais precisam ser verificados em uma execucao da master apos a integracao. A pipeline nao cria infraestrutura e nao implanta na EC2; armazenar imagens no ECR consome a franquia/creditos conforme o plano.

Antes de ativar CD de backend:
1. Aprovar a infraestrutura dev e seu orçamento, validar permissões da conta AWS e a viabilidade de OIDC.
2. Definir registry ECR, host/orquestrador, rede, HTTPS, gerenciamento de segredos e health/readiness reais.
3. Criar a role do GitHub com identidade temporária e escopo por repositório, branch/ambiente e recursos permitidos. Conceder `id-token: write` somente ao job de deploy.
4. Publicar imagem da principal com tag do commit e registrar seu digest no registry; implantar o mesmo digest validado.
5. Verificar saúde, preservar versão anterior e documentar rollback. Migrations precisam ser compatíveis com a versão anterior ou ter plano próprio de recuperação.
6. Definir ambiente e aprovação disponíveis no plano. Não adicionar chaves AWS permanentes como atalho caso OIDC esteja indisponível.

Referências: [workflows e permissões](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), [artefatos](https://docs.github.com/en/actions/tutorials/store-and-share-data) e proposta TEC-004 do projeto.


## Publicacao de imagens no ECR — TEC-007 (entrega parcial)

Configuracao por repositorio em Settings > Secrets and variables > Actions > Variables:

| Variavel | Valor |
| --- | --- |
| `AWS_REGION` | `us-east-2` |
| `AWS_ROLE_ARN` | `arn:aws:iam::338128499585:role/nomi-dev-github-academic-role` |
| `ECR_REPOSITORY` | `nomi-academic` |

O provedor IAM usa `https://token.actions.githubusercontent.com`, audience `sts.amazonaws.com`, e uma role propria por servico. A confianca deve limitar o subject OIDC ao repositorio e `refs/heads/master`, incluindo os IDs imutaveis usados pelos repositorios novos. Nao adicionar GitHub Environment ao job sem revisar a confianca: ele altera o subject. Nao usar access keys permanentes nem senhas de banco neste workflow.

Disparos: push em master publica automaticamente depois de todos os jobs de CI aprovados. Na execucao manual, selecionar master e marcar `publish_image`; o padrao e falso. PRs, forks, outras branches e execucoes manuais sem essa opcao nao publicam. A role tambem deve impor essa restricao no IAM. Os checks existentes e a opcao `export_image` permanecem.

O job de publicacao constroi sua propria imagem e executa um novo teste de liveness nela antes de obter credenciais AWS e publicar exatamente essa imagem. Essa construcao adicional evita transferir arquivos grandes entre jobs e garante que a imagem enviada foi exercitada; ela nao comprova readiness RDS nem funcionalidade de negocio.

A tag `sha-<commit>-run-<run_id>-<run_attempt>` distingue commits, execucoes manuais e retries sem sobrescrever tags imutaveis. Nao usamos `latest`. O job consulta o digest no ECR, confere sua presenca na imagem local apos o push e registra `image_digest_uri` no resumo do Actions e no artefato pequeno `ecr-image.json`, retido por 7 dias. A futura implantacao deve usar esse URI com `@sha256:...`; nao reconstruir no servidor.

Se as variaveis estiverem ausentes ou divergentes, o job falha antes de autenticar. Se houver `AccessDenied`/erro de OIDC, conferir provider, audience, subject, role ARN e politica do ECR; nao ampliar o acesso a outras branches como atalho. Se o push tiver concluido e um passo posterior falhar, a imagem pode permanecer no ECR. Um retry usa tag nova; configurar retencao do registry preservando as imagens em uso e de rollback.

### Implantacao ainda pendente

Faltam configuracao de execucao/segredos por servico na EC2, HTTPS, autenticacao das APIs, readiness das dependencias, implantacao por digest e rollback. Assistant e Notifications ainda nao possuem persistencia integrada ao codigo. Publicar uma imagem nao conclui CD, TEC-007 ou HUs funcionais.
