import { useEffect, useRef, useState } from 'react';
import { CheckCircle2, FileText, Loader2, Pencil, Trash2, Upload, User, X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import {
  deleteProfile,
  getProfiles,
  importProfileFile,
  importProfileText,
  updateProfileFile,
  updateProfileText,
} from '../services/api';
import type { ProfileResponse } from '../types/api';

interface ProfileImportPageProps {
  onProfileSaved?: (profile: ProfileResponse) => void;
  onProfileDeleted?: (deletedId: number) => void;
}

export function ProfileImportPage({ onProfileSaved, onProfileDeleted }: ProfileImportPageProps) {
  const { t, i18n } = useTranslation();
  const [profiles, setProfiles] = useState<ProfileResponse[]>([]);
  const [editingProfile, setEditingProfile] = useState<ProfileResponse | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<ProfileResponse | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);

  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [pastedText, setPastedText] = useState('');
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [isLoadingProfiles, setIsLoadingProfiles] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const loadProfiles = async () => {
    setIsLoadingProfiles(true);
    try {
      const list = await getProfiles();
      setProfiles(list);
    } catch {
      // Ignora erro inicial se o backend ainda estiver subindo
    } finally {
      setIsLoadingProfiles(false);
    }
  };

  useEffect(() => {
    void loadProfiles();
  }, []);

  const handleFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0] ?? null;
    setError(null);
    setSuccessMessage(null);
    if (!file) {
      setSelectedFile(null);
      return;
    }

    const lower = file.name.toLowerCase();
    if (!lower.endsWith('.pdf') && !lower.endsWith('.docx')) {
      setError(t('profile.errors.invalidFormat'));
      event.target.value = '';
      setSelectedFile(null);
      return;
    }

    setSelectedFile(file);
  };

  const clearSelectedFile = () => {
    setSelectedFile(null);
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  const startEditing = (profile: ProfileResponse) => {
    setEditingProfile(profile);
    setFullName(profile.fullName ?? '');
    setEmail(profile.email ?? '');
    setPastedText(profile.rawText);
    clearSelectedFile();
    setError(null);
    setSuccessMessage(null);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const cancelEditing = () => {
    setEditingProfile(null);
    setFullName('');
    setEmail('');
    setPastedText('');
    clearSelectedFile();
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setError(null);
    setSuccessMessage(null);

    if (!selectedFile && !pastedText.trim()) {
      setError(t('profile.errors.emptyInput'));
      return;
    }

    setIsSaving(true);
    try {
      let result: ProfileResponse;
      if (editingProfile) {
        result = selectedFile
          ? await updateProfileFile(editingProfile.id, selectedFile, fullName, email)
          : await updateProfileText(editingProfile.id, {
              text: pastedText,
              fullName: fullName.trim() || undefined,
              email: email.trim() || undefined,
            });
        setProfiles((prev) => prev.map((p) => (p.id === result.id ? result : p)));
        setSuccessMessage(t('profile.successUpdated', { id: result.id }));
        setEditingProfile(null);
        setFullName('');
        setEmail('');
        setPastedText('');
      } else {
        result = selectedFile
          ? await importProfileFile(selectedFile, fullName, email)
          : await importProfileText({
              text: pastedText,
              fullName: fullName.trim() || undefined,
              email: email.trim() || undefined,
            });
        setProfiles((prev) => [result, ...prev]);
        setSuccessMessage(
          t('profile.successSaved', {
            id: result.id,
            count: result.characterCount.toLocaleString(i18n.language),
          })
        );
        setFullName('');
        setEmail('');
        setPastedText('');
      }

      clearSelectedFile();
      onProfileSaved?.(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('profile.errors.saveFailed'));
    } finally {
      setIsSaving(false);
    }
  };

  const handleConfirmDelete = async () => {
    if (!deleteTarget) return;
    setIsDeleting(true);
    setError(null);
    try {
      await deleteProfile(deleteTarget.id);
      const deletedId = deleteTarget.id;
      setProfiles((prev) => prev.filter((p) => p.id !== deletedId));
      if (editingProfile?.id === deletedId) {
        cancelEditing();
      }
      setDeleteTarget(null);
      onProfileDeleted?.(deletedId);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('profile.errors.deleteFailed'));
    } finally {
      setIsDeleting(false);
    }
  };

  const latestProfile = profiles[0] ?? null;

  return (
    <div className="space-y-6">
      <div className="glass-card rounded-2xl p-6 shadow-xl">
        <div className="flex items-center gap-2">
          <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-lime-400 text-xs font-bold text-slate-950">
            2
          </span>
          <h2 className="text-lg font-semibold text-white">{t('profile.stepTitle')}</h2>
        </div>
        <p className="mt-1 text-sm text-slate-400">
          {t('profile.subtitlePrefix')} <strong className="text-slate-200">.pdf</strong>{' '}
          {t('profile.subtitleOr')} <strong className="text-slate-200">.docx</strong>{' '}
          {t('profile.subtitleSuffix')}
        </p>

        {editingProfile && (
          <div className="mt-4 flex flex-wrap items-center justify-between gap-2 rounded-xl border border-violet-500/40 bg-violet-500/15 px-4 py-2.5 text-xs text-violet-200">
            <span>
              {t('profile.editingBanner', {
                id: editingProfile.id,
                name: editingProfile.fullName || t('profile.list.unnamed'),
              })}
            </span>
            <button
              type="button"
              onClick={cancelEditing}
              className="rounded-full border border-white/20 px-3 py-1 font-semibold text-white hover:bg-white/10"
            >
              {t('profile.cancelEdit')}
            </button>
          </div>
        )}

        <form onSubmit={handleSubmit} className="mt-5 space-y-5">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="fullName" className="block text-xs font-medium text-slate-300">
                {t('profile.fullNameLabel')} <span className="text-slate-500">{t('profile.optional')}</span>
              </label>
              <input
                id="fullName"
                type="text"
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                placeholder={t('profile.fullNamePlaceholder')}
                className="mt-1.5 w-full rounded-xl border border-white/10 bg-[#0b0b12] px-3.5 py-2.5 text-sm text-white placeholder:text-slate-500 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-500/30"
              />
            </div>

            <div>
              <label htmlFor="email" className="block text-xs font-medium text-slate-300">
                {t('profile.emailLabel')} <span className="text-slate-500">{t('profile.optional')}</span>
              </label>
              <input
                id="email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder={t('profile.emailPlaceholder')}
                className="mt-1.5 w-full rounded-xl border border-white/10 bg-[#0b0b12] px-3.5 py-2.5 text-sm text-white placeholder:text-slate-500 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-500/30"
              />
            </div>
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-300">{t('profile.uploadLabel')}</label>
            <div className="mt-1.5 flex flex-wrap items-center gap-3">
              <label className="inline-flex cursor-pointer items-center gap-2 rounded-xl border border-dashed border-violet-500/40 bg-violet-500/10 px-4 py-3 text-sm font-medium text-violet-200 transition hover:border-lime-400/60 hover:bg-violet-500/20">
                <Upload className="h-4 w-4 text-lime-400" />
                <span>{t('profile.selectFile')}</span>
                <input
                  ref={fileInputRef}
                  type="file"
                  accept=".pdf,.docx,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                  onChange={handleFileChange}
                  className="hidden"
                />
              </label>

              {selectedFile && (
                <div className="flex items-center gap-2 rounded-xl border border-lime-400/30 bg-lime-400/10 px-3 py-2 text-sm text-lime-200">
                  <FileText className="h-4 w-4 text-lime-400" />
                  <span className="max-w-xs truncate font-medium">{selectedFile.name}</span>
                  <button
                    type="button"
                    onClick={clearSelectedFile}
                    className="rounded p-0.5 text-lime-300 hover:bg-white/10"
                    aria-label={t('profile.removeFileAria')}
                  >
                    <X className="h-4 w-4" />
                  </button>
                </div>
              )}
            </div>
          </div>

          <div>
            <label htmlFor="resumeText" className="block text-xs font-medium text-slate-300">
              {t('profile.pasteLabel')}
            </label>
            <textarea
              id="resumeText"
              rows={7}
              value={pastedText}
              onChange={(e) => setPastedText(e.target.value)}
              disabled={Boolean(selectedFile)}
              placeholder={
                selectedFile ? t('profile.pasteDisabledPlaceholder') : t('profile.pastePlaceholder')
              }
              className="mt-1.5 w-full rounded-xl border border-white/10 bg-[#0b0b12] px-3.5 py-2.5 text-sm text-white placeholder:text-slate-500 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-500/30 disabled:cursor-not-allowed disabled:opacity-50"
            />
          </div>

          {error && (
            <div className="rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-300">
              {error}
            </div>
          )}

          {successMessage && (
            <div className="flex items-center gap-2 rounded-xl border border-lime-400/30 bg-lime-400/10 px-4 py-3 text-sm text-lime-300">
              <CheckCircle2 className="h-4 w-4 shrink-0 text-lime-400" />
              <span>{successMessage}</span>
            </div>
          )}

          <div className="flex justify-end">
            <button
              type="submit"
              disabled={isSaving}
              className="inline-flex items-center gap-2 rounded-full bg-lime-400 px-6 py-2.5 text-sm font-bold text-slate-950 transition hover:bg-lime-300 disabled:opacity-60"
            >
              {isSaving && <Loader2 className="h-4 w-4 animate-spin" />}
              <span>
                {editingProfile
                  ? isSaving
                    ? t('profile.updatingButton')
                    : t('profile.updateButton')
                  : isSaving
                    ? t('profile.savingButton')
                    : t('profile.saveButton')}
              </span>
            </button>
          </div>
        </form>
      </div>

      {/* Lista de Perfis Cadastrados */}
      <div className="glass-card rounded-2xl p-6 shadow-xl">
        <h3 className="text-base font-semibold text-white">{t('profile.list.title')}</h3>

        {isLoadingProfiles ? (
          <div className="flex items-center justify-center gap-2 py-8 text-sm text-slate-400">
            <Loader2 className="h-4 w-4 animate-spin text-violet-400" />
          </div>
        ) : profiles.length === 0 ? (
          <p className="mt-3 text-sm text-slate-400">{t('profile.list.empty')}</p>
        ) : (
          <div className="mt-4 space-y-3">
            {profiles.map((profile) => (
              <div
                key={profile.id}
                className="flex flex-wrap items-center justify-between gap-4 rounded-xl border border-white/10 bg-[#0b0b12] p-4"
              >
                <div className="flex items-start gap-3">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-violet-600/20 text-violet-300">
                    <User className="h-5 w-5" />
                  </div>
                  <div>
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-sm font-semibold text-white">
                        {profile.fullName || t('profile.list.unnamed')}
                      </span>
                      <span className="rounded-full border border-white/10 bg-white/5 px-2 py-0.5 text-[10px] font-medium text-violet-300">
                        #{profile.id} · {profile.sourceType.toUpperCase()}
                      </span>
                    </div>
                    <p className="text-xs text-slate-400">
                      {profile.email || t('profile.list.noEmail')}
                    </p>
                    <p className="mt-0.5 text-[11px] text-slate-500">
                      {t('profile.list.updatedAt', {
                        date: new Date(profile.updatedAt || profile.createdAt).toLocaleString(i18n.language),
                      })}
                    </p>
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() => startEditing(profile)}
                    className="inline-flex items-center gap-1.5 rounded-full border border-white/15 bg-white/5 px-3.5 py-1.5 text-xs font-semibold text-white transition hover:bg-white/10"
                  >
                    <Pencil className="h-3.5 w-3.5 text-lime-400" />
                    <span>{t('profile.list.editBtn')}</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => setDeleteTarget(profile)}
                    className="inline-flex items-center gap-1.5 rounded-full border border-red-500/30 bg-red-500/10 px-3.5 py-1.5 text-xs font-semibold text-red-300 transition hover:bg-red-500/20"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                    <span>{t('profile.list.deleteBtn')}</span>
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {!isLoadingProfiles && latestProfile && (
        <div className="glass-card rounded-2xl p-6 shadow-xl">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-base font-semibold text-white">{t('profile.latestTitle')}</h3>
            <span className="rounded-full border border-white/10 bg-white/5 px-3 py-1 text-xs font-medium text-violet-300">
              ID #{latestProfile.id} · {latestProfile.sourceType.toUpperCase()}
              {latestProfile.sourceFileName ? ` (${latestProfile.sourceFileName})` : ''}
            </span>
          </div>
          <p className="mt-1 text-xs text-slate-400">
            {t('profile.latestMeta', {
              count: latestProfile.characterCount.toLocaleString(i18n.language),
              date: new Date(latestProfile.updatedAt || latestProfile.createdAt).toLocaleString(i18n.language),
            })}
          </p>
          <pre className="mt-3 max-h-56 overflow-y-auto whitespace-pre-wrap rounded-xl border border-white/10 bg-[#0b0b12] p-3.5 text-xs leading-relaxed text-slate-300">
            {latestProfile.rawText}
          </pre>
        </div>
      )}

      {/* Modal de Confirmação de Exclusão */}
      {deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4 backdrop-blur-sm">
          <div className="glass-card w-full max-w-md rounded-2xl p-6 shadow-2xl">
            <h4 className="text-base font-bold text-white">{t('profile.deleteModal.title')}</h4>
            <p className="mt-2 text-sm text-slate-300">
              {t('profile.deleteModal.description', {
                id: deleteTarget.id,
                name: deleteTarget.fullName || t('profile.list.unnamed'),
              })}
            </p>
            <div className="mt-5 flex justify-end gap-2.5">
              <button
                type="button"
                disabled={isDeleting}
                onClick={() => setDeleteTarget(null)}
                className="rounded-full border border-white/15 px-4 py-2 text-xs font-semibold text-slate-300 hover:bg-white/10"
              >
                {t('profile.deleteModal.cancel')}
              </button>
              <button
                type="button"
                disabled={isDeleting}
                onClick={() => void handleConfirmDelete()}
                className="inline-flex items-center gap-1.5 rounded-full bg-red-600 px-4 py-2 text-xs font-bold text-white hover:bg-red-500 disabled:opacity-60"
              >
                {isDeleting && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                <span>{t('profile.deleteModal.confirm')}</span>
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
