# Nomi — Contratos de APIs e eventos

**Referência:** TEC-003 · **Versão:** 0.2 · **Data:** 04/11/2026  
**Estado:** documento aprovado pelo responsável pelo projeto em 04/11/2026; implementação ainda não realizada.

## 1. Objetivo e escopo

Definir como os aplicativos mobile e Wear OS e os três microsserviços se comunicam. O documento cobre identidade, perfil, matérias, professores, compromissos, grade recorrente, anotações, grupos, materiais, conversas, voz, notificações e integração com relógios.

As seções 3 a 10 detalham o fluxo de compromissos e lembretes. As seções 13 a 21 completam os contratos dos demais módulos e estabelecem regras comuns de implementação. Esta é uma proposta documental completa para revisão. A aprovação e a geração dos arquivos OpenAPI e JSON Schema são etapas de entrega técnica distintas.

As palavras **deve** e **obrigatório** descrevem regras propostas deste contrato. Escolhas de infraestrutura, prazos de retenção e limites numéricos ainda dependem de revisão.

### Navegação

- [Responsabilidades e fluxo principal](#2-responsabilidades)
- [Convenções de API e erros](#4-convenções-comuns)
- [Compromissos](#5-modelo-de-compromisso)
- [Eventos de compromissos](#9-eventos-publicados-pelo-academic)
- [Identidade, onboarding e perfil](#14-identidade-onboarding-e-perfil)
- [Matérias e professores](#15-matérias-e-professores--academic)
- [Grade e agenda](#16-grade-de-aulas-recorrência-e-agenda--academic)
- [Anotações, grupos e materiais](#17-anotações-grupos-e-materiais--academic)
- [Conversas e voz](#18-conversas-voz-e-propostas-de-onboarding--assistant)
- [Notificações e adiamento](#19-notificações-leitura-e-adiamento)
- [Wear OS](#20-wear-os--contrato-do-nomi-watch)
- [Integrações e fechamento](#21-catálogo-de-integração-e-critérios-de-fechamento)

## 2. Responsabilidades

| Componente | Responsabilidade | Dados sob seu controle |
| --- | --- | --- |
| Mobile | Exibir informações, capturar entradas e solicitar confirmação | Preferências locais e sessão armazenada de forma segura |
| Academic | Validar permissões e regras acadêmicas; persistir compromissos; publicar mudanças | Matérias, professores, compromissos, grade, anotações e grupos |
| Assistant | Interpretar texto e, futuramente, áudio; produzir propostas estruturadas | Propostas e contexto de conversa autorizado |
| Notifications | Calcular envios, registrar dispositivos, aplicar preferências e entregar avisos | Preferências de aviso, agendamentos, notificações e tentativas |
| Cognito | Autenticar pessoas e emitir tokens | Identidades e credenciais |
| Watch | Cliente Kotlin com Jetpack Compose para Wear OS: consultas rápidas, avisos e captura por voz | Sessão própria e cache limitado ao usuário |

Cada serviço acessa apenas seu próprio armazenamento. As integrações usam APIs ou eventos. O Assistant obtém somente contexto acadêmico autorizado e limitado ao pedido; respostas da IA passam por validação estruturada.

Uma HU pode envolver mais de um repositório. Seu componente coordenador identifica quem acompanha a entrega inteira.

## 3. Fluxo de referência

### 3.1 Cadastro manual

1. O estudante preenche e confirma o compromisso no mobile.
2. O mobile envia a solicitação ao Academic com token e chave de idempotência.
3. O Academic valida dados e permissões e salva compromisso e evento pendente na mesma transação.
4. O mobile recebe o compromisso salvo e informa sucesso.
5. O publicador da outbox entrega o evento ao transporte de mensagens.
6. O Notifications processa o evento e agenda os avisos conforme as preferências vigentes.

Salvar o compromisso e agendar o lembrete têm confirmações distintas. A resposta do Academic confirma a persistência; o processamento de lembretes ocorre depois. O mobile pode consultar a situação dos avisos no Notifications.

### 3.2 Cadastro assistido

1. O estudante descreve o compromisso no mobile.
2. O Assistant retorna uma proposta ou uma pergunta de esclarecimento.
3. O estudante responde, corrige os campos e confirma o resumo no mobile.
4. O mobile envia os dados confirmados ao mesmo endpoint de criação do Academic.
5. O fluxo de persistência e lembretes segue as mesmas regras do cadastro manual.

Gerar ou editar uma proposta não cria um compromisso. O Academic revalida todos os dados recebidos, inclusive os produzidos pela IA.

## 4. Convenções comuns

| Tema | Contrato proposto |
| --- | --- |
| Endereçamento | Cada serviço expõe `/api/v1`; host e roteamento serão definidos na TEC-004 |
| Formato | JSON UTF-8, propriedades `camelCase` e enumerações em inglês |
| Identificadores | UUID gerado pelo serviço proprietário; tratado como valor opaco pelos clientes |
| Instantes | ISO 8601 com offset; eventos usam UTC com sufixo `Z` |
| Datas e fusos | Data civil `YYYY-MM-DD`; fuso IANA, como `America/Sao_Paulo` |
| Paginação | Cursor opaco; proposta inicial: 20 itens por página e máximo de 100 |
| Concorrência | Recursos mutáveis retornam `ETag`; alterações exigem `If-Match` |
| Rastreabilidade | Cabeçalho `X-Correlation-Id` e propagação de contexto de tracing entre serviços |
| Evolução | Campos novos opcionais podem ser adicionados na v1; mudanças incompatíveis exigem versão nova |

Clientes devem tolerar campos extras nas respostas. Valores desconhecidos de enumeração precisam de apresentação segura, sem executar ações baseadas em suposição.

### Autenticação e autorização

Chamadas do mobile usam `Authorization: Bearer <access-token>`. Cada API valida assinatura, expiração, emissor, finalidade do token, cliente e escopos permitidos; audiência também é validada quando aplicável à configuração do recurso. Os valores concretos do Cognito serão registrados por ambiente.

O identificador do usuário vem do token validado. Solicitações comuns não recebem um `userId` editável para escolher o proprietário. Cada consulta ou alteração verifica o vínculo do recurso ao usuário. Para recursos inexistentes ou fora do acesso do solicitante, retornar `404`, evitando revelar registros de terceiros.

Chamadas internas exigem identidade de serviço e permissões próprias. Conhecer um UUID ou um identificador de usuário não concede acesso. O mecanismo de autenticação interna será escolhido na arquitetura AWS.

### Idempotência

`POST /commitments` exige `Idempotency-Key`, gerada pelo cliente para uma intenção de criação. Uma nova tentativa da mesma intenção reutiliza a chave.

- O Academic reserva a chave de forma atômica, no escopo do usuário e da operação.
- Mesma chave e mesmo conteúdo normalizado retornam o resultado original, inclusive o mesmo ID.
- Mesma chave com conteúdo diferente retorna `409 IDEMPOTENCY_KEY_REUSED`.
- Requisição original ainda em processamento retorna `409 REQUEST_IN_PROGRESS`, com orientação para tentar novamente.
- Proposta inicial de retenção: 24 horas. O cliente preserva a chave durante esse período; após expirar, não repete automaticamente uma criação de resultado desconhecido.

Operações de alteração e exclusão usam controle de versão. Uma resposta perdida deve ser reconciliada consultando o recurso antes de repetir a ação.

### Erros

Formato proposto: `application/problem+json`, com campos adicionais `code`, `traceId` e `errors`.

```json
{
  "type": "about:blank",
  "title": "Dados inválidos",
  "status": 422,
  "detail": "Revise os campos indicados.",
  "code": "VALIDATION_FAILED",
  "traceId": "correlation-example",
  "errors": { "title": ["Informe um título."] }
}
```

| HTTP | Uso |
| --- | --- |
| 400 | JSON, parâmetro ou cabeçalho malformado |
| 401 | Token ausente ou inválido |
| 403 | Usuário autenticado sem permissão para uma operação geral |
| 404 | Recurso inexistente ou inacessível |
| 409 | Conflito de estado ou idempotência |
| 412 | `If-Match` não corresponde à versão atual |
| 422 | Conteúdo compreendido, mas inválido pelas regras do domínio |
| 428 | Falta `If-Match` em operação que exige controle de versão |
| 429 | Limite de chamadas; respeitar `Retry-After` quando fornecido |
| 503 | Dependência temporariamente indisponível |

Erros não expõem tokens, detalhes internos, prompts completos ou conteúdo privado de outros usuários.

## 5. Modelo de compromisso

| Campo | Tipo | Regra |
| --- | --- | --- |
| `id` | UUID | Gerado pelo Academic |
| `title` | string | Obrigatório; proposta de limite: 1 a 160 caracteres após remover espaços nas extremidades |
| `description` | string ou null | Opcional; proposta de limite: 4.000 caracteres |
| `kind` | enum | `exam`, `assignment` ou `reminder` |
| `subjectId` | UUID ou null | Quando informado, deve apontar para matéria acessível e válida para o vínculo |
| `due` | objeto | Prazo com ou sem horário, conforme abaixo |
| `status` | enum | `pending` ou `completed`; criado como `pending` |
| `version` | inteiro | Incrementado pelo Academic em cada mudança efetiva |
| `createdAt`, `updatedAt` | instante UTC | Gerados pelo servidor |

Excluir um compromisso produz uma remoção lógica para integração e o retira das consultas normais. A política de retenção e eliminação definitiva será definida separadamente.

### Prazo com horário

```json
{
  "precision": "dateTime",
  "localDate": "2026-11-20",
  "localTime": "14:00:00",
  "timeZone": "America/Sao_Paulo",
  "offset": "-03:00"
}
```

O Academic valida a combinação e devolve também `instant`, neste exemplo `2026-11-20T17:00:00Z`. Horários inexistentes no fuso são rejeitados. Em horários ambíguos, o offset precisa identificar uma ocorrência válida; o cliente solicita esclarecimento quando necessário.

### Prazo somente com data

```json
{
  "precision": "date",
  "localDate": "2026-11-20",
  "timeZone": "America/Sao_Paulo"
}
```

Não converter esse prazo em meia-noite. A resposta não inclui `instant`, `localTime` ou `offset`. Para gerar aviso, o Notifications exige uma preferência explícita de horário para prazos sem hora. Sem essa preferência, informa `requiresTimePreference`.

Uma alteração posterior do fuso do perfil não modifica silenciosamente compromissos já cadastrados. Alterar o prazo ou seu fuso exige uma operação explícita.

## 6. API do Academic

Todas as rotas abaixo são relativas a `/api/v1`. Estas rotas tratam compromissos pessoais e pontuais; recorrência e compartilhamento são definidos nas seções complementares.

| Método e rota | Entrada principal | Resultado |
| --- | --- | --- |
| `POST /commitments` | Título, tipo, descrição, matéria e prazo | `201`, recurso, `Location` e `ETag` |
| `GET /commitments/{id}` | ID | `200`, recurso e `ETag` |
| `GET /commitments` | Filtros e cursor | `200`, `items` e `nextCursor` |
| `PATCH /commitments/{id}` | Campos editoriais e `If-Match` | `200`, recurso atualizado e `ETag` |
| `PUT /commitments/{id}/due` | Prazo completo e `If-Match` | `200`, recurso atualizado e `ETag` |
| `PUT /commitments/{id}/completion` | `{ "completed": true }` ou `false`, e `If-Match` | `200`, recurso atualizado e `ETag` |
| `DELETE /commitments/{id}` | ID e `If-Match` | `204` após remoção |

O `PATCH` aceita somente `title`, `description`, `kind` e `subjectId`. Campo omitido permanece igual; `null` limpa apenas campos opcionais. Campos desconhecidos de escrita retornam erro. Prazo e conclusão usam as operações específicas.

Repetir a definição do estado atual, com versão válida, retorna o recurso sem gerar evento novo. Repetir uma exclusão já concluída retorna `204` para uma remoção reconhecida do mesmo proprietário; recursos não reconhecidos continuam retornando `404`.

### Exemplo de criação

```http
POST /api/v1/commitments
Authorization: Bearer <access-token>
Idempotency-Key: 8f392846-6e23-489f-aec9-c5dd17faac8f
Content-Type: application/json
```

```json
{
  "title": "Entregar trabalho de Cálculo",
  "description": "Lista de exercícios 3",
  "kind": "assignment",
  "subjectId": "afc54592-a167-4de7-881b-91b240d13c40",
  "due": {
    "precision": "dateTime",
    "localDate": "2026-11-20",
    "localTime": "14:00:00",
    "timeZone": "America/Sao_Paulo",
    "offset": "-03:00"
  }
}
```

Resposta `201`: os campos aceitos, o prazo normalizado com `instant`, `id`, `status: "pending"`, `version: 1`, `createdAt` e `updatedAt`. Exemplo de `ETag`: `"1"`.

### Consultas e conflitos

Filtros propostos: `fromDate`, `toDate`, `timeZone`, `subjectId`, `kind`, `status`, `cursor` e `limit`. O intervalo é inclusivo no início e exclusivo no fim. Prazos com horário são comparados aos limites no fuso da consulta; prazos somente com data usam sua data civil. A ordenação é por data exibida, horário quando existente e ID; itens sem horário aparecem em seção própria da data. O cursor preserva filtros e ordenação.

Ao receber `412`, o mobile carrega a versão atual, apresenta a mudança e permite reconciliar a edição. Não repete a atualização automaticamente sobre o conteúdo mais recente.

## 7. API do Assistant

| Método e rota | Finalidade |
| --- | --- |
| `POST /api/v1/commitment-proposals` | Criar proposta a partir de texto |
| `POST /api/v1/commitment-proposals/{id}/messages` | Responder esclarecimento e gerar nova versão da proposta |
| `DELETE /api/v1/commitment-proposals/{id}` | Descartar proposta do usuário |

As operações de geração aceitam `Idempotency-Key`, com as mesmas regras de repetição propostas para criação. Geração inicial retorna `201`; esclarecimento retorna `200`. Descarte retorna `204`.

Exemplo de solicitação:

```json
{
  "text": "Tenho trabalho de Cálculo dia 20 de novembro às 14h",
  "locale": "pt-BR",
  "timeZone": "America/Sao_Paulo"
}
```

O servidor registra o instante de referência da interpretação. Expressões relativas, como “amanhã”, usam esse instante e o fuso informado. Datas ambíguas, matérias homônimas ou referências como “na próxima aula” exigem esclarecimento quando o contexto autorizado não resolve a intenção com segurança.

Resposta ilustrativa, após esclarecer o ano:

```json
{
  "id": "f96da20b-56c8-4f6d-bc74-f2742870cb8a",
  "version": 1,
  "status": "needsClarification",
  "candidate": {
    "title": "Trabalho de Cálculo",
    "kind": "assignment",
    "subjectId": null,
    "due": {
      "precision": "dateTime",
      "localDate": "2026-11-20",
      "localTime": "14:00:00",
      "timeZone": "America/Sao_Paulo",
      "offset": "-03:00"
    }
  },
  "questions": [
    { "field": "subjectId", "message": "Qual matéria de Cálculo você quer vincular?" }
  ],
  "expiresAt": "2026-11-04T13:00:00Z"
}
```

Estados propostos: `needsClarification`, `readyForReview` e `unsupported`. Campos não reconhecidos ficam ausentes ou nulos conforme o esquema; o Assistant não inventa IDs nem executa instruções contidas em materiais consultados.

Esclarecimentos recebem `{ "text": "Cálculo I", "expectedVersion": 1 }`. Uma versão diferente retorna `409 PROPOSAL_VERSION_CONFLICT`. Proposta expirada retorna `410 PROPOSAL_EXPIRED`; acesso de outro usuário retorna `404`.

O usuário pode corrigir a proposta no formulário do mobile. A confirmação envia somente os campos aceitos pelo Academic para `POST /commitments`. A indisponibilidade do Assistant permite continuar pelo cadastro manual.

Proposta inicial: expirar propostas em 1 hora e limitar texto de entrada a 4.000 caracteres. Esses limites precisam de aprovação. Os contratos de voz, conversas e propostas de cadastro acadêmico constam na seção 18.

## 8. API do Notifications

| Método e rota | Finalidade |
| --- | --- |
| `GET /api/v1/notification-preferences` | Consultar preferências efetivas |
| `PUT /api/v1/notification-preferences` | Substituir preferências do usuário, usando `If-Match` |
| `PUT /api/v1/devices/{installationId}` | Registrar ou atualizar token de entrega da instalação |
| `DELETE /api/v1/devices/{installationId}` | Desvincular instalação do usuário |
| `GET /api/v1/reminder-plans/{commitmentId}` | Consultar situação do agendamento |
| `GET /api/v1/notifications` | Consultar central de notificações com cursor |
| `PUT /api/v1/notifications/{id}/read` | Marcar ou desmarcar leitura com `{ "read": true }` |

Preferências são criadas pelo servidor com valores seguros na primeira consulta, retornando `ETag`. A proposta inicial começa com avisos desativados, até o estudante configurar horários e autorizar notificações no aparelho. Registrar um token não ativa avisos automaticamente.

Exemplo de preferências explícitas:

```json
{
  "enabled": true,
  "leadTimesMinutes": [60, 1440],
  "dateOnlyReminder": {
    "daysBefore": 1,
    "localTime": "09:00:00"
  }
}
```

Antecedências devem ser positivas, únicas e limitadas; proposta: até 3 valores e até 30 dias. `dateOnlyReminder` pode ser `null`, impedindo avisos de prazos sem horário. Esse aviso usa o fuso armazenado no compromisso.

O registro de dispositivo recebe `platform`, `provider` e `pushToken`. Os valores de provedor e formatos de token serão definidos ao escolher a entrega mobile. A instalação fica vinculada ao usuário autenticado; troca de conta deve substituir esse vínculo atomicamente. Tokens não aparecem em listagens nem em logs.

Um plano informa `commitmentId`, `appliedCommitmentVersion`, `state` e os horários previstos. Estados propostos: `scheduled`, `disabled`, `requiresTimePreference`, `noFutureDelivery`, `completed`, `cancelled` e `failed`. Se o evento ainda não chegou, retornar `404 REMINDER_PLAN_NOT_AVAILABLE`; o mobile pode mostrar “Preparando lembretes”.

Mudanças de preferência recalculam os planos futuros no próprio Notifications. Antecedências já ultrapassadas não geram uma sequência retroativa de avisos. Os demais horários futuros continuam válidos.

Falhas de entrega têm nova tentativa e prazo máximo a definir. Aceitação pelo provedor de push registra `acceptedByProvider`; confirmação de leitura é um estado distinto.

## 9. Eventos publicados pelo Academic

O Academic publica eventos quando uma transação altera efetivamente um compromisso. Todos carregam um retrato suficiente para o Notifications atualizar sua projeção sem consultar o banco Academic.

| Tipo | Gatilho |
| --- | --- |
| `academic.commitment.created.v1` | Criação |
| `academic.commitment.updated.v1` | Mudança de título, descrição, tipo ou matéria |
| `academic.commitment.rescheduled.v1` | Mudança de prazo ou fuso |
| `academic.commitment.completed.v1` | Conclusão |
| `academic.commitment.reopened.v1` | Retorno a pendente |
| `academic.commitment.deleted.v1` | Exclusão |

Envelope proposto:

```json
{
  "eventId": "21a4aeb6-dedf-40de-a1d9-bd6e032ae7ac",
  "eventType": "academic.commitment.rescheduled.v1",
  "schemaVersion": 1,
  "occurredAt": "2026-11-04T12:00:00Z",
  "producer": "academic",
  "correlationId": "correlation-example",
  "aggregateType": "commitment",
  "aggregateId": "546142db-3e7c-4a78-871a-dd0dd0b21863",
  "aggregateVersion": 2,
  "data": {
    "userId": "authenticated-user-sub",
    "commitmentId": "546142db-3e7c-4a78-871a-dd0dd0b21863",
    "title": "Entregar trabalho de Cálculo",
    "kind": "assignment",
    "status": "pending",
    "deleted": false,
    "due": {
      "precision": "dateTime",
      "localDate": "2026-11-21",
      "localTime": "14:00:00",
      "timeZone": "America/Sao_Paulo",
      "offset": "-03:00",
      "instant": "2026-11-21T17:00:00Z"
    }
  }
}
```

`aggregateId` deve ser igual a `data.commitmentId`. Os seis tipos usam esse mesmo retrato mínimo; na exclusão, `deleted` é `true` e a versão avança. Descrição, áudio e conteúdo de anotações ficam fora do evento de lembretes.

### Entrega, ordenação e recuperação

- A publicação usa transactional outbox: alteração e evento são gravados na mesma transação do Academic.
- O publicador pode entregar o mesmo evento mais de uma vez. O Notifications registra `eventId` processado e atualiza projeção e agendamentos atomicamente.
- Uma versão igual ou inferior à já aplicada não substitui o estado atual. Exclusões mantêm uma marca de versão para impedir que mensagens antigas recriem lembretes.
- Cada evento contém um retrato completo para esse consumidor. Assim, uma versão mais recente pode ser aplicada mesmo quando chegar antes de uma intermediária; a intermediária posterior será ignorada.
- Ao reagendar, substituir os avisos futuros da versão anterior. Concluir ou excluir cancela avisos pendentes; reabrir recalcula os futuros.
- Antes de enviar, o worker confere se plano e versão continuam ativos. Uma mensagem já aceita pelo provedor pode chegar mesmo após cancelamento; essa janela deve ser documentada e medida.
- A entrega externa de push pode duplicar se o provedor aceitar a mensagem e a resposta se perder. Usar chave estável por aviso quando o provedor permitir; não prometer entrega exatamente uma vez.
- Mensagens inválidas ou com falhas persistentes vão para uma fila de falhas. Reprocessamento mantém os IDs originais e as regras de idempotência.

Transporte AWS, tempo de retenção, política de tentativas, alertas e procedimento de recuperação serão detalhados nas TEC-004, TEC-006 e TEC-008.

## 10. Critérios de revisão do fluxo

Os cenários abaixo orientam a revisão dos contratos e os testes futuros da implementação. Nenhum foi executado nesta etapa documental.

| Cenário | Comportamento esperado |
| --- | --- |
| Repetir criação após queda de rede | Mesma chave retorna o mesmo compromisso |
| Reutilizar chave com outro conteúdo | Conflito explícito; nenhum segundo compromisso |
| Dois usuários editam versões diferentes | Alteração desatualizada recebe `412` |
| Consultar recurso de outra pessoa | `404`, sem exposição dos dados |
| Assistant recebe data ou matéria ambígua | Solicita esclarecimento |
| Proposta é descartada | Nenhum compromisso é criado |
| Compromisso tem somente data | Nenhum horário de vencimento é inventado |
| Evento chega duas vezes | Plano não duplica |
| Versão antiga chega depois da nova | Estado mais recente permanece |
| Compromisso muda antes do envio | Worker descarta aviso pendente obsoleto |
| Compromisso é concluído ou excluído | Avisos futuros são cancelados |
| Notifications fica temporariamente indisponível | Compromisso permanece salvo; evento será processado depois |
| Usuário altera preferências | Planos futuros são recalculados |

## 11. Decisões pendentes

| Decisão | Proposta ou situação | Tarefa relacionada |
| --- | --- | --- |
| Regras e limites do contrato | Revisar campos, enumerações, paginação e limites propostos | TEC-003 |
| Autenticação | Cognito definido; falta configuração por ambiente e escopos | TEC-003 / TEC-004 / TEC-008 |
| Hospedagem e transporte | AWS definida; execução, região e mensageria pendentes | TEC-004 |
| Identidade entre serviços | Mecanismo e permissões pendentes | TEC-004 / TEC-008 |
| Persistência | PostgreSQL e EF Core previstos; separação física e migrações pendentes | TEC-005 |
| Provedor de IA e transcrição | Pendente; contrato independente do fornecedor | TEC-003 / Assistant |
| Provedor de push | Pendente; revisar suporte no Expo e no Android | Mobile / Notifications |
| Outbox e filas | Definir tentativas, retenção e operação da fila de falhas | TEC-006 |
| Observabilidade | Definir métricas, rastreamento e alertas | TEC-008 |
| Relógio | Wear OS definido; validar versão mínima, aparelhos de referência e integração nativa do mobile | Watch / TEC-001 |
| Áudio e dados pessoais | Definir consentimento, retenção e exclusão | Assistant / TEC-008 |

Essas decisões devem ser registradas com data, responsável e justificativa quando aprovadas.

## 12. Entrega da TEC-003

Para aprovar este documento, a equipe deve revisar os campos, validar os exemplos e resolver as decisões que bloqueiam a implementação. Publicar os contratos executáveis OpenAPI dos três serviços e os JSON Schemas dos eventos nos respectivos repositórios, usando exemplos coerentes com este documento.

As seções seguintes especificam os demais módulos. O alvo do relógio é Wear OS; o RM22/Micro OS 10 deixa de orientar os contratos. Não há dependência de compatibilidade com esse modelo para desenvolver o aplicativo do relógio.

Este arquivo é uma proposta de coordenação entre repositórios. A revisão e o merge dos contratos devem seguir as proteções de branch acordadas pela equipe.

## 13. Regras de escrita e esquemas comuns aos módulos

Todas as rotas nas seções seguintes usam `/api/v1`, autenticação e autorização da seção 4. As tabelas indicam o serviço proprietário. Criações por `POST` exigem `Idempotency-Key`, inclusive convites, uploads, mensagens, transcrições e ações do relógio. Aplicam-se as regras da seção 4, com escopo por usuário e operação. O cliente recebe a resposta original em uma repetição válida.

Recursos editáveis incluem `id`, `version`, `createdAt` e `updatedAt`. Consultas individuais e alterações retornam `ETag`. `PUT`, `PATCH` e `DELETE` sobre recursos versionados exigem `If-Match`, salvo ações explicitamente idempotentes de leitura, vínculo de dispositivo e descarte de proposta. Recursos que nunca foram criados não podem ser atualizados por suposição.

Criação retorna `201` com recurso e `Location`; consulta e alteração retornam `200`; remoção retorna `204`. Processamentos assíncronos retornam `202` com ID, estado e `Location` para acompanhamento. Listagens retornam `{ "items": [], "nextCursor": null }`. Ordenação padrão é por `createdAt` decrescente e ID como desempate, salvo regra específica.

Convenção de `PATCH`: propriedades ausentes permanecem iguais; `null` limpa somente campos opcionais; propriedades desconhecidas são rejeitadas. IDs relacionados devem pertencer ao contexto autorizado. Nomes iguais não implicam identidade igual: o servidor retorna candidatos e exige escolha quando necessário.

Limites deste documento são propostas a revisar. Definir no contrato executável comprimentos máximos, limites de paginação e enums fechados de cada escrita. Os códigos funcionais mencionados complementam o formato de erro da seção 4.

## 14. Identidade, onboarding e perfil

### 14.1 Cadastro e sessão

O Cognito controla cadastro, confirmação de e-mail, entrada, recuperação de senha, renovação de sessão e saída. Mobile e Watch usam clientes públicos distintos, sem segredo incorporado ao aplicativo. O adapter de autenticação traduz os erros do provedor em mensagens consistentes.

| Ação do produto | Contrato de comportamento |
| --- | --- |
| Criar conta | Solicitar nome, e-mail e senha conforme política configurada; exigir confirmação do e-mail |
| Confirmar cadastro | Aceitar código válido e permitir reenvio sujeito aos limites do provedor |
| Entrar | Obter sessão válida antes de acessar dados privados |
| Recuperar acesso | Resposta visual genérica, sem confirmar a existência de uma conta |
| Renovar sessão | Usar o mecanismo do provedor; falha definitiva encerra sessão local |
| Sair | Desvincular dispositivo de notificações, encerrar sessão e limpar cache privado |

Não criar uma segunda base de senhas nos microsserviços. O registro de perfil no Academic é criado uma única vez após a primeira autenticação válida. A tentativa simultânea de inicialização deve retornar o mesmo perfil.

### 14.2 Perfil no Academic

| Rota | Entrada ou saída |
| --- | --- |
| `GET /me` | Perfil e preferências gerais; inicializa perfil quando ainda não existe |
| `PATCH /me` | `displayName`, `theme`, `locale`, `timeZone` |
| `PUT /me/avatar` | `{ "fileId": "..." }`, apontando para upload autorizado, validado e do próprio usuário |
| `DELETE /me/avatar` | Remove a imagem e usa avatar padrão |
| `GET /me/onboarding` | Estado e progresso do onboarding |
| `PUT /me/onboarding` | `{ "completedSteps": ["profile", "subjects", "professors", "preferences"] }` |

`displayName`: 1–100 caracteres; `theme`: `system`, `light` ou `dark`; `locale` inicial: `pt-BR`; `timeZone` deve ser um identificador IANA válido. O e-mail exibido vem da identidade validada, sem permitir alterá-lo por `PATCH /me`. Alteração de e-mail precisa do fluxo de verificação do provedor.

Etapas opcionais do onboarding podem ser puladas. Cadastro por voz usa propostas da seção 18; o indicador de onboarding não cria matérias ou professores. Ao abrir em outro dispositivo, o aplicativo recupera esse progresso do servidor.

Avisos são configurados no Notifications. O Academic não mantém uma cópia editável dessas preferências. Histórico de conversas e metadados de áudio ficam no Assistant.

## 15. Matérias e professores — Academic

### 15.1 Matérias

Esquema: `name` (1–120 caracteres), `code` (opcional, até 40), `colorToken` e `iconKey` (chaves do catálogo do design), `professorIds` (IDs autorizados) e `archived` (booleano). O usuário é proprietário do cadastro; matérias de pessoas diferentes têm identidades distintas.

| Rota | Comportamento |
| --- | --- |
| `POST /subjects` | Criar matéria ativa |
| `GET /subjects?archived=false&query=...` | Listar por nome e ID, com paginação |
| `GET /subjects/{id}` | Consultar detalhes e professores vinculados |
| `PATCH /subjects/{id}` | Editar nome, código e identidade visual |
| `PUT /subjects/{id}/archive` | `{ "archived": true }` ou `false` |
| `PUT /subjects/{id}/professors` | Substituir vínculos por `{ "professorIds": [] }` |

Arquivar preserva grade, notas e compromissos históricos. A matéria deixa de aparecer em seleções ativas e não aceita novos vínculos até ser reativada. Vínculos existentes continuam consultáveis. Atualizar a lista de professores valida todos os IDs antes de salvar; falha em um deles rejeita a alteração inteira.

Nomes duplicados são permitidos, pois turmas podem ter nomes iguais. Busca e proposta por IA apresentam opções distinguíveis pelo código e pelo professor. A API não combina registros automaticamente.

### 15.2 Professores

Esquema: `name` (1–120 caracteres), `email` (opcional, válido, até 254), `office` (opcional, até 160) e `archived`. Nome e contato são dados cadastrados pelo estudante; o registro não cria uma conta para o professor e não envia mensagens.

| Rota | Comportamento |
| --- | --- |
| `POST /professors` | Criar professor |
| `GET /professors?archived=false&query=...` | Listar por nome e ID |
| `GET /professors/{id}` | Consultar registro autorizado |
| `PATCH /professors/{id}` | Editar nome e contatos |
| `PUT /professors/{id}/archive` | Arquivar ou reativar |

Arquivamento preserva vínculos históricos. Remover um vínculo da matéria não remove o professor. Não existe busca pública global de contatos privados de professores.

## 16. Grade de aulas, recorrência e agenda — Academic

### 16.1 Série de aulas

Esquema de série: `subjectId`, `title`, `timeZone`, `startDate`, `endDate` inclusivo, `weekdays` (inteiros ISO: segunda=1 a domingo=7), `localStartTime`, `localEndTime`, `location` opcional e `active`. A primeira versão aceita repetição semanal; regras arbitrárias ficam fora do enum aceito.

Exemplo:

```json
{
  "subjectId": "afc54592-a167-4de7-881b-91b240d13c40",
  "title": "Aula de Cálculo I",
  "timeZone": "America/Sao_Paulo",
  "startDate": "2027-02-01",
  "endDate": "2027-06-30",
  "weekdays": [1, 3],
  "localStartTime": "19:00:00",
  "localEndTime": "20:40:00",
  "location": "Sala 12"
}
```

Datas inicial e final são obrigatórias e a duração proposta da série é de até 366 dias. Na v1, a aula termina no mesmo dia e depois do início. Ocorrências em horários inválidos ou ambíguos no fuso exigem resolução explícita antes de confirmar a série; retornar as datas afetadas em `422 RECURRENCE_TIME_AMBIGUOUS`.

| Rota | Comportamento |
| --- | --- |
| `POST /class-series` | Criar série semanal |
| `GET /class-series?subjectId=...&active=true` | Listar séries |
| `GET /class-series/{id}` | Consultar regra e versão |
| `PATCH /class-series/{id}` | Alterar a série inteira e recalcular ocorrências futuras |
| `DELETE /class-series/{id}` | Cancelar a série; preservar histórico anterior |
| `PUT /class-series/{id}/exceptions/{originalDate}` | Alterar ou cancelar somente uma ocorrência |
| `DELETE /class-series/{id}/exceptions/{originalDate}` | Remover exceção e restaurar a regra dessa ocorrência |
| `POST /class-conflict-checks` | Simular regra e retornar conflitos antes de salvar |

Exceção recebe `action: "cancel"` ou `"override"`; neste último caso, recebe `localDate`, horários, fuso e local completos. O identificador estável da ocorrência é `(seriesId, originalDate)`, mesmo quando ela muda de data. Alterações de série e exceções exigem `If-Match` da série, cujo número de versão avança em ambas.

Editar a série aplica mudanças às ocorrências ainda não iniciadas. Histórico anterior é preservado. Exceções futuras permanecem somente se a data original ainda pertencer à nova regra; caso contrário, retornar `409 EXCEPTIONS_REQUIRE_REVIEW` com a lista para o usuário confirmar remoção em nova solicitação. Uma opção explícita `discardExceptionDates` permite removê-las na mesma transação.

Conflitos usam intervalos `[início, fim)`, portanto aulas consecutivas não se sobrepõem. Salvar um conflito exige `acceptConflicts: true`; caso contrário, retornar `409 SCHEDULE_CONFLICT` com as ocorrências afetadas. A consulta de simulação não substitui a validação no momento da gravação.

### 16.2 Agenda e Home

| Rota | Resultado |
| --- | --- |
| `GET /agenda?fromDate=...&toDate=...&timeZone=...` | Ocorrências de aulas e compromissos no intervalo |
| `GET /home?date=...&timeZone=...` | Resumo do dia, próximos prazos pendentes e atividades atrasadas |
| `GET /commitments/next?timeZone=...` | Próximo compromisso pendente com prazo futuro, ou `204` |

Agenda exige intervalo de no máximo 31 dias; `toDate` é exclusivo. Cada item informa `itemType` (`classOccurrence` ou `commitment`), identificador estável, título, matéria e dados temporais. Filtros e cursor preservam a ordenação definida na seção 6. O resumo informa `generatedAt`, permitindo exibir atualização e cache.

Prazos somente com data são considerados atrasados após o fim da respectiva data no fuso armazenado. Eles não são elegíveis para o endpoint `next`, que exige um instante; aparecem no resumo como itens sem horário. Aulas recorrentes são consultadas na agenda e não são convertidas em compromissos persistidos.

## 17. Anotações, grupos e materiais — Academic

### 17.1 Anotações pessoais

Esquema: `title` (1–160), `content` (texto Markdown, até 100 mil caracteres), `subjectId` opcional e `fileIds` (até 10 arquivos próprios já aprovados). HTML incorporado e links renderizados precisam de tratamento seguro no cliente.

| Rota | Comportamento |
| --- | --- |
| `POST /notes` | Criar anotação pessoal |
| `GET /notes?subjectId=...&query=...` | Pesquisar título e conteúdo autorizados, por atualização decrescente |
| `GET /notes/{id}` | Consultar anotação |
| `PATCH /notes/{id}` | Alterar campos; conflito de versão exige reconciliação |
| `DELETE /notes/{id}` | Excluir e revogar seus compartilhamentos na mesma transação |

Compartilhar uma anotação não transfere sua propriedade. Anexos e conteúdo ficam acessíveis somente enquanto existir permissão válida. A remoção de um anexo do texto não apaga arquivos usados em outros recursos; a eliminação de arquivos órfãos segue política de retenção.

### 17.2 Grupos e participantes

Grupo: `name` (1–120), `description` opcional (até 2.000), proprietário, membros e estado ativo. Papéis: `owner`, `admin` e `member`. Toda pessoa autenticada pode criar um grupo; apenas membros ativos podem ler seu conteúdo.

| Rota | Comportamento |
| --- | --- |
| `POST /groups` | Criar grupo com solicitante como único proprietário |
| `GET /groups` | Listar grupos de que o usuário participa |
| `GET /groups/{id}` | Consultar grupo |
| `PATCH /groups/{id}` | Proprietário ou administrador altera nome e descrição |
| `GET /groups/{id}/members` | Listar participantes, nome de exibição e papel; sem divulgar contatos privados |
| `PUT /groups/{id}/members/{memberId}/role` | Proprietário define `admin` ou `member` |
| `POST /groups/{id}/ownership-transfers` | Proprietário transfere posse a membro ativo, com versão do grupo |
| `DELETE /groups/{id}/members/{memberId}` | Remover participante conforme permissões |
| `DELETE /groups/{id}` | Proprietário encerra grupo após confirmação no app |

O proprietário pode remover qualquer outro participante; um administrador pode remover membros comuns; cada participante pode remover a própria associação. O proprietário precisa transferir a posse ou encerrar o grupo antes de sair. A transferência é atômica e deixa exatamente um proprietário; o anterior passa a administrador.

Remover participação revoga acesso a conteúdo, buscas, downloads e avisos futuros do grupo. Tokens de convite ou URLs de arquivo já expirados não podem reabrir acesso. Revogação não apaga cópias que alguém já tenha baixado.

### 17.3 Convites

| Rota | Comportamento |
| --- | --- |
| `POST /groups/{id}/invitations` | Owner/admin cria convite de uso único, com validade proposta de 7 dias |
| `DELETE /groups/{id}/invitations/{invitationId}` | Owner/admin revoga convite pendente |
| `POST /group-invitation-previews` | `{ "token": "..." }`; retorna somente nome do grupo e validade |
| `POST /group-invitation-acceptances` | `{ "token": "..." }`; adiciona usuário autenticado ao grupo |

O token aleatório é armazenado como hash no servidor e não entra em logs. A resposta de criação devolve o link uma única vez; o usuário escolhe como compartilhá-lo. Não há envio de e-mail automático neste contrato. Estados: `pending`, `accepted`, `revoked` e `expired`. Consumo e associação são atômicos. Aceitação repetida pela mesma pessoa retorna a associação original; outra pessoa recebe `409 INVITATION_ALREADY_USED`. Token inválido ou expirado usa resposta genérica `410 INVITATION_UNAVAILABLE`.

### 17.4 Materiais e compartilhamentos

| Rota | Comportamento |
| --- | --- |
| `POST /groups/{id}/materials` | Membro compartilha anotação própria ou arquivo próprio, com `kind`, `resourceId` e `title` |
| `GET /groups/{id}/materials` | Listar materiais acessíveis |
| `GET /groups/{id}/materials/{materialId}` | Consultar conteúdo permitido e metadados |
| `DELETE /groups/{id}/materials/{materialId}` | Autor ou owner/admin revoga o compartilhamento |

`kind` aceita `note` ou `file`. Um compartilhamento de nota aponta para a versão atual e informa sua versão; alterações posteriores ficam visíveis aos membros. A interface precisa comunicar essa regra ao compartilhar. Membros leitores não ganham permissão de editar a nota original. Arquivos finalizados são imutáveis; substituição cria outro arquivo e outro vínculo.

Um mesmo recurso não pode ser publicado duas vezes no mesmo grupo sem revogar o vínculo anterior (`409 MATERIAL_ALREADY_SHARED`). Ao remover um membro, seus materiais compartilhados nesse grupo são revogados na mesma transação; recursos pessoais permanecem com o autor.

### 17.5 Uploads e downloads

O Academic controla metadados e permissões de avatar e materiais; o S3 armazena bytes privados.

| Rota | Comportamento |
| --- | --- |
| `POST /files/uploads` | Recebe `purpose`, `fileName`, `contentType`, `sizeBytes` e `sha256`; devolve `fileId`, URL assinada e validade |
| `POST /files/{id}/completion` | Verifica tamanho/hash e inicia validação; retorna `202` |
| `GET /files/{id}` | Metadados e estado `pendingUpload`, `validating`, `ready`, `rejected` ou `expired` |
| `POST /files/{id}/download-links` | Valida acesso atual e devolve URL assinada de duração proposta de 60 segundos |
| `DELETE /files/{id}` | Proprietário remove arquivo sem vínculos; retorna `409 FILE_IN_USE` se houver vínculos |

Proposta inicial: avatar JPEG/PNG até 5 MB; material PDF/JPEG/PNG até 20 MB. Validar o conteúdo real, não apenas a extensão. Arquivo só pode ser anexado quando estiver `ready`; uploads incompletos expiram em 24 horas. URLs assinadas não aparecem em listagens ou logs. Revogação bloqueia novas URLs; URLs emitidas podem funcionar até seu curto prazo de expiração.

## 18. Conversas, voz e propostas de onboarding — Assistant

### 18.1 Conversas e consultas

| Rota | Comportamento |
| --- | --- |
| `POST /conversations` | Criar conversa privada, com título opcional |
| `GET /conversations` | Listar conversas por atualização decrescente |
| `GET /conversations/{id}/messages?cursor=...` | Histórico paginado, em ordem cronológica estável |
| `POST /conversations/{id}/messages` | Receber texto e `expectedVersion`; retornar `202` com `operationId` |
| `GET /operations/{operationId}` | Estado e resultado da geração |
| `DELETE /conversations/{id}` | Solicitar remoção da conversa e dos artefatos associados |

Mensagem de entrada: `text` até 4.000 caracteres, `locale`, `timeZone` e, opcionalmente, `transcriptionId`. O servidor associa a mensagem ao usuário e à conversa autorizados; uma transcrição só é usada pelo seu proprietário.

Operação: `queued`, `processing`, `succeeded`, `failed` ou `cancelled`. Resultado de sucesso contém `messageId`, `text`, `references`, `dataAsOf` e `proposalId` opcional. Consultas como “quais entregas tenho esta semana?” retornam referências a recursos do Academic, com ID e tipo; nunca links internos arbitrários produzidos pela IA.

O Assistant consulta APIs autorizadas do Academic no contexto do usuário. Falta de contexto ou falha do Academic deve aparecer na resposta estruturada como `contextUnavailable`; resultados de cache informam sua data. Instruções para alterar registros sempre geram proposta e confirmação explícita.

Admitir uma geração ativa por conversa na primeira versão. Concorrência retorna `409 CONVERSATION_BUSY`; repetição da mesma chave de idempotência retorna a operação original. Cancelar ou excluir conversa impede novas gravações do resultado; tratamento dos dados já enviados ao provedor segue sua política e o contrato de retenção.

### 18.2 Captura e transcrição de voz

O mobile e o relógio solicitam permissão ao iniciar a primeira captura. A gravação tem indicação visível, ação de cancelar e limite proposto de 60 segundos. Negar permissão mantém a entrada por texto disponível.

| Rota | Comportamento |
| --- | --- |
| `POST /audio-uploads` | `contentType`, `sizeBytes`, `durationSeconds`, `sha256`, `locale`; devolve upload privado temporário e `audioId` |
| `POST /transcriptions` | `{ "audioId": "...", "locale": "pt-BR" }`; retorna `202` e ID |
| `GET /transcriptions/{id}` | Estado, texto quando disponível e `expiresAt` |
| `DELETE /transcriptions/{id}` | Cancelar trabalho pendente e solicitar remoção dos artefatos |

Formatos propostos: AAC em M4A (`audio/mp4`) e PCM WAV (`audio/wav`), mono, máximo de 10 MB e 60 segundos. O servidor inspeciona conteúdo e duração, converte pelo adapter e rejeita mídia inválida. A escolha de transcritor deve suportar esses formatos via adapter, preservando o contrato dos clientes.

Estados: `queued`, `processing`, `succeeded`, `failed` e `cancelled`. Falhas funcionais: `UNSUPPORTED_AUDIO`, `AUDIO_TOO_LONG`, `NO_SPEECH_DETECTED`, `TRANSCRIPTION_UNAVAILABLE`. Não interpretar áudio parcialmente enviado. Expiração do upload exige nova URL; repetição da transcrição usa a chave original.

Proposta de retenção: upload incompleto por até 1 hora; áudio até 24 horas após estado terminal; transcrição sem conversa por até 24 horas. Texto incorporado a uma conversa segue a retenção do histórico, configurada e comunicada ao usuário. Solicitações de remoção devem incluir armazenamento e provedor quando houver suporte contratual; documentar limites do fornecedor antes de produção.

A transcrição retorna texto para revisão. Somente após essa revisão o cliente solicita uma proposta. Se o reconhecimento de fala do sistema do relógio já retornar texto, usar diretamente a API textual e informar `inputSource: "wearSystemSpeech"`; o Nomi não recebe nem armazena o áudio nesse caminho. A interface deve informar que o reconhecimento do sistema pode ter processamento próprio.

### 18.3 Cadastro de matérias e professores por voz ou texto

| Rota | Comportamento |
| --- | --- |
| `POST /academic-setup-proposals` | Receber texto/transcrição e retornar proposta em lote |
| `POST /academic-setup-proposals/{id}/messages` | Esclarecer ambiguidades, com `expectedVersion` |
| `DELETE /academic-setup-proposals/{id}` | Descartar proposta |
| `POST /academic-setup-imports` no **Academic** | Persistir lote confirmado de matérias, professores e vínculos |

Proposta do Assistant: `id`, `version`, `status`, `questions`, `expiresAt`, `subjects` e `professors`. Cada item novo possui `clientRef` exclusivo dentro do lote. Um vínculo usa `professorClientRefs` para professor novo ou `professorIds` para existente. Candidatos existentes devem ser escolhidos explicitamente.

O lote confirmado enviado ao Academic contém somente os campos aceitos nos cadastros, referências locais e IDs existentes. Limite proposto: 20 matérias e 20 professores por lote. Validação e gravação são atômicas: um erro rejeita o lote completo e identifica o caminho, como `subjects[1].name`. A resposta devolve mapas `clientRef → id`, permitindo atualizar o onboarding. Idempotência impede duplicar todo o lote após falha de rede.

O texto “Cálculo com a professora Ana e Física com Bruno” deve resultar em dois cadastros de matéria, dois professores e vínculos revisáveis. Nenhum cadastro é criado antes da confirmação.

## 19. Notificações, leitura e adiamento

Complemento da seção 8: registro de instalação recebe `platform: "android" | "ios" | "wearOs"`, `provider`, `pushToken` quando aplicável, `appVersion`, `capabilities` e `deliveryMode`. As capabilities aceitas na v1 são `displayReminder`, `snooze` e `captureVoice`; o servidor não trata essa declaração como concessão de permissões.

`deliveryMode` pode ser `phoneOnly`, `phoneWithBridge` ou `watchDirect`. O usuário escolhe a política; na primeira vinculação, propor `phoneWithBridge` e informar o alcance. Ao habilitar envio direto ao relógio, desabilitar espelhamento da categoria correspondente para reduzir duplicação. A aceitação pelo provedor permanece distinta da exibição.

| Rota | Comportamento |
| --- | --- |
| `POST /reminders/{reminderId}/snoozes` | `{ "delayMinutes": 10, "expectedPlanVersion": 2 }`; criar um único novo horário |
| `GET /notifications?read=false&cursor=...` | Filtrar central de avisos |
| `POST /notifications/read-receipts` | `{ "notificationIds": [] }`, até 100 IDs do próprio usuário; operação idempotente |
| `DELETE /devices/{installationId}` | Desvincular entrega e invalidar ações futuras da instalação |

Adiamento aceita proposta de 5, 10, 15 ou 30 minutos. `reminderId` identifica um aviso específico; retorno inclui ID do adiamento, horário e versão do plano. Repetição da chave retorna o mesmo resultado. Versão obsoleta, compromisso concluído/excluído ou adiamento após expiração retorna `409 REMINDER_NO_LONGER_ACTIONABLE`; o cliente atualiza o estado. A ação não altera o prazo acadêmico.

Notification payload mínimo: `notificationId`, `reminderId`, `commitmentId`, `planVersion`, `category`, `title`, `scheduledFor`, `expiresAt` e `correlationId`. Links internos são gerados pelo aplicativo a partir de IDs validados. Exibir texto em tela bloqueada depende da preferência de privacidade do usuário e do sistema.

Para cada aviso, o Notifications controla destinos e um ID lógico comum. Celular e relógio usam esse ID para substituir uma atualização da mesma notificação. Ações repetidas vindas dos dois dispositivos passam pelas mesmas regras de idempotência e versão.

## 20. Wear OS — contrato do nomi-watch

### 20.1 Plataforma e comunicação

O alvo é um aplicativo nativo Kotlin com Jetpack Compose para Wear OS. O mobile continua React Native + Expo + TypeScript. O módulo de integração Android do mobile precisará de código nativo e de build próprio; a escolha do Expo não garante a disponibilidade do Data Layer em um cliente genérico de desenvolvimento.

O relógio acessa as mesmas APIs HTTPS dos serviços usando sua identidade autenticada. O Data Layer serve para coordenação e troca de pequenos estados entre os aplicativos Android e Wear OS. Não é usado como proxy das requisições de rede do relógio. Essa decisão segue a [orientação oficial sobre rede no Wear OS](https://developer.android.com/training/wearables/data/network-communication).

O Data Layer depende de um celular Android compatível e do app correspondente. Por isso, o produto deve oferecer acesso autenticado à nuvem sem exigir esse canal para toda consulta. O comportamento sem celular e a versão mínima de Wear OS devem ser validados nos aparelhos de referência. [Documentação do Data Layer](https://developer.android.com/training/wearables/data/data-items).

### 20.2 Autenticação e conta

Usar cliente Cognito próprio para Watch e fluxo OAuth de autorização com PKCE compatível com a experiência do relógio, podendo iniciar a autorização no celular. Cada instalação mantém sua sessão e renovação de forma segura. Não incorporar segredo de cliente, senha ou credencial administrativa no app.

Credential Manager é uma opção recomendada pela plataforma; sua integração com a configuração de identidade escolhida deve ser validada. O compartilhamento de autenticação pelo Data Layer pode ser um facilitador, mas exige alternativa de login. Não declarar o suporte do Cognito a um fluxo de dispositivo específico sem validá-lo. [Métodos de autenticação do Wear OS](https://developer.android.com/training/wearables/apps/auth-wear).

Após login, registrar a instalação no Notifications, indicar a conta ativa e carregar dados autorizados. Troca de conta limpa cache e fila local antes de exibir o novo perfil. Sair no celular pode solicitar saída do relógio pelo canal de coordenação; indisponibilidade desse canal deve ficar visível. Expiração ou revogação de sessão é tratada também nas APIs, sem depender da entrega dessa mensagem.

### 20.3 Operações no relógio

| Experiência | Contrato utilizado |
| --- | --- |
| Ver próximo compromisso | `GET Academic /commitments/next` |
| Consultar dia | `GET Academic /home` ou `/agenda` com intervalo limitado |
| Receber aviso | Payload e destino de Notifications, seção 19 |
| Adiar aviso | `POST Notifications /reminders/{id}/snoozes` |
| Capturar voz | Reconhecimento do sistema ou upload/transcrição do Assistant |
| Revisar comando | `POST Assistant /commitment-proposals`; mostrar resumo e ambiguidades |
| Confirmar compromisso | `POST Academic /commitments` com chave estável |
| Continuar no celular | Mensagem de handoff com ID de proposta e conta correspondente |

O reconhecimento por voz depende dos recursos e permissões do aparelho. Não assumir palavra de ativação permanente. Comando curto pode ser confirmado no relógio quando todos os campos forem legíveis e válidos; proposta longa ou ambígua pode continuar no celular. Cancelar ou perder a conexão não significa que um compromisso foi salvo. [Entrada por voz no Wear OS](https://developer.android.com/training/wearables/user-input/voice).

### 20.4 Mensagens locais entre celular e relógio

Protocolo Nomi v1, com campos `protocolVersion`, `messageId`, `type`, `sentAt`, `expiresAt`, `accountSubject` e `payload`. O receptor valida identidade do aplicativo, conta ativa, versão e expiração antes de agir. `messageId` permite ignorar repetição. Proposta: validade de 5 minutos para ações e limite de 32 KB por mensagem; áudio usa upload HTTPS.

| Caminho lógico | Conteúdo e regra |
| --- | --- |
| `/nomi/v1/capabilities` | Versão do app e recursos disponíveis; negocia capacidades comuns |
| `/nomi/v1/refresh` | Sugestão de atualizar dados; cada cliente consulta a API autorizada |
| `/nomi/v1/handoff` | `proposalId` e destino de tela; celular busca a proposta na API antes de mostrar |
| `/nomi/v1/handoff-ack` | `messageId` original e estado `received` ou `opened` |
| `/nomi/v1/signout` | Solicita limpar dados locais da conta correspondente |

O canal não transmite refresh tokens nesse protocolo inicial. Um handoff recebido não confirma criação de compromisso. O cliente que cria o compromisso usa a chave vinculada à intenção original, compartilhada pelo payload de handoff; ambos recebem o mesmo resultado se tentarem confirmar. A chave é preservada enquanto o usuário apenas corrige a mesma intenção antes da gravação; após conflito de conteúdo ou criação conhecida, uma alteração exige operação explícita sobre o recurso salvo.

O Assistant disponibiliza `GET /commitment-proposals/{id}` para esse handoff, verificando proprietário e expiração. A proposta informa `confirmationKey`, criada uma única vez e mantida em suas revisões. Essa chave é aceita pelo Academic como uma chave de idempotência comum, sem conferir privilégios adicionais. Se conteúdos diferentes forem confirmados simultaneamente, um vence e o outro recebe conflito; o cliente consulta o resultado antes de oferecer nova edição.

### 20.5 Cache, reconexão e bateria

Cache de consultas informa `fetchedAt` e mantém dados separados por conta. A interface identifica informação desatualizada. Quando não houver rede, o relógio pode mostrar o cache, guardar um rascunho local e tentar novamente após reconexão; gravação no servidor exige confirmação e resultado explícitos.

Proposta de adiamento offline fica como `pendingSync`; só mostrar sucesso após resposta do Notifications. Se a versão do plano mudou nesse intervalo, o servidor rejeita a ação e o relógio informa a situação atual. Expiração da chave de idempotência ou resultado desconhecido exige reconciliação antes de nova tentativa.

Atualizações ocorrem ao abrir a tela, por eventos de invalidação e por tarefas agendadas compatíveis com as restrições do sistema. Evitar polling contínuo. Tiles e complicações, se incluídos no produto, usam o mesmo cache e permissões; conteúdo privado deve respeitar estado de bloqueio e preferências do usuário.

## 21. Catálogo de integração e critérios de fechamento

### 21.1 Eventos complementares

Além dos seis eventos da seção 9, ficam definidos os seguintes eventos de integração. Cada um usa o mesmo envelope, versão de agregado e regras de entrega da seção 9, com `aggregateType` obrigatório em todos os envelopes, inclusive os de compromisso. Para compromissos, seu valor é `commitment`.

| Tipo | Retrato mínimo em `data` | Consumidor e efeito |
| --- | --- | --- |
| `academic.group.membership-changed.v1` | `groupId`, `membershipId`, `userId`, `role`, `active` | Notifications cancela destinos de grupo quando participação é removida |
| `academic.group.material-shared.v1` | `groupId`, `materialId`, `authorUserId`, `title`, `active: true` | Notifications prepara aviso aos membros elegíveis |
| `academic.group.material-revoked.v1` | Mesmos identificadores, `active: false` | Notifications invalida avisos pendentes desse material |
| `academic.group.closed.v1` | `groupId`, `active: false` | Notifications cancela avisos futuros do grupo |

Eventos de participação usam `aggregateType: "groupMembership"`, ID da associação e versão própria; materiais usam `groupMaterial` e versão própria; encerramento usa `group`. O Notifications aplica cada fluxo de versão separadamente. A marca de grupo encerrado impede reativar entrega por eventos tardios.

Na primeira versão, eventos de material não carregam a lista privada de participantes. O Notifications consulta `GET Academic /internal/groups/{id}/notification-recipients?cursor=...`, com identidade de serviço de escopo restrito. A resposta contém somente IDs elegíveis e papel; não inclui e-mails. Somente após persistir a lista de destinatários e os registros de entrega o consumidor conclui o evento. Cada entrega é única por `(eventId, userId)`.

Antes de enviar um aviso de grupo, validar acesso atual por `POST Academic /internal/group-notification-authorizations`, com `groupId`, `materialId` e até 100 `userIds`. O resultado informa somente elegibilidade por ID. Se o Academic estiver indisponível, adiar envio; não usar autorização antiga para expor conteúdo revogado. O push usa texto genérico e o aplicativo carrega o conteúdo após nova autorização.

As rotas `/internal` não ficam disponíveis para clientes mobile ou Watch. Autenticação de serviço é obrigatória; cabeçalhos enviados por um cliente comum não concedem acesso interno.

Consultas de resumo do dia não geram eventos. Nome, cor e professor de matéria são resolvidos pelo Academic nas consultas; o Notifications não precisa replicar esses cadastros para enviar lembretes. Alterações de grade não geram lembretes de aula automaticamente nesta v1: são exibidas na agenda; incluir avisos de aula exige uma decisão explícita de produto e evento versionado.

### 21.2 Matriz adicional de cenários de revisão

| Cenário | Resultado esperado |
| --- | --- |
| Cadastro de matérias por voz falha em um campo | Lote inteiro rejeitado, sem cadastros parciais |
| Matéria é arquivada | Histórico permanece e novos vínculos são bloqueados |
| Professor é desvinculado | Cadastro do professor é preservado |
| Série sofre alteração com exceções incompatíveis | Conflito explícito e lista das exceções a revisar |
| Aula muda de data | Identidade da ocorrência continua ligada à data original |
| Membro sai ou é removido | Acesso e compartilhamentos próprios no grupo são revogados |
| Convite é usado simultaneamente por duas pessoas | Uma aceitação; a outra recebe conflito |
| Upload não foi validado | Anexo não pode ser publicado nem usado como avatar |
| Nota compartilhada muda | Leitores autorizados recebem a versão atual identificada |
| Evento de material chega após fechamento do grupo | Nenhum aviso novo é entregue |
| Voz não detecta fala ou permissão é negada | Erro recuperável e alternativa por texto |
| Watch e mobile confirmam a mesma proposta | Uma criação ou conflito de conteúdo explícito, sem duplicação automática |
| Celular fica indisponível | Relógio consulta nuvem com sessão própria, quando houver rede |
| Ação offline fica desatualizada | Servidor rejeita versão e cliente reconcilia |
| Conta é trocada no relógio | Cache, fila e tokens anteriores deixam de ser utilizados |

### 21.3 Artefatos necessários para implementar

| Repositório | Artefatos previstos |
| --- | --- |
| `nomi-academic` | OpenAPI das seções 6 e 14–17; importação de onboarding; APIs internas; schemas dos eventos publicados |
| `nomi-assistant` | OpenAPI de propostas, conversas, operações, áudio e transcrição; schemas dos resultados estruturados da IA |
| `nomi-notifications` | OpenAPI de preferências, dispositivos, planos, central e adiamento; schemas consumidos e política de reprocessamento |
| `nomi-mobile` | Cliente tipado dos contratos, adapter Cognito e protocolo de integração Android/Watch |
| `nomi-watch` | Cliente Kotlin dos contratos, autenticação, cache e mensagens Nomi v1 |

Este documento registra a definição documental aprovada do sistema. Não há implementação, alteração de infraestrutura ou teste de runtime concluído por este arquivo.

## Referências do projeto

- Documentos locais `docs/architecture.md` dos repositórios Academic, Assistant, Notifications e Mobile.
- [Quadro de desenvolvimento](https://github.com/orgs/nomi-corporate/projects/1).
- [Protótipo Nomi no Figma](https://www.figma.com/design/VdmGhvLilFgMFDro8qt3GM?node-id=7-6).
