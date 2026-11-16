# Preparação do ERP Imobiliário para EasyPanel

**Estado:** artefatos locais preparados; nenhum serviço, domínio, volume, banco, usuário ou configuração remota foi criado ou alterado.

## Conteúdo e fronteiras

- O fragmento `erp-compose.fragment.yml` é **aditivo** ao projeto `business-core-g3`; não o use como substituto da configuração existente. Ele não redefine `core-api`, `core-identity`, `postgres-g3` ou `g3-coordinator`.
- `erp-web` é o produto e `erp-postgres` é seu banco independente. O PostgreSQL do Core nunca é usado pelo ERP.
- A rede `erp-private` conecta somente a aplicação e o banco do ERP e é marcada `internal`. O banco não publica porta e não se conecta à rede Core.
- `core-integration` deve referenciar uma rede já existente no EasyPanel. Seu nome e sua capacidade de resolver os endpoints HTTPS reais ainda precisam ser confirmados no momento operacional.
- O runtime recebe somente `ERP_RUNTIME_CONNECTION_STRING`. A credencial de bootstrap do PostgreSQL serve à inicialização do cluster e não é passada à aplicação. A credencial de migration deve ser diferente de ambas e permanecer fora do serviço web.
- O volume `business-core-g3-erp-postgres-data` contém dados do ERP; `business-core-g3-erp-data-protection` guarda as chaves de proteção de cookie do ERP. As chaves são cifradas pelo certificado PFX externo; volume, certificado e password pertencem exclusivamente ao ERP. Nunca compartilhe chaves com o Core. Faça backup protegido de volume e certificado e inclua o banco em ensaio de restauração.

## Build local

O projeto web declara `net10.0`; os pacotes de autenticação estão na linha `10.0.x`. O Dockerfile usa as imagens oficiais fixadas por digest: SDK `10.0.404` (`mcr.microsoft.com/dotnet/sdk@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb`) e ASP.NET Runtime `10.0.14` (`mcr.microsoft.com/dotnet/aspnet@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee`). Compila apenas `backend/`, publica em Release e executa como UID `1654`. Os digests foram inspecionados localmente; qualquer atualização de patch exige revisão e atualização explícita desses pins.

```powershell
docker build --tag araia/erp-imobiliario:local-20261116 .
docker image inspect araia/erp-imobiliario:local-20261116 --format '{{.Id}} {{json .RepoDigests}}'
```

Use o digest da imagem construída no registro privado autorizado, se houver, para preencher `ERP_IMAGE` no EasyPanel. Não publique a imagem nesta etapa. O digest final só existirá após publicação futura no registro.

## Variáveis para o serviço

Defina os valores no gerenciador de secrets/configuração do EasyPanel. `erp.env.example` contém apenas nomes e placeholders e não é um arquivo de segredos.

| Variável | Uso |
| --- | --- |
| `ERP_IMAGE` | Referência imutável da imagem ERP publicada, preferencialmente `registro/repositório@sha256:...`. |
| `ERP_POSTGRES_IMAGE` | Imagem oficial PostgreSQL 17 fixada por digest; o template traz o digest da imagem local inspecionada. |
| `ERP_DB_NAME` | Nome exclusivo do banco ERP, igual ao usado na connection string de runtime. |
| `ERP_BOOTSTRAP_USER`, `ERP_BOOTSTRAP_PASSWORD` | Credencial administrativa inicial do cluster ERP, mantida apenas no serviço PostgreSQL e no cofre operacional. |
| `ERP_RUNTIME_CONNECTION_STRING` | Connection string `erp_runtime`, com `Host=erp-postgres;Port=5432`, sem superuser, owner ou `BYPASSRLS`. |
| `CORE_BASE_URL` | Origem/base HTTPS confiável da API Core, sem userinfo ou query string. O código rejeita HTTP. |
| `CORE_CA_FILE` | Caminho opcional para CA privada montada como arquivo somente leitura; vazio quando a cadeia pública já é confiável. |
| `OIDC_AUTHORITY`, `OIDC_AUDIENCE` | Issuer HTTPS do provedor central e audience da API Core, exatamente conforme o contrato publicado. |
| `OIDC_CLIENT_ID`, `OIDC_CLIENT_SECRET` | Cliente OIDC específico do ERP. O secret só deve ser fornecido se exigido pelo método de autenticação do cliente no Core. |
| `OIDC_CA_FILE` | Caminho opcional da CA privada do issuer OIDC, montada somente para leitura. |
| `ERP_DP_CERT_HOST_PATH`, `ERP_DP_CERT_PASSWORD` | Arquivo PFX e password fora da imagem. O PFX deve conter certificado atual com chave RSA privada de ao menos 2048 bits; o arquivo é montado somente para leitura. Produção falha ao iniciar sem ambos. |
| `CORE_NETWORK_NAME` | Nome exato de uma rede EasyPanel existente que permita alcançar Core/identidade por TLS confiável. Não inventar nem criar o nome na implantação. |
| `FORWARDED_HEADERS_KNOWN_PROXY_0`, `FORWARDED_HEADERS_ALLOWED_HOST_0` | IP exato do proxy que conecta ao ERP e hostname público autorizado. Configuração obrigatória no fragmento; valores devem ser observados/confirmados no ambiente. |
| `ERP_WEB_MEMORY_LIMIT`, `ERP_WEB_CPU_LIMIT`, `ERP_POSTGRES_MEMORY_LIMIT`, `ERP_POSTGRES_CPU_LIMIT` | Limites ajustáveis. Defaults iniciais: web 768 MiB/0,75 CPU; PostgreSQL 1536 MiB/1 CPU. Confirmar a capacidade/uso real da VPS antes de ativar. |

O PostgreSQL oficial usa `POSTGRES_USER` como superuser inicial. Essa credencial não é a credencial do ERP. Antes das migrations, um operador deve preparar a role proprietária/DDL e a role `erp_runtime` de acordo com as migrations e os grants existentes, com `NOSUPERUSER NOBYPASSRLS`; não conceder grants para contornar falhas. O código da aplicação não contém comando de migration e não as executa na inicialização.

## OIDC, Core e acesso integrado

O login permanece no provedor central. Não criar contas ou senhas locais nem compartilhar cookies entre serviços. Registrar no Core um cliente OIDC próprio para o ERP segundo o contrato vigente. O callback padrão do middleware é `https://<domínio-erp>/signin-oidc`; configure o redirect URI exatamente, junto do issuer e audience oficiais. O ERP solicita `openid` e `profile`, usa code flow com PKCE e encaminha o token autenticado às APIs do Core. Licença, membership, organização, filial ativa, perfil e direitos efetivos continuam sendo resolvidos/validados pelo Core e pelo backend do produto; publicar a imagem não concede acesso.

O serviço usa o caminho interno HTTP `8080`; o domínio deve terminar TLS no proxy EasyPanel. O ASP.NET precisa receber o esquema/host originais para montar o callback OIDC. O pipeline local foi preparado para processar somente `X-Forwarded-Proto` e `X-Forwarded-Host` quando recebe IPs exatos em `ForwardedHeaders:KnownProxies` e hosts DNS autorizados em `ForwardedHeaders:AllowedHosts`, com limite de um proxy. O proxy deve descartar valores recebidos do cliente e escrever seus próprios cabeçalhos. Não use `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` como atalho: identifique o peer real e configure o IP exato. Sem essa evidência, deixe a configuração não habilitada e não libere o login.

Também é necessário que `CORE_BASE_URL` e `OIDC_AUTHORITY` sejam alcançáveis da rede do serviço por HTTPS, com certificado válido para o host e issuer idêntico ao claim `iss`. Não substitua a URL pública por um alias Docker HTTP. Se o Core só estiver disponível por HTTP interno, ou se issuer/CA/rede não forem compatíveis, a integração permanece bloqueada; não desative validação TLS.

## Banco e procedimento de migration

1. Confirmar que o alvo é um banco **novo e exclusivo do ERP**, não `postgres-g3`, e que o nome retornado por `current_database()` coincide com `ERP_DB_NAME`. Para um banco já existente, obter backup verificado e consultar `real_estate.schema_versions` antes de qualquer alteração; nunca assumir schema vazio nem reaplicar a cadeia inteira.
2. Provisionar separadamente uma credencial de migration com capacidade de assumir a role DDL do ERP e a credencial de runtime `erp_runtime`. O serviço web recebe apenas runtime. A role DDL não deve ser superuser nem `BYPASSRLS`; o bootstrap administrativo deve ser usado apenas para criar banco/roles com privilégios mínimos e retirado do processo cotidiano.
3. Aplicar em ordem lexicográfica somente arquivos oficiais `database/migrations/001_*.sql` a `032_*.sql`, em sessão controlada como `erp_ddl`, interrompendo no primeiro erro. Cada migration registra sua versão em `real_estate.schema_versions`; conferir a versão após cada aplicação. Em PowerShell, para um banco novo já provisionado, a forma de invocação por arquivo é:

   ```powershell
   Get-ChildItem .\database\migrations\*.sql | Sort-Object Name | ForEach-Object {
       psql 'service=erp_migrator' -X -v ON_ERROR_STOP=1 -c 'SET ROLE erp_ddl' -f $_.FullName
       if ($LASTEXITCODE -ne 0) { throw "Migration interrompida: $($_.Name)" }
   }
   ```

   O serviço `erp_migrator` do `pg_service.conf` e seu `.pgpass` devem ser protegidos fora do repositório. A role de conexão deve poder assumir `erp_ddl`. Em alvo existente, adapte o conjunto de arquivos somente após comparar o ledger com um prefixo sequencial; se houver lacuna, versão acima de 032, divergência ou erro, pare e faça plano específico. O comando acima não deve ser executado contra banco persistente sem backup/restore testado e gate operacional específico.

4. Após a aplicação, confirmar versões 1–32, ownership/grants, RLS e FORCE RLS e autenticação real de `erp_runtime`; não corrigir permissões manualmente. Só então iniciar `erp-web` com `ERP_RUNTIME_CONNECTION_STRING`.

Nenhuma migration será aplicada por Dockerfile, entrypoint, healthcheck ou reinício da aplicação.

## Saúde, domínio e recursos

- Healthcheck HTTP para EasyPanel: `GET /health/live`, porta interna `8080`, caminho `/health/live`; resposta `200` comprova somente que o processo atende. Ainda não existe endpoint de readiness que valide PostgreSQL/Core.
- Configure o domínio no ingress do EasyPanel com TLS válido; encaminhe para `erp-web:8080`, sem publicar `8080` no host. Verifique callback e caminhos de login após confirmar trusted proxy, issuer e reachability.
- Em `Production`, o ERP persiste chaves no volume dedicado e exige certificado PFX externo + senha para cifrá-las. Prepare cópia protegida/recuperável do volume e do certificado. Rotação/substituição do certificado deve preservar a capacidade de descriptografar chaves existentes; não substitua o PFX sem um plano de migração/rollback.
- Os limites no fragmento são defaults de partida, não uma garantia de capacidade. A VPS conhecida tem 2 vCPU e cerca de 8 GiB no painel; Core e serviços existentes já consomem recursos. Confirmar memória/CPU no momento da operação e reduzir limites se a folga não cobrir Core, sistema, cache de PostgreSQL e backups. Não alterar limites do Core para acomodar o ERP sem decisão.

## Ordem futura de implantação e reversão

1. Identificar a versão do ERP a implantar, revisar diff/artefato e gerar imagem; registrar SHA-256/digest e manter a imagem anterior disponível.
2. Confirmar rede existente, DNS/TLS, IP do proxy, hosts permitidos, issuer/audience, cliente OIDC, CA e segredos no EasyPanel. Nenhum desses valores é presumido por este template.
3. Fazer backup do alvo e testar recuperação; confirmar que o banco é exclusivamente ERP. Criar roles mínimas separadas; aplicar apenas migrations pendentes pelo procedimento acima.
4. Adicionar o fragmento ao projeto existente, preencher configuração/segredos e criar o domínio no proxy sem alterar os quatro serviços Core.
5. Verificar `/health/live`, autenticação real única, organização/filial, licença, perfil e direitos. Executar a homologação funcional autorizada; healthcheck e build não substituem esse gate.
6. Se o app falhar, retirar/desabilitar somente `erp-web` do roteamento, voltar a referência da imagem para o digest anterior e preservar `erp-postgres`/volumes. Não remover banco/volumes como rollback de aplicação.
7. Se uma migration persistente precisar de reversão, não faça downgrade automático: interrompa a aplicação, preserve backup/WAL e restaure em procedimento controlado conforme o plano aprovado. Migrations versionadas não são desfeitas por trocar a imagem.

## Estado e limites desta preparação

**Preparado:** Dockerfile multi-stage, contexto restrito de build, fragmento aditivo, isolamento de rede/banco, variáveis esperadas, persistência e cifragem das chaves de proteção, configuração explícita de proxy confiável e procedimento de migration/rollback.

**A validar localmente nesta tarefa:** build, imagem e `/health/live`, sem PostgreSQL.

**Pendente da etapa operacional:** digest da imagem ERP publicada, domínio/TLS, rede EasyPanel existente, IP real do proxy, reachability TLS do Core/IdP, cadastro do cliente OIDC, segredos, backup/restore, provisionamento/migrations do banco ERP, rights/licença efetivos do usuário e homologação funcional. Nada disso foi alterado aqui.
