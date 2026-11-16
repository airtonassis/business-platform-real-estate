# IMOB-END-02 — correções de homologação

**Estado vigente: ajustes finais das três ressalvas implementados**, conforme seção “Ajustes finais de homologação” ao fim deste documento. As seções iniciais preservam a entrega anterior: confirmação de CEP e país visível foram substituídos pelo novo comportamento. Nova categoria persistente depende da autorização específica descrita abaixo.

06/11/2026. Implementado e validado localmente; **nova homologação humana pendente**. [Tarefa Monday](https://airtonrassiss-team.monday.com/boards/18437841554/pulses/13493883470). Escopo autorizado: assistência de CEP, prioridade dos campos estruturados, Novo imóvel e link Maps. Nenhum aceite, commit, push ou deploy inferido.

Registro de entrega publicado na tarefa existente: atualização Monday **5703658360**, com arquivos, comandos/evidências, PASS/NOT_RUN e pendências. Nenhum status de aceite alterado.

## Comportamento entregue

- CEP: ao completar oito dígitos, com hífen opcional, consulta HTTPS direta ao [ViaCEP](https://viacep.com.br/). Timeout de cinco segundos, sem retries automáticos, sem cookies/credenciais ou referer. Somente CEP é enviado. Falha de rede, formato/retorno inválido ou CEP não encontrado preservam a entrada manual; botão permite nova tentativa.
- A consulta **sugere** logradouro, bairro, município, UF e IBGE disponíveis. Mostra prévia textual acessível; somente “Confirmar e aplicar sugestão” substitui os campos presentes na resposta. Não altera número, S/N, complemento, coordenadas ou texto legado. Conferência e correção manual permanecem possíveis. CEP encontrado e IBGE sugerido não certificam localização exata nem correspondência oficial do imóvel.
- Mudança posterior de qualquer campo invalida a consulta/sugestão. AbortController, geração e snapshot impedem resposta antiga ou confirmação vencida de sobrescrever digitação posterior. Eventos input/change do mesmo valor não descartam a sugestão nem disparam consulta duplicada.
- Editor prioriza CEP e campos estruturados: logradouro, número/S/N, complemento, bairro, município, UF e IBGE. Texto livre permanece visível para conferência e referência rural, sem decomposição. Nenhuma nova obrigatoriedade de endereço em Draft.
- Consulta/edição oferece **Novo imóvel** somente com leitura autorizada e permissão PROPERTY_CREATE; PROPERTY_UPDATE isolada não libera a ação. O destino mantém o contexto do servidor e as verificações de criação existentes.
- Maps: link externo HTTPS em nova aba com noopener/noreferrer, usando par válido de coordenadas ou endereço/referência livre codificado, conforme [Maps URLs oficial](https://developers.google.com/maps/documentation/urls/get-started). Campos ausentes não viram coordenadas zero. Não há SDK, chave, chamada de API paga, iframe, geocodificação ou seleção simulada. O endereço só é enviado ao Google quando o usuário abre o link.

Não foi criado endpoint público de CEP. CSP conserva scripts locais e adiciona exclusivamente `connect-src 'self' https://viacep.com.br` para essa assistência. API v1/v2, DTOs, domínio, application, writers/readers e migrations não foram alterados. JavaScript é assistência opcional: salvar manualmente continua disponível mesmo sem ele.

## Arquivos deste incremento

| Arquivo | Alteração |
| --- | --- |
| `backend/src/RealEstate.Web/wwwroot/address-editor.js` | Novo módulo reutilizável de consulta/sugestão e Maps |
| `backend/src/RealEstate.Web/Pages/Shared/_AddressEditor.cshtml` | Ordem dos campos, prévia, confirmação, status acessível e avisos |
| `backend/src/RealEstate.Web/Pages/Property.cshtml` | Novo imóvel, link Maps fora do fieldset desabilitado e carga do módulo |
| `backend/src/RealEstate.Web/Pages/Property.cshtml.cs` | Consulta independente da permissão de criação |
| `backend/src/RealEstate.Web/Program.cs` | Restrição CSP para conexão ViaCEP; rotas e contratos preservados |
| `tests/RealEstate.Tests/AddressAssistanceTests.cs` | Novas provas de apresentação e permissões |
| `tests/RealEstate.Tests/UiPresentationTests.cs` | Fixture update-only e nome atualizado da prova de entrada manual |
| `tests/RealEstate.Tests/AddressApiTests.cs` | Nome atualizado da prova de consulta opcional, sem mudar o contrato testado |
| `tests/address-assistance.test.mjs` | Consulta, rede, timeout, respostas antigas e segurança do link |
| `scripts/end02_browser.mjs` | Roteiro Edge dirigido com transporte externo controlado |
| `scripts/ui1_browser.py` | Opção --end02 com diretório próprio; cenários históricos preservados |
| `scripts/end02_persistence.py` | Cadastro/edição/reabertura Razor no PostgreSQL descartável existente |
| `scripts/end01_homologation.py` | Resume do host de teste preservando banco sintético; credencial efêmera restrita renovada somente no container de ownership comprovado |
| `docs/IMOB_END_02.md`, `docs/IMOB_END_01_HOMOLOGACAO.md` | Resultado e encaminhamento da nova conferência |

## Validação e evidências

Ambiente: Windows, .NET 10, Node, Edge isolado, sem Core/OIDC real. Os testes externos simulam somente o transporte CEP; DOM, módulo JavaScript, Razor, antiforgery e fluxos de salvar são reais. O store do navegador é em memória; a prova PostgreSQL é registrada separadamente.

| Comando / prova | Resultado |
| --- | --- |
| `dotnet test tests/RealEstate.Tests/RealEstate.Tests.csproj --filter 'FullyQualifiedName!~DatabaseTests' --no-restore --nologo --logger 'trx;LogFileName=end02-safe.trx' --results-directory TestResults/imob-end-02` | PASS_EXECUTED **50/50**; apresentação/permissões, regras e regressões HTTP v1/v2. [TRX](../TestResults/imob-end-02/end02-safe.trx) |
| `node --test tests/address-assistance.test.mjs` | PASS_EXECUTED **6/6**; normalização, formato inválido, indisponibilidade/retorno inválido, timeout, resposta antiga e Maps |
| `python -B scripts/ui1_browser.py --end02` | PASS_EXECUTED **22/22**; confirmação por Enter, estado anunciado, atraso/edição, offline, CEP ausente, formato CEP/IBGE, Maps, urbano/rural, readonly/update-only, v1/409/v2 e atualização legada preservando campos. [Checks](../TestResults/imob-end-02/browser-checks.json) |
| `python -B scripts/end02_persistence.py` | PASS_EXECUTED **8/8** verificações dirigidas; POST Razor/antiforgery, cadastro urbano/rural, edição e reabertura no runtime PostgreSQL restrito do container tmpfs já identificado. [Prova](../TestResults/imob-end-02/persistence.json) |
| `python -B scripts/verify_ui_assets.py` | PASS_EXECUTED: sete assets UI-1 íntegros |
| Consulta pública sintética `https://viacep.com.br/ws/01001000/json/`, timeout 8s, Origin loopback | PASS_EXECUTED: HTTP 200, Access-Control-Allow-Origin `*`, logradouro/bairro/município/UF/IBGE retornados. Prova pontual, não garantia de disponibilidade futura |
| Comparação SHA256 com recibo END01 | PASS_EXECUTED: seis arquivos de domínio/application/store/migration 003 constantes do recibo sem diferenças. Alcance localizado, não certificado global do repositório |
| Capturas 1440/390 | Produzidas e inspecionadas: [desktop](../TestResults/imob-end-02/editor-desktop.png), [mobile emulado](../TestResults/imob-end-02/editor-mobile.png). Não equivalem a aceite humano |

Autocorreções: primeiro build bloqueado pelo DLL em uso no host de homologação (MSB3027); suspenso somente o processo TEST_ONLY identificado. Os testes novos então demonstraram ausência de prioridade dos campos/Novo imóvel. O primeiro evento Enter da automação não gerava ativação nativa; completado o evento CDP com código virtual e caractere Enter, sem alterar o produto. A prova adicional de blur demonstrou descarte indevido por input/change duplicados; corrigida comparação do estado observado e repetido somente o roteiro afetado. Resultados finais acima sem falhas.

Verificações auxiliares: parser AST dos três helpers Python e `node --check` do módulo e cenário Edge passaram. `git diff --check` apontou whitespace preexistente em `.ai/framework/DEPENDENCY_RULES.md:17`, fora do escopo; arquivo preservado. Grande parte do produto já estava não rastreada, portanto git diff sozinho não comprova o conteúdo completo do incremento.

**NOT_RUN nesta rodada:** suíte PostgreSQL completa de constraints/RLS/concorrência/migrations (escopo persistente inalterado e provas anteriores preservadas), suíte histórica de 92 verificações Edge, OIDC/licença real, leitor de tela, aparelho móvel real, zoom nativo e nova homologação humana. Os 61 testes anteriores não são apresentados como execução nova. Nenhum serviço persistente, outro produto ou infraestrutura compartilhada foi alterado.

## Sessão e pendências

ERP restaurado em http://127.0.0.1:65182, TEST_ONLY, banco tmpfs original preservado. Atualizar a página para carregar a versão corrigida. O roteiro de encerramento continua `python -B scripts/end01_homologation.py stop`; ownership é conferido antes de remover recursos. Perfil de navegador e host de teste dirigidos pela suíte Edge foram encerrados.

**CORRIGIDO:** quatro itens de IMOB-END-02 e defeito reproduzido de eventos duplicados. **DECISÃO HUMANA NECESSÁRIA:** homologar a nova experiência, sobretudo confirmação/correção de CEP, rural parcial e Novo imóvel, além de teclado, zoom nativo e mobile. O FAIL humano genérico anterior fica no histórico; não é convertido automaticamente em PASS. Nenhuma decisão nova de schema ou contrato está pendente.

Serviço público de CEP pode ficar indisponível, bloquear uso massivo ou retornar dados incompletos; entrada manual e CEP genérico continuam operacionais. Link Maps é busca aproximada e expõe os dados escolhidos ao destino quando aberto. Não foi testada navegação ao Google; somente construção e segurança do link, sem consumir API.

## Ajustes finais de homologação

Escopo autorizado posteriormente pelo proprietário: seleção de categorias, CEP automático em campos livres e retirada de País/Sigla. Sem nova migration, API pública, permissão ou mudança em outros produtos.

### Categoria e delta dependente de autorização

Inspeção localizada de código, `database/migrations`, DTOs/rotas e `docs/catalog/real-estate.v1.json`: há somente `properties.category` textual. Não existe tabela de categorias, endpoint de manutenção ou grant de categoria; o catálogo JSON é de recursos/permissões do produto, não um catálogo de categorias imobiliárias.

Implementado select com valores distintos dos imóveis retornados pela consulta autorizada no contexto atual, mais a categoria do próprio imóvel aberto. Nenhuma lista global arbitrária foi inventada. Categorias legadas atuais permanecem selecionáveis, inclusive se o imóvel não estiver entre os 100 primeiros da listagem. POST web recusa valor fora dessas opções, sem alterar o comportamento público das APIs v1/v2. A fixture de apresentação passou a incluir uma categoria Rural em um imóvel sintético para exercitar a seleção; isso não cria catálogo no produto.

**Nova categoria** é uma ação nativa de disclosure (`details/summary`), disponível no editor com escrita, que explica a indisponibilidade da criação persistente. Não simula cadastro nem concede manutenção com PROPERTY_CREATE/UPDATE. O select depende dos imóveis visíveis e do limite atual da listagem; empresa sem categorias visíveis não pode cadastrar a primeira categoria por esta tela. Essa limitação fica explícita, sem recorrer a armazenamento paralelo ou valores livres.

**DECISÃO HUMANA NECESSÁRIA, somente para criação persistente:** autorizar incremento separado com catálogo pertencente à organização (nome/identidade, unicidade, estado ativo e isolamento), leitura e manutenção próprias, migration aditiva se necessária e grant específico de manutenção. Definir como categorias textuais existentes passam a integrar o catálogo sem renomear dados históricos. Definir contrato de manutenção (handler web ou API versionada, conforme decisão) e paginação independente dos imóveis. Este documento é proposta de delta; não aprova nomes de grants, schema nem contrato. Até essa autorização, criação persistente é **BLOQUEADA POR DEPENDÊNCIA**; nenhuma alteração no Core foi feita.

### CEP e país

CEP agora aplica automaticamente somente os campos retornados que estejam vazios e não tenham sido editados manualmente nesta abertura do editor. Não há botão de confirmação. Valores existentes, valores corrigidos e até campos que o usuário apagou permanecem preservados. Após autofill é possível corrigir normalmente. Geração, abort e comparação do estado continuam descartando respostas atrasadas após edição. Mensagens de resultado continuam em região aria-live; retry permanece acessível por teclado. Timeout, operação manual, rural, S/N, CEP genérico e Maps foram preservados.

País/Sigla não é renderizado, nem como campo oculto editável. Novos cadastros brasileiros pela web recebem Country `BR` no servidor. Edições web leem o registro autorizado e preservam o país já persistido, incluindo null legado e país diferente de BR; um Country forjado no POST não substitui esse valor. Não houve atualização em massa, remoção de coluna ou alteração das regras/DTOs da API. Novos cadastros feitos diretamente pela API continuam sujeitos ao contrato existente, sem um novo default global.

### Evidências finais

| Comando/prova | Resultado |
| --- | --- |
| `dotnet test tests/RealEstate.Tests/RealEstate.Tests.csproj --filter 'FullyQualifiedName!~DatabaseTests' --no-restore --nologo --logger 'trx;LogFileName=end02-final-safe.trx' --results-directory TestResults/imob-end-02-final` | PASS_EXECUTED **55/55**, incluindo estado sem categorias, categoria forjada, país novo BR, país legado null/PT e regressões v1/v2. [TRX](../TestResults/imob-end-02-final/end02-final-safe.trx) |
| `node --test tests/address-assistance.test.mjs` | PASS_EXECUTED **7/7**: campos livres, proteção de campos manuais, rede, timeout, atraso e Maps |
| `python -B scripts/ui1_browser.py --end02-final` | PASS_EXECUTED **26/26**, com autofill, correção manual, campo apagado, resposta atrasada, categoria, país ausente/BR persistido, mobile, permissões, v1/409/v2 e preservação legada. [Checks](../TestResults/imob-end-02-final/browser-checks.json) |
| `python -B scripts/end02_persistence.py --final` | PASS_EXECUTED **10/10**, POST Razor urbano/rural e edição/reabertura no PostgreSQL tmpfs existente, BR persistido e preservado contra POST forjado. [Prova](../TestResults/imob-end-02-final/persistence.json) |
| `python -B scripts/verify_ui_assets.py` | PASS_EXECUTED: sete assets UI-1 íntegros |

Testes novos demonstraram primeiro a ausência do autofill seguro e a presença do botão antigo; após a mudança passaram. Nenhuma falha final demonstrada. Diretório de evidências final separado para conservar o histórico anterior. Capturas finais: [desktop](../TestResults/imob-end-02-final/editor-desktop.png) e [mobile emulado](../TestResults/imob-end-02-final/editor-mobile.png). Não constituem homologação humana.

Monday: atualização **5703701867** registrou os ajustes e o delta de catálogo; a rodada final acrescentou dois testes do estado vazio, elevando a execução .NET de 53 para 55 (não somar as rodadas).

Arquivos ajustados nesta continuação: `Pages/Property.cshtml(.cs)`, `Pages/Shared/_AddressEditor.cshtml`, `wwwroot/address-editor.js`, `AddressAssistanceTests.cs`, `UiAddressStore.cs` (somente fixture), `tests/address-assistance.test.mjs`, `scripts/end02_browser.mjs`, `scripts/ui1_browser.py`, `scripts/end02_persistence.py` e este relatório. Sem alterações em Program.cs, domínio, application, persistência ou migrations nesta continuação.

**NOT_RUN:** criação persistente de categoria (não autorizada), nova homologação humana, zoom nativo/aparelho real/leitor de tela, Core/OIDC real e suíte completa de banco/migrations/RLS (camadas inalteradas). ERP TEST_ONLY restaurado no mesmo endereço local, com dados sintéticos preservados; atualizar a página. Sem commit, push ou deploy. Próximo passo: conferir os ajustes e decidir exclusivamente o delta de catálogo, caso deseje habilitar a criação persistente.
