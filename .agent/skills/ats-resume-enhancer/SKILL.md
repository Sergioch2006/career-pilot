\---

name: ats-resume-enhancer

description: Gera uma versão do currículo otimizada para sistemas ATS para uma vaga específica, permitindo injetar dinamicamente conquistas e tecnologias aprendidas no desenvolvimento do CareerPilot (ou outros projetos pessoais).

\---



\# Instruções

1\. Receba:

&#x20;  - O Perfil Base do candidato (`UserProfile` + `ProfileQA`).

&#x20;  - A descrição da Vaga Alvo (`JobOffers`).

&#x20;  - A lista de "Conquistas/Aprendizados Selecionados" (ex: implementação de MCP Client em .NET 10, abstração agnóstica com Microsoft.Extensions.AI, Polly fallback, OpenTelemetry, Eval Gates).

2\. Extraia as palavras-chave exatas (hard skills e termos arquiteturais) da Vaga Alvo.

3\. Reescreva o currículo mantendo 100% de veracidade, incorporando os aprendizados selecionados do projeto CareerPilot na seção de "Projetos Relevantes" ou "Experiência Técnica", traduzindo-os para a linguagem e palavras-chave que o ATS da vaga procura.

4\. Regras de formatação ATS:

&#x20;  - Layout de coluna única, sem gráficos, tabelas complexas ou ícones que quebrem leitores automáticos.

&#x20;  - Títulos padrão (Summary, Core Skills, Professional Experience, Relevant Projects, Education).

&#x20;  - Bullet points no formato Ação + Tecnologia + Resultado.

5\. Retorne o currículo estruturado pronto para exportação em `.pdf` (texto selecionável) e `.docx`.

