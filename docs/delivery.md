# Integração e entrega

## Política pretendida para master

- Branch principal: `master`.
- Todos trabalham em branches `feat/`, `fix/` ou `chore/` e abrem pull requests.
- Apenas gabrielwilliandev e Lucas200507 podem integrar mudanças na master.
- Exigir ao menos uma revisão de outro colaborador, resolver conversas e passar nos checks obrigatórios.
- Invalidar aprovações quando novos commits forem enviados.
- Aplicar verificações também aos administradores; sem bypass, force push ou exclusão da master.
- CODEOWNERS exige revisão quando habilitado nas regras; sozinho não restringe quem faz merge.

Esta política NÃO está aplicada pelo conteúdo deste arquivo. Configurar as regras no GitHub após criar o remoto. Responsáveis definidos: gabrielwilliandev e Lucas200507. A organização ainda precisa ser criada.

A restrição de quem pode enviar alterações à branch protegida é oferecida para organizações, conforme o plano e a visibilidade. Em repositórios privados, verificar GitHub Team ou os recursos do plano disponível. Não substituir proteção por uma simples verificação de nome de usuário no workflow.

## CI/CD

O workflow inicial `Repository checks` verifica somente a estrutura e espaços inválidos: ainda não há código de aplicativo. Ele NÃO comprova compilação ou testes de produto.

Ao adicionar o código, incluir instalação reproduzível, compilação e testes: npm ci/build/test no mobile; dotnet restore/build/test nos serviços. Cada repositório terá seu pipeline independente.

Para CD: executar após merge aprovado em master, produzir artefato identificado pelo commit e usar ambientes separados. Autenticar na AWS com OIDC e permissões mínimas por repositório/ambiente, sem chaves AWS permanentes no GitHub. Produção e aprovação de deploy dependerão dos recursos do plano. Deploy ainda não foi configurado.

## Referências

- https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches
- https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments
