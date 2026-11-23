---
name: job-fit-analyzer
description: Analisa uma vaga ofertada cruzando com o perfil do candidato, pesquisa a empresa via MCP e compara com vagas similares no mercado.
---

# Instruções
1. Leia a descrição da vaga e extraia: requisitos obrigatórios, desejáveis, stack, senioridade, modelo de trabalho e faixa salarial (se houver).
2. Acione as ferramentas MCP disponíveis para:
   - Buscar no site oficial da empresa: missão, valores, produtos principais e tempo de mercado.
   - Buscar 3 a 5 vagas similares (mesmo cargo/stack) no LinkedIn/web para identificar padrões de mercado e requisitos ocultos.
3. Compare com o Perfil + `ProfileQA` do usuário e retorne JSON contendo:
   - `match_score` (0-100) e justificativa.
   - `pontos_fortes` (evidências reais do currículo).
   - `gaps_e_estudos` (tópicos técnicos/comportamentais para revisar antes da entrevista).
   - `dossie_empresa` (cultura, missão e ganchos para a pergunta "Why this company?").