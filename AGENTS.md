# AGENTS.md — ERP Imobiliário AraIa

Para status, retomada e diagnóstico do ambiente local persistente Core + Imob, use `../business-platform-core-s2-1-ui/scripts/araia_local_environment.ps1` ou a Skill Codex `araia-local-environment`; migrations e GRANTs exigem autorização operacional separada.

## Fontes e limites

- Para regras e decisões do produto, consulte somente os documentos relevantes em `docs/`, especialmente `IMOB_END_01.md`, `IMOB_END_02.md`, `IMOB_UX_02.md`, `IMOB_MID_01.md`, `IMOB_CADASTRO.md`, `PLATFORM_DECISIONS.md`, catálogo e migrations/código/testes afetados.
- Para padrões transversais, consulte `business-platform-core/docs/standards/DEVELOPMENT_STANDARDS.md` e `POL-DOC-001.md`. O Framework `DEVELOPMENT_STANDARDS.md` rege convenções de código; não é catálogo de componentes UI.
- Antes de criar tela ou fluxo de tela, aplicar `ARS-FUNC-001`, `ARS-TPL-001` e o gate proporcional `ARS-GATE-001` da fonte transversal Core: localizar contratos e componentes existentes no ERP, confirmar referência visual aprovada, documentar lacunas em vez de inventar modelo definitivo e manter domínio, serviços e autorização sob responsabilidade do Imob. Correções pequenas verificam somente critérios afetados.
- Para arquitetura empresarial e fronteiras, use `erp-imobiliario-enterprise-architecture/architecture/IMOB_ARQ_REFERENCIA.md` e documentos relacionados, confirmando sempre decisão e estado.
- `docs/ui/araia-assets.manifest.json` identifica um snapshot local do repositório `business-platform-core-s2-1-ui`, com `publishedRelease: false`. Não o tratar como biblioteca versionada/distribuível nem afirmar que os assets são um release oficial.
- Documentos derivados apontam para a regra oficial; atualizar em vez de duplicar. Registrar IDs, versão, estado, decisão de origem e histórico para regras materiais, distinguindo Proposto, Aprovado, Implementado, Validado tecnicamente, Homologado e Publicado.

## Execução

- Reutilizar padrões implementados e testados; documentar lacunas quando componente compartilhado não existe. Não inferir disponibilidade de API/serviço externo.
- Preservar autonomia do ERP, contratos v1/v2, Core, limites de produto, organização/estabelecimento, permissões, RLS e decisões aprovadas.
- Executar validação proporcional ao diff, reutilizando evidência apenas com baseline, ambiente e escopo aplicáveis; separar execução nova, evidência histórica, cobertura ausente e aceite humano.
- Manter registros locais de dados sintéticos separados de produção; não executar operações em infraestrutura persistente sem autorização específica.
- Atualizar a fonte oficial de regra após mudança de comportamento. Documentar comandos e estados reais sem promover aceite, gate ou publicação por inferência.
- Sem autorização explícita aplicável, não alterar produto fora do escopo, banco/contratos aprovados, credenciais, infraestrutura, Monday, nem executar commit/push/merge/deploy.

