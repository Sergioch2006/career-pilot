import { useEffect, useState } from 'react';
import {
  Activity,
  Briefcase,
  CheckCircle2,
  Eye,
  FileCheck2,
  FileDown,
  Loader2,
  Plus,
  Sparkles,
} from 'lucide-react';
import { useTranslation } from 'react-i18next';
import {
  analyzeJobOffer,
  createMilestone,
  downloadCustomAtsResume,
  downloadJobInterviewPdf,
  generateCustomAtsPreview,
  getJobs,
  getMilestones,
  getTelemetryLogs,
} from '../services/api';
import type {
  AtsResumePreviewResponse,
  JobOfferResponse,
  ProjectMilestoneItem,
  TelemetryLogsResponse,
} from '../types/api';

function MatchRing({ score, label }: { score: number; label: string }) {
  const clamped = Math.max(0, Math.min(100, score));
  const radius = 34;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference - (clamped / 100) * circumference;

  return (
    <div className="relative flex h-24 w-24 shrink-0 items-center justify-center">
      <svg className="h-24 w-24 -rotate-90" viewBox="0 0 84 84">
        <circle cx="42" cy="42" r={radius} stroke="rgba(255,255,255,0.1)" strokeWidth="7" fill="none" />
        <circle
          cx="42"
          cy="42"
          r={radius}
          stroke="#8b5cf6"
          strokeWidth="7"
          strokeLinecap="round"
          strokeDasharray={circumference}
          strokeDashoffset={offset}
          fill="none"
        />
      </svg>
      <div className="absolute flex flex-col items-center text-center">
        <span className="text-lg font-bold text-lime-400">{clamped}%</span>
        <span className="text-[9px] font-semibold tracking-widest text-slate-400">{label}</span>
      </div>
    </div>
  );
}

export function JobOffersPage() {
  const { t, i18n } = useTranslation();
  const [jobs, setJobs] = useState<JobOfferResponse[]>([]);
  const [milestones, setMilestones] = useState<ProjectMilestoneItem[]>([]);
  const [selectedMilestonesByJob, setSelectedMilestonesByJob] = useState<Record<number, number[]>>({});
  const [activeJobTab, setActiveJobTab] = useState<Record<number, 'analysis' | 'ats'>>({});
  const [previewByJob, setPreviewByJob] = useState<Record<number, AtsResumePreviewResponse | null>>({});

  const [title, setTitle] = useState('');
  const [company, setCompany] = useState('');
  const [url, setUrl] = useState('');
  const [description, setDescription] = useState('');

  const [newMilestoneTitle, setNewMilestoneTitle] = useState('');
  const [newMilestoneDesc, setNewMilestoneDesc] = useState('');
  const [newMilestoneTags, setNewMilestoneTags] = useState('');
  const [isAddingMilestone, setIsAddingMilestone] = useState(false);

  const [isLoading, setIsLoading] = useState(true);
  const [isAnalyzing, setIsAnalyzing] = useState(false);
  const [downloadingGuideId, setDownloadingGuideId] = useState<number | null>(null);
  const [atsBusyJobId, setAtsBusyJobId] = useState<number | null>(null);
  const [atsSavedNotice, setAtsSavedNotice] = useState<string | null>(null);
  const [telemetry, setTelemetry] = useState<TelemetryLogsResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadData = async () => {
    setIsLoading(true);
    setError(null);
    try {
      const [jobsData, milestonesData, telemetryData] = await Promise.all([
        getJobs(),
        getMilestones().catch(() => [] as ProjectMilestoneItem[]),
        getTelemetryLogs().catch(() => null),
      ]);
      setJobs(jobsData);
      setMilestones(milestonesData);
      setTelemetry(telemetryData);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('jobs.errors.loadFailed'));
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    void loadData();
  }, []);

  useEffect(() => {
    const hasReanalyzing = jobs.some((j) => j.analysisStatus === 'Reanalyzing');
    if (!hasReanalyzing) return;

    const timer = setInterval(() => {
      void Promise.all([
        getJobs().then(setJobs).catch(() => {}),
        getTelemetryLogs().then((t) => setTelemetry(t)).catch(() => {}),
      ]);
    }, 3000);

    return () => clearInterval(timer);
  }, [jobs]);

  const getSelectedIds = (jobId: number): number[] =>
    selectedMilestonesByJob[jobId] ?? milestones.map((m) => m.id);

  const toggleMilestone = (jobId: number, milestoneId: number) => {
    const current = getSelectedIds(jobId);
    const next = current.includes(milestoneId)
      ? current.filter((id) => id !== milestoneId)
      : [...current, milestoneId];
    setSelectedMilestonesByJob((prev) => ({ ...prev, [jobId]: next }));
  };

  const handleAddMilestone = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!newMilestoneTitle.trim() || !newMilestoneDesc.trim()) return;

    setIsAddingMilestone(true);
    setError(null);
    try {
      const created = await createMilestone({
        title: newMilestoneTitle.trim(),
        description: newMilestoneDesc.trim(),
        tags: newMilestoneTags.trim() || undefined,
      });
      setMilestones((prev) => [...prev, created]);
      setNewMilestoneTitle('');
      setNewMilestoneDesc('');
      setNewMilestoneTags('');
    } catch (err) {
      setError(err instanceof Error ? err.message : t('jobs.errors.saveMilestoneFailed'));
    } finally {
      setIsAddingMilestone(false);
    }
  };

  const handleAnalyze = async (event: React.FormEvent) => {
    event.preventDefault();
    setError(null);
    setAtsSavedNotice(null);

    if (!description.trim() && !url.trim()) {
      setError(t('jobs.errors.missingDescriptionOrUrl'));
      return;
    }

    setIsAnalyzing(true);
    try {
      const created = await analyzeJobOffer({
        title: title.trim() || undefined,
        company: company.trim() || undefined,
        url: url.trim() || undefined,
        description: description.trim() || undefined,
      });
      setJobs((prev) => [created, ...prev]);
      setTitle('');
      setCompany('');
      setUrl('');
      setDescription('');
      const updatedTelemetry = await getTelemetryLogs().catch(() => null);
      if (updatedTelemetry) setTelemetry(updatedTelemetry);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('jobs.errors.analyzeFailed'));
    } finally {
      setIsAnalyzing(false);
    }
  };

  const handleDownloadGuidePdf = async (jobId: number) => {
    setDownloadingGuideId(jobId);
    setError(null);
    try {
      await downloadJobInterviewPdf(jobId);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('jobs.errors.guidePdfFailed'));
    } finally {
      setDownloadingGuideId(null);
    }
  };

  const handlePreviewAts = async (jobId: number) => {
    setAtsBusyJobId(jobId);
    setError(null);
    try {
      const preview = await generateCustomAtsPreview(jobId, getSelectedIds(jobId));
      setPreviewByJob((prev) => ({ ...prev, [jobId]: preview }));
    } catch (err) {
      setError(err instanceof Error ? err.message : t('jobs.errors.previewFailed'));
    } finally {
      setAtsBusyJobId(null);
    }
  };

  const handleDownloadAts = async (jobId: number, format: 'pdf' | 'docx') => {
    setAtsBusyJobId(jobId);
    setError(null);
    setAtsSavedNotice(null);
    try {
      const fileName = await downloadCustomAtsResume(jobId, getSelectedIds(jobId), format);
      setAtsSavedNotice(t('jobs.atsSavedNotice', { fileName }));
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : t('jobs.errors.exportFailed', { format: format.toUpperCase() })
      );
    } finally {
      setAtsBusyJobId(null);
    }
  };

  return (
    <div className="space-y-6">
      <div className="glass-card rounded-2xl p-6 shadow-xl">
        <div className="flex items-center gap-2">
          <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-lime-400 text-xs font-bold text-slate-950">
            1
          </span>
          <h2 className="text-lg font-semibold text-white">{t('jobs.formTitle')}</h2>
        </div>
        <p className="mt-1 text-sm text-slate-400">{t('jobs.formSubtitle')}</p>

        <form onSubmit={handleAnalyze} className="mt-5 space-y-4">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="jobTitle" className="block text-xs font-medium text-slate-300">
                {t('jobs.titleLabel')} <span className="text-slate-500">{t('jobs.optional')}</span>
              </label>
              <input
                id="jobTitle"
                type="text"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder={t('jobs.titlePlaceholder')}
                className="mt-1.5 w-full rounded-xl border border-white/10 bg-[#0b0b12] px-3.5 py-2.5 text-sm text-white placeholder:text-slate-500 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-500/30"
              />
            </div>

            <div>
              <label htmlFor="jobCompany" className="block text-xs font-medium text-slate-300">
                {t('jobs.companyLabel')} <span className="text-slate-500">{t('jobs.optional')}</span>
              </label>
              <input
                id="jobCompany"
                type="text"
                value={company}
                onChange={(e) => setCompany(e.target.value)}
                placeholder={t('jobs.companyPlaceholder')}
                className="mt-1.5 w-full rounded-xl border border-white/10 bg-[#0b0b12] px-3.5 py-2.5 text-sm text-white placeholder:text-slate-500 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-500/30"
              />
            </div>
          </div>

          <div>
            <label htmlFor="jobUrl" className="block text-xs font-medium text-slate-300">
              {t('jobs.urlLabel')} <span className="text-slate-500">{t('jobs.optional')}</span>
            </label>
            <input
              id="jobUrl"
              type="url"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder={t('jobs.urlPlaceholder')}
              className="mt-1.5 w-full rounded-xl border border-white/10 bg-[#0b0b12] px-3.5 py-2.5 text-sm text-white placeholder:text-slate-500 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-500/30"
            />
          </div>

          <div>
            <label htmlFor="jobDescription" className="block text-xs font-medium text-slate-300">
              {t('jobs.descriptionLabel')}
            </label>
            <textarea
              id="jobDescription"
              rows={5}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder={t('jobs.descriptionPlaceholder')}
              className="mt-1.5 w-full rounded-xl border border-white/10 bg-[#0b0b12] px-3.5 py-2.5 text-sm text-white placeholder:text-slate-500 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-500/30"
            />
          </div>

          {error && (
            <div className="rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-300">
              {error}
            </div>
          )}

          {atsSavedNotice && (
            <div className="flex items-center gap-2 rounded-xl border border-lime-400/30 bg-lime-400/10 px-4 py-3 text-sm text-lime-300">
              <CheckCircle2 className="h-4 w-4 shrink-0 text-lime-400" />
              <span>{atsSavedNotice}</span>
            </div>
          )}

          <div className="flex justify-end">
            <button
              type="submit"
              disabled={isAnalyzing}
              className="inline-flex items-center gap-2 rounded-full bg-lime-400 px-6 py-2.5 text-sm font-bold text-slate-950 shadow-[0_0_20px_rgba(163,230,53,0.25)] transition hover:bg-lime-300 disabled:opacity-60"
            >
              {isAnalyzing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
              <span>{isAnalyzing ? t('jobs.analyzingButton') : t('jobs.analyzeButton')}</span>
            </button>
          </div>
        </form>
      </div>

      <div className="space-y-4">
        <h3 className="text-base font-semibold text-white">{t('jobs.listTitle')}</h3>

        {isLoading ? (
          <div className="glass-card flex items-center justify-center gap-2 rounded-2xl py-10 text-sm text-slate-400">
            <Loader2 className="h-4 w-4 animate-spin text-violet-400" />
            <span>{t('jobs.loading')}</span>
          </div>
        ) : jobs.length === 0 ? (
          <div className="glass-card rounded-2xl py-10 text-center text-sm text-slate-400">
            {t('jobs.emptyState')}
          </div>
        ) : (
          jobs.map((job) => {
            const tab = activeJobTab[job.id] ?? 'analysis';
            const isDownloadingGuide = downloadingGuideId === job.id;
            const isAtsBusy = atsBusyJobId === job.id;
            const gaps = job.fitAnalysis?.gaps_e_estudos ?? [];
            const strengths = job.fitAnalysis?.pontos_fortes ?? [];
            const selectedIds = getSelectedIds(job.id);
            const preview = previewByJob[job.id];

            return (
              <div key={job.id} className="glass-card rounded-2xl p-6 shadow-xl">
                <div className="flex flex-col gap-5 sm:flex-row sm:items-center sm:justify-between">
                  <div className="flex items-center gap-4">
                    {job.fitScore !== null ? (
                      <MatchRing score={job.fitScore} label={t('jobs.matchLabel')} />
                    ) : (
                      <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-violet-600/20 text-violet-300">
                        <Briefcase className="h-6 w-6" />
                      </div>
                    )}
                    <div>
                      <div className="flex flex-wrap items-center gap-2">
                        <h4 className="text-lg font-bold text-white">{job.title}</h4>
                        <span
                          className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-[10px] font-semibold ${
                            job.analysisStatus === 'Reanalyzing'
                              ? 'bg-amber-500/20 text-amber-300'
                              : job.analysisStatus === 'Error'
                                ? 'bg-red-500/20 text-red-300'
                                : 'bg-lime-400/15 text-lime-300'
                          }`}
                        >
                          {job.analysisStatus === 'Reanalyzing' && <Loader2 className="h-3 w-3 animate-spin" />}
                          <span>
                            {t(`jobs.status.${job.analysisStatus}`, { defaultValue: job.analysisStatus })}
                          </span>
                        </span>
                      </div>
                      <p className="text-sm text-violet-300">{job.company}</p>
                      <div className="mt-2 flex flex-wrap gap-2 text-[11px] text-slate-300">
                        <button
                          type="button"
                          onClick={() => setActiveJobTab((p) => ({ ...p, [job.id]: 'analysis' }))}
                          className={`rounded-full px-3 py-1 font-medium transition ${
                            tab === 'analysis'
                              ? 'bg-violet-600 text-white'
                              : 'border border-white/10 bg-white/5 text-slate-300 hover:bg-white/10'
                          }`}
                        >
                          {t('jobs.tabAnalysis')}
                        </button>
                        <button
                          type="button"
                          onClick={() => setActiveJobTab((p) => ({ ...p, [job.id]: 'ats' }))}
                          className={`inline-flex items-center gap-1 rounded-full px-3 py-1 font-medium transition ${
                            tab === 'ats'
                              ? 'bg-lime-400 font-bold text-slate-950'
                              : 'border border-white/10 bg-white/5 text-slate-300 hover:bg-white/10'
                          }`}
                        >
                          <FileCheck2 className="h-3.5 w-3.5" />
                          <span>{t('jobs.tabAts')}</span>
                        </button>
                      </div>
                    </div>
                  </div>

                  <div className="flex flex-wrap items-center gap-2.5">
                    <button
                      type="button"
                      disabled={isDownloadingGuide}
                      onClick={() => void handleDownloadGuidePdf(job.id)}
                      className="inline-flex items-center gap-2 rounded-full border border-white/15 bg-white/5 px-4 py-2 text-xs font-semibold text-white transition hover:bg-white/10 disabled:opacity-60"
                    >
                      {isDownloadingGuide ? (
                        <Loader2 className="h-4 w-4 animate-spin" />
                      ) : (
                        <FileDown className="h-4 w-4 text-violet-400" />
                      )}
                      <span>{isDownloadingGuide ? t('jobs.generatingGuidePdf') : t('jobs.downloadGuidePdf')}</span>
                    </button>
                  </div>
                </div>

                {tab === 'analysis' ? (
                  <>
                    {job.fitAnalysis?.justificativa && (
                      <p className="mt-4 text-sm leading-relaxed text-slate-300">{job.fitAnalysis.justificativa}</p>
                    )}

                    {strengths.length > 0 && (
                      <div className="mt-4">
                        <h5 className="text-xs font-semibold uppercase tracking-wider text-lime-400">
                          {t('jobs.strengthsTitle')}
                        </h5>
                        <ul className="mt-2 space-y-1.5 text-sm text-slate-300">
                          {strengths.map((s, idx) => (
                            <li key={idx} className="flex items-start gap-2">
                              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-lime-400" />
                              <span>{s}</span>
                            </li>
                          ))}
                        </ul>
                      </div>
                    )}

                    {gaps.length > 0 && (
                      <div className="mt-5">
                        <h5 className="text-xs font-semibold uppercase tracking-wider text-violet-300">
                          {t('jobs.gapsTitle')}
                        </h5>
                        <div className="mt-2 grid grid-cols-1 gap-2.5 md:grid-cols-2">
                          {gaps.map((g, idx) => (
                            <div key={idx} className="rounded-xl border border-white/10 bg-[#0b0b12] p-3.5 text-sm">
                              <div className="flex items-center justify-between gap-2 font-semibold text-white">
                                <span>{g.topico}</span>
                                <span className="rounded-full bg-violet-500/20 px-2 py-0.5 text-[10px] uppercase text-violet-300">
                                  {g.prioridade}
                                </span>
                              </div>
                              <p className="mt-1.5 text-xs leading-relaxed text-slate-400">{g.plano_estudo}</p>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}
                  </>
                ) : (
                  <div className="mt-5 space-y-5 border-t border-white/10 pt-5">
                    <div>
                      <h5 className="text-xs font-semibold uppercase tracking-wider text-lime-400">
                        {t('jobs.milestonesPrompt')}
                      </h5>
                      <div className="mt-3 grid grid-cols-1 gap-2.5 md:grid-cols-2">
                        {milestones.map((m) => {
                          const checked = selectedIds.includes(m.id);
                          return (
                            <label
                              key={m.id}
                              className={`flex cursor-pointer items-start gap-3 rounded-xl border p-3 text-xs transition ${
                                checked
                                  ? 'border-lime-400/50 bg-lime-400/10'
                                  : 'border-white/10 bg-[#0b0b12] hover:border-white/20'
                              }`}
                            >
                              <input
                                type="checkbox"
                                checked={checked}
                                onChange={() => toggleMilestone(job.id, m.id)}
                                className="mt-0.5 h-4 w-4 rounded accent-lime-400"
                              />
                              <div className="space-y-1">
                                <div className="flex flex-wrap items-center gap-1.5 font-semibold text-white">
                                  <span>{m.title}</span>
                                  <span className="rounded-full bg-violet-500/20 px-2 py-0.5 text-[10px] text-violet-300">
                                    {m.category}
                                  </span>
                                </div>
                                <p className="leading-relaxed text-slate-400">{m.description}</p>
                              </div>
                            </label>
                          );
                        })}
                      </div>
                    </div>

                    <form
                      onSubmit={handleAddMilestone}
                      className="space-y-3 rounded-xl border border-white/10 bg-[#0b0b12] p-4"
                    >
                      <div className="flex items-center gap-1.5 text-xs font-semibold text-slate-300">
                        <Plus className="h-3.5 w-3.5 text-lime-400" />
                        <span>{t('jobs.addMilestoneTitle')}</span>
                      </div>
                      <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2">
                        <input
                          type="text"
                          value={newMilestoneTitle}
                          onChange={(e) => setNewMilestoneTitle(e.target.value)}
                          placeholder={t('jobs.milestoneTitlePlaceholder')}
                          className="rounded-lg border border-white/10 bg-[#11111b] px-3 py-2 text-xs text-white placeholder:text-slate-500 focus:border-violet-500 focus:outline-none"
                        />
                        <input
                          type="text"
                          value={newMilestoneTags}
                          onChange={(e) => setNewMilestoneTags(e.target.value)}
                          placeholder={t('jobs.milestoneTagsPlaceholder')}
                          className="rounded-lg border border-white/10 bg-[#11111b] px-3 py-2 text-xs text-white placeholder:text-slate-500 focus:border-violet-500 focus:outline-none"
                        />
                      </div>
                      <div className="flex flex-col gap-2 sm:flex-row">
                        <input
                          type="text"
                          value={newMilestoneDesc}
                          onChange={(e) => setNewMilestoneDesc(e.target.value)}
                          placeholder={t('jobs.milestoneDescPlaceholder')}
                          className="flex-1 rounded-lg border border-white/10 bg-[#11111b] px-3 py-2 text-xs text-white placeholder:text-slate-500 focus:border-violet-500 focus:outline-none"
                        />
                        <button
                          type="submit"
                          disabled={isAddingMilestone || !newMilestoneTitle.trim() || !newMilestoneDesc.trim()}
                          className="rounded-full border border-white/15 bg-white/10 px-4 py-2 text-xs font-semibold text-white hover:bg-white/15 disabled:opacity-50"
                        >
                          {isAddingMilestone ? t('jobs.savingMilestone') : t('jobs.saveMilestone')}
                        </button>
                      </div>
                    </form>

                    <div className="flex flex-wrap items-center gap-3">
                      <button
                        type="button"
                        disabled={isAtsBusy}
                        onClick={() => void handlePreviewAts(job.id)}
                        className="inline-flex items-center gap-2 rounded-full border border-violet-500/50 bg-violet-600/20 px-4 py-2 text-xs font-semibold text-violet-200 transition hover:bg-violet-600/30 disabled:opacity-60"
                      >
                        {isAtsBusy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Eye className="h-4 w-4" />}
                        <span>{t('jobs.previewAtsButton')}</span>
                      </button>

                      <button
                        type="button"
                        disabled={isAtsBusy}
                        onClick={() => void handleDownloadAts(job.id, 'pdf')}
                        className="inline-flex items-center gap-2 rounded-full bg-lime-400 px-4 py-2 text-xs font-bold text-slate-950 transition hover:bg-lime-300 disabled:opacity-60"
                      >
                        <FileDown className="h-4 w-4" />
                        <span>{t('jobs.downloadPdfButton')}</span>
                      </button>

                      <button
                        type="button"
                        disabled={isAtsBusy}
                        onClick={() => void handleDownloadAts(job.id, 'docx')}
                        className="inline-flex items-center gap-2 rounded-full border border-white/15 bg-white/5 px-4 py-2 text-xs font-semibold text-white transition hover:bg-white/10 disabled:opacity-60"
                      >
                        <FileDown className="h-4 w-4 text-lime-400" />
                        <span>{t('jobs.downloadDocxButton')}</span>
                      </button>
                    </div>

                    {preview && (
                      <div className="space-y-2">
                        <div className="flex items-center justify-between text-xs text-slate-400">
                          <span>
                            {t('jobs.previewHeader', {
                              lang: preview.language.toUpperCase(),
                              fileName: preview.fileName,
                            })}
                          </span>
                        </div>
                        <iframe
                          title={t('jobs.previewIframeTitle', { id: job.id })}
                          srcDoc={preview.html}
                          className="h-[520px] w-full rounded-xl border border-white/15 bg-white"
                        />
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })
        )}
      </div>

      {telemetry && telemetry.logs.length > 0 && (
        <div className="glass-card rounded-2xl p-6 shadow-xl">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-white/10 pb-3">
            <div className="flex items-center gap-2">
              <Activity className="h-4 w-4 text-lime-400" />
              <h3 className="text-sm font-semibold text-white">{t('jobs.telemetry.title')}</h3>
            </div>
            <div className="flex flex-wrap gap-3 text-xs text-slate-400">
              <span>
                {t('jobs.telemetry.executions')}:{' '}
                <strong className="text-white">{telemetry.summary.totalExecutions}</strong>
              </span>
              <span>
                {t('jobs.telemetry.avgLatency')}:{' '}
                <strong className="text-white">{telemetry.summary.avgLatencyMs} ms</strong>
              </span>
              <span>
                {t('jobs.telemetry.tokens')}:{' '}
                <strong className="text-white">
                  {(telemetry.summary.totalInputTokens + telemetry.summary.totalOutputTokens).toLocaleString(
                    i18n.language
                  )}
                </strong>
              </span>
              <span>
                {t('jobs.telemetry.estimatedCost')}:{' '}
                <strong className="text-lime-400">${telemetry.summary.totalEstimatedCostUsd.toFixed(6)}</strong>
              </span>
            </div>
          </div>

          <div className="mt-3 overflow-x-auto">
            <table className="w-full text-left text-xs text-slate-300">
              <thead className="border-b border-white/10 text-[11px] uppercase text-slate-500">
                <tr>
                  <th className="py-2 pr-3">{t('jobs.telemetry.colProvider')}</th>
                  <th className="py-2 pr-3">{t('jobs.telemetry.colModel')}</th>
                  <th className="py-2 pr-3">{t('jobs.telemetry.colLatency')}</th>
                  <th className="py-2 pr-3">{t('jobs.telemetry.colTokens')}</th>
                  <th className="py-2">{t('jobs.telemetry.colCost')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-white/5">
                {telemetry.logs.slice(0, 5).map((log) => (
                  <tr key={log.id}>
                    <td className="py-2 pr-3 font-medium text-white">
                      {log.provider}
                      {log.usedFallback && (
                        <span className="ml-1.5 rounded-full bg-amber-500/20 px-1.5 py-0.5 text-[10px] text-amber-300">
                          {t('jobs.telemetry.fallbackBadge')}
                        </span>
                      )}
                    </td>
                    <td className="py-2 pr-3 text-slate-400">{log.model}</td>
                    <td className="py-2 pr-3">{log.latencyMs} ms</td>
                    <td className="py-2 pr-3">
                      {log.inputTokens} / {log.outputTokens}
                    </td>
                    <td className="py-2 text-lime-400">${log.estimatedCostUsd.toFixed(6)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
