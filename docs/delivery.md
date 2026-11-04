# Integração e entrega — nomi-academic

## Escopo e rastreabilidade

Esta entrega estrutura a parte de CI da [TEC-007 — CI/CD](https://github.com/orgs/nomi-corporate/projects/1/views/1?pane=issue&itemId=292993590). Ela não conclui o CD nem a TEC inteira.

Branch principal: `master`. Cada alteração entra por branch e PR associado à HU/TEC, com responsável, parte entregue, dependências e evidências. Uma HU que atravessa repositórios só termina após validar a entrega completa.

Os mantenedores designados são `gabrielwilliandev` e `Lucas200507`. Autorizações excepcionais e proteções são configuradas no GitHub; este documento não as altera. Conferir as regras efetivas antes de integrar. Não falsificar checks nem desabilitar proteções para fazer o pipeline passar.

## Disparos e segurança

- PRs destinados a `master`, pushes em `master` e execução manual em Actions. A execução manual passa a aparecer quando o workflow estiver na branch principal.
- Não há filtro por caminho: uma alteração documental também publica os checks obrigatórios, evitando estados Expected indefinidos.
- Novos commits cancelam execuções anteriores do mesmo PR. Execuções já iniciadas da principal não são canceladas automaticamente; a fila de concurrency pode substituir execuções ainda pendentes.
- Token com `contents: read`; checkout sem persistir credenciais. Nenhum job recebe credenciais AWS ou publica em registry, loja ou hospedagem.
- Actions fixadas por SHA. Para atualizar, conferir a origem e o commit da versão desejada, atualizar os pins e executar os checks.
- O job `Workflow lint` valida todos os YAMLs com actionlint 1.7.12, cujo binário é conferido por SHA-256.
- Não usar `pull_request_target` para executar código de PR. Runners são hospedados no GitHub; não executar contribuições externas em máquina pessoal com segredos.

## Pipeline atual

Workflow `Service CI`, jobs:
- `Build and smoke check`: restaura, compila Release e publica a API, executa os projetos de teste disponíveis em `tests/**/*.csproj` e exige HTTP 200 com corpo `Healthy` em `/health/live`.
- `Container build`: constrói a imagem, inicia um contêiner, valida o mesmo liveness e exporta a imagem.
- `Workflow lint`: valida o próprio workflow.

Checks de proteção existentes mantêm seus nomes: `Build and smoke check` e `Container build`. Recomenda-se tornar `Workflow lint` obrigatório também; adicionar essa exigência é uma configuração separada do GitHub.

Ainda não há projetos de teste. O resumo informa essa ausência; passar em build/liveness não significa que regras de negócio foram testadas. Novos projetos de teste devem ficar sob `tests/`, ser incluídos na solução e verificar comportamento relevante.

## Pacotes gerados

- API publicada em arquivo `nomi-academic-api.tar.gz`, com build.json, retenção de 7 dias.
- Imagem Docker `nomi-academic:<SHA>` em `nomi-academic-image.tar.gz`, acompanhada de SHA256SUMS e ID local da imagem. Retenção: 3 dias em PR e 7 dias na principal/manual.
- Resultados TRX, quando existirem, por 7 dias. Não gerar relatório vazio para simular teste.

Para inspecionar uma imagem, baixar o artefato, conferir `sha256sum --check SHA256SUMS`, executar `docker load --input nomi-academic-image.tar.gz` e usar a tag de commit registrada. O ID local não é o digest de um registry.

O pacote de API e a imagem são construídos por jobs distintos a partir do mesmo SHA. A futura implantação deve usar a imagem testada, sem reconstruí-la. Liveness comprova processo respondendo; readiness de banco/filas entra quando essas dependências existirem.

## Identidade e artefatos

Os nomes incluem o SHA testado e a tentativa da execução. Em PR, `github.sha` identifica o merge de teste do GitHub; não confundir esse valor com o último commit da branch de origem. `build.json` registra repositório, SHA, execução e tentativa.

Artefatos servem para inspeção e reaproveitamento técnico. Um artefato de PR não é uma release confiável para implantação. Para promover, usar uma execução bem-sucedida de push da branch principal, conferir origem, SHA e ambiente. A retenção curta não é armazenamento permanente de releases.

A indisponibilidade do upload de um artefato obrigatório falha o job, mesmo que o build tenha passado. Se atingir a cota de artefatos, revisar armazenamento e retenção; não mascarar a falha.

## CD AWS — próxima etapa

O deploy ainda não está implementado e esta pipeline não cria recursos pagos.

Antes de ativar CD de backend:
1. Aprovar a infraestrutura dev e seu orçamento, validar permissões da conta AWS e a viabilidade de OIDC.
2. Definir registry ECR, host/orquestrador, rede, HTTPS, gerenciamento de segredos e health/readiness reais.
3. Criar a role do GitHub com identidade temporária e escopo por repositório, branch/ambiente e recursos permitidos. Conceder `id-token: write` somente ao job de deploy.
4. Publicar imagem da principal com tag do commit e registrar seu digest no registry; implantar o mesmo digest validado.
5. Verificar saúde, preservar versão anterior e documentar rollback. Migrations precisam ser compatíveis com a versão anterior ou ter plano próprio de recuperação.
6. Definir ambiente e aprovação disponíveis no plano. Não adicionar chaves AWS permanentes como atalho caso OIDC esteja indisponível.

Referências: [workflows e permissões](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), [artefatos](https://docs.github.com/en/actions/tutorials/store-and-share-data) e proposta TEC-004 do projeto.
