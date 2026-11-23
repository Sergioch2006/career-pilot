import { useState } from 'react';
import { Briefcase, Ghost, MessageSquareText, Send, Sparkles, UserCircle2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { JobOffersPage } from './components/JobOffersPage';
import { ProfileImportPage } from './components/ProfileImportPage';
import { ProfileInterviewPage } from './components/ProfileInterviewPage';
import { RecruiterReplyPage } from './components/RecruiterReplyPage';

type Tab = 'profile' | 'interview' | 'jobs' | 'reply';

export function App() {
  const { t, i18n } = useTranslation();
  const [activeTab, setActiveTab] = useState<Tab>('jobs');
  const [dataVersion, setDataVersion] = useState(0);

  const currentLang = i18n.language === 'en-US' ? 'en-US' : 'pt-BR';

  const switchLanguage = (lang: 'pt-BR' | 'en-US') => {
    void i18n.changeLanguage(lang);
  };

  return (
    <div className="min-h-screen bg-[#07070b] bg-hero-glow text-slate-100">
      <header className="sticky top-0 z-20 border-b border-white/10 bg-[#07070b]/85 backdrop-blur-md">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-4 px-4 py-3.5">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-full border border-lime-400/50 bg-violet-600/20 text-lime-400 shadow-[0_0_16px_rgba(163,230,53,0.2)]">
              <Ghost className="h-5 w-5" />
            </div>
            <div>
              <span className="block text-sm font-bold tracking-wider text-white">
                {t('app.brandMain')} <span className="text-violet-400">{t('app.brandAccent')}</span>
              </span>
              <span className="text-[11px] text-slate-400">{t('app.tagline')}</span>
            </div>
          </div>

          <nav className="flex flex-wrap items-center gap-1.5" aria-label={t('app.nav.ariaLabel')}>
            <button
              type="button"
              onClick={() => setActiveTab('profile')}
              className={`inline-flex items-center gap-1.5 rounded-full px-3.5 py-1.5 text-xs font-medium transition ${
                activeTab === 'profile'
                  ? 'bg-violet-600 text-white shadow-[0_0_12px_rgba(139,92,246,0.45)]'
                  : 'text-slate-300 hover:bg-white/5 hover:text-white'
              }`}
            >
              <UserCircle2 className="h-3.5 w-3.5" />
              <span>{t('app.nav.profile')}</span>
            </button>

            <button
              type="button"
              onClick={() => setActiveTab('interview')}
              className={`inline-flex items-center gap-1.5 rounded-full px-3.5 py-1.5 text-xs font-medium transition ${
                activeTab === 'interview'
                  ? 'bg-violet-600 text-white shadow-[0_0_12px_rgba(139,92,246,0.45)]'
                  : 'text-slate-300 hover:bg-white/5 hover:text-white'
              }`}
            >
              <MessageSquareText className="h-3.5 w-3.5" />
              <span>{t('app.nav.interview')}</span>
            </button>

            <button
              type="button"
              onClick={() => setActiveTab('jobs')}
              className={`inline-flex items-center gap-1.5 rounded-full px-3.5 py-1.5 text-xs font-medium transition ${
                activeTab === 'jobs'
                  ? 'bg-violet-600 text-white shadow-[0_0_12px_rgba(139,92,246,0.45)]'
                  : 'text-slate-300 hover:bg-white/5 hover:text-white'
              }`}
            >
              <Briefcase className="h-3.5 w-3.5" />
              <span>{t('app.nav.jobs')}</span>
            </button>

            <button
              type="button"
              onClick={() => setActiveTab('reply')}
              className={`inline-flex items-center gap-1.5 rounded-full px-3.5 py-1.5 text-xs font-medium transition ${
                activeTab === 'reply'
                  ? 'bg-violet-600 text-white shadow-[0_0_12px_rgba(139,92,246,0.45)]'
                  : 'text-slate-300 hover:bg-white/5 hover:text-white'
              }`}
            >
              <Send className="h-3.5 w-3.5" />
              <span>{t('app.nav.reply')}</span>
            </button>

            {/* Seletor global de idioma PT-br / EN-us */}
            <div
              role="group"
              aria-label="Language switcher"
              className="mx-1 inline-flex items-center rounded-full border border-white/10 bg-white/5 p-0.5"
            >
              <button
                type="button"
                onClick={() => switchLanguage('pt-BR')}
                className={`rounded-full px-2.5 py-1 text-[11px] font-semibold transition ${
                  currentLang === 'pt-BR'
                    ? 'bg-violet-600 text-white'
                    : 'text-slate-400 hover:text-white'
                }`}
              >
                {t('app.lang.pt')}
              </button>
              <button
                type="button"
                onClick={() => switchLanguage('en-US')}
                className={`rounded-full px-2.5 py-1 text-[11px] font-semibold transition ${
                  currentLang === 'en-US'
                    ? 'bg-violet-600 text-white'
                    : 'text-slate-400 hover:text-white'
                }`}
              >
                {t('app.lang.en')}
              </button>
            </div>

            <button
              type="button"
              onClick={() => setActiveTab('jobs')}
              className="inline-flex items-center gap-1.5 rounded-full bg-lime-400 px-4 py-1.5 text-xs font-bold text-slate-950 transition hover:bg-lime-300"
            >
              <Sparkles className="h-3.5 w-3.5" />
              <span>{t('app.nav.analyzeCta')}</span>
            </button>
          </nav>
        </div>
      </header>

      <section className="mx-auto max-w-5xl px-4 pt-8 pb-4">
        <div className="flex flex-col justify-between gap-4 border-b border-white/10 pb-6 md:flex-row md:items-end">
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
              {t('app.hero.titlePart1')}{' '}
              <span className="text-violet-400">{t('app.hero.titlePart2')}</span>{' '}
              <span className="text-lime-400">{t('app.hero.titlePart3')}</span>
            </h1>
            <p className="mt-2 max-w-2xl text-sm text-slate-400">{t('app.hero.subtitle')}</p>
          </div>
          <div className="flex items-center gap-3 text-xs text-slate-400">
            <span className="rounded-full border border-white/10 bg-white/5 px-3 py-1">
              {t('app.hero.badgeLocal')}
            </span>
            <span className="rounded-full border border-lime-400/30 bg-lime-400/10 px-3 py-1 text-lime-300">
              {t('app.hero.badgeReady')}
            </span>
          </div>
        </div>
      </section>

      <main className="mx-auto max-w-5xl px-4 py-6">
        {activeTab === 'profile' && (
          <ProfileImportPage onProfileDeleted={() => setDataVersion((v) => v + 1)} />
        )}
        {activeTab === 'interview' && <ProfileInterviewPage key={`interview-${dataVersion}`} />}
        {activeTab === 'jobs' && <JobOffersPage key={`jobs-${dataVersion}`} />}
        {activeTab === 'reply' && <RecruiterReplyPage key={`reply-${dataVersion}`} />}
      </main>
    </div>
  );
}

export default App;
