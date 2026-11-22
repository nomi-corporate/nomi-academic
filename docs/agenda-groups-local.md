# Hoje, Agenda e Grupos — implementação local parcial

Referência: TEC-003 v0.2, seções 16.2 e 17.2. Este documento descreve o que o código atual faz; não altera o contrato aprovado nem indica uma HU concluída.

## Leituras de compromissos

- `GET /api/v1/agenda?fromDate=YYYY-MM-DD&toDate=YYYY-MM-DD&timeZone=IANA&cursor=&limit=` consulta compromissos do usuário autenticado em um intervalo civil de até 31 dias. `toDate` é exclusivo. `limit` padrão é 20 e máximo 100; `nextCursor` incorpora intervalo e fuso e não pode ser reutilizado com outros filtros.
- `GET /api/v1/home?date=YYYY-MM-DD&timeZone=IANA` devolve `items` do dia, `upcoming` pendentes futuros, `overdue` pendentes atrasados, `hasMore` e `generatedAt`. Os resultados são limitados a 100 por grupo. Um prazo só com data fica atrasado após o fim da data em seu fuso de origem.
- `GET /api/v1/commitments/{id}?timeZone=IANA` consulta apenas o compromisso do próprio usuário, com `ETag`. O fuso opcional controla `displayDate` e `displayTime`; sem ele, a apresentação usa o fuso de origem. `due.localDate` e `due.timeZone` sempre descrevem o prazo original. Um prazo sem horário tem `due.precision: "date"`, `displayTime: null` e não recebe `instant` artificial.
- As respostas usam `Cache-Control: no-store`. `/agenda` e `/home` retornam `classOccurrencesAvailable: false` enquanto a série de aulas não estiver implementada. Isso impede o cliente de apresentar a ausência de aulas como uma agenda completa.
- Intervalos inválidos, fuso inválido e datas acima dos limites retornam 422. Cursor inválido ou incompatível retorna 400. Leitura de compromisso alheio retorna 404. Falha na validação da sessão retorna 401 ou 503 conforme Identity.

A tabela `academic.commitments` armazena compromissos pessoais e vínculo opcional a matéria do mesmo usuário. Os três compromissos usados na validação local anterior foram inseridos diretamente como fixtures sintéticas; essa validação não cobre a nova rota de criação.

## Criação de compromisso

- `POST /api/v1/commitments` exige sessão validada pelo Identity, `Idempotency-Key` de até 128 caracteres, `title` de 1 a 160 caracteres, `kind` (`exam`, `assignment` ou `reminder`) e `due` conforme TEC-003. `description` é opcional (até 4.000 caracteres); `subjectId` opcional precisa apontar para matéria ativa do mesmo proprietário.
- Prazos `date` guardam data civil e fuso sem inventar horário. Prazos `dateTime` exigem hora e offset explícitos; horário inexistente ou offset incompatível com o fuso retorna 422. O Mobile atual envia apenas `date`.
- Criação, registro da resposta de idempotência e evento `academic.commitment.created.v1` na tabela `academic.outbox_events` são persistidos na mesma operação do EF. Repetir a mesma chave e conteúdo devolve o mesmo 201, `Location`, `ETag` e recurso; reutilizar a chave com conteúdo diferente devolve 409. A versão inicial é 1, com `createdAt` e `updatedAt` do servidor.
- A outbox está pronta para um publicador futuro; **nenhuma entrega ao Notifications ou lembrete é efetuada por esta implementação**. Edição, reagendamento, conclusão, exclusão, recorrência e ocorrência de aula seguem pendentes.

## Grupos

- `POST /api/v1/groups` exige `Idempotency-Key` (até 128 caracteres), `name` de 1 a 120 caracteres e `description` opcional de até 2.000. Cria o grupo e sua única participação inicial `owner` na mesma gravação. Repetir a chave com o mesmo conteúdo devolve o mesmo 201 e ID; mudar o conteúdo devolve 409.
- `GET /api/v1/groups?cursor=&limit=` lista somente grupos ativos com participação ativa do usuário. `limit` padrão 20, máximo 100. O cursor percorre IDs em ordem determinística.
- `GET /api/v1/groups/{id}` só responde para participante ativo de grupo ativo; os demais recebem 404. `ETag` e `Cache-Control: no-store` acompanham a resposta.

As tabelas `academic.study_groups` e `academic.study_group_memberships` guardam propriedade e participação. **Convites, alterações de papel, remoção de membros, materiais, encerramento, eventos e notificações de grupo ainda não foram implementados.** O Mobile mostra esse limite na tela de detalhes.

## Migrações e validação

`20261121011434_AddCommitments` e `20261121012100_AddStudyGroups` foram revisadas por SQL e aplicadas apenas no volume local de validação `nomi-academic-validation-20261120_postgres_data`. A nova `20261121135632_AddCommitmentCreateOutbox` foi gerada, revisada no código e **não foi aplicada**. O volume antigo `nomi-academic-local_postgres_data` foi preservado.

Testes automatizados anteriores usam SQLite para contrato, isolamento, datas civis, idempotência de grupos e revogação de participação. No PostgreSQL local, duas contas sintéticas comprovaram criação/replay/conflito/isolamento de grupos, consulta de perfil, Hoje e Agenda, detalhe por fuso e isolamento de compromissos. **A nova criação de compromisso e a migration da outbox ainda não foram testadas ou aplicadas em PostgreSQL.** A interface nova ainda requer validação visual no Android.
