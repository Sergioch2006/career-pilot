import i18n from '../i18n';
import type {
  AtsResumePreviewResponse,
  JobOfferResponse,
  ProfileQaItem,
  ProfileResponse,
  ProjectMilestoneItem,
  TelemetryLogsResponse,
} from '../types/api';

function apiFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const headers = new Headers(init?.headers);
  headers.set('Accept-Language', i18n.language || 'pt-BR');
  return fetch(input, { ...init, headers });
}

async function parseErrorMessage(response: Response): Promise<string> {
  try {
    const data = (await response.json()) as { error?: string; message?: string };
    return data.error || data.message || `HTTP ${response.status}`;
  } catch {
    return `HTTP ${response.status}`;
  }
}

export async function getLatestProfile(): Promise<ProfileResponse | null> {
  const response = await apiFetch('/api/profile/latest');
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as ProfileResponse;
}

export async function getProfiles(): Promise<ProfileResponse[]> {
  const response = await apiFetch('/api/profiles');
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as ProfileResponse[];
}

export async function updateProfileText(
  id: number,
  payload: { text: string; fullName?: string; email?: string }
): Promise<ProfileResponse> {
  const response = await apiFetch(`/api/profiles/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as ProfileResponse;
}

export async function updateProfileFile(
  id: number,
  file: File,
  fullName?: string,
  email?: string
): Promise<ProfileResponse> {
  const formData = new FormData();
  formData.append('file', file);
  if (fullName?.trim()) formData.append('fullName', fullName.trim());
  if (email?.trim()) formData.append('email', email.trim());

  const response = await apiFetch(`/api/profiles/${id}`, {
    method: 'PUT',
    body: formData,
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as ProfileResponse;
}

export async function deleteProfile(id: number): Promise<void> {
  const response = await apiFetch(`/api/profiles/${id}`, {
    method: 'DELETE',
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
}

export async function importProfileText(payload: {
  text: string;
  fullName?: string;
  email?: string;
}): Promise<ProfileResponse> {
  const response = await apiFetch('/api/profile/import', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as ProfileResponse;
}

export async function importProfileFile(
  file: File,
  fullName?: string,
  email?: string
): Promise<ProfileResponse> {
  const formData = new FormData();
  formData.append('file', file);
  if (fullName?.trim()) {
    formData.append('fullName', fullName.trim());
  }
  if (email?.trim()) {
    formData.append('email', email.trim());
  }

  const response = await apiFetch('/api/profile/import', {
    method: 'POST',
    body: formData,
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as ProfileResponse;
}

export async function getProfileQAs(profileId?: number): Promise<ProfileQaItem[]> {
  const query = profileId ? `?profileId=${profileId}` : '';
  const response = await apiFetch(`/api/profile/qa${query}`);
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as ProfileQaItem[];
}

export async function generateInterviewQuestions(): Promise<ProfileQaItem[]> {
  const response = await apiFetch('/api/profile/interview/generate', {
    method: 'POST',
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as ProfileQaItem[];
}

export async function createProfileQa(payload: {
  userProfileId?: number;
  question: string;
  answer?: string;
  category?: string;
}): Promise<ProfileQaItem> {
  const response = await apiFetch('/api/profile/qa', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as ProfileQaItem;
}

export async function saveProfileQaAnswer(id: number, answer: string): Promise<ProfileQaItem> {
  const response = await apiFetch(`/api/profile/qa/${id}/answer`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ answer }),
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as ProfileQaItem;
}

export async function getJobs(): Promise<JobOfferResponse[]> {
  const response = await apiFetch('/api/jobs');
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as JobOfferResponse[];
}

export async function analyzeJobOffer(payload: {
  title?: string;
  company?: string;
  url?: string;
  description?: string;
}): Promise<JobOfferResponse> {
  const response = await apiFetch('/api/jobs/analyze', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as JobOfferResponse;
}

export async function downloadJobInterviewPdf(jobId: number): Promise<void> {
  const response = await apiFetch(`/api/jobs/${jobId}/pdf`);
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }

  const blob = await response.blob();
  const disposition = response.headers.get('content-disposition');
  const match = disposition?.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
  const fileName = match?.[1] ? decodeURIComponent(match[1]) : `Guia_Entrevista_Vaga_${jobId}.pdf`;

  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export async function generateRecruiterReply(jobId: number): Promise<JobOfferResponse> {
  const response = await apiFetch(`/api/jobs/${jobId}/reply`, {
    method: 'POST',
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as JobOfferResponse;
}

export async function downloadAtsResumePdf(jobId: number): Promise<string> {
  const response = await apiFetch(`/api/jobs/${jobId}/resume/pdf`);
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }

  const blob = await response.blob();
  const disposition = response.headers.get('content-disposition');
  const match = disposition?.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
  const fileName = match?.[1] ? decodeURIComponent(match[1]) : `Resume_ATS_${jobId}.pdf`;

  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);

  return fileName;
}

export async function getTelemetryLogs(): Promise<TelemetryLogsResponse> {
  const response = await apiFetch('/api/telemetry/logs?take=10');
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as TelemetryLogsResponse;
}

export async function getMilestones(): Promise<ProjectMilestoneItem[]> {
  const response = await apiFetch('/api/milestones');
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as ProjectMilestoneItem[];
}

export async function createMilestone(payload: {
  title: string;
  description: string;
  category?: string;
  tags?: string;
}): Promise<ProjectMilestoneItem> {
  const response = await apiFetch('/api/milestones', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as ProjectMilestoneItem;
}

export async function generateCustomAtsPreview(
  jobId: number,
  milestoneIds: number[]
): Promise<AtsResumePreviewResponse> {
  const response = await apiFetch(`/api/jobs/${jobId}/ats-resume`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ milestoneIds, format: 'preview' }),
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }
  return (await response.json()) as AtsResumePreviewResponse;
}

export async function downloadCustomAtsResume(
  jobId: number,
  milestoneIds: number[],
  format: 'pdf' | 'docx'
): Promise<string> {
  const response = await apiFetch(`/api/jobs/${jobId}/ats-resume`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ milestoneIds, format }),
  });
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response));
  }

  const blob = await response.blob();
  const disposition = response.headers.get('content-disposition');
  const match = disposition?.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
  const fileName = match?.[1] ? decodeURIComponent(match[1]) : `Resume_ATS_${jobId}.${format}`;

  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);

  return fileName;
}
