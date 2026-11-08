# Nomi dev na EC2 — Docker Compose

Entrega parcial da [TEC-004](https://github.com/orgs/nomi-corporate/projects/1/views/1?pane=issue&itemId=292993577) e [TEC-007](https://github.com/orgs/nomi-corporate/projects/1/views/1?pane=issue&itemId=292993590). Esta pasta reúne a operação do ambiente compartilhado; não transforma o Academic em orquestrador de negócio nem cria dependência entre as compilações dos serviços. A configuração pode ser extraída para um repositório de infraestrutura futuramente.

## O que já existe e o que este Compose muda

Em 08/11/2026, o responsável validou três APIs Docker na EC2 de Ohio, com readiness saudável e conexão TLS ao RDS por usuários de aplicação separados. O ambiente usa uma EC2 de desenvolvimento e duas instâncias RDS: Academic em uma; Assistant e Notifications em bancos distintos da outra. Repositórios ECR e publicação GitHub Actions/OIDC estão operacionais. Isso não comprova funcionalidades das HUs, HTTPS ou autenticação de usuários.

Este Compose assume a execução dos três contêineres já existentes. Não cria instância, rede AWS, banco, usuário, migration, certificado ou segredo. O `compose.yaml` da raiz continua servindo exclusivamente ao PostgreSQL local do Academic.

| Serviço | Nome do contêiner | Porta na EC2 | Arquivo de conexão |
| --- | --- | --- | --- |
| Academic | `nomi-academic` | `127.0.0.1:5101` | `academic.env` |
| Assistant | `nomi-assistant` | `127.0.0.1:5102` | `assistant.env` |
| Notifications | `nomi-notifications` | `127.0.0.1:5103` | `notifications.env` |

Cada serviço tem rede Docker própria, acesso de saída ao RDS, limite de 384 MiB, logs locais limitados e reinício `unless-stopped`. O certificado é montado readonly. O health check executa HTTP `/health/ready` com Bash já presente nas imagens; não instala curl/wget nem pacotes no servidor. O estado `healthy` indica conexão ao banco, não domínio funcional ou tabelas atualizadas. Docker não reinicia automaticamente um processo só porque ficou `unhealthy`.

## Arquivos

- `compose.yaml`: execução e health checks. Não contém senhas nem builds.
- `release.env`: manifesto versionado dos três digests já publicados e validados na EC2. Sem `latest` ou tags mutáveis.
- `runtime.env.example`: modelo dos caminhos absolutos no host. Copiar para `runtime.env` somente na EC2.
- `compose.py`: validação e comandos operacionais com Python 3.9+. Rejeita imagem sem digest, registry/repositório incorreto, configuração trocada e permissões de arquivos abertas.

Os arquivos `~/nomi/env/*.env` contêm senhas em texto, protegidos por permissões de arquivo. Não são um gerenciador de segredos e não devem ser enviados ao GitHub, em prints ou em logs. O helper nunca imprime `docker compose config`, pois a saída expandida pode conter essas senhas. `logs` mostra os logs das aplicações, que devem continuar sem dados sensíveis.

## Preparação no servidor

1. Manter os dois RDS e a EC2 ligados, com o grupo da EC2 autorizado na porta 5432 dos bancos.
2. Conferir `docker compose version`: requer plugin Compose 2.30+ por causa de `env_file.format: raw`. Esse modo preserva `$` e aspas das connection strings, como no `docker run --env-file` atual. Se o plugin não estiver instalado, instalar a partir da [documentação oficial](https://docs.docker.com/compose/how-tos/install/linux/) antes de parar qualquer contêiner. Python 3 e Docker Engine já são pré-requisitos.
3. Colocar os quatro arquivos de execução (`compose.yaml`, `release.env`, `runtime.env.example`, `compose.py`) da **mesma revisão aprovada** em `~/nomi/deploy`. Não executar arquivos remotos diretamente por pipe. Não é necessário copiar o código-fonte inteiro nem compilar na EC2.
4. Nesse diretório, copiar `runtime.env.example` para `runtime.env` e conferir os caminhos. Credenciais existentes: pasta `~/nomi/env` com permissão 700 e cada `.env` com 600. O certificado público em `~/global-bundle.pem` precisa ser legível pelo usuário não privilegiado do contêiner.

```bash
cd "$HOME/nomi/deploy"
cp --no-clobber runtime.env.example runtime.env
python3 compose.py check
```

O `check` valida arquivos, imagens e sintaxe sem iniciar contêineres. Não comprova senha correta, certificado válido ou alcance do RDS; essas condições são verificadas pelo readiness após o `up`.

Renovar o login no ECR e baixar as imagens antes da troca:

```bash
aws ecr get-login-password --region us-east-2 | docker login --username AWS --password-stdin 338128499585.dkr.ecr.us-east-2.amazonaws.com
python3 compose.py pull
```

O servidor usa sua role IAM de leitura. Não instalar access keys nem enviar as senhas dos migradores para os contêineres.

## Primeira adoção — um serviço por vez

O Compose não adota automaticamente contêineres criados com `docker run`. Os nomes estão ocupados; preservar o contêiner atual parado antes de criar o substituto. Fazer isso somente após `check` e `pull` passarem.

Exemplo Academic:

```bash
docker stop nomi-academic
docker rename nomi-academic nomi-academic-before-compose
python3 compose.py up academic
python3 compose.py status academic
```

Se o nome `nomi-academic-before-compose` já existir, não remover nem sobrescrever: inspecionar qual versão ele contém antes de repetir. O comando `up` aguarda até 120 segundos e falha se o readiness não ficar saudável.

Depois que Academic ficar `healthy`, repetir para Assistant e Notifications, trocando o nome do serviço e do contêiner. Os backups `nomi-assistant-previous` e `nomi-notifications-previous` de uma atualização anterior são diferentes de `*-before-compose`; preservar até decidir sua retenção. Não executar `up` sem selecionar serviço durante a adoção inicial, pois os outros nomes ainda podem estar ocupados.

Se a adoção do Academic falhar, recuperar o contêiner manual:

```bash
docker rm --force nomi-academic
docker rename nomi-academic-before-compose nomi-academic
docker start nomi-academic
```

Esses comandos removem somente a tentativa nova de API; não alteram o RDS. Se o `up` não chegou a criar o novo contêiner, pular a remoção. Confirmar nomes antes de agir. Restaurar o contêiner manual não o coloca sob gerenciamento do Compose.

## Operação normal

Após os três serviços terem sido adotados:

```bash
python3 compose.py status
python3 compose.py up
python3 compose.py logs assistant
python3 compose.py stop
```

`up` sem seleção recria os três serviços e aguarda saúde. `up assistant` recria somente Assistant e recarrega seu arquivo de configuração; os demais continuam em execução. `stop` pausa contêineres, mas **não interrompe a EC2 nem os RDS e não suspende o custo de processamento desses recursos AWS**. Para economizar créditos, interromper EC2/RDS pelo console conforme a rotina da equipe; armazenamento e outros recursos persistentes continuam sujeitos a consumo. O Compose não cria recursos AWS adicionais.

## Atualização e recuperação por imagem

1. Guardar uma cópia do manifesto atual, por exemplo `release-previous.local.env`, fora do Git. Registrar também os digests realmente executados; não assumir que o manifesto local já foi aplicado.
2. Alterar **somente** o digest do serviço em `release.env`, usando o `image_digest_uri` de uma execução bem-sucedida da master do repositório correspondente. Revisar e versionar a mudança. Não usar a imagem de um PR.
3. Executar `check`, renovar login ECR, `pull assistant` e `up assistant`, conforme o serviço. Esse processo tem breve indisponibilidade; não é rolling deploy nem CD automático.
4. Validar `status assistant` e as verificações funcionais da entrega. Um `up` com erro não faz rollback automaticamente.
5. Para restaurar a imagem anterior: `python3 compose.py --release release-previous.local.env up assistant`. O arquivo precisa conter as três referências válidas, mas somente Assistant será recriado. Isso não restaura senhas alteradas, dados ou migrations; recuperar essas partes exige plano próprio.

O helper ignora variáveis `NOMI_*` e `COMPOSE_*` herdadas do shell, para que um `export` antigo não substitua silenciosamente os arquivos selecionados. Não mudar manualmente para outro projeto Compose: o nome operacional é `nomi-dev`.

## Verificação automatizada e limites

`python scripts/Test-Ec2Compose.py` testa a definição com APIs reais e PostgreSQL descartável, sem AWS. Localmente, usa os clones Assistant/Notifications ao lado do Academic; caminhos podem ser informados pelos argumentos do script. No CI, os dois checkouts usam commits fixados. Não baixa as imagens privadas; compila baselines de fonte, portanto não comprova conteúdo de um digest novo do ECR.

Os testes verificam health checks, portas loopback, limite de memória, certificado readonly, configuração exclusiva por serviço, preservação de `$` na senha, rejeição de configuração inválida, falha de readiness e recuperação seletiva sem reiniciar as outras APIs. Recursos recebem nomes únicos e são removidos ao final. Um encerramento abrupto do processo pode exigir limpar apenas recursos com o prefixo da execução de teste.

Ainda faltam HTTPS, autenticação de usuários, segredos gerenciados, deploy remoto automatizado, backups/restauração validados e recuperação de migrations. Esta entrega organiza a operação de desenvolvimento e não conclui as TECs ou HUs funcionais.


## Entrada HTTPS opcional

O [guia HTTPS](HTTPS.md) adiciona Caddy sem recriar as APIs, usando o domínio gratuito de desenvolvimento. Somente health checks GET/HEAD ficam públicos até implementar autenticação. A ativação depende de DNS, regras de entrada da EC2 e emissão de certificado público; a base Compose segue utilizável sem esse overlay.
