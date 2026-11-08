# HTTPS de desenvolvimento — entrada limitada a health checks

Entrega parcial da TEC-004/TEC-007. O domínio informado pelo responsável é `nomi-dev-equipe.duckdns.org`, no DuckDNS gratuito. Em 08/11/2026, o DNS A resolvia para `18.191.214.104`, confirmado pelo responsável como o IPv4 público atual da EC2. Esse IP pode mudar ao interromper/iniciar a instância; não é um Elastic IP fixo.

## Comportamento e limites

O overlay `compose.https.yaml` adiciona Caddy 2, fixado por digest de imagem oficial, às três redes Docker já existentes. Somente o proxy publica TCP 80/443; as APIs permanecem em loopback nas portas 5101–5103 e os RDS continuam privados. O proxy não recebe connection strings, senhas de banco, certificado cliente do RDS ou token DuckDNS.

HTTP redireciona para HTTPS; o Caddy obtém e renova certificado público automaticamente quando DNS e portas estiverem corretos. `/data` e `/config` ficam em volumes persistentes do projeto `nomi-dev`. O Caddy usa 128 MiB e logs locais limitados; não cria outro servidor, balanceador, NAT Gateway, endereço IP ou serviço AWS. O disco e a transferência continuam sujeitos aos custos do ambiente existente.

Nesta etapa, somente **GET/HEAD** destes caminhos são encaminhados às APIs:

| Caminho externo | Destino interno |
| --- | --- |
| `/academic/health/live` e `/academic/health/ready` | Academic |
| `/assistant/health/live` e `/assistant/health/ready` | Assistant |
| `/notifications/health/live` e `/notifications/health/ready` | Notifications |

Os demais caminhos e métodos retornam 404. A raiz também retorna 404 intencionalmente; não há página inicial pública. Readiness pode revelar disponibilidade do banco, mas não retorna credenciais, endereços ou diagnósticos detalhados. O proxy preserva a resposta 503 de uma API indisponível. HTTPS protege o transporte; **não implementa autenticação ou autorização**, e endpoints de negócio só devem ser liberados junto com essa implementação.

Não há access log HTTP habilitado nem API administrativa Caddy exposta. O protocolo usa HTTP/1.1 e HTTP/2, sem exigir porta UDP 443.

## Pré-requisitos na AWS e no DNS

1. Conferir que o registro A do domínio corresponde ao IPv4 público **atual** da EC2. Remover/corrigir qualquer AAAA que aponte para IPv6 não configurado. Atualizações DuckDNS não são automáticas nesta entrega; após ligar a EC2, conferir/atualizar o registro antes de usar as APIs. Não enviar o token DuckDNS ao chat ou ao GitHub.
2. No grupo de segurança **da EC2**, adicionar HTTP/TCP 80 e HTTPS/TCP 443 com origem `0.0.0.0/0`. Preservar SSH restrito ao IP do responsável. Não abrir as portas 5101–5103 nem alterar os grupos do RDS.
3. Manter saída de rede/DNS/HTTPS da EC2 funcionando para emissão/renovação ACME. O domínio gratuito não torna EC2/RDS gratuitos.
4. As três APIs devem estar saudáveis no Compose antes de ativar o proxy. Certificados precisam ser renovados enquanto o servidor está ligado e acessível; indisponibilidade prolongada pode impedir a renovação.

## Instalação na EC2

Adicionar os arquivos `Caddyfile`, `compose.https.yaml`, `https.env.example` e `https.py` da **mesma revisão aprovada** a `~/nomi/deploy`, ao lado de `compose.py`/`compose.yaml` já utilizados. Não substituir `runtime.env`, `release.env` ou os arquivos de conexão locais.

```bash
cd "$HOME/nomi/deploy"
cp --no-clobber https.env.example https.env
python3 https.py pull
python3 https.py check
```

O helper verifica o domínio, reaproveita a pré-validação da configuração base e executa `caddy validate`. O `check` pode baixar/usar a imagem pública e criar volumes Docker, mas não publica portas nem valida um certificado público. Não precisa de login ECR para a imagem Caddy.

Após validar DNS e as regras de entrada:

```bash
python3 https.py up
python3 https.py status
python3 https.py logs
```

Esses comandos operam **somente o proxy**, com `--no-deps`. Não recriam as APIs nem alteram os bancos. O estado `running` do Caddy não comprova que a emissão do certificado já concluiu; validar pela conexão HTTPS abaixo.

## Validação real

Em um computador externo à EC2, ou no Terminal da própria EC2 com acesso ao DNS público:

```bash
curl --fail --show-error https://nomi-dev-equipe.duckdns.org/academic/health/ready
curl --fail --show-error https://nomi-dev-equipe.duckdns.org/assistant/health/ready
curl --fail --show-error https://nomi-dev-equipe.duckdns.org/notifications/health/ready
```

Os três devem retornar `Healthy`, com certificado aceito pelo cliente sem `-k`/`--insecure`. Se houver falha, conferir logs do proxy, DNS atual e portas 80/443. Não solicitar certificados repetidamente como teste: as autoridades certificadoras aplicam limites de emissão. Um 404 na raiz ou em um endpoint de negócio é esperado.

O helper aceita `--release`, `--runtime` e `--https` para selecionar arquivos locais. `https.env` contém somente o hostname; é ignorado pelo Git. Valores herdados `NOMI_*`/`COMPOSE_*` do shell não substituem silenciosamente essa configuração.

## Operação e recuperação

- Atualizar API: continuar usando `compose.py up assistant` (ou o serviço desejado). Não é necessário recriar o Caddy para trocar uma imagem backend mantendo o serviço/rede.
- Alterar Caddyfile/domínio: validar e recriar somente o proxy com `https.py up`. Preservar arquivos anteriores para restaurar a configuração se necessário.
- Desativar entrada pública: `python3 https.py stop`. As APIs continuam disponíveis pelo loopback na EC2. Isso não interrompe EC2/RDS nem elimina custos de armazenamento.
- Preservar os volumes `nomi-dev_caddy_data` e `nomi-dev_caddy_config`. Não usar `docker compose down --volumes` no ambiente real: isso apagaria o estado de certificados/conta ACME. Chaves TLS são material sensível e exigem proteção no backup.
- O helper base pode avisar sobre o contêiner Caddy como órfão por usar apenas o arquivo das APIs; isso não o remove. Não usar `--remove-orphans` como atalho para silenciar o aviso. Consultar proxy com `https.py status`.

## Testes automatizados

O teste `scripts/Test-Ec2Compose.py` executa a configuração real com APIs reais, PostgreSQL descartável, hostname `localhost`, CA interna efêmera e portas locais dinâmicas. O cliente Python confia explicitamente nessa CA e verifica TLS; não usa `--insecure`, domínio público, token DuckDNS ou emissão ACME pública.

Verifica redirect HTTP→HTTPS, seis health routes, GET/HEAD permitidos, POST e caminhos não publicados bloqueados, header Server removido, resposta 503 preservada e continuidade do certificado/CA após recriar o proxy com volumes persistentes. Mantém também os testes de isolamento das credenciais e de atualização seletiva das APIs. Isso não comprova DNS, regras AWS nem emissão pública; a conclusão depende do teste real após implantação.

Referências: [HTTPS automático do Caddy](https://caddyserver.com/docs/automatic-https), [política de rotas](https://caddyserver.com/docs/caddyfile/directives/handle), [DuckDNS](https://www.duckdns.org/spec.jsp).
