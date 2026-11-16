# AraiA Imob — especificação funcional detalhada do cadastro de imóveis

**Proprietário integrado — 08/11/2026:** complementação autorizada de PF/PJ, contato, endereço independente e consulta de CEP no mesmo cadastro/UUID. Fonte oficial, delta de persistência, provas direcionadas e pendência de homologação: [CAD-PROPRIETARIO-002 v1.0](IMOB_CADASTRO.md#cad-proprietario-contato-cep). Não implica conclusão de outras categorias.

**Prefixos comerciais complementares, 08/11/2026:** aprovados e configurados KIT, COB, STD, LOF, GAL, PRC, EMP e IND para os oito tipos existentes; preservados os prefixos anteriores e VEN/LOC/TEM. Fonte oficial, diagnóstico das Casas, teste único com rollback e pendências históricas em [IMOB_CADASTRO.md — prefixos aprovados](IMOB_CADASTRO.md#cad-prefixos-tipos-aprovados). Nenhum código histórico foi atribuído ou renumerado; as menções anteriores à falta de decisão para esses oito tipos são históricas. Não representa nova homologação humana.

**Correção pontual v1.5, 08/11/2026:** Mídias apresenta a galeria existente, sem formulários repetidos de metadados por imagem; seus dados permanecem no contrato e na persistência. Duplicar a partir de um anúncio selecionado mantém sua finalidade, valores e condições no formulário de novo imóvel independente, preservando as demais modalidades, `Price` sem classificação, nulos/zero e adaptação. Causa, verificações direcionadas, capturas desktop/móvel e integridade dos originais em [IMOB_CADASTRO.md — Mídias e Valores](IMOB_CADASTRO.md#cad-copia-midias-valores). Correção técnica local; **homologação humana pendente**, sem novas migrations ou alterações de arquitetura/regras comerciais.

**Decisão vigente — Duplicar imóvel v1.4, 08/11/2026:** a ação Duplicar cria **outro imóvel físico independente** usando o formulário original preenchido e editável, sem gravação até Salvar. Não cria outro anúncio vinculado. Proprietário, unidade, áreas, finalidade, valores, seleções e fotos são revisáveis; novo UUID/código/versão/histórico e mídias próprias, original preservado. Temporada permanece disponível com prefixo **TEM**, aprovado pelo responsável. Regra, incompatibilidade encontrada, migration aditiva 017, preservação de 015/016 e evidências reais estão na fonte [IMOB_CADASTRO.md — Duplicar imóvel](IMOB_CADASTRO.md#cad-imovel-duplicacao). As interpretações v1.1–v1.3 abaixo são históricas para essa ação. Os anúncios já existentes não são excluídos ou convertidos. Correção local validada tecnicamente, **homologação humana pendente**; dez aceites anteriores preservados.

**Correção vigente CAD-ANUNCIO-001 v1.3, 08/11/2026:** a experiência v1.2 abaixo foi rejeitada na homologação. Duplicação e manutenção reutilizam exclusivamente o cadastro original: carteira → abrir anúncio → Duplicar → formulário original preenchido → revisar finalidade/valores → salvar → carteira. Sem tela comercial paralela, cartões intermediários ou criação no GET. Dados físicos e fotos compartilhados ficam em consulta na cópia; somente o contrato comercial existente é gravado, preservando a origem. Decisão, fronteiras, testes direcionados e jornada real em [IMOB_CADASTRO.md — cadastro original](IMOB_CADASTRO.md#cad-anuncio-001-formulario). Correção técnica local, **nova homologação humana pendente**, preservados dez aceites, persistência e migrations 015/016. Os registros v1.2 seguintes são históricos e não representam o fluxo operacional vigente.

**Correção CAD-ANUNCIO-001 v1.2, 08/11/2026:** ação identificável na carteira/manutenção abre revisão GET sem persistência; dados compatíveis pré-preenchidos e origem consultável; novo UUID/código e dados revisados gravados atomicamente apenas em Salvar. Carteira reorganizada em grupos legíveis e detalhes expansíveis, preservando controles e permissões. Causa, seis testes novos, jornada OIDC real e capturas em [IMOB_CADASTRO.md — correção de navegação](IMOB_CADASTRO.md#cad-anuncio-001-navegacao). Correção técnica local; homologação humana pendente, sem alterar os dez aceites anteriores ou migrations 015/016.

**Categoria 6 — atualização em 08/11/2026:** Venda, Locação, Venda e locação, formatação numérica e preservação do preço histórico **HOMOLOGADOS — APROVADOS** no recorte anterior. Valores/condições independentes e `Price` sem classificação automática permanecem conforme [CAD-VALORES-001 v1.0](IMOB_CADASTRO.md#cad-valores-001). Alteração de finalidade não foi testada pelo homologador. Sua nova decisão foi implementada localmente em [CAD-ANUNCIO-001 v1.1](IMOB_CADASTRO.md#cad-anuncio-001-implementacao): imóvel físico compartilhado, anúncios exclusivos com identidade própria, finalidade imutável, duplicação transacional e um anúncio Draft ativo por modalidade. Combinados históricos permanecem intactos e podem receber anúncios exclusivos adicionais, conforme autorização expressa, sem conversão automática. Novos combinados não são criados pelo fluxo exclusivo. Evidências técnicas e limites constam no registro; **homologação humana do incremento de anúncios PENDENTE**. Preservados os cinco aceites do Incremento A.

**Encerramento da homologação humana do Incremento A, 08/11/2026:** os cinco critérios — cadastro adaptativo, condomínio/herança, troca/desvinculação, responsável pela captação e fotos/descrições — foram aprovados pelo responsável. Registrar o estado vigente e as pendências separadas em [IMOB_CADASTRO.md — encerramento](IMOB_CADASTRO.md#cad-incremento-a-homologacao). O teste de câmera física permanece pendente; a diretriz transversal numérica está aprovada e sua implementação deve ser acompanhada por produto; Incremento B de condomínios está planejado, sem autorização de implementação. Este aceite não declara as 14 categorias concluídas nem altera evidências técnicas anteriores.

**Incremento A autorizado em 08/11/2026:** aplicação 1.1 da adaptação/herança no recorte apartamento/locação, ocultação de permuta em locação exclusiva, origem e desvínculo coletivo, responsável pelo diretório existente e mensagem curta junto da foto. Implementação, preservação e evidências em [IMOB_CADASTRO.md — Incremento A](IMOB_CADASTRO.md#cad-incremento-a). Novos grupos de condomínio/Incremento B continuam propostos, sem promoção a implementado ou homologado.

**Diretriz permanente aprovada em 08/11/2026:** a fonte oficial das regras `CAD-ADAPT-001/002` e `CAD-COND-001/002`, sua matriz por tipo/finalidade e o delta de evolução do condomínio estão em [IMOB_CADASTRO.md — Diretriz permanente](IMOB_CADASTRO.md#cad-adapt-cond-v1). A diretriz de adaptação/reuso é aprovada; os novos grupos/opções de condomínio permanecem **proposta de detalhamento**, sem implementação ou homologação automática. Os demais requisitos e aceites deste documento continuam preservados.

**Atualização de apresentação autorizada em 08/11/2026, após revalidação v3 — registro histórico:** cadastro simplificado de proprietário aprovado; tela completa apenas planejada no mesmo UUID. Móveis e equipamentos passa a suceder Ambientes e características na navegação, preservando IDs e responsabilidades. A tela de áreas é simplificada sem converter os campos independentes já persistidos. O estado vigente dos cinco critérios do Incremento A está no registro de [encerramento humano](IMOB_CADASTRO.md#cad-incremento-a-homologacao); esse aceite posterior não altera o fato histórico de que a experiência móvel não foi testada naquela revalidação nem encerra critérios alheios aos cinco aprovados.

**Complemento aprovado em 08/11/2026 — histórico funcional:** envio/exibição de fotos na galeria homologados pelo responsável. Código comercial `FINALIDADE + TIPO + "-" + SEQUÊNCIA`, imutável após atribuição, sequência única por empresa entre matriz/filiais, Draft sem código permitido. Prefixos aprovados: VEN/LOC/AMB; AP/CAS/SAL/TER e RUR para Chácara/Fazenda/Sítio. Não inferir prefixos adicionais. Descrição opcional por imagem de até 200 caracteres, editável sem reenviar e exibida na ampliação. O aceite posterior de todos os cinco critérios do Incremento A está no [registro de encerramento](IMOB_CADASTRO.md#cad-incremento-a-homologacao); código comercial, câmera física e outras funções mantêm seus estados independentes.

**Versão:** 1.0 — 07/11/2026, com complemento aprovado de 08/11/2026 acima.  
**Natureza:** consolidação das decisões aprovadas nesta conversa; referência para confrontar com repositório, `AGENTS.md`, `POL-DOC-001`, `IMOB_CADASTRO(1).md` e `IMOB_ARQ_REFERENCIA.md`.  
**Estado:** requisitos funcionais aprovados pelo proprietário; **não** implica implementação, homologação ou publicação. Este arquivo é uma entrega local para encaminhamento ao Codex; **não foi gravado automaticamente no GitHub**.

## 0. Convenções de interação comuns

- Interface responsiva/mobile-first, navegação limpa, sem títulos/campos duplicados; cabeçalho contextual com código do imóvel e proprietário. Ícones funcionais e cores conforme identidade visual aprovada, sem presumir implementação de Figma.
- **Chips clicáveis:** cada opção aparece como botão/etiqueta; clique seleciona, novo clique desmarca; selecionadas têm estado visual distinto, acessível por teclado e leitor de tela. Gravar **identificadores estáveis** e não apenas texto visual. Múltipla seleção salvo onde expressamente indicado. Não gravar uma escolha apenas por exibi-la no protótipo.
- **+ Outros por grupo:** abre campo curto para inserir opção não existente; impedir duplicata normalizada, permitir selecionar/desmarcar, preservar valor customizado ao reabrir, respeitar escopo organizacional e autorizações para reuso. Não criar vocabulários globais irrestritos.
- **Detalhes condicionais:** campos complementares aparecem apenas quando relevantes, não são obrigatórios por padrão. Mudança de finalidade/tipologia não pode apagar silenciosamente dados previamente gravados; definir tratamento seguro de valores ocultos.
- **Draft:** salvar incompleto, reabrir mesmo UUID, preservar alterações parciais e versionamento; ausência ≠ zero; não converter Draft em publicável por completar campos. Não inventar campos obrigatórios legais.
- **Privacidade e escopo:** identidade/licença/filial ativa vêm do Core; servidor valida permissões, isolamento tenant/estabelecimento e visibilidade pública/interna. Publicação depende de seleção, prévia e autorização explícita.
- **Implementação:** primeiro comparar esta especificação com o código e a documentação real. Não reconstruir recursos já implementados. Contratos v1/v2 e migrações existentes exigem compatibilidade.

## 1. Identificação

**Campos e interações:** código comercial estável gerado/controlado pelo sistema; tipo de imóvel; finalidade (venda, locação, temporada, combinações aplicáveis); título/identificação comercial opcional; status de rascunho; proprietário selecionado ou criado **no início** do fluxo por busca. Cabeçalho contextual com ícone de oportunidade comercial, categoria e proprietário, evitando rótulos redundantes.

**Proprietário:** PF por CPF ou PJ por CNPJ, unicidade no âmbito da empresa, UUID interno imutável, busca e reuso (um proprietário pode ter vários imóveis). Alterar dados cadastrais não é o mesmo que substituir vínculo do imóvel; manter rastreabilidade. Sem exigir documentos legais no primeiro cadastro. Não inventar coproprietários como requisito atual.

## 2. Localização

**Campos:** CEP com preenchimento assistido; logradouro, número ou **s/n**, complemento, bairro, município, UF e demais dados estruturados já existentes. Endereço parcial deve ser aceito no Draft.

**Mapa:** apresentar mapa interativo quando provedor/credenciais reais estiverem disponíveis; usuário confirma posição do marcador; lat/long persistidas internamente, sem campos manuais de coordenadas; atualização de CEP/endereço não sobrescreve marcador confirmado silenciosamente. Endereço aproximado para publicação pública conforme autorização específica. Não alegar Google Maps ativo sem verificação de API, chave e condições operacionais.

## 3. Áreas e dimensões

**Campos adaptativos:** área total, construída, privativa, útil e do terreno; frente, fundos, laterais e medidas aplicáveis à tipologia. Rural: hectares/alqueires com indicação da unidade e variante regional quando aplicável; informações descritivas de reserva legal/APP são opcionais e não substituem documentação jurídica.

**Regras:** unidade explícita, preservar valor original; cálculo derivado apenas como **sugestão sujeita à confirmação**; terrenos irregulares não devem receber multiplicação simplista; desconhecido não é zero; não exigir todos os campos.

## 4. Ambientes e características

**Quantidades:** dormitórios, suítes, banheiros, lavabos, vagas; **suítes estão incluídas no total de dormitórios**, não somadas a ele. Salas (estar, jantar, TV), escritório, cozinha, lavanderia/área de serviço; áreas externas, depósitos e demais ambientes aplicáveis. Para temporada, capacidade de hóspedes e camas quando pertinente.

**Interação:** controles numéricos opcionais e lista de ambientes adicionais com inclusão de Outros; adaptar por residencial, comercial, rural, terreno etc.; não repetir equipamentos/estrutura das categorias 5 e 10.

## 5. Estrutura e comodidades

**Padrão:** chips multisseleção por grupos, com **+ Outros em cada grupo** e detalhe opcional por característica quando necessário.

**Grupos temáticos:** segurança/proteção; conforto/climatização; lazer **privativo**; tecnologia/sustentabilidade; acabamentos; instalações técnicas comerciais/industriais; benfeitorias rurais. Exibir grupos aplicáveis ao tipo de imóvel. Características de condomínio pertencem à categoria 11; móveis soltos à categoria 10.

## 6. Valores e condições — incluindo permuta da categoria 14

**Campos:** valor autorizado pelo proprietário e valor público anunciado, separados **quando diferentes**; finalidade venda/locação/temporada/combinações; encargos como IPTU com periodicidade mensal/anual, condomínio com frequência, condições de financiamento e negociabilidade. Valores monetários BRL com precisão decimal; histórico de alteração de preço e autoria. Nenhum preço obrigatório para Draft.

**Permuta:** campo **Aceita permuta?** sim/não; quando sim, mostrar chips multisseleção para preferências: apartamento, casa, terreno, imóvel comercial, imóvel rural, veículo, outro bem; texto complementar opcional (região, faixa, diferença financeira, condições). Essas informações pertencem **à categoria 6**; **não criar etapa 14 independente** na captação. Propostas/contrapropostas efetivas ficam no fluxo posterior de negociação e fechamento.

## 7. Proprietários e captação

**Campos:** responsável pela captação vinculado a usuário/corretor autorizado; origem da captação, data, status, observações internas; exclusividade sim/não e período opcional de início/fim; autorização de publicação separada do ato de publicar. Proprietário já escolhido no início: aqui mostrar vínculo/resumo e permitir gestão autorizada, **sem exigir segundo cadastro**. Cargo não equivale a perfil de acesso.

## 8. Documentação — fora da captação inicial

**Decisão:** não exibir etapa vazia ou exigir documentos jurídicos na captação. Reservar módulo seguro para etapa posterior de comercialização/contratação, conforme finalidade e permissões. Não bloquear Draft por documentação ausente.

## 9. Mídias e vistoria

**Fotos:** seleção múltipla no explorador de arquivos; captura pela câmera do celular quando disponível; upload com confirmação de persistência real, estado/erro; imagem de capa, ordem, ambiente associado, legenda e **descrição curta opcional do diferencial por foto** (limite inicial proposto de **200 caracteres**, editável). Legenda, ambiente, diferencial e observações internas são informações distintas.

**Visibilidade:** por arquivo, público ou interno; respeitar autorização antes de publicar. Vídeos por upload e link externo opcional, conforme suporte e validações. Fotos e observações de vistoria simples durante captação; laudo formal/assinado somente em etapa futura. Reabrir Draft deve preservar mídias, metadados e vínculos.

## 10. Móveis e equipamentos

**Estado de mobília:** sem mobília, semimobiliado, mobiliado, a definir. Chips por ambiente: sala, cozinha/lavanderia, dormitórios, outros equipamentos; **+ Outros por grupo**. Para cada item, quantidade e conservação/observações opcionais; negociação individual: **incluído**, **não incluído**, **a negociar**. Vínculo opcional com foto já cadastrada na categoria 9. Não confundir mobília com características estruturais.

## 11. Condomínio e infraestrutura coletiva

**Condomínio reutilizável** entre imóveis da mesma empresa: buscar existente ou registrar nome, tipo residencial/comercial/misto/outro, torre/bloco/unidade e números de torres/pavimentos opcionais, observações. Não criar entidade duplicada sem verificar modelo existente.

**Chips + Outros por grupo:** segurança e acesso (portaria 24h/virtual, controle de acesso, câmeras, cerca elétrica, biometria); lazer/convivência (piscina, aquecida, churrasqueira, salão de festas, gourmet, playground, brinquedoteca, quadra, academia, sauna); serviços/conveniências (elevadores, lavanderia coletiva, coworking, mercadinho, bicicletário, espaço pet, gerador); infraestrutura/sustentabilidade (energia solar coletiva, reaproveitamento de água, coleta seletiva, carregador elétrico, acessibilidade, vagas visitantes). Detalhes opcionais. Exibir somente quando condomínio/infraestrutura coletiva for aplicável. Taxa condominial permanece na categoria 6; lazer privativo na 5. Mudanças em características compartilhadas precisam ser rastreáveis e não sobrescrever exceções individuais silenciosamente.

## 12. Comissões e taxas internas — referência ao fluxo já aprovado

**Na captação:** condições preliminares de comissão de venda, locação e administração quando aplicáveis; política padrão da imobiliária com exceções por imóvel/negociação; taxas internas e observações com acesso restrito. Não inventar percentual padrão.

**Fluxo posterior já definido:** negociação efetiva → apuração de comissões/taxas → rateio por participantes e beneficiários (internos/externos, PF/PJ conforme contrato), base, percentual, valor, parcelas, vencimento e condição → conferência → aprovação por gestor autorizado **distinto do solicitante** → instrução financeira aprovada/versionada → integração eventual com ERP Financeiro → retornos de estados. Participante comercial e beneficiário financeiro são papéis distintos. Condições à vista, parceladas ou condicionadas são admitidas; alterações após aprovação exigem motivo, versão e revalidação.

**Fronteira:** Imob interpreta contrato e calcula/apura obrigações mesmo sem ERP Financeiro; ERP Financeiro controla títulos, cobrança, recebimento, pagamento, conciliação e caixa. Envio/ACK/aprovação não significa pagamento. Integração exige IDs estáveis, idempotência, correlação, retorno, auditoria e isolamento. **Não implementar o módulo financeiro completo como parte da ficha de captação.** Consultar `IMOB_CADASTRO(1).md` e `IMOB_ARQ_REFERENCIA.md` para contratos e decisões mais detalhadas.

## 13. Garantias aceitas para locação

**Chips multisseleção:** fiador, seguro-fiança, caução em dinheiro, título de capitalização, garantia por instituição financeira, **sem garantia**, + Outros. **Sem garantia** é opção exclusiva, incompatível com demais. Campo complementar opcional **por modalidade**. Sugerir modalidades da política da imobiliária, com ajuste por imóvel. Exibir apenas para locação (inclusive finalidade combinada). Draft não exige garantia.

**Distinção:** modalidade aceita pelo proprietário ≠ modalidade escolhida na negociação ≠ garantia contratada/aprovada. Verificar condições legais na contratação, sem automatizar aceite jurídico.

## 14. Permuta e condições de negociação — integrada, sem tela própria

**Decisão aprovada:** incorporar à categoria 6 chips de preferências de tipos de bens e descrição complementar; não repetir `Aceita permuta?` em outro lugar. Propostas, contrapropostas, descontos, aprovações e fechamento ocorrem em fluxo posterior, com permissões e histórico próprios.

## 15. Critérios de aceite integrados

1. Criar Draft sem proprietário ou preços, salvar e reabrir sem perda.
2. Selecionar/criar proprietário no começo, impedir duplicata por CPF/CNPJ no âmbito da empresa, exibir no cabeçalho.
3. Confirmar localização no mapa sem substituição silenciosa do pin; tratar provedor indisponível honestamente.
4. Selecionar/desmarcar chips, adicionar + Outros em cada grupo pertinente, salvar e reabrir mantendo escolhas e detalhes.
5. Alternar tipologia/finalidade sem perda silenciosa de dados; mostrar campos adaptativos corretos.
6. Registrar valores distintos e histórico; aceitar permuta com preferências na categoria 6; não mostrar categoria 14 como tela.
7. Carregar múltiplas fotos, definir capa, ordem, ambiente, descrição de diferencial, visibilidade, confirmar persistência e reabrir.
8. Inventariar móveis, indicar individualmente inclusão na negociação e vincular foto opcional.
9. Reutilizar condomínio e características coletivas sem duplicar dados da unidade.
10. Garantias de locação múltiplas com condições por modalidade; `Sem garantia` exclusiva.
11. Comissões internas não expostas publicamente; não inferir liquidação financeira de estimativa, aprovação ou ACK.
12. Verificar isolamento por empresa/filial, permissões e compatibilidade dos contratos atuais.
13. Preservar provas anteriores válidas; executar somente testes adicionais relevantes; distinguir implementado, testado, homologado e publicado.
14. Entregar uma jornada integrada local para homologação humana, sem deploy/merge/publicação implícitos.

## 16. Instrução operacional ao Codex

1. Ler este arquivo e as fontes oficiais do repositório antes de modificar código.
2. Construir matriz por categoria e controle: **existente comprovado / parcialmente existente / ausente / dependência externa / fora da captação**.
3. Reaproveitar implementações, contratos e evidências; não criar segunda fonte de verdade.
4. Implementar as lacunas locais autorizadas com UX coerente, validação de servidor, persistência e testes por impacto.
5. Atualizar documentação e tarefas **existentes** no Monday, sem duplicação e sem mudar status sem prova.
6. Entregar relatório de diferenças, evidências, bloqueios reais e roteiro único de homologação humana.

**Limites:** sem commit/push/merge/deploy/produção ou alterações na VPS sem autorização específica; não simular provedores externos, e-mail, mapa ou ERP Financeiro. Não afirmar que um componente foi implementado apenas porque está nesta especificação.
