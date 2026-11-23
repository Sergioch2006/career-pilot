import { useEffect, useState } from 'react';
import { Check, Copy, Loader2, MessageCircle, RefreshCw, Send } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { generateRecruiterReply, getJobs } from '../services/api';
import type { JobOfferResponse } from '../types/api';

export function RecruiterReplyPage() {
  const { t } = useTranslation();
  const [jobs, setJobs] = useState<JobOfferResponse[]>([]);
  const [selectedJobId, setSelectedJobId] = useState<number | null>(null);
  const [linkedinText, setLinkedinText] = useState('');
  const [whatsappText, setWhatsappText] = useState('');
  const [isLoading, setIsLoading] = useState(true);
  const [isGenerating, setIsGenerating] = useState(false);
  const [copiedChannel, setCopiedChannel] = useState<'linkedin' | 'whatsapp' | null>(null);
  const [error, setError] = useState<string | null>(null);

  const syncReplyFields = (job: JobOfferResponse | undefined) => {
    setLinkedinText(job?.fitAnalysis?.resposta_recrutador?.linkedin ?? '');
    setWhatsappText(job?.fitAnalysis?.resposta_recrutador?.whatsapp ?? '');
  };

  useEffect(() => {
    let active = true;
    setIsLoading(true);
    getJobs()
      .then((data) => {
        if (!active) return;
        setJobs(data);
        if (data.length > 0) {
          setSelectedJobId(data[0].id);
          syncReplyFields(data[0]);
        }
      })
      .catch((err) => {
        if (active) setError(err instanceof Error ? err.message : t('reply.errors.loadFailed'));
      })
      .finally(() => {
        if (active) setIsLoading(false);
      });

    return () => {
      active = false;
    };
  }, []);

  const selectedJob = jobs.find((j) => j.id === selectedJobId);

  const handleSelectJob = (id: number) => {
    setSelectedJobId(id);
    setError(null);
    syncReplyFields(jobs.find((j) => j.id === id));
  };

  const handleGenerate = async () => {
    if (!selectedJobId) return;
    setIsGenerating(true);
    setError(null);
    try {
      const updated = await generateRecruiterReply(selectedJobId);
      setJobs((prev) => prev.map((j) => (j.id === updated.id ? updated : j)));
      syncReplyFields(updated);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('reply.errors.generateFailed'));
    } finally {
      setIsGenerating(false);
    }
  };

  const handleCopy = async (channel: 'linkedin' | 'whatsapp', text: string) => {
    if (!text.trim()) return;
    await navigator.clipboard.writeText(text);
    setCopiedChannel(channel);
    setTimeout(() => setCopiedChannel(null), 2000);
  };

  return (
    <div className="space-y-6">
      <div className="glass-card rounded-2xl p-6 shadow-xl">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold text-white">{t('reply.title')}</h2>
            <p className="mt-1 text-sm text-slate-400">{t('reply.subtitle')}</p>
          </div>

          {selectedJob && (
            <button
              type="button"
              disabled={isGenerating}
              onClick={() => void handleGenerate()}
              className="inline-flex items-center gap-2 rounded-full bg-lime-400 px-4 py-2 text-xs font-bold text-slate-950 transition hover:bg-lime-300 disabled:opacity-60"
            >
              {isGenerating ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
              <span>
                {linkedinText || whatsappText ? t('reply.regenerateButton') : t('reply.generateButton')}
              </span>
            </button>
          )}
        </div>

        {error && (
          <div className="mt-4 rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-300">
            {error}
          </div>
        )}

        {isLoading ? (
          <div className="flex items-center justify-center gap-2 py-12 text-sm text-slate-400">
            <Loader2 className="h-4 w-4 animate-spin text-violet-400" />
            <span>{t('reply.loading')}</span>
          </div>
        ) : jobs.length === 0 ? (
          <div className="py-10 text-center text-sm text-slate-400">{t('reply.emptyJobs')}</div>
        ) : (
          <div className="mt-5 space-y-5">
            <div>
              <label htmlFor="jobSelect" className="block text-xs font-medium text-slate-300">
                {t('reply.selectJobLabel')}
              </label>
              <select
                id="jobSelect"
                value={selectedJobId ?? ''}
                onChange={(e) => handleSelectJob(Number(e.target.value))}
                className="mt-1.5 w-full rounded-xl border border-white/10 bg-[#0b0b12] px-3.5 py-2.5 text-sm text-white focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-500/30"
              >
                {jobs.map((job) => (
                  <option key={job.id} value={job.id}>
                    #{job.id} — {job.title} ({job.company})
                  </option>
                ))}
              </select>
            </div>

            {!linkedinText && !whatsappText ? (
              <div className="rounded-xl border border-dashed border-white/15 bg-[#0b0b12] p-6 text-center text-sm text-slate-400">
                {t('reply.emptyReply')}
              </div>
            ) : (
              <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
                <div className="flex flex-col rounded-2xl border border-white/10 bg-[#0b0b12] p-4">
                  <div className="mb-2.5 flex items-center justify-between">
                    <span className="inline-flex items-center gap-1.5 text-sm font-semibold text-white">
                      <Send className="h-4 w-4 text-violet-400" />
                      {t('reply.linkedin')}
                    </span>
                    <button
                      type="button"
                      onClick={() => void handleCopy('linkedin', linkedinText)}
                      className="inline-flex items-center gap-1 rounded-full border border-white/15 bg-white/5 px-3 py-1 text-xs font-medium text-slate-200 hover:bg-white/10"
                    >
                      {copiedChannel === 'linkedin' ? (
                        <>
                          <Check className="h-3.5 w-3.5 text-lime-400" />
                          <span>{t('reply.copied')}</span>
                        </>
                      ) : (
                        <>
                          <Copy className="h-3.5 w-3.5" />
                          <span>{t('reply.copy')}</span>
                        </>
                      )}
                    </button>
                  </div>
                  <textarea
                    rows={7}
                    value={linkedinText}
                    onChange={(e) => setLinkedinText(e.target.value)}
                    className="w-full flex-1 rounded-xl border border-white/10 bg-[#11111b] p-3 text-sm leading-relaxed text-slate-200 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-500/30"
                  />
                </div>

                <div className="flex flex-col rounded-2xl border border-white/10 bg-[#0b0b12] p-4">
                  <div className="mb-2.5 flex items-center justify-between">
                    <span className="inline-flex items-center gap-1.5 text-sm font-semibold text-white">
                      <MessageCircle className="h-4 w-4 text-lime-400" />
                      {t('reply.whatsapp')}
                    </span>
                    <button
                      type="button"
                      onClick={() => void handleCopy('whatsapp', whatsappText)}
                      className="inline-flex items-center gap-1 rounded-full border border-white/15 bg-white/5 px-3 py-1 text-xs font-medium text-slate-200 hover:bg-white/10"
                    >
                      {copiedChannel === 'whatsapp' ? (
                        <>
                          <Check className="h-3.5 w-3.5 text-lime-400" />
                          <span>{t('reply.copied')}</span>
                        </>
                      ) : (
                        <>
                          <Copy className="h-3.5 w-3.5" />
                          <span>{t('reply.copy')}</span>
                        </>
                      )}
                    </button>
                  </div>
                  <textarea
                    rows={7}
                    value={whatsappText}
                    onChange={(e) => setWhatsappText(e.target.value)}
                    className="w-full flex-1 rounded-xl border border-white/10 bg-[#11111b] p-3 text-sm leading-relaxed text-slate-200 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-500/30"
                  />
                </div>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
