import { useEffect, useState } from 'react';
import { Bot, Check, Loader2, MessageSquarePlus, Send, Sparkles, User } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import {
  createProfileQa,
  generateInterviewQuestions,
  getProfileQAs,
  saveProfileQaAnswer,
} from '../services/api';
import type { ProfileQaItem } from '../types/api';

export function ProfileInterviewPage() {
  const { t } = useTranslation();
  const [items, setItems] = useState<ProfileQaItem[]>([]);
  const [draftAnswers, setDraftAnswers] = useState<Record<number, string>>({});
  const [editingId, setEditingId] = useState<number | null>(null);
  const [savingId, setSavingId] = useState<number | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isGenerating, setIsGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [newQuestion, setNewQuestion] = useState('');
  const [isCreatingQuestion, setIsCreatingQuestion] = useState(false);

  const loadQuestions = async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await getProfileQAs();
      setItems(data);
      const initialDrafts: Record<number, string> = {};
      for (const item of data) {
        initialDrafts[item.id] = item.answer ?? '';
      }
      setDraftAnswers(initialDrafts);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('interview.errors.loadFailed'));
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    void loadQuestions();
  }, []);

  const handleGenerateQuestions = async () => {
    setIsGenerating(true);
    setError(null);
    try {
      const created = await generateInterviewQuestions();
      setItems((prev) => [...prev, ...created]);
      setDraftAnswers((prev) => {
        const next = { ...prev };
        for (const q of created) next[q.id] = '';
        return next;
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : t('interview.errors.generateFailed'));
    } finally {
      setIsGenerating(false);
    }
  };

  const handleSaveAnswer = async (id: number) => {
    const answer = draftAnswers[id]?.trim() ?? '';
    if (!answer) {
      setError(t('interview.errors.emptyAnswer'));
      return;
    }

    setSavingId(id);
    setError(null);
    try {
      const updated = await saveProfileQaAnswer(id, answer);
      setItems((prev) => prev.map((item) => (item.id === id ? updated : item)));
      setEditingId(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('interview.errors.saveFailed'));
    } finally {
      setSavingId(null);
    }
  };

  const handleCreateQuestion = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!newQuestion.trim()) return;

    setIsCreatingQuestion(true);
    setError(null);
    try {
      const created = await createProfileQa({ question: newQuestion.trim() });
      setItems((prev) => [...prev, created]);
      setDraftAnswers((prev) => ({ ...prev, [created.id]: '' }));
      setNewQuestion('');
    } catch (err) {
      setError(err instanceof Error ? err.message : t('interview.errors.createFailed'));
    } finally {
      setIsCreatingQuestion(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="glass-card rounded-2xl p-6 shadow-xl">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-white/10 pb-4">
          <div>
            <h2 className="text-lg font-semibold text-white">{t('interview.title')}</h2>
            <p className="mt-1 text-sm text-slate-400">{t('interview.subtitle')}</p>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={isGenerating}
              onClick={() => void handleGenerateQuestions()}
              className="inline-flex items-center gap-1.5 rounded-full bg-lime-400 px-4 py-1.5 text-xs font-bold text-slate-950 transition hover:bg-lime-300 disabled:opacity-60"
            >
              {isGenerating ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Sparkles className="h-3.5 w-3.5" />}
              <span>{isGenerating ? t('interview.generatingAiButton') : t('interview.generateAiButton')}</span>
            </button>
            <button
              type="button"
              onClick={() => void loadQuestions()}
              className="rounded-full border border-white/15 bg-white/5 px-3 py-1.5 text-xs font-medium text-slate-300 transition hover:bg-white/10"
            >
              {t('interview.refreshButton')}
            </button>
          </div>
        </div>

        {error && (
          <div className="mt-4 rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-300">
            {error}
          </div>
        )}

        {isLoading ? (
          <div className="flex items-center justify-center gap-2 py-12 text-sm text-slate-400">
            <Loader2 className="h-4 w-4 animate-spin text-violet-400" />
            <span>{t('interview.loading')}</span>
          </div>
        ) : items.length === 0 ? (
          <div className="py-10 text-center text-sm text-slate-400">{t('interview.emptyState')}</div>
        ) : (
          <div className="mt-5 space-y-6">
            {items.map((item) => {
              const isAnswered = Boolean(item.answer);
              const isEditing = !isAnswered || editingId === item.id;
              const isSaving = savingId === item.id;

              return (
                <div key={item.id} className="space-y-3 border-b border-white/10 pb-6 last:border-b-0 last:pb-0">
                  <div className="flex items-start gap-3">
                    <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-violet-600/25 text-violet-300">
                      <Bot className="h-4 w-4" />
                    </div>
                    <div className="max-w-2xl rounded-2xl rounded-tl-none border border-white/10 bg-[#0b0b12] px-4 py-3 text-sm text-slate-200">
                      <div className="mb-1 flex items-center gap-2 text-xs text-slate-400">
                        <span className="font-medium text-slate-300">
                          {t('interview.questionLabel', { id: item.id })}
                        </span>
                        {item.category && (
                          <span className="rounded-full bg-violet-500/20 px-2 py-0.5 text-[11px] font-medium text-violet-300">
                            {item.category}
                          </span>
                        )}
                      </div>
                      <p className="leading-relaxed">{item.question}</p>
                    </div>
                  </div>

                  {!isEditing && item.answer ? (
                    <div className="flex flex-col items-end gap-1">
                      <div className="flex items-start gap-3">
                        <div className="max-w-2xl rounded-2xl rounded-tr-none bg-violet-600 px-4 py-3 text-sm text-white shadow-sm">
                          <p className="whitespace-pre-wrap leading-relaxed">{item.answer}</p>
                        </div>
                        <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-lime-400 text-slate-950">
                          <User className="h-4 w-4" />
                        </div>
                      </div>
                      <div className="mr-11 flex items-center gap-3 text-xs text-slate-400">
                        <span className="inline-flex items-center gap-1 text-lime-400">
                          <Check className="h-3.5 w-3.5" />
                          {t('interview.savedBadge')}
                        </span>
                        <button
                          type="button"
                          onClick={() => setEditingId(item.id)}
                          className="font-medium text-violet-300 hover:underline"
                        >
                          {t('interview.editAnswer')}
                        </button>
                      </div>
                    </div>
                  ) : (
                    <div className="ml-11 flex flex-col gap-2">
                      <textarea
                        rows={3}
                        value={draftAnswers[item.id] ?? ''}
                        onChange={(e) =>
                          setDraftAnswers((prev) => ({ ...prev, [item.id]: e.target.value }))
                        }
                        placeholder={t('interview.answerPlaceholder')}
                        className="w-full rounded-xl border border-white/10 bg-[#0b0b12] px-3.5 py-2.5 text-sm text-white placeholder:text-slate-500 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-500/30"
                      />
                      <div className="flex justify-end gap-2">
                        {isAnswered && (
                          <button
                            type="button"
                            onClick={() => setEditingId(null)}
                            className="rounded-full border border-white/15 px-3.5 py-1.5 text-xs font-medium text-slate-300 hover:bg-white/5"
                          >
                            {t('interview.cancel')}
                          </button>
                        )}
                        <button
                          type="button"
                          disabled={isSaving}
                          onClick={() => void handleSaveAnswer(item.id)}
                          className="inline-flex items-center gap-1.5 rounded-full bg-lime-400 px-4 py-1.5 text-xs font-bold text-slate-950 transition hover:bg-lime-300 disabled:opacity-60"
                        >
                          {isSaving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Send className="h-3.5 w-3.5" />}
                          <span>{isSaving ? t('interview.saving') : t('interview.saveAnswer')}</span>
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>

      <form
        onSubmit={handleCreateQuestion}
        className="glass-card flex flex-col gap-3 rounded-2xl p-4 shadow-xl sm:flex-row sm:items-center"
      >
        <div className="flex flex-1 items-center gap-2">
          <MessageSquarePlus className="h-4 w-4 shrink-0 text-violet-400" />
          <input
            type="text"
            value={newQuestion}
            onChange={(e) => setNewQuestion(e.target.value)}
            placeholder={t('interview.addQuestionPlaceholder')}
            className="w-full rounded-xl border border-white/10 bg-[#0b0b12] px-3.5 py-2 text-sm text-white placeholder:text-slate-500 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-500/30"
          />
        </div>
        <button
          type="submit"
          disabled={isCreatingQuestion || !newQuestion.trim()}
          className="inline-flex items-center justify-center gap-1.5 rounded-full border border-white/15 bg-white/10 px-4 py-2 text-xs font-semibold text-white transition hover:bg-white/15 disabled:opacity-50"
        >
          {isCreatingQuestion && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
          <span>{t('interview.addQuestionButton')}</span>
        </button>
      </form>
    </div>
  );
}
