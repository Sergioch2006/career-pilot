export interface ProfileResponse {
  id: number;
  sourceType: string;
  sourceFileName: string | null;
  fullName: string | null;
  email: string | null;
  characterCount: number;
  rawText: string;
  createdAt: string;
  updatedAt: string;
}

export interface ProfileQaItem {
  id: number;
  userProfileId: number;
  question: string;
  answer: string | null;
  category: string | null;
  createdAt: string;
  answeredAt: string | null;
}

export interface JobGapStudyItem {
  topico: string;
  prioridade: string;
  plano_estudo: string;
}

export interface JobFitAnalysis {
  match_score?: number;
  justificativa?: string;
  pontos_fortes?: string[];
  gaps_e_estudos?: JobGapStudyItem[];
  dossie_empresa?: {
    resumo?: string;
    cultura_e_missao?: string;
    ganchos_why_this_company?: string[];
  };
  vagas_similares_padroes?: string[];
  resposta_recrutador?: {
    linkedin?: string;
    whatsapp?: string;
  };
}

export interface JobOfferResponse {
  id: number;
  title: string;
  company: string;
  url: string | null;
  description: string;
  fitScore: number | null;
  analysisStatus: 'UpToDate' | 'Reanalyzing' | 'Error' | string;
  userProfileId: number | null;
  fitAnalysis: JobFitAnalysis | null;
  createdAt: string;
}

export interface AiExecutionLogItem {
  id: number;
  operation: string;
  jobOfferId: number | null;
  provider: string;
  model: string;
  usedFallback: boolean;
  latencyMs: number;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  estimatedCostUsd: number;
  success: boolean;
  createdAt: string;
}

export interface TelemetryLogsResponse {
  summary: {
    totalExecutions: number;
    totalInputTokens: number;
    totalOutputTokens: number;
    totalEstimatedCostUsd: number;
    avgLatencyMs: number;
  };
  logs: AiExecutionLogItem[];
}

export interface ProjectMilestoneItem {
  id: number;
  title: string;
  description: string;
  category: string;
  tags: string | null;
  isDefault: boolean;
  createdAt: string;
}

export interface AtsResumePreviewResponse {
  fileName: string;
  language: string;
  html: string;
}
