// Client dell'API v1. I tipi vengono da schema.d.ts, generato da docs/api/openapi-v1.json (npm run gen:api):
// se l'API cambia, la CI fallisce finché i tipi non sono rigenerati, così interfaccia e server restano allineati.
import type { components } from './schema';

type S = components['schemas'];
export type Health = S['Health'];
export type Profile = S['ProfileRow'];
export type NewProfile = S['NewProfile'];
export type ProfilePatch = S['ProfilePatch'];
export type Source = S['SourceRow'];
export type SourcePatch = S['SourcePatch'];
export type Channel = S['ChannelRow'];
export type Stream = S['StreamRow'];
// recordFrom e recordTo (orari con i margini) sono calcolati dal server e arrivano sempre, ma OpenAPI li segna facoltativi
export type Recording = S['RecordingRow'] & { recordFrom: string; recordTo: string };
export type RecordingDetail = Omit<S['RecordingDetail'], 'recording'> & { recording: Recording };
export type NewRecording = S['NewRecording'];
export type ScheduledRecording = S['ScheduledRecording'];
export type SeriesRule = S['SeriesRule'];
export type SeriesCheck = S['SeriesCheck'];
export type SeriesEpisode = S['SeriesEpisode'];
export type SeriesPreview = S['SeriesPreview'];
export type ConversionRow = S['ConversionRow'];
export type HomeSections = S['HomeSections'];
export type ImportResult = S['ImportResult'];
export type BackupInfo = S['BackupInfo'];
export type RatingSettings = S['RatingSettings'];
export type GuideProgramme = S['GuideProgramme'];
export type GuideStatus = S['GuideStatus'];
export type TonightItem = S['TonightItem'];
export type WorkDetail = S['WorkDetail'];
export type RatingSettingsPatch = S['RatingSettingsPatch'];
export type UpdateStatus = S['UpdateStatus'];
export type LibraryEntry = S['LibraryEntry'];
export type LibraryFile = S['LibraryFile'];
export type LibraryItem = S['LibraryItemDetail'];
export type LibraryStatus = S['LibraryStatus'];
export type LibraryFolder = S['LibraryFolderRow'];
export type ListSummary = S['ListSummary'];
export type ListDetail = S['ListDetail'];
export type ListEntry = S['ListEntry'];
export type MatchCandidate = S['MatchCandidate'];
export type MatchBody = S['MatchBody'];
export type PreferenceRow = S['PreferenceRow'];
export type PreferenceBody = S['PreferenceBody'];
export type Proposal = S['Proposal'];
export type IgnoredWork = S['IgnoredWork'];
export type AlertRow = S['AlertRow'];
export type AlertBody = S['AlertBody'];
export type AlertKind = 'tv' | 'subscription' | 'rent_buy';
export type NotificationRow = S['NotificationRow'];
export type NotificationList = S['NotificationList'];
export type ProfileSubscriptions = S['ProfileSubscriptions'];
export type FamilySubscriptions = S['FamilySubscriptions'];
export type Platform = S['Platform'];
export type SearchHit = S['SearchHit'];
export type SearchResult = S['SearchResult'];
export type FromTmdbBody = S['FromTmdbBody'];
export type SearchOptions = S['SearchOptions'];
/**
 * Filtri della ricerca avanzata: tutti facoltativi (vuoto o 0 = filtro spento). genres: uno qualsiasi; hideSeen e
 * hideHome tolgono i titoli già visti e quelli in casa. È anche la ricerca salvata di una lista dinamica (SavedQuery).
 */
export type SearchQuery = {
  q: string; kind: 'all' | 'film' | 'series' | 'documentary'; genres: number[]; yearFrom: number; yearTo: number;
  country: string; minRating: number; avail: ('tv' | 'paytv' | 'mine' | 'paid' | 'home')[]; hideSeen: boolean; hideHome: boolean; limit?: number;
};
export type SavedQuery = S['SavedQuery'];
export const EMPTY_QUERY: SearchQuery = { q: '', kind: 'all', genres: [], yearFrom: 0, yearTo: 0, country: '', minRating: 0, avail: [], hideSeen: false, hideHome: false };
const AVAILS = ['tv', 'paytv', 'mine', 'paid', 'home'] as const;
/** Ricerca salvata di una lista dinamica → filtri della pagina Cerca. */
export function fromSaved(q: SavedQuery | null | undefined): SearchQuery {
  if (!q) return { ...EMPTY_QUERY, genres: [], avail: [] };
  const kind = q.kind === 'film' || q.kind === 'series' || q.kind === 'documentary' ? q.kind : 'all';
  return {
    q: q.q ?? '', kind, genres: [...(q.genres ?? [])], yearFrom: q.yearFrom ?? 0, yearTo: q.yearTo ?? 0, country: q.country ?? '',
    minRating: q.minRating ?? 0, avail: (q.avail ?? []).filter((a): a is SearchQuery['avail'][number] => (AVAILS as readonly string[]).includes(a)),
    hideSeen: !!q.hideSeen, hideHome: !!q.hideHome,
  };
}
/** Filtri della pagina Cerca → ricerca da salvare in una lista dinamica. */
export function toSaved(f: SearchQuery): SavedQuery {
  return {
    q: f.q.trim(), kind: f.kind, genres: [...f.genres], yearFrom: f.yearFrom, yearTo: f.yearTo, country: f.country,
    minRating: f.minRating, avail: [...f.avail], hideSeen: f.hideSeen, hideHome: f.hideHome,
  };
}
export type GeneralSettings = S['GeneralSettings'];
export type GeneralSettingsPatch = S['GeneralSettingsPatch'];
export type NotificationSettings = S['NotificationSettings'];
export type NotificationSettingsPatch = S['NotificationSettingsPatch'];
export type GenreCount = S['GenreCount'];
export type LibrarySection = 'all' | 'films' | 'series' | 'documentaries' | 'programmes' | 'recordings' | 'unmatched' | 'watched';
export type LibrarySort = 'added' | 'title' | 'rating' | 'stars';

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
  tonight: (profileId: number) => call<TonightItem[]>('GET', `/profiles/${profileId}/tonight`),
  addTonight: (profileId: number, workId: number) => call<void>('POST', `/profiles/${profileId}/tonight`, { workId }),
  removeTonight: (profileId: number, workId: number) => call<void>('DELETE', `/profiles/${profileId}/tonight/${workId}`),
  setStars: (profileId: number, workId: number, stars: number | null) => call<void>('PUT', `/profiles/${profileId}/works/${workId}/stars`, { stars }),
  work: (id: number, profileId?: number) => call<WorkDetail>('GET', `/works/${id}` + (profileId ? `?profileId=${profileId}` : '')),
  watchedIds: (profileId: number) => call<number[]>('GET', `/profiles/${profileId}/watched`),
  watched: (profileId: number, workId: number) => call<void>('POST', `/profiles/${profileId}/watched`, { workId }),
  unwatched: (profileId: number, workId: number) => call<void>('DELETE', `/profiles/${profileId}/watched/${workId}`),

  sources: () => call<Source[]>('GET', '/sources'),
  addSource: (name: string, url: string) => call<ImportResult>('POST', '/sources', { name, url }),
  updateSource: (id: number, p: SourcePatch) => call<Source>('PATCH', `/sources/${id}`, p),
  deleteSource: (id: number) => call<void>('DELETE', `/sources/${id}`),
  refreshSource: (id: number) => call<ImportResult>('POST', `/sources/${id}/refresh`),
  checkSource: (id: number) => call<void>('POST', `/sources/${id}/check`),
  channels: () => call<Channel[]>('GET', '/channels'),
  streams: (channelId: number) => call<Stream[]>('GET', `/channels/${channelId}/streams`),
  allStreams: () => call<Stream[]>('GET', '/streams'),

  guide: (q: { from: Date; to: Date; channelId?: number; text?: string; group?: string; sub?: string; minRating?: number; onlyRecordable?: boolean; max?: number }) => {
    const p = new URLSearchParams({ from: q.from.toISOString(), to: q.to.toISOString() });
    if (q.channelId) p.set('channelId', String(q.channelId));
    if (q.text) p.set('text', q.text);
    if (q.group) p.set('group', q.group);
    if (q.sub) p.set('sub', q.sub);
    if (q.minRating) p.set('minRating', String(q.minRating));
    if (q.onlyRecordable) p.set('onlyRecordable', 'true');
    if (q.max) p.set('max', String(q.max));
    return call<GuideProgramme[]>('GET', '/guide?' + p.toString());
  },
  guideProgramme: (id: number) => call<GuideProgramme>('GET', `/guide/programmes/${id}`),
  guideStatus: () => call<GuideStatus>('GET', '/guide/status'),
  refreshGuide: () => call<void>('POST', '/guide/refresh'),

  ratingSettings: () => call<RatingSettings>('GET', '/settings/ratings'),
  updateRatingSettings: (p: RatingSettingsPatch) => call<RatingSettings>('PUT', '/settings/ratings', p),

  updates: () => call<UpdateStatus>('GET', '/settings/updates'),
  setUpdateCheck: (enabled: boolean) => call<UpdateStatus>('PUT', '/settings/updates', { enabled }),
  checkUpdates: () => call<UpdateStatus>('POST', '/settings/updates/check'),

  library: (section: LibrarySection, profileId?: number, sort: LibrarySort = 'added', text = '') => {
    const p = new URLSearchParams({ section, sort });
    if (profileId) p.set('profileId', String(profileId));
    if (text) p.set('text', text);
    return call<LibraryEntry[]>('GET', '/library?' + p.toString());
  },
  libraryStatus: () => call<LibraryStatus>('GET', '/library/status'),
  scanLibrary: () => call<void>('POST', '/library/scan'),
  addFolder: (path: string, kind: string) => call<LibraryFolder>('POST', '/library/folders', { path, kind }),
  updateFolder: (id: number, p: { kind?: string | null; enabled?: boolean | null }) => call<LibraryFolder>('PATCH', `/library/folders/${id}`, { kind: p.kind ?? null, enabled: p.enabled ?? null }),
  deleteFolder: (id: number) => call<void>('DELETE', `/library/folders/${id}`),
  libraryItem: (id: number, profileId?: number) => call<LibraryItem>('GET', `/library/items/${id}` + (profileId ? `?profileId=${profileId}` : '')),
  prepareItem: (id: number) => call<void>('POST', `/library/items/${id}/prepare`),
  matchItem: (id: number, body: MatchBody) => call<LibraryItem>('PUT', `/library/items/${id}/match`, body),
  removeItem: (id: number, deleteFile: boolean) => call<void>('DELETE', `/library/items/${id}?deleteFile=${deleteFile}`),
  searchMatch: (q: string, kind: 'movie' | 'tv', year?: number) => {
    const p = new URLSearchParams({ q, kind });
    if (year) p.set('year', String(year));
    return call<MatchCandidate[]>('GET', '/library/match?' + p.toString());
  },
  streamUrl: (id: number) => `/api/v1/library/items/${id}/stream`,
  subtitleUrl: (id: number, track: string) => `/api/v1/library/items/${id}/subtitles/${track}`,
  saveProgress: (profileId: number, itemId: number, positionS: number, durationS: number, keepalive = false) =>
    fetch(`/api/v1/profiles/${profileId}/items/${itemId}/progress`, {
      method: 'PUT', keepalive, headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ positionS: Math.floor(positionS), durationS: Math.floor(durationS) }),
    }).then((r) => (r.ok ? (r.json() as Promise<{ watched: boolean }>) : { watched: false })).catch(() => ({ watched: false })),
  itemWatched: (profileId: number, itemId: number) => call<void>('POST', `/profiles/${profileId}/items/${itemId}/watched`),
  continueWatching: (profileId: number) => call<LibraryItem[]>('GET', `/profiles/${profileId}/continue`),

  lists: (profileId: number) => call<ListSummary[]>('GET', `/profiles/${profileId}/lists`),
  list: (profileId: number, listId: number, sort: LibrarySort = 'added') => call<ListDetail>('GET', `/profiles/${profileId}/lists/${listId}?sort=${sort}`),
  createList: (profileId: number, name: string, family: boolean, query: SavedQuery | null = null) =>
    call<ListSummary>('POST', `/profiles/${profileId}/lists`, { name, family, query }),
  /** I titoli di una lista dinamica: i risultati della sua ricerca, come in Cerca. */
  listResults: (profileId: number, listId: number, limit = 40) => call<SearchResult>('GET', `/profiles/${profileId}/lists/${listId}/results?limit=${limit}`),
  updateList: (profileId: number, listId: number, p: { name?: string | null; family?: boolean | null; query?: SavedQuery | null; onHome?: boolean | null }) =>
    call<ListSummary>('PATCH', `/profiles/${profileId}/lists/${listId}`, { name: p.name ?? null, family: p.family ?? null, query: p.query ?? null, onHome: p.onHome ?? null }),
  deleteList: (profileId: number, listId: number) => call<void>('DELETE', `/profiles/${profileId}/lists/${listId}`),
  addToList: (profileId: number, listId: number, workId: number) => call<void>('POST', `/profiles/${profileId}/lists/${listId}/items`, { workId }),
  removeFromList: (profileId: number, listId: number, workId: number) => call<void>('DELETE', `/profiles/${profileId}/lists/${listId}/items/${workId}`),

  preferences: (profileId: number) => call<PreferenceRow[]>('GET', `/profiles/${profileId}/preferences`),
  addPreference: (profileId: number, b: PreferenceBody) => call<PreferenceRow>('POST', `/profiles/${profileId}/preferences`, b),
  updatePreference: (profileId: number, id: number, b: PreferenceBody) => call<PreferenceRow>('PUT', `/profiles/${profileId}/preferences/${id}`, b),
  deletePreference: (profileId: number, id: number) => call<void>('DELETE', `/profiles/${profileId}/preferences/${id}`),
  excludedGroups: (profileId: number) => call<{ groups: string[] }>('GET', `/profiles/${profileId}/excluded-groups`),
  setExcludedGroups: (profileId: number, groups: string[]) => call<{ groups: string[] }>('PUT', `/profiles/${profileId}/excluded-groups`, { groups }),
  proposals: (profileId: number, days = 7) => call<Proposal[]>('GET', `/profiles/${profileId}/proposals?days=${days}`),
  ignored: (profileId: number) => call<IgnoredWork[]>('GET', `/profiles/${profileId}/ignored`),
  ignore: (profileId: number, workId: number) => call<void>('POST', `/profiles/${profileId}/ignored`, { workId }),
  unignore: (profileId: number, key: string) => call<void>('DELETE', `/profiles/${profileId}/ignored?key=${encodeURIComponent(key)}`),
  genres: () => call<GenreCount[]>('GET', '/guide/genres'),

  alerts: (profileId: number, workId?: number) => call<AlertRow[]>('GET', `/profiles/${profileId}/alerts` + (workId ? `?workId=${workId}` : '')),
  saveAlert: (profileId: number, b: AlertBody) => call<AlertRow>('PUT', `/profiles/${profileId}/alerts`, b),
  deleteAlert: (profileId: number, id: number) => call<void>('DELETE', `/profiles/${profileId}/alerts/${id}`),

  notifications: (profileId: number) => call<NotificationList>('GET', `/profiles/${profileId}/notifications`),
  markRead: (profileId: number, upTo: number) => call<void>('POST', `/profiles/${profileId}/notifications/read`, { upTo }),

  familySubscriptions: () => call<FamilySubscriptions>('GET', '/subscriptions'),
  platforms: () => call<Platform[]>('GET', '/platforms'),
  search: (s: SearchQuery, profileId?: number) => {
    const p = new URLSearchParams();
    if (s.q.trim()) p.set('q', s.q.trim());
    if (profileId) p.set('profileId', String(profileId));
    if (s.kind !== 'all') p.set('kind', s.kind);
    if (s.genres.length) p.set('genres', s.genres.join(','));
    if (s.yearFrom) p.set('yearFrom', String(s.yearFrom));
    if (s.yearTo) p.set('yearTo', String(s.yearTo));
    if (s.country) p.set('country', s.country);
    if (s.minRating) p.set('minRating', String(s.minRating));
    if (s.avail.length) p.set('avail', s.avail.join(','));
    if (s.hideSeen) p.set('hideSeen', 'true');
    if (s.hideHome) p.set('hideHome', 'true');
    if (s.limit) p.set('limit', String(s.limit));
    return call<SearchResult>('GET', '/search?' + p.toString());
  },
  searchOptions: () => call<SearchOptions>('GET', '/search/options'),
  workFromTmdb: (b: FromTmdbBody) => call<{ workId: number }>('POST', '/works/from-tmdb', b),
  setFamilySubscriptions: (services: string[]) => call<FamilySubscriptions>('PUT', '/subscriptions', { services }),
  profileSubscriptions: (profileId: number) => call<ProfileSubscriptions>('GET', `/profiles/${profileId}/subscriptions`),
  setProfileSubscriptions: (profileId: number, inherits: boolean, services: string[] | null) =>
    call<ProfileSubscriptions>('PUT', `/profiles/${profileId}/subscriptions`, { inherits, services }),

  generalSettings: () => call<GeneralSettings>('GET', '/settings/general'),
  updateGeneralSettings: (p: Partial<GeneralSettingsPatch>) =>
    call<GeneralSettings>('PUT', '/settings/general', { beforeMin: null, afterMin: null, format: null, maxParallel: null, minFreeGb: null, setupDone: null, ...p }),
  notificationSettings: () => call<NotificationSettings>('GET', '/settings/notifications'),
  updateNotificationSettings: (p: Partial<NotificationSettingsPatch>) =>
    call<NotificationSettings>('PUT', '/settings/notifications', { telegramToken: null, telegramChat: null, clearTelegram: null, quietFrom: null, quietTo: null, ...p }),
  previewUrl: (channelId: number) => `/api/v1/channels/${channelId}/preview?t=${Date.now()}`,

  backups: () => call<BackupInfo[]>('GET', '/backups'),
  createBackup: () => call<BackupInfo>('POST', '/backups'),

  recordings: () => call<Recording[]>('GET', '/recordings'),
  recording: (id: number) => call<RecordingDetail>('GET', `/recordings/${id}`),
  scheduleRecording: (r: NewRecording) => call<ScheduledRecording>('POST', '/recordings', r),
  updateRecording: (id: number, r: NewRecording) => call<ScheduledRecording>('PUT', `/recordings/${id}`, r),
  stopRecording: (id: number) => call<void>('POST', `/recordings/${id}/stop`),
  cancelRecording: (id: number) => call<void>('DELETE', `/recordings/${id}`),

  // «Registra la serie» (20/11/2026)
  seriesRules: () => call<SeriesRule[]>('GET', '/series'),
  recordSeries: (b: { programmeId?: number | null; workId?: number | null; profileId?: number | null }) =>
    call<SeriesCheck>('POST', '/series', { programmeId: null, workId: null, profileId: null, ...b }),
  checkSeries: (id: number) => call<SeriesCheck>('POST', `/series/${id}/check`),
  seriesPreview: (programmeId: number) => call<SeriesPreview>('GET', `/series/preview?programmeId=${programmeId}`),
  stopSeries: (id: number) => call<void>('DELETE', `/series/${id}`),

  // sezioni predefinite di La mia lista nascoste dal profilo (20/11/2026)
  homeSections: (profileId: number) => call<HomeSections>('GET', `/profiles/${profileId}/home`),
  setHomeSections: (profileId: number, hidden: string[]) => call<HomeSections>('PUT', `/profiles/${profileId}/home`, { hidden }),

  // «Converti...» (20/11/2026)
  conversions: () => call<ConversionRow[]>('GET', '/conversions'),
  convert: (b: { recordingId?: number | null; libraryItemId?: number | null; format: 'mp4' | 'mp3'; profileId?: number | null }) =>
    call<ConversionRow>('POST', '/conversions', { recordingId: null, libraryItemId: null, profileId: null, ...b }),
  cancelConversion: (id: number) => call<void>('DELETE', `/conversions/${id}`),
};
