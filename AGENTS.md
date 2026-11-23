# Time de Multiagentes - CareerPilot Local

## Regras Gerais
- Projeto 100% local-first.
- Backend: .NET 10 (ASP.NET Core Web API) na pasta `/src/backend`.
- Frontend: React + Vite + TypeScript + Tailwind na pasta `/src/frontend`.
- Banco de Dados: SQLite local (arquivo `careerpilot.db`).
- IA Agnóstica: Usar `Microsoft.Extensions.AI` (`IChatClient` / `IEmbeddingGenerator`) para permitir troca entre Gemini, OpenAI, Anthropic ou Ollama via configuração.
- Engenharia de Plataforma: Todas as chamadas de IA e MCP devem ter resiliência (Polly), telemetria de tokens/latência (OpenTelemetry) e validação via testes automatizados (Evaluation Gates).
- Internacionalização (i18n): Todo texto visível no Frontend deve usar `react-i18next` (nunca textos fixos no código), com suporte completo a `PT-br` e `EN-us` via seletor persistente no cabeçalho.

## Papéis dos Agentes
1. **@Architect**: Define estrutura de pastas, tabelas do banco e contratos da API.
2. **@Backend**: Escreve código C#, endpoints REST, leitura de PDF/DOCX, conexão com SQLite/MCP, Polly e OpenTelemetry.
3. **@Frontend**: Cria as telas em React, formulários de upload/paste, checklist de aprendizados e visualização de PDFs.
4. **@PDF_Specialist**: Cria templates HTML/CSS (Guia de Entrevista e Currículo ATS) e gera os arquivos finais.
5. **@QA_Platform**: Cria testes de Evaluation Gates (`xUnit`) para validar qualidade, schema JSON e consistência das respostas da IA.