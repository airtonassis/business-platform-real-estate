# IMOB-UX-02 — Carteira de Imóveis

Implementação local concluída. **Homologação visual desktop aceita pelo proprietário** com a captura enviada e a declaração “dentro do esperado” (Monday IMOB-UX-02, update 5703759842, 2026-11-06). O aceite cobre a captura desktop e não comprova revisão manual em celular, todos os fluxos, leitor de tela ou conformidade WCAG. Os testes técnicos documentados abaixo permanecem evidência apenas do escopo que executaram. Identidade AraIa e sete assets protegidos da UI-1 preservados. Sem commit, push, deploy, nova migration, permissão ou alteração dos contratos HTTP v1/v2.

Padrões transversais aplicáveis: `ARS-LIST`, `ARS-VIS`, `ARS-FUNC-001`, `ARS-TPL-001` e `ARS-GATE-001` em [DEVELOPMENT_STANDARDS.md do Core](../../business-platform-core/docs/standards/DEVELOPMENT_STANDARDS.md). O manifesto AraIa do Imob continua identificando um snapshot local não publicado, sem contrato de compatibilidade de biblioteca compartilhada.

## Comportamento entregue

- Pesquisa geral por código, título, localização, categoria, tipo, finalidade e situação persistida. Filtros independentes por código, título, cidade, UF, bairro, categoria, tipo, finalidade, situação, faixa de preço e estabelecimento autorizado. Pesquisa textual contém termo, sem remoção de acentos; `%`, `_` e barras são literais, não wildcards enviados pelo usuário.
- Ordenação ascendente/descendente por atualização, código, título, cidade, UF, bairro, categoria, tipo, finalidade, preço e situação; cabeçalhos ordenáveis preservam os filtros e reiniciam na página 1. Preço é ordenado numericamente. Empates são resolvidos por ID para ordem determinística.
- Paginação real de 25 imóveis. Filtros, autorização/RLS e ordenação são aplicados no PostgreSQL **antes de LIMIT/OFFSET**, incluindo contagem filtrada. Não se filtra apenas o subconjunto dos 100 imóveis das APIs existentes. Pesquisa é enviada por “Aplicar filtros”/submit nativo; digitar não oculta silenciosamente registros da página atual.
- Colunas: seleção, foto principal, código/título, cidade/UF, bairro, categoria, tipo, finalidade, preço, situação e ações. Dados ausentes são apresentados como “Não informada/o”. Endereço livre legado não é interpretado para inferir cidade/bairro; categoria é a persistida. Todos os imóveis usam placeholder neutro “Sem foto”, sem imagem fictícia ou solicitação externa.
- Seleção múltipla restrita à página, checkbox selecionar página com estado intermediário, contador anunciado e limpeza. Nova consulta/página reinicia seleção. Não há operações em lote, escrita, exportação ou novos direitos decorrentes de selecionar registros.
- Desktop usa tabela com região de rolagem horizontal acessível por teclado; em mobile os mesmos registros viram cartões, sem duplicar IDs/checkboxes ou rolagem horizontal da página. Títulos longos permanecem nas células, preços não são fragmentados. Cabeçalhos mantidos para tecnologia assistiva e estado `aria-sort`; rótulos e região `aria-live` para filtros/seleção.
- Criação, proprietário e edição continuam sujeitos às permissões existentes. Usuário apenas de leitura vê “Consultar”; contexto negado/indisponível não renderiza dados ou filtros da carteira.

## Limites e contratos

Criado um porto interno `IPropertyListingStore` e serviço de leitura para a web. Os tipos públicos `PropertyInput`, `PropertyInputV2`, `Workspace`, `WorkspaceV2`, rotas e semântica das APIs v1/v2 permanecem iguais: lista v1 continua filtrando incompatíveis antes do limite e consulta parcial v1 continua 409 após autorização. A carteira usa representação v2 e inclui rascunhos parciais autorizados.

Somente identificadores de coluna constantes da allowlist entram no ORDER BY e filtros SQL; todos os valores do usuário são parâmetros. `PROPERTY_READ` é exigida antes da validação/consulta. Filtro por estabelecimento fora dos grants retorna 403. A mesma transação/contexto de organização, estabelecimentos e legado autorizado é reutilizada; RLS/FORCE RLS permanecem intactos. Página/limites/textos/faixas inválidos e sort fora da allowlist são recusados. GET web não cria nem altera imóveis.

Padrão reutilizável: `_ListingControls` organiza filtros/ordenação da carteira; `listing.css` fornece layout/estados responsivos; `listing.js` opera seleção via atributos `data-listing`, `data-select-page`, `data-select-row`, `data-clear-selection` e `data-selection-status`. Novas listagens podem reutilizar o shell/seleção, fornecendo seus próprios filtros e consultas autorizadas. O listener antigo de pesquisa local ignora explicitamente a pesquisa de servidor.

Não inclui galeria, vídeo, vitrine, links públicos, integração MID-01 ou outros produtos. Cadastro persistente de categorias continua com a dependência específica já registrada no END-02; este incremento não cria catálogo nem modifica o editor de endereços.

## Arquivos

| Camada | Arquivos |
| --- | --- |
| Domínio interno | `backend/src/RealEstate.Domain/PropertyListing.cs` — consulta validada, página e interface interna |
| Aplicação | `backend/src/RealEstate.Application/PropertyListingService.cs` — autorização, validação e estabelecimento |
| PostgreSQL | `backend/src/RealEstate.Infrastructure/PostgresPropertyListing.cs` — partial do store; filtros/ordenação/contagem/paginação parametrizados |
| Web | `Program.cs` (somente DI), `Pages/Index.cshtml(.cs)`, `Pages/Shared/_ListingControls.cshtml` |
| Apresentação | `wwwroot/listing.css`, `wwwroot/listing.js`, `wwwroot/erp.js` (separa pesquisa servidor da local) |
| Testes | `PropertyListingTests.cs`, `PropertyListingDatabaseTests.cs`, `UiAddressStore.cs` e `UiPresentationTests.cs` (fixture/DI do porto interno) |
| Execução/evidência | `scripts/ux02_browser.mjs`, `scripts/ux02_evidence.py`, opções --ux02 em `scripts/ui1_browser.py` e `scripts/end01_database.py` |
| Relatório | `docs/IMOB_UX_02.md` |

## Verificação

Ambiente Windows/.NET 10/Edge, identidade TEST_ONLY. Novo PostgreSQL exclusivo `postgres:17-alpine` local, tmpfs, porta loopback e label verificados antes de migrations existentes/seed. Nenhum banco persistente usado. Runtime NOSUPERUSER/NOBYPASSRLS. Container de ensaio removido ao final com limpeza confirmada; não confundir com a sessão de homologação sintética mantida aberta.

| Comando | Resultado e evidência |
| --- | --- |
| `python -B scripts/end01_database.py --execute --ux02` | PASS_EXECUTED **74/74 .NET**, incluindo 58 provas seguras e 16 PostgreSQL. [TRX](../TestResults/imob-ux-02/end01-v2-database.trx), [isolamento](../TestResults/imob-ux-02/database-isolation.json), [resumo/limpeza](../TestResults/imob-ux-02/database-summary.json) |
| `dotnet test tests/RealEstate.Tests/RealEstate.Tests.csproj --filter 'FullyQualifiedName~PropertyListingTests\|FullyQualifiedName~UiPresentationTests' --no-restore --nologo --logger 'trx;LogFileName=ux02-final-ui.trx' --results-directory TestResults/imob-ux-02` | PASS_EXECUTED **11/11**, repetição dirigida após ajuste da mensagem de filtro. [TRX](../TestResults/imob-ux-02/ux02-final-ui.trx). Não somar às 74 provas como testes distintos |
| `dotnet build tests/RealEstate.Tests/RealEstate.Tests.csproj --no-restore --nologo` | PASS_EXECUTED, zero avisos/erros |
| `python -B scripts/ui1_browser.py --ux02` | PASS_EXECUTED **25/25 Edge**, fixture em memória exclusiva; filtros combinados, preço asc/desc, páginas 25+15, seleção/limpeza/teclado, permissões, placeholder, legado, layout mobile e ausência de sobreposição. [Checks](../TestResults/imob-ux-02/browser-checks.json) |
| `python -B scripts/verify_ui_assets.py` | PASS_EXECUTED: sete assets UI-1 íntegros |
| `python -B scripts/ux02_evidence.py` | PASS_EXECUTED: captura atual de integridade de 86 fontes/auxiliares e artefatos, contagens verificadas, migration 003 idêntica ao recibo anterior, limpeza confirmada. [Recibo](../TestResults/imob-ux-02/source-verification.json). Não é baseline retroativa de binários históricos |
| GET local `/?Filter.Sort=price&Filter.Descending=false` | PASS_EXECUTED HTTP 200, carteira renderizada no PostgreSQL sintético existente |

A execução PostgreSQL foi justificada pela **nova consulta SQL**. Sua prova cria 107 registros e encontra o mais antigo por cidade, verifica contagem/páginas, ordenação numérica, ausência de sobreposição, preço, wildcard literal, entrada hostil, ordenação inválida, autorização negada, outra organização e outro estabelecimento. Os demais testes cobrem regressões v1/v2, constraints, concorrência e RLS existentes. Fixtures privilegiadas usadas só para preparar/finalizar o banco descartável; acesso de consulta testado com login restrito real.

Autocorreções documentadas: testes iniciais provaram que filtros/ordenação eram ignorados; adicionada consulta de servidor. Checagem indiscriminada de ModelState capturava validação do formulário de proprietário durante GET; restrita aos campos Filter. Captura desktop revelou título invadindo cidade por table-layout fixed herdado; teste DOM reproduziu a falha e novo CSS isolado corrigiu a contenção. Preços receberam largura/nowrap próprios. Resultado final sem FAIL demonstrado; suítes históricas de navegador não foram repetidas integralmente.

## Capturas e homologação

Capturas técnicas inspecionadas, com dados sintéticos, sem representar aceite humano:

- [Desktop 1920](../TestResults/imob-ux-02/portfolio-desktop-wide.png).
- [Desktop 1440, região com rolagem horizontal](../TestResults/imob-ux-02/portfolio-desktop.png).
- [Mobile emulado 390, cartões](../TestResults/imob-ux-02/portfolio-mobile.png).

Capturas limitadas à região inicial de até 1800 px de altura para manter legibilidade; não representam toda a lista.

Roteiro humano: atualizar http://127.0.0.1:65182; pesquisar e aplicar, combinar cidade/UF/categoria/preço/estabelecimento, alternar ordenação e navegar páginas; selecionar um/vários/página, limpar e confirmar reinício ao mudar consulta; conferir rolagem horizontal desktop e cartões mobile; verificar que legado sem cidade e sem foto permanece explícito; abrir consulta/edição e cadastrar conforme seus grants. Comparar a experiência com a referência de tickets e registrar divergências. A sessão usa o banco sintético anterior preservado; encerramento continua `python -B scripts/end01_homologation.py stop`.

**NOT_RUN:** revisão manual em celular/aparelho físico, tablet, leitor de tela, zoom nativo, auditoria WCAG independente, todos os fluxos fora da captura desktop aceita, Core/OIDC/licenças reais nesta execução, carga/EXPLAIN com grande volume e integração MID-01. A aceitação humana desktop registrada não cobre estes itens. Filtros ILIKE/count podem exigir otimização futura em grandes volumes; nenhuma migration/índice foi criado. Contagem e leitura seguem o isolamento transacional atual; alterações concorrentes podem mover registros entre páginas, sem promessa de snapshot entre requisições.

**Pendências:** avaliação manual mobile e de fluxos/estados não cobertos permanece **DECISÃO HUMANA NECESSÁRIA**; leitor de tela e auditoria WCAG independente também não foram homologados. Foto real depende do MID-01 (**BLOQUEADO POR DEPENDÊNCIA**, fora deste recorte). O aceite desktop permanece preservado; não há pendência de reaprovação do recorte aceito.

## Consolidação visual — cartões de imóveis (X2-01 / IMOB-03, 09/11/2026)

**Melhoria visual especificada; não implementada; testes funcionais aprovados não reabertos.** Aplicar a especificação tanto aos cartões responsivos da pesquisa comercial IMOB-03 quanto aos cartões mobile da Carteira/Consulta, preservando o template de tabela desktop atualmente aceito.

O modelo existente `PropertyV2.Data.Capture` expõe `Rooms.Bedrooms`, `Rooms.Parking`, `Values.CondominiumFee`, `Values.CondominiumPeriod`, `Values.PropertyTax` e `Values.PropertyTaxPeriod`. Os dois primeiros são informações opcionais do imóvel. Valores monetários e períodos também podem ser mantidos por anúncio em `AnnouncementInput`; `ListingAnnouncement` hoje projeta finalidade, código, `OperationValue` e `LegacyAnchor`, mas não os encargos do anúncio. A pesquisa comercial já recebe o modelo de imóvel e usa `_ListingControls`, `listing.css`, `listing.js` e a seleção existente; não exige uma nova pesquisa ou consulta por cartão.

Diretriz de apresentação: acrescentar dormitórios, vagas, condomínio e IPTU apenas a partir de campo existente e autorizado. Manter ausência distinta de zero, usar `Brl.Currency` para moeda e mostrar período só quando conhecido. O cartão deve continuar compacto e preservar foto/capa real ou placeholder, código comercial, título comercial, localização, valor ativo e seleção. Não usar UUID, notas internas ou descrições sintéticas como conteúdo comercial principal.

**Decisão humana necessária antes de exibir encargos:** estabelecer precedência entre valor da captação e valor do anúncio comercial ativo, pois ambos têm origem persistida e a listagem não projeta atualmente os encargos específicos do anúncio. Se a decisão selecionar o anúncio ativo, a projeção de listagem existente deverá fornecer os campos sem consulta N+1 ou modelo duplicado. Confirmar também o tratamento visual quando o valor existe e a periodicidade não está preenchida.

Os SVG disponíveis incluem símbolos genéricos `i-building` no snapshot Core e `building`, `rooms`, `wallet` e `file` em `cad-icons.svg`; não foi identificada confirmação semântica clara de símbolo dedicado para cama, veículo e tributo. Selecionar símbolos existentes adequados ou aprovar esses assets no conjunto oficial antes do uso; não inventar ID nem introduzir uma paleta paralela. A diretriz de ícones condicionais segue `ARS-TPL-001`; a biblioteca continua sujeita a ARS-VIS-04.
Direção semântica visual específica do Imob já aprovada: imóveis em ciano/teal, Agenda em violeta, atendimentos em azul, concluído em verde, aguardando em âmbar/laranja e restrição/erro em vermelho. Aplicar apenas por tokens AraIa compatíveis, sempre acompanhados de texto/ícone/estrutura, sem criar paleta paralela.

O cartão atual renderiza `property.Data.Title` quando preenchido e não usa resumo/notas internas como fallback. O modelo/projeção não classifica título comercial versus texto sintético; não há regra técnica comprovada para filtrar automaticamente descrições sintéticas. Antes de implementar uma ocultação, definir uma fonte/indicador comercial autorizado ou marcador de conteúdo de homologação; não alterar registros nem inferir pelo texto sem regra aprovada.

Esta especificação integra o item existente Monday X2-01 (Sprint ARAIA-X2, estado `Parado`) e a melhoria visual pendente do IMOB-03. O Monday não foi alterado nesta execução documental local. A Fase 2 do IMOB-03 segue aguardando homologação visual humana.
