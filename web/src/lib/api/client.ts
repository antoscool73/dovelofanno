// Client dell'API v1. I tipi vengono da schema.d.ts, generato da docs/api/openapi-v1.json (npm run gen:api):
// se l'API cambia, la CI fallisce finché i tipi non sono rigenerati, così interfaccia e server restano allineati.
import type { components } from './schema';

type S = components['schemas'];
export type Health = S['Health'];
export type Profile = S['ProfileRow'];
export type NewProfile = S['NewProfile'];
export type ProfilePatch = S['ProfilePatch'];
export type Source = S['SourceRow'];
export type Channel = S['ChannelRow'];
export type Stream = S['StreamRow'];
export type Recording = S['RecordingRow'];
export type RecordingDetail = S['RecordingDetail'];
export type NewRecording = S['NewRecording'];
export type ScheduledRecording = S['ScheduledRecording'];
export type ImportResult = S['ImportResult'];
export type BackupInfo = S['BackupInfo'];

/** Errore dell'API con il messaggio del server (campo "error"), già in italiano. */
export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  let res: Response;
  try {
    res = await fetch('/api/v1' + path, {
      method,
      headers: body === undefined ? { Accept: 'application/json' } : { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, 'Il server di casa non risponde: è acceso?');
  }
  if (res.status === 204 || res.status === 202) return undefined as T;
  const text = await res.text();
  const data: unknown = text ? JSON.parse(text) : undefined;
  if (!res.ok) {
    const msg = typeof data === 'object' && data !== null && 'error' in data ? String((data as { error: unknown }).error) : `errore ${res.status}`;
    throw new ApiError(res.status, msg);
  }
  return data as T;
}

export const api = {
  health: () => call<Health>('GET', '/health'),

  profiles: () => call<Profile[]>('GET', '/profiles'),
  createProfile: (p: NewProfile) => call<Profile>('POST', '/profiles', p),
  updateProfile: (id: number, p: ProfilePatch) => call<Profile>('PATCH', `/profiles/${id}`, p),
  deleteProfile: (id: number) => call<void>('DELETE', `/profiles/${id}`),
  unlockProfile: (id: number, pin: string | null) => call<void>('POST', `/profiles/${id}/unlock`, { pin }),

  sources: () => call<Source[]>('GET', '/sources'),
  channels: () => call<Channel[]>('GET', '/channels'),
  streams: (channelId: number) => call<Stream[]>('GET', `/channels/${channelId}/streams`),

  recordings: () => call<Recording[]>('GET', '/recordings'),
  recording: (id: number) => call<RecordingDetail>('GET', `/recordings/${id}`),
  scheduleRecording: (r: NewRecording) => call<ScheduledRecording>('POST', '/recordings', r),
  stopRecording: (id: number) => call<void>('POST', `/recordings/${id}/stop`),
  cancelRecording: (id: number) => call<void>('DELETE', `/recordings/${id}`),
};
