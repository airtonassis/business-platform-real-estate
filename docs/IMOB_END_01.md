# IMOB-END-01 — persistência estruturada e coexistência v1/v2

**Continuidade CAD-1C (07/11/2026):** editor de Localização reorganizado segundo AraiA v1, estado do ponto e continuidade da etapa após salvar; modelo/contratos/migrations de END-01 preservados. Fonte vigente de requisitos, evidências, dependências de mapa e homologação: [IMOB_CADASTRO v1.12](IMOB_CADASTRO.md#cad-1c--localização-v112). O histórico abaixo não implica reinício ou nova autorização de schema.

Homologação assistida em andamento: [roteiro executável e registro humano](IMOB_END_01_HOMOLOGACAO.md). Abertura do ERP, cadastro/edição/reabertura urbano e cadastro/reabertura rural parcial confirmados pelo proprietário; demais itens e aceite final permanecem separados.

**Estado vigente — continuação autorizada e implementada localmente:** a solicitação posterior do responsável e a atualização Monday **5702606753** aprovaram a seção 4. Migration 003, contratos v2, editor e provas estão concluídos no escopo abaixo. As seções 1–5 preservam o histórico da primeira etapa e não representam bloqueio atual de schema/contrato. [Seção 6](#6-continuação-autorizada--entrega-técnica) é o relatório vigente. Homologação humana/produção permanece separada; nenhum commit, push ou deploy.

06/11/2026 · Branch `imob-end-01-address`, derivada do estado local UI-1 sem commit/reset. [Tarefa Monday](https://airtonrassiss-team.monday.com/boards/18437841554/pulses/13490660141), atualização **5702503739**, consultada somente em leitura. Autorização funcional não é autorização de schema/contrato: a tarefa e a solicitação exigem parar essa parte e solicitar aprovação específica.

## 1. Inventário dirigido e impedimento demonstrado

| Camada/cadastro existente | Endereço efetivamente implementado | Efeito no incremento |
| --- | --- | --- |
| Domain `PropertyInput` | Address string livre, PostalCode string, Latitude/Longitude decimal?; sem município/UF/IBGE/partes estruturadas | É também entrada pública HTTP e dados serializados de Property; modificar a forma afeta API |
| Domain `PropertyRules` | Endereço obrigatório até 500, CEP obrigatório ASCII de 8 dígitos com hífen opcional na entrada; coordenadas obrigatórias nos intervalos | A obrigatoriedade atual não equivale ao requisito novo de Draft incompleto |
| Web `Program.cs` | POST/GET `/api/v1/properties`, GET/PUT por ID; UpdateProperty(Data,Version); OwnerInput somente Name | Não introduzir silenciosamente campos públicos ou mudar semântica de nulos |
| Web `Pages/Property.cshtml`/PageModel | Um formulário criação/consulta/edição, Input.Address/PostalCode/Latitude/Longitude; required e Version; UI-1 ativa | Não exibir campos estruturados sem round trip durável |
| Infrastructure `PostgresPropertyStore` | SQL explícito; strings via GetString e coordenadas via GetDecimal; escrita exige `.Value` | Apenas retirar required da UI causaria falha/perda de dados; precisa reader/writer compatível com nulos |
| SQL `001_properties.sql` | address NOT NULL e comprimento 1–500; postal_code NOT NULL e regex 8 dígitos; latitude/longitude NOT NULL; nenhuma coluna IBGE/UF/município/logradouro/número/complemento/bairro/país/fonte | Necessidade real de migration aditiva para estrutura; relaxamento controlado para Draft incompleto; migration antiga deve permanecer imutável |
| Proprietário | Owner(Id,Name), OwnerInput(Name), tabela owners sem endereço; formulário apenas nome | Nenhum endereço de proprietário a padronizar já existente. Adicionar requer modelo/schema/contrato e regra de acesso próprios; não criar cadastro fictício |

Não existem outros cadastros de endereço executáveis demonstrados no ERP examinado. Fonte arquitetural aplicável: P1.1/P1.3, P2.3 e P3.1 na matriz IMOB-ARQ; endereço, ponto e perímetro continuam distintos. Nenhum documento de módulo futuro foi promovido a implementação.

## 2. Implementado dentro do limite atual

- `backend/src/RealEstate.Domain/AddressRules.cs`: regra local reutilizável para CEP (normalização ASCII, sem inferência de existência postal) e par de coordenadas. Permite ausência somente quando chamada explicitamente em modo opcional; par parcial ou valores fora de faixa é inválido.
- `backend/src/RealEstate.Domain/Property.cs`: reaproveita essas regras no consumidor atual **em modo obrigatório**, preservando ordem/códigos de erro, payload e comportamento existente. CEP com hífen continua salvo em oito dígitos. CEP genérico é sintaticamente aceito; nem CEP terminado em 000 nem qualquer código bem formado é apresentado como validado nos Correios.
- `backend/src/RealEstate.Web/Pages/Property.cshtml`: mantém IDs/names/required/Version e UI-1; acrescenta autocomplete e descrição acessível de preenchimento manual, referência de acesso rural/S/N e CEP genérico, sem consulta externa. S/N permanece texto no endereço livre, não coluna/boolean persistido.
- `tests/RealEstate.Tests/AddressRulesTests.cs`: positivos/negativos de CEP, códigos genéricos, zeros, caracteres não ASCII, formato/whitespace, ausência opcional versus modo legado, limites/par de coordenadas.
- `tests/RealEstate.Tests/UiPresentationTests.cs`: prova de campos legados/autocomplete/aria-describedby e ausência de integração externa na página.

O helper não é um contrato HTTP novo, consulta de CEP ou value object completo de endereço. Não foi criado DTO paralelo sem consumidor. A estrutura completa e rascunhos incompletos **não estão implementados na jornada persistida**, pois dependem da autorização abaixo. Nenhuma flexibilização disfarçada de validação foi aplicada ao v1 existente.

## 3. Proposta concreta do padrão completo — NÃO IMPLEMENTADA

Objeto comum candidato `Address` para o domínio ERP, reutilizado por cadastros que realmente recebam endereço. Campo ausente não deve ser inventado para satisfazer schema. Um único editor/validador compartilhado deve alimentar o mesmo objeto usado no caso de uso e persistência, com adaptações por etapa, não motor duplicado por cadastro.

| Campo candidato | Regra proposta |
| --- | --- |
| postalCode | string? normalizada, oito dígitos ASCII quando informada; genérico permitido; não confundir validade de formato com existência |
| municipalityIbgeCode | string? com sete dígitos; sintaxe isolada não prova município. Validação de existência e consistência com UF/nome depende de catálogo local versionado ou fonte autorizada |
| state / municipality | UF normalizada e nome municipal preservado; conferir contra catálogo quando disponível; desconhecido diverge de inválido |
| street / number / withoutNumber | Logradouro e número como texto (não inteiro); `withoutNumber=true` representa S/N e não pode coexistir com número contraditório; rural admite referência de acesso sem logradouro urbano |
| complement / neighborhood / country | Texto limitado por contrato; país explícito, sem inferir localização de dados legados; ausência permitida no Draft conforme checklist aprovado |
| latitude / longitude / coordinateSource | Par opcional, origem opcional; ausente permanece null e nunca 0,0 fabricado. Ponto não vira perímetro ou prova de domínio |
| origin / sourceVersion / checkedAt | Proveniência da sugestão/verificação quando houver; entrada manual não é classificada como validada por provedor |

Rascunho incompleto: validar formato dos campos presentes, guardar pendências separadas, não exigir CEP/logradouro/número/ponto inexistentes; liberação futura exige checklist da operação, sem iniciar D1. Não presumir que ausência de CEP inviabiliza imóvel rural ou que CEP genérico identifica ponto/geometria.

### 3.1 CEP, IBGE e Maps sem dependência externa

Preparação contratual, não adaptadores já disponíveis: assistência de CEP recebe CEP válido e devolve **sugestão** com campos/fonte/instante ou indisponível/não encontrado; nunca sobrescreve edição manual sem revisão. Sem fonte configurada, operação permanece manual e explicita “não consultado”, sem botão prometendo serviço existente. Timeout não bloqueia salvar Draft; não guardar logs com endereço pessoal integral sem necessidade.

Validação municipal distingue formato válido, conferido em catálogo e não verificado. Recomenda-se catálogo local versionado de municípios/UF, obtido e validado de fonte oficial em recorte autorizado; não fabricar lista parcial como se fosse nacional, nem chamar código de sete dígitos de município existente. Carregamento/distribuição da base ainda não executados. Portas poderão ser adicionadas quando houver consumidor estruturado aprovado, com testes de ausência/indisponibilidade/divergência; nenhum pacote ou endpoint novo nesta etapa.

Google Maps: adaptador opcional futuro, desligado sem configuração autorizada; nenhum SDK/script/chave/URL de consumo pago no formulário. Contrato futuro separa geocodificar endereço, selecionar ponto e exibir mapa, registrando precisão/fonte/consentimento aplicável; sugestão aceita só por usuário autorizado. Sem Maps, coordenadas manuais ou ausentes continuam possíveis no novo contrato. Não implementar fake map ou usar 0,0 como fallback.

## 4. Autorização específica solicitada — antes da próxima parte

Recomendação para revisão: **nova migration aditiva, DTO estruturado versionado e editor persistente**, sem tocar Core ou ampliar permissões por inferência.

1. **Schema:** nova migration, sem editar 001/002 já existentes; acrescentar campos estruturados nullable a properties (código IBGE/UF/município/logradouro/número/SN/complemento/bairro/país/fonte). Permitir null nos campos legados address/postal_code/latitude/longitude para os novos Draft incompletos, mantendo verificações de formato/faixa quando presentes e par de coordenadas coerente. Ajustar reader/writer explicitamente. Não alterar IDs/organização/estabelecimento/versionamento, RLS/FORCE, ownership ou eventos. Dados legados devem permanecer intactos, sem parsing de endereço livre nem backfill inferido de município/ponto.
2. **Contrato público:** introduzir DTO e rotas **v2** de imóveis com `address` estruturado, parcial em Draft, mantendo os nomes/rotas/payloads v1 de clientes legados. Para preservar a garantia anterior de endereço completo no v1, propor que registros novos incompatíveis com o formato legado sejam acessíveis somente pelo v2 e não pela lista v1; consulta v1 por ID desses registros devolve incompatibilidade identificável (proposta 409 `Property.AddressVersionRequired`), sem expor nulos inesperados. Essa regra de visibilidade/resposta também precisa aprovação explícita; não está vigente. O v1 continua aceitando/escrevendo o formato antigo, sem apagar campos estruturados desconhecidos em atualizações antigas. Testes de coexistência devem fixar essa semântica antes da implementação.
3. **UI:** mover o cadastro existente para o caso de uso estruturado aprovado, reutilizar editor em criação/consulta/edição e preservar o shell UI-1; formulário não deve descartar campos antigos nem normalizar destrutivamente. Comprovar round trip completo e incompleto. Dados legados exibidos como endereço livre preservado até conferência humana; não inventar separação automática.
4. **Proprietários:** não há endereço atual nem permissão de manutenção/leitura completa de proprietário no catálogo. Recomenda-se não ampliar esse cadastro neste delta; o tipo/editor fica reutilizável quando essa capacidade e seus direitos forem aprovados. Caso seja obrigatório incluir endereço de proprietário agora, solicitar separadamente extensão de DTO/schema e ações autorizadas, sem reutilizar OWNER_CREATE como permissão universal.
5. **Banco de teste:** autorizar criação/aplicação da nova migration **somente em PostgreSQL isolado e descartável comprovado**, com schema antigo/dados sintéticos completos, prova de preservação, nulos, constraints, concorrência/versionamento, rollback/auditoria e login restrito. Aprovação de arquivo de migration não autoriza aplicar em serviços atuais/produção. Sem credenciais, APIs pagas, provisionamento compartilhado ou deploy.

As cinco partes acima constituem uma proposta revisável, não implementação iniciada. A alternativa de adicionar campos só na UI/JSON livre sem persistência foi rejeitada por perda de dados; preencher endereço fictício/CEP 00000000/ponto 0,0 para contornar NOT NULL também não é solução. Formato sintaticamente aceito pelo legado não autoriza inventar dado.

## 5. Evidências e pendências

Ambiente Windows/PowerShell local; teste de regras primeiro falhou por ausência de AddressRules (CS0103), depois passou com implementação e regressões. Comando novo:

`dotnet test tests/RealEstate.Tests/RealEstate.Tests.csproj --filter 'FullyQualifiedName!~DatabaseTests' --nologo --logger 'trx;LogFileName=end01-safe.trx' --results-directory TestResults/imob-end-01`

**PASS_EXECUTED:** 38/38, compilação incluída, testes novos de regras e apresentação mais regressões seguras UI-1/CoreAccess de teste. [TRX](../TestResults/imob-end-01/end01-safe.trx). `python -B scripts/verify_ui_assets.py`: sete assets UI-1 pinados íntegros. Não recertifica identidade/licença Core real.

**BLOQUEADO POR DEPENDÊNCIA / DECISÃO HUMANA NECESSÁRIA:** persistência/contrato/editor de endereço completo, IBGE/catalogação, proprietário com endereço e Draft sem localização/endereço/CEP, conforme §4. O pedido para antes dessa parte é exigência expressa da solicitação e da atualização Monday 5702503739, não uma exigência inferida de skill.

**NOT_RUN:** migrations, integração PostgreSQL, consultas Correios/IBGE/Maps, browser/capturas novas e testes da jornada estruturada. Não existe backend novo fingindo disponibilidade de consulta. Não repetida a UI-0 nem homologação UI-1; testes de apresentação foram novos por impacto dos atributos/textos. A última prova UI-1 de navegador continua histórica, não reclassificada como nova.

Nenhuma falha pendente nos testes executados; FAIL inicial é a etapa vermelha do teste, não defeito remanescente. APIs/DTOs v1, stores, migrations, permissões e assets/UI-1 (exceto descrições/atributos de endereço) preservados. Core/CRM/Financeiro/outros produtos e Monday não alterados; sem D1/Connect, commit/push/deploy.

Próxima ação: responsável aprovar ou ajustar especificamente a proposta §4, incluindo semântica de coexistência v1/v2, antes de implementar essa parte. O incremento completo IMOB-END-01 permanece parcial; não é marcado concluído por ter helper/testes verdes.

## 6. Continuação autorizada — entrega técnica

Fonte: [IMOB-END-01 no Monday](https://airtonrassiss-team.monday.com/boards/18437841554/pulses/13490660141), atualização **5702606753**, lida sem alteração. Implementado na mesma branch local `imob-end-01-address`, preservando UI-1 e alterações preexistentes. Nenhum endereço de proprietário, D1, Connect, Core ou outro produto foi implementado/modificado.

### 6.1 Contratos e comportamento implementados

| Superfície | Regra vigente |
| --- | --- |
| GET/POST `/api/v1/properties`; GET/PUT `/api/v1/properties/{id}` | DTOs legados, nomes, formato e validação obrigatória preservados. Lista SQL filtra representabilidade **antes de ORDER BY/LIMIT 100**; não filtra só os 100 já lidos. Sem paginação nova ou ampliação do limite |
| GET v1 por ID parcial | Após direito PROPERTY_READ atual e leitura restrita por Tenant/unidade: 409 `Property.AddressVersionRequired`. Sem direito: 401/403 conforme contexto; invisível por RLS/inexistente: 404. Não revelar existência inter-Tenant por conflito |
| PUT v1 | Mantém contrato UpdateProperty(Data,Version), validação e permissões antigas. SQL atualiza apenas campos conhecidos; campos estruturados novos não são zerados. Uma gravação v1 completa pode tornar novamente representável um registro antes parcial, sem apagar os campos novos |
| GET/POST `/api/v2/properties`; GET/PUT `/api/v2/properties/{id}` | `PropertyInputV2`: code, title, ownerId, category, type, purpose, price, address, establishmentId; PUT usa `{data,version}`. Respostas PropertyV2/WorkspaceV2, status sempre Draft; lista inclui parciais no mesmo limite 100 |
| Address v2 | legacyText, postalCode, municipalityIbgeCode, state, municipality, street, number, withoutNumber, complement, neighborhood, country, latitude, longitude, coordinateSource. Campos opcionais; ausência de address equivale a endereço vazio, sem inventar valores |
| Normalização/validação | CEP informado em oito dígitos, com hífen opcional de entrada; UF entre as 27 siglas, normalizada; IBGE formato ASCII de sete dígitos; país duas letras; número textual e S/N sem contradição; limites por campo. Coordenadas ambas ausentes ou par na faixa. 0,0 só é gravado se fornecido explicitamente, não como fallback |
| Limite de verificação externa | UF é validada localmente; IBGE/CEP validam formato, não existência/correspondência no catálogo oficial. A interface explicita “não consultado”. Nenhuma fonte externa, catálogo incompleto fictício ou geocodificador foi ativado |

Representabilidade v1 significa existência do endereço livre legado, CEP e coordenadas válidos no formato antigo; não certifica completude jurídica/territorial. Um endereço estruturado sem projeção livre continua disponível no v2, sem gerar texto automaticamente para parecer compatível. O limite antigo de 100 registros continua sendo uma lista recente, não paginação cursor completa.

Exemplo mínimo de criação v2 (IDs reais do escopo autorizado devem substituir os placeholders):

```json
{
  "code": "RURAL-01",
  "title": "Rascunho rural",
  "ownerId": "<proprietário autorizado>",
  "category": "Rural",
  "type": "Fazenda",
  "purpose": "Venda",
  "price": 3000,
  "establishmentId": "<unidade autorizada>",
  "address": { "withoutNumber": true }
}
```

O contrato aceita endereço parcial, não remove regras preexistentes de identificação/proprietário/preço/unidade. Nenhum Draft é promovido a imóvel completo ou publicável por estar salvo. Rights existentes PROPERTY_READ/CREATE/UPDATE e OWNER_CREATE permanecem os mesmos; a autorização continua no caso de uso, inclusive fora do HTTP.

### 6.2 Migration e preservação

[003_structured_address.sql](../database/migrations/003_structured_address.sql) acrescenta dez colunas nullable, sem UPDATE/backfill/decomposição: IBGE, UF, município, logradouro, número, S/N, complemento, bairro, país e fonte das coordenadas. Relaxa NOT NULL de address/postal_code/latitude/longitude; os checks antigos de formato/faixa continuam válidos quando há valor. Acrescenta par de coordenadas, S/N coerente, limites dos campos, UF/país/IBGE e regra de endereço parcial restrita a Draft. Não altera RLS/FORCE, policies, owners, privilégios, IDs, estabelecimento, auditoria ou versões dos registros. 001/002 permanecem byte a byte intactas.

Reader v2 lê nulos explicitamente; reader v1 tolera leitura interna para permitir ao serviço devolver o conflito aprovado, sem serializar esse registro como v1. Writer v2 usa parâmetros tipados/DBNull, transação, lock FOR UPDATE, comparação/incremento de Version e evento obrigatório no mesmo commit. Writer v1 conserva colunas desconhecidas. Índices, FK por organização/proprietário, isolamento de unidades e tratamento de duplicidade permanecem.

LegacyText aponta para a coluna address preexistente. Texto legado é mostrado livre, sem parsing; na leitura não se infere UF, município, número ou país. Na atualização v2, omitir LegacyText mantém o texto anterior por COALESCE. Para alterar texto explicitamente, o usuário envia a nova versão; não há exclusão silenciosa. Alterações estruturadas e texto legado podem exigir conferência humana — não são declarados equivalentes automaticamente.

Migration foi aplicada **somente em dois containers PostgreSQL descartáveis deste ensaio**, nunca em serviço persistente/compartilhado. Para uma implantação futura, o aplicativo compatível com v1/v2 e reader nullable deve estar disponível antes de permitir escritas parciais; não tratar binários antigos como preparados para esses dados. Release/produção não estão autorizados por esta entrega.

### 6.3 Editor e arquivos

`Pages/Shared/_AddressEditor.cshtml` é o editor compartilhado usado na mesma página de criação/consulta/edição. Bind prefix `Input.Address.*`; readonly é herdado do fieldset sem escrita. Campos de localização são opcionais e rótulos/hints explicitam formato, entrada manual, legado e ausência de catálogo externo. `Version`, antiforgery e controles de submit da UI-1 permanecem. Lista web usa v2 para não esconder os imóveis parciais do usuário autorizado; nenhuma alteração no shell/assets CSS/JS base.

Criados nesta continuação:

- Domain: `backend/src/RealEstate.Domain/StructuredAddress.cs`.
- Application: `backend/src/RealEstate.Application/PropertyServiceV2.cs`.
- Infrastructure: `backend/src/RealEstate.Infrastructure/PostgresPropertyStoreV2.cs` (partial com transação/auditoria compartilhadas).
- Web: `backend/src/RealEstate.Web/Pages/Shared/_AddressEditor.cshtml`.
- SQL: `database/migrations/003_structured_address.sql`.
- Provas: `scripts/end01_database.py`, `scripts/end01_browser.mjs`, `tests/RealEstate.Tests/StructuredAddressTests.cs`, `AddressApiTests.cs`, `AddressDatabaseTests.cs`, `UiAddressStore.cs`.

Modificados: `PropertyService.cs` (gate v1), `PostgresPropertyStore.cs` (filtro/null reader/partial), `Program.cs` (DI/rotas v2/status409), `Pages/Index.cshtml.cs` (lista v2/mensagens), `Pages/Property.cshtml.cs` e `Property.cshtml` (edição v2/editor), `scripts/ui1_browser.py` (opção `--address-v2`, saída própria sem sobrescrever evidência UI-1), `DatabaseTests.cs` (serialização das provas PostgreSQL) e `UiPresentationTests.cs` (store comum de teste e nomes estruturados). Este relatório foi atualizado, sem documentos paralelos de decisão.

### 6.4 Evidências novas e autocorreção

Ambiente Windows/PowerShell, .NET10, Docker local disponível e imagem **postgres:17-alpine já presente**, sem download. Aplicadas skills de entrega vertical, teste antes da implementação e validação por impacto; não se repetiu inventário UI-0. Primeira execução dos testes estruturados falhou pela ausência da nova regra (CS0103); após implementação passaram.

| Comando | Resultado atual |
| --- | --- |
| `dotnet test tests/RealEstate.Tests/RealEstate.Tests.csproj --filter 'FullyQualifiedName!~DatabaseTests' --nologo --logger 'trx;LogFileName=end01-v2-safe.trx' --results-directory TestResults/imob-end-01-v2` | PASS_EXECUTED: **46/46**, regras/HTTP/editor e regressões seguras; [TRX](../TestResults/imob-end-01-v2/end01-v2-safe.trx) |
| `python -B scripts/end01_database.py --execute` | PASS_EXECUTED: **61/61 .NET** na execução final, incluindo os 46 acima e 15 provas PostgreSQL. Não somar 46+61 como testes distintos. [TRX final](../TestResults/imob-end-01-v2/end01-v2-database.trx) e [resumo/limpeza](../TestResults/imob-end-01-v2/database-summary.json) |
| `python -B scripts/ui1_browser.py --address-v2` | PASS_EXECUTED: **92/92 Edge**, fixture exclusiva em memória, sem rede externa/banco. [Checks](../TestResults/imob-end-01-v2/browser-checks.json) |
| `python -B scripts/verify_ui_assets.py` | PASS_EXECUTED: sete assets AraIa UI-1 continuam íntegros |

O script PostgreSQL primeiro comprovou engine/imagem local; criou somente container com nome/label únicos, storage **tmpfs**, sem bind/volume de dados, porta aleatória **127.0.0.1**, banco e senhas efêmeros. Antes de migration verificou label, mounts, bind loopback e tmpfs. [Recibo de isolamento](../TestResults/imob-end-01-v2/database-isolation.json) não inclui credenciais. Não acessou containers de outros serviços. Finalmente removeu o container pelo ID após conferir ownership e confirmou que não existe mais. A primeira execução comprovou 59 testes; a final incluiu os novos testes HTTP e corrigiu apenas avisos de estilo dos testes, totalizando 61. Ambas limpezas foram confirmadas.

Provas PostgreSQL: snapshot dos campos legados sem alteração após migration e campos novos nulos, constraints por SQL sem depender do formulário, Draft parcial/nulos, round trip de campos estruturados, leitura v1 incompatível e precedência de autorização, isolamento Tenant/unidade, filtro v1 antes de limite com **105 parciais mais recentes**, preservação das colunas v2 em update v1, texto legado omitido conservado no v2, corrida de duas escritas com um vencedor, auditoria transacional/rollback e login runtime real NOSUPERUSER/NOBYPASSRLS. Fixtures privilegiadas usadas somente para DDL/seed/falha controlada de grant no banco descartável; não equivalem ao login da aplicação nem a homologação OIDC/Core.

Provas de navegador: nove larguras de UI-1, busca/menu/foco/readonly/erros/conflito, formulário ampliado e reflow equivalente a 200%/400%, além de criação/reabertura rural S/N sem CEP/logradouro/ponto, IBGE/UF persistidos na fixture e GET v1 com409. [Rural parcial mobile](../TestResults/imob-end-01-v2/partial-rural-390.png), [formulário desktop](../TestResults/imob-end-01-v2/form-1440.png), [formulário mobile](../TestResults/imob-end-01-v2/form-390.png). Captura rural foi inspecionada nesta rodada. Persistência real é provada separadamente nos testes PostgreSQL, não pelo store em memória do browser.

Autocorreção: o cenário de navegador antigo ainda procurava `Input.Address` plano; foi adaptado em **novo cenário versionado** ao prefixo `Input.Address.*`, sem modificar o histórico UI-1 nem esconder uma falha de produto. Dois avisos xUnit do teste de concorrência foram resolvidos sem mudança funcional. Revisão independente somente leitura não identificou achado bloqueante nos contratos, migration, autorização, persistência ou editor. Testes finais verdes; nenhuma falha de produto remanescente demonstrada.

**NOT_RUN:** OIDC/Keycloak/licença real, produção, deploy, cadastro de proprietário com endereço, consulta oficial de CEP/IBGE, Google Maps/geocodificação, leitor de tela/homologação humana completa e zoom nativo de navegador (reflow equivalente foi executado). Não se presume que código de município ou CEP sintaticamente válido existe ou corresponde à UF informada. Integração futura com catálogo oficial exige recorte próprio; nenhuma API paga ou chave foi utilizada.

**Pendência real:** homologação humana do endereço completo/parcial e do convívio v1/v2; operação futura em banco persistente exige autorização própria de implantação. Não há mais decisão bloqueante para o código/schema deste recorte já autorizado. Não marcar aceite/produção automaticamente nem alterar o Monday. Próximo passo: responsável revisar contrato/capturas/evidências e homologar o incremento local; D1 e Connect continuam fora.


### Referencia de evolucao CAD-1A (07/11/2026)

A fonte vigente do rascunho minimo e dos contratos nullable/PATCH v2 e [IMOB_CADASTRO.md](IMOB_CADASTRO.md), secao CAD-1A. A migration 005 sucede 001-004, sem reescreve-las; v1 permanece estrita. Registros sem campos comerciais tambem sao filtrados antes de LIMIT em v1; consulta incompatível continua 409 apos autorizacao. Criacoes exigem filial ativa do contexto validada pelo backend, sem fallback para payload. Historico END-01 acima preservado; novos resultados nao promovem homologacao.


**Continuidade de contexto CAD-1A (07/11/2026):** a fonte [IMOB_CADASTRO.md](IMOB_CADASTRO.md), versao 1.8, registra o produtor DCSE-CTX-V1 implementado e a criacao/reabertura real de dois Drafts com organizacao, unidade, criador e versao corretos em bancos privados descartaveis. Homologacao humana pendente. Filial ativa ausente para criacao usa Context.ActiveEstablishmentRequired/409, distinta de falta de permissao/403, autenticacao/401 e indisponibilidade/503. Sem alteracao das migrations ou dos payloads de propriedade neste delta.
