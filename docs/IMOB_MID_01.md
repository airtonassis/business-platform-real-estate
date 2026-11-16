# IMOB-MID-01 — galeria privada de fotos e vídeos

**Estado vigente — 08/11/2026:** fotos e descrições do Incremento A **HOMOLOGADAS — APROVADAS** pelo responsável, conforme [registro de encerramento](IMOB_CADASTRO.md#cad-incremento-a-homologacao). A câmera física ainda está pendente de teste. A aprovação não inclui vídeos, publicação, experiência móvel ou novas funções da categoria. Evidências anteriores abaixo permanecem preservadas; estados “pendente” ali são históricos quando substituídos por este aceite.

**Incremento A, 08/11/2026 — histórico técnico:** opções existentes de envio/câmera preservadas; Mensagem curta e Salvar descrição ficam junto de cada foto, reutilizando o mesmo Caption/UUID e o formulário de Draft, sem reenvio do binário. Envio e reabertura foram comprovados pela identidade real; o responsável esclareceu a necessidade de interface. Evidências, limites e roteiro em [IMOB_CADASTRO.md — Incremento A](IMOB_CADASTRO.md#cad-incremento-a). O aceite humano posterior está registrado no estado vigente acima.

**Implementação e preservação:** descrição opcional de até 200 caracteres e ampliação com descrição implementadas/validadas localmente. Reutilizado `Capture.Media[].Caption` e o mesmo `MediaId`: Salvar rascunho atualiza sem reenviar arquivo; legendas antigas longas permanecem intactas quando não editadas. Arquivos, hashes, ordenação, vínculos e histórico preservados. Os detalhes técnicos abaixo sustentam o critério homologado no estado vigente do início deste documento; não promovem vídeos, câmera física, publicação ou experiência móvel.

Histórico Monday anterior: IMOB-MID-01, atualização `5703768905`. Encerramento humano atual registrado no mesmo item existente pela atualização `5705296193`, sem mudança de status do item/gate.

## Evidência e integração

Não há AGENTS.md próprio no ERP; aplicadas as instruções fornecidas do AGENTS.md do workspace. Consultados `docs/PLATFORM_DECISIONS.md`, catálogo `docs/catalog/real-estate.v1.json`, contratos v1/v2, migrations existentes, Program.cs, cadastro e carteira.

- `database/migrations/001_properties.sql` e 002/003: imóveis, proprietários e auditoria; não existe tabela de mídias, capa, ordenação ou payload binário.
- Código e projeto Web: nenhum serviço/adapter de armazenamento, handler de upload ou dependência de decodificação de imagem/vídeo encontrada. Kestrel limita requisições a 32768 bytes. `wwwroot` serve arquivos estáticos e não será usado para mídias privadas.
- `Pages/Property.cshtml(.cs)`: integra a galeria somente após imóvel salvo e autorizado. O formulário cadastral/antiforgery/versionamento atual permanece separado das operações de mídia.
- `Pages/Index.cshtml`, `PropertyListingService` e `PostgresPropertyListing`: integrar somente ID/metadados mínimos da capa por consulta interna, sem carregar todos os binários nem fazer uma consulta por linha.
- Grants existentes: PROPERTY_READ/CREATE/UPDATE e OWNER_CREATE. Proposta reutiliza READ para consulta e UPDATE para upload/capa/ordenação/remoção de imóvel já salvo, sem novo grant. As verificações devem ocorrer no servidor em cada operação e no acesso ao conteúdo; esconder botão não autoriza nada.
- Homologação visual da UX-02 é informada como dependência na solicitação atual. O relatório anterior ainda registra homologação pendente; não foi alterado retrospectivamente nem foi inventada evidência de aceite.

## Delta proposto para autorização específica

**Proposta recomendada para o recorte local:** migration aditiva `004_property_media.sql`, a ser escrita e executada somente após autorização; tabela `real_estate.property_media` com conteúdo privado `bytea` no PostgreSQL do próprio ERP. Não contratar armazenamento, acessar VPS/EasyPanel, criar volumes compartilhados, publicar URLs estáticas ou adicionar processamento externo. Um porto de armazenamento mantém futura troca de backend possível; esta proposta não aprova bytea para produção em grande escala.

Estrutura proposta, ainda não executada:

| Elemento | Conteúdo/invariante |
| --- | --- |
| Chave | organization_id + id, UUID gerado pelo servidor |
| Vínculo | property_id e FK composta (organization_id, property_id) ao imóvel; evitar referência entre empresas |
| Metadados | tipo image/video, MIME validado, extensão canônica, tamanho, nome original apenas informativo, SHA256, posição, is_cover, versão e ator/data |
| Conteúdo | bytea privado; nome do usuário nunca vira caminho, comando ou URL pública |
| Capa | CHECK is_cover implica image; índice único parcial por organização/imóvel para no máximo uma capa |
| Acesso | ENABLE/FORCE RLS e política que exige organização e visibilidade do imóvel pai segundo suas políticas de estabelecimento/legado; grants mínimos ao runtime existente |
| Concorrência | lock do imóvel pai e comparação de revisão da coleção antes de capa/reordenação/remoção; revisão opaca derivada dos IDs/versões atuais, evitando alterar a versão pública do imóvel |
| Auditoria | operações de mídia em `real_estate.events` na mesma transação; nenhum evento de sucesso antes do commit |

Binário e metadados na mesma transação evitam arquivo órfão e dispensam um filesystem privado novo. Se limites do parser de multipart exigirem spool temporário, ele deve usar diretório temporário privado fora de wwwroot, nome aleatório, limite e limpeza no sucesso/falha/cancelamento. Não pressupor ausência de temporários apenas porque o armazenamento final é bytea.

Proposta de limites locais: JPG/JPEG e PNG até 10 MiB cada, MP4 até 50 MiB cada, até 20 arquivos e 100 MiB por envio, cota de 250 MiB por imóvel. Limites aplicados por leitura efetiva de bytes, além de Content-Length. Imagens com limite de dimensões/pixels e decodificação validada; rejeitar extensão/MIME/conteúdo divergentes, SVG/HTML/executáveis e estruturas truncadas. MP4 exige inspeção estrutural do container e identificação de trilha de vídeo/codec aceito; não apresentar assinatura inicial como validação completa. Sem transcodificação, miniaturização externa ou garantia de reprodução de todo codec MP4. Dependências locais de validação serão explícitas no diff; nenhum serviço externo presumido.

Elevar o limite HTTP somente para operações de upload, mantendo os limites atuais das rotas cadastrais/API. Tratamento de 413, formato recusado, timeout/cancelamento, indisponibilidade e conflito sem salvar lote parcial silenciosamente. Proposta: lote atômico, ordem inicial na sequência de envio; primeira imagem torna-se capa apenas se ainda não houver capa. Remoção da capa escolhe a primeira imagem restante, ou mantém nenhuma se só houver vídeos; isso não transforma vídeo em capa.

## Contrato web proposto, sem alterar a API pública

Handlers Razor autenticados no contexto `/properties/{key}`: consultar galeria/conteúdo, upload multipart múltiplo, definir capa, reordenar lista completa de IDs e remover mídia. POST exige antiforgery, autorização e revisão da coleção; conteúdo GET valida organização, imóvel e estabelecimento antes de responder. Nenhum ID ou nome de arquivo fornecido dispensa essas verificações. Arquivos devolvidos com MIME validado, nosniff, cache privado/no-store e tratamento seguro de nome. Range para vídeo deve repetir a autorização e respeitar limites; não fornecer URL pública ou acesso por chave apenas adivinhável.

READ para imagem principal da carteira e galeria; UPDATE para toda mutação. Imóvel ainda não salvo solicita salvar primeiro. Em consulta sem UPDATE, prévia permanece e comandos de escrita não aparecem; chamadas forjadas são negadas. A API v1/v2 do imóvel não ganha campos/endpoints por inferência. Se uma API pública de mídia for desejada depois, exigirá autorização de contrato própria.

Interfaces planejadas: seção Galeria no cadastro salvo, seletor múltiplo com limites, cards com nome/tipo/ordem/status, preview `<img>` ou `<video controls preload="metadata">`, badge Vídeo, ações de capa somente em imagens, reordenação também por botões/teclado e confirmação de remoção. Carteira mostra capa autenticada com dimensão reservada e placeholder se ausente/indisponível. Layout usa os tokens AraIa existentes, sem refatorar o shell.

## Arquivos previstos após liberação

`database/migrations/004_property_media.sql`; tipos/regras/portos de mídia em Domain; serviço de mídia em Application; store PostgreSQL e validação local de conteúdo; DI/limites específicos em Web; handlers/parcial de galeria e CSS/JS próprios; consulta interna de capa na carteira; testes unitários/HTTP/PostgreSQL/Edge e scripts/evidências próprios. Não modificar migrations 001/002/003, contratos públicos de imóveis ou catálogo de grants neste recorte proposto.

## Resultado da preparação anterior — histórico

**Alterado:** somente este relatório. **Implementado:** nenhum recurso de mídia; nenhuma migration escrita/aplicada. **Verificado por leitura:** ausência de schema/adapter de mídia, limite 32 KiB e pontos de integração/grants existentes.

| Testes/capturas solicitados | Estado |
| --- | --- |
| Upload válido/inválido/múltiplo e validação de conteúdo | NOT_RUN — implementação depende da migration autorizada |
| Capa, troca, ordenação, exclusão e transações | NOT_RUN — mesma dependência |
| Isolamento tenant/unidade, autorização e acesso privado | NOT_RUN para mídia; provas de outros incrementos não substituem estas |
| Falhas, cancelamento e limpeza de temporários | NOT_RUN |
| Capa na carteira e responsividade | NOT_RUN |
| Capturas desktop/mobile da galeria | NOT_RUN — não existe galeria implementada; não reutilizar capturas UX-02 como prova MID-01 |
| Regressão existente | NOT_RUN nesta preparação documental; nenhuma mudança de comportamento justifica repetir as suítes |

Roteiro de homologação a executar depois da implementação autorizada: salvar imóvel sintético; enviar lote misto; conferir preview e identificação de vídeo; trocar capa somente entre imagens; ordenar por teclado e reabrir; remover capa e confirmar substituição/placeholder; conferir carteira desktop/mobile; tentar excesso/formato falso e acesso de outra empresa/unidade/usuário readonly; verificar rollback, auditoria e limpeza após falha. Usar somente ambiente local descartável comprovadamente isolado.

**Autorização necessária:** aprovar migration 004 e armazenamento privado bytea no PostgreSQL local do ERP, com limites e operações web propostas, reutilizando READ/UPDATE. Isso libera a implementação local e ensaios descartáveis, sem implantação em banco persistente/compartilhado, VPS ou produção. Se o armazenamento recomendado não for aprovado, escolher backend privado e autorizar sua infraestrutura antes da persistência. A solicitação dessa liberação decorre expressamente do item 4 do pedido do proprietário, não de aprovação automática de skill.

## Execução após liberação específica

Autorização local registrada pelo proprietário nesta conversa e no Monday, atualização **5703789124**. Leitura das decisões existentes confirmada; não foi necessário criar ADR nem alterar grants do Core. UX-02 aceita visualmente conforme decisão **5703760466**; isso não aprova a galeria automaticamente.

### Funcionalidades e segurança implementadas

- Upload múltiplo JPEG/PNG/MP4 vinculado a imóvel salvo; lote atômico, preview autenticado de foto/vídeo, capa inicial na primeira imagem, troca de capa, movimentação por botões/teclado e remoção com confirmação. Remover capa escolhe a primeira imagem restante; se só restarem vídeos, não há capa. A ordem é compactada após remover. Vídeos têm controles nativos e identificação visível, nunca botão de capa.
- Mesmos limites aprovados: imagem 10 MiB; vídeo 50 MiB; 20 arquivos e 100 MiB por lote; 250 MiB por imóvel. Os bytes efetivamente lidos são contados. O limite HTTP de 102 MiB inclui overhead multipart e aplica-se exclusivamente ao POST Upload de imóvel; demais rotas conservam 32 KiB. Não se aumenta o payload permitido de mídia além dos 100 MiB.
- MultipartReader processa arquivos por streaming, com limites de cabeçalhos e boundary. Autorização UPDATE e visibilidade do imóvel são conferidas antes de consumir arquivos. Antiforgery chega pelo header, e as factories de leitura de formulário são removidas somente nesse handler. Nenhum arquivo temporário de upload é criado: bytes validados ficam em memória limitada até o commit. Cancelamento/falha antes do store não persiste o lote.
- [StbImageSharp 2.30.16](https://www.nuget.org/packages/StbImageSharp/2.30.16), dependência gerenciada local sem serviço externo, identifica dimensões e decodifica JPEG/PNG. Imagens limitadas a 12000 por dimensão e 20 milhões de pixels. PNG verifica CRC/chunks/fim exato e rejeita APNG; JPEG exige início/fim e decodificação. Extensão e MIME devem corresponder ao formato admitido; nomes com caminhos, controles, traversal ou excesso são recusados. Nomes não viram caminhos/URLs nem comandos.
- MP4 é validado estruturalmente: caixas com limites, marcas admitidas, trilha vide, sample entry avc1/avc3, configuração AVC/SPS/PPS, dimensões e amostras declaradas compatíveis com os bytes mdat. Vídeo truncado, container inválido ou codec não admitido é recusado. Não é decodificação integral de todos os frames, scanner antimalware ou transcodificação; compatibilidade do player continua dependente do navegador/codec.
- Migration **004_property_media.sql** cria apenas a tabela de mídias, constraints/índices/RLS e versão de schema. FK composta ao imóvel, ON DELETE CASCADE, conteúdo bytea e comprimento coerente, capa única com CHECK de imagem, posições únicas diferíveis para reordenação. ENABLE/FORCE RLS herda a visibilidade do pai, incluindo estabelecimento/legado autorizado. Migrations anteriores permanecem intactas.
- Todas as mutações bloqueiam o imóvel pai, comparam revisão da coleção, aplicam cota e registram auditoria na mesma transação. Revisão inclui versões/ordem/capa e geração derivada dos eventos de mídia, detectando inclusive coleção que ficou vazia e foi alterada desde uma leitura antiga. Rollback da auditoria conserva conteúdo e revisão. Versão/contratos públicos do imóvel permanecem independentes.
- GET de conteúdo repete READ e RLS; URL não concede acesso por si só. Resposta com MIME validado, nosniff, cache no-store e Range autorizado para vídeo. Não há arquivo em wwwroot, bucket, chave pública, link compartilhável ou uso de VPS/EasyPanel.
- Carteira busca IDs de capa de toda a página em uma consulta, sem binários/N+1. Imagens carregam por handler privado e lazy loading; sem capa ou em erro, placeholder neutro. Não se duplica o arquivo para criar miniatura: o navegador reduz a imagem original na apresentação.
- Galeria e formulário cadastral separados. Mutações da galeria recarregam a ficha; se houver edição cadastral não salva, o usuário recebe confirmação antes de descartar. Sem UPDATE, prévias ficam disponíveis e comandos de escrita desaparecem; chamadas forjadas continuam negadas no servidor. Upload/ações dependem de JavaScript e CSRF; mensagens acessíveis indicam limite, formato, conflito e falha.

### Arquivos alterados

| Camada | Arquivos |
| --- | --- |
| Schema | `database/migrations/004_property_media.sql` (nova) |
| Domínio | `backend/src/RealEstate.Domain/PropertyMedia.cs` (tipos/portos/limites/revisão) |
| Aplicação | `backend/src/RealEstate.Application/PropertyMediaService.cs` |
| Infraestrutura | `backend/src/RealEstate.Infrastructure/PostgresPropertyMedia.cs`, `MediaContentValidator.cs`, `RealEstate.Infrastructure.csproj` |
| Web | `Program.cs` (DI/limite exclusivo), `MediaStreaming.cs`, `Pages/Property.cshtml(.cs)`, `Pages/Property.Media.cs`, `Pages/Shared/_MediaGallery.cshtml`, `Pages/Index.cshtml(.cs)` |
| Assets | `wwwroot/media.css`, `wwwroot/media.js`, `wwwroot/listing.js` (fallback da capa) |
| Testes | `MediaValidationTests.cs`, `MediaLimitTests.cs`, `MediaDatabaseTests.cs`, `MediaTestFiles.cs`, `EmptyMediaStore.cs`, `FixtureProgram.cs`, `UiPresentationTests.cs`, `Fixtures/synthetic.jpeg` e `synthetic.mp4` |
| Helpers | `scripts/mid01_browser.py`, `mid01_browser.mjs`, `mid01_evidence.py`, opções MID no `end01_database.py`, `end01_homologation.py`, `ui1_browser.py` |
| Documentação | este relatório consolidado |

Fixtures de JPEG e vídeo são sintéticas, geradas no próprio navegador local; não usam arquivos ou processamento externos. Fixture de mídia vazia atende apenas regressões históricas de UI em memória; provas da galeria usam PostgreSQL real descartável.

### Resultado final PASS / FAIL / NOT_RUN

A rodada completa passou **88/88 .NET**, incluindo 69 provas seguras e 19 PostgreSQL. Após ajuste localizado da compactação de ordem na remoção, os três testes SQL/HTTP de mídia foram repetidos e passaram em diretório separado. Não somar 88+3 como testes distintos. Nenhum FAIL final demonstrado.

| Comando | Resultado / evidência |
| --- | --- |
| `python -B scripts/end01_database.py --execute --mid01` | PASS_EXECUTED **88/88**: [TRX](../TestResults/imob-mid-01/end01-v2-database.trx), [isolamento](../TestResults/imob-mid-01/database-isolation.json), [resumo/limpeza](../TestResults/imob-mid-01/database-summary.json) |
| `python -B scripts/end01_database.py --execute --mid01 --media-final` | PASS_EXECUTED **3/3 dirigidos**, após compactação da ordem: [TRX](../TestResults/imob-mid-01/final-media/end01-v2-database.trx), [limpeza](../TestResults/imob-mid-01/final-media/database-summary.json) |
| `python -B scripts/mid01_browser.py` | PASS_EXECUTED **17/17**, em PostgreSQL tmpfs existente, upload real acima de 32 KiB, JPEG/PNG decodificados, vídeo com relógio avançando, capa/teclado, ordenação, remoção, fallback, leitura readonly, acesso anônimo/outro tenant/outra unidade recusados. [Checks](../TestResults/imob-mid-01/browser-checks.json) |
| `python -B scripts/ui1_browser.py --ux02 --mid01-regression` | PASS_EXECUTED **25/25**, carteira: [checks](../TestResults/imob-mid-01/regression-ux02/browser-checks.json) |
| `python -B scripts/ui1_browser.py --end02-final --mid01-regression` | PASS_EXECUTED **26/26**, endereço e coexistência v1/v2: [checks](../TestResults/imob-mid-01/regression-end02/browser-checks.json) |
| `python -B scripts/verify_ui_assets.py` | PASS_EXECUTED, sete assets AraIa protegidos íntegros |
| `dotnet build tests/RealEstate.Tests/RealEstate.Tests.csproj --no-restore --nologo` | PASS_EXECUTED, sem avisos/erros |
| `python -B scripts/mid01_evidence.py` | PASS_EXECUTED: integridade atual de 106 fontes/fixtures/auxiliares, hashes dos artefatos e contagens, sete fontes de contratos/migrations preservadas contra recibo UX-02; [recibo](../TestResults/imob-mid-01/source-verification.json). Captura atual, não baseline retroativa de binários |

Banco de ensaio criado exclusivamente com imagem PostgreSQL local já disponível, label/porta loopback/tmpfs conferidos, identidades restritas NOSUPERUSER/NOBYPASSRLS. Aplicadas migrations 001–004 no banco novo, com snapshot dos imóveis legados antes/depois intacto. Testes privilegiados usados somente para preparação/constraints/falha de grant controlada. Ambos containers de ensaio removidos e ausência confirmada. A sessão de homologação tem ownership próprio, permanece aberta e recebeu somente migration004 autorizada; seus imóveis anteriores foram preservados.

Conteúdo validado: JPEG/PNG reais, PNG com CRC alterado, imagem truncada, dimensões/pixels excessivos, extensão/MIME incoerentes, traversal, executável/HTML renomeado e MP4 truncado/caixa inválida; streams de bytes aferem limites reais por arquivo/lote/contagem e cancelamento. A prova de cota usa payload sintético no porto de store, sem alegar que esse payload é vídeo válido. Validação real de vídeo é provada separadamente no validador e no navegador. O teste HTTP instala um IFormFeature que lança erro se qualquer leitura bufferizada de formulário ocorrer; upload legítimo e rejeições continuam passando, sem temporários.

Concorrência: duas escritas com mesma revisão têm um vencedor; ordem exige exatamente os IDs atuais e não aceita IDs estrangeiros; exclusão substitui capa/compacta ordem; nenhuma capa pode ser vídeo, inclusive por SQL direto; RLS nega tenant/unidade; lote inválido não deixa arquivos parciais; falha de auditoria desfaz a remoção; coleção esvaziada não reutiliza revisão antiga. Cota de 250 MiB testada pelo runtime real.

Autocorreções: corrigida chamada da API de identificação da dependência (FromStream); imagem lazy aguardada somente após tornar a galeria visível; prova de reprodução colocou o vídeo na viewport antes de medir avanço; ações seguintes aguardam DOM/scripts completos após recarga; verificação de fallback foi restringida à célula que falhou, pois a carteira contém mais de um imóvel sintético. Essas falhas do roteiro não foram tratadas como defeito de segurança do produto. Compactação de posições na remoção foi implementada e verificada no store real. Todos os resultados finais acima passaram.

### Capturas e homologação local

- [Galeria desktop](../TestResults/imob-mid-01/gallery-desktop.png).
- [Galeria mobile emulado 390](../TestResults/imob-mid-01/gallery-mobile.png).
- [Carteira com capa desktop](../TestResults/imob-mid-01/portfolio-cover-desktop.png).
- [Carteira com capa mobile](../TestResults/imob-mid-01/portfolio-cover-mobile.png).

Capturas técnicas inspecionadas, com mídias sintéticas, não evidenciam aceite humano. Imóvel de demonstração final: **http://127.0.0.1:65182/properties/1c196b39-cb4f-4a9c-a472-49f6c2d9106a#gallery**. Carteira: http://127.0.0.1:65182/. Acesso automático TEST_ONLY; não usar credenciais ou dados reais. A galeria final contém duas imagens e um vídeo, já persistidos. Tentativas anteriores do roteiro deixaram somente imóveis/mídias sintéticos adicionais, sem remoção de dados do proprietário.

Roteiro humano: abrir a galeria, reproduzir o vídeo; adicionar fotos/vídeos admitidos; trocar capa apenas em imagens; mover por botões/teclado e reabrir; remover capa com confirmação e conferir substituição; verificar a carteira e o fallback; testar excesso/formato inválido e ler mensagens. Conferir desktop/mobile e registrar PASS/FAIL por item. Antes de mutar mídias com cadastro não salvo, confirmar conscientemente o descarte ou salvar o cadastro primeiro.

Para encerrar a sessão somente após a conferência: `python -B scripts/end01_homologation.py stop`. O helper verifica ownership antes de parar/remover recursos. O banco é tmpfs: parar/remover o container perde dados sintéticos e mídias da sessão. Reprodução do ensaio independente não depende dessa sessão: o comando --mid01 cria banco novo descartável e remove ao final. Não apontar esses testes para serviço persistente.

**Não executado neste ensaio histórico:** escopo completo MID-01, aparelho/câmera física, leitor de tela, Core/OIDC/licença reais, carga/consumo de memória com múltiplos uploads simultâneos, transcodificação, todos os codecs de vídeo e produção. O critério específico de fotos e descrições do Incremento A foi aprovado posteriormente, conforme estado vigente no início deste documento; a câmera física permanece pendente. Resultados anteriores permanecem preservados.

### Registros no Monday

- Tarefa IMOB-MID-01 **13494043535**: atualização **5703904520**, resultado técnico e evidências; status **Aguardando revisão**, sem marcar Feito/homologado.
- Sprint ARAIA-X3 **13490284525**: atualização **5703904519**, avanço local autorizado e limites; demais trilhas e encerramento formal preservados.
- Marco M6 **13428486582**: atualização **5703904521**, capacidade local disponível; integração empresarial/Core, homologação e produção continuam separadas, sem validar o marco por inferência.

**Pendências vigentes:** teste de câmera física. A homologação de fotos/descrições do Incremento A está encerrada; autorização para qualquer armazenamento/implantação adicional e aceite de capacidades fora desse recorte continuam independentes.
# Correção direcionada — entrada de fotos no Novo, 08/11/2026

**Atualização posterior — registro histórico:** a requisição humana rejeitada chegou sem sessão autenticada e falhou em antiforgery antes da persistência. Tratamento/retomada OIDC corrigidos no recorte registrado em [Novo imóvel — retomada do acesso](IMOB_CADASTRO.md#cad-novo-retomada-sessao), com preservação de três arquivos selecionados e descrições. O responsável confirmou recuperação do salvamento. O aceite posterior de fotos e descrições está no [encerramento do Incremento A](IMOB_CADASTRO.md#cad-incremento-a-homologacao); não atribuir o motivo original da invalidação da sessão a expiração sem evidência.

O seletor nativo aparece também em `/properties/new`. **Enviar mídias** salva primeiro o Draft preenchido pelo fluxo existente e usa seu UUID/revisão para o upload real. Um arquivo inválido conserva campos/seleção e permite retry no mesmo registro. Mensagem curta, edição sem reenvio e ampliação existentes foram preservadas. Uma jornada real `hom-owner` comprovou seleção nativa, rejeição 400 de conteúdo inválido, upload 200, descrição e reabertura. Evidências, limites e roteiro em [IMOB_CADASTRO.md](IMOB_CADASTRO.md#cad-correcao-entrada-captacao-midias). O estado de homologação está atualizado no encerramento; câmera física permanece pendente. Incremento B planejado, sem implementação autorizada.

