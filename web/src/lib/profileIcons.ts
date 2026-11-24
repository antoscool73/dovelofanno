// Icone dei profili (Antonio, 24/11/2026): al posto dell'iniziale, una ventina di disegni semplici a tratto, nello
// stesso stile delle altre icone dell'app (viewBox 24, tratto 2, estremi arrotondati). Il nome si salva nel profilo
// (profiles.icon); un nome sconosciuto o vuoto mostra l'iniziale come prima. Testo SVG fisso, scritto qui: si può
// inserire con {@html}.

export const profileIcons: { id: string; label: string; svg: string }[] = [
  { id: 'stella', label: 'Stella', svg: '<path d="M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z"/>' },
  { id: 'cuore', label: 'Cuore', svg: '<path d="M12 20s-7.5-4.6-7.5-10.2A4.2 4.2 0 0 1 12 7.2a4.2 4.2 0 0 1 7.5 2.6C19.5 15.4 12 20 12 20z"/>' },
  { id: 'luna', label: 'Luna', svg: '<path d="M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z"/>' },
  { id: 'sole', label: 'Sole', svg: '<circle cx="12" cy="12" r="4"/><path d="M12 2.5v2M12 19.5v2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M2.5 12h2M19.5 12h2M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4"/>' },
  { id: 'fulmine', label: 'Fulmine', svg: '<path d="M13 2.5L4.5 13.5h6.5l-1 8 8.5-11h-6.5z"/>' },
  { id: 'fiore', label: 'Fiore', svg: '<circle cx="12" cy="9" r="1.8"/><circle cx="12" cy="5" r="2"/><circle cx="15.8" cy="7.8" r="2"/><circle cx="14.4" cy="12.2" r="2"/><circle cx="9.6" cy="12.2" r="2"/><circle cx="8.2" cy="7.8" r="2"/><path d="M12 14.5v7M12 19c-1.8 0-3.2-.8-4-2"/>' },
  { id: 'foglia', label: 'Foglia', svg: '<path d="M5 19C5 10.5 10 5.5 20 4c-1.2 9.8-6.2 15-15 15z"/><path d="M5 19l8-8"/>' },
  { id: 'nota', label: 'Musica', svg: '<path d="M9 18V5.5l11-2V16"/><circle cx="6" cy="18" r="3"/><circle cx="17" cy="16" r="3"/>' },
  { id: 'razzo', label: 'Razzo', svg: '<path d="M12 2.5c3 2.2 4.8 6 4.8 9.8L15 16H9l-1.8-3.7C7.2 8.5 9 4.7 12 2.5z"/><circle cx="12" cy="9.5" r="1.8"/><path d="M9 16l-2.5 3.5L10 18.5M15 16l2.5 3.5L14 18.5M12 18.5v3"/>' },
  { id: 'corona', label: 'Corona', svg: '<path d="M3.5 8l4 4.5L12 5.5l4.5 7 4-4.5-2 11h-13z"/>' },
  { id: 'fantasma', label: 'Fantasma', svg: '<path d="M5 21V11a7 7 0 0 1 14 0v10l-2.3-1.8-2.3 1.8-2.4-1.8-2.4 1.8-2.3-1.8z"/><path d="M9.5 10.5v1M14.5 10.5v1"/>' },
  { id: 'gatto', label: 'Gatto', svg: '<path d="M5 10V4l4 3h6l4-3v6a7 7 0 0 1-14 0z"/><path d="M9.5 11.5v.5M14.5 11.5v.5M11 15h2"/>' },
  { id: 'pesce', label: 'Pesce', svg: '<path d="M3 12c3-5 9-6.2 13-2l5-3v10l-5-3c-4 4.2-10 3-13-2z"/><path d="M8 11v.5"/>' },
  { id: 'albero', label: 'Albero', svg: '<path d="M12 3l6 8h-3l4 6H5l4-6H6z"/><path d="M12 17v4"/>' },
  { id: 'montagna', label: 'Montagna', svg: '<path d="M2.5 20l7-12 4 6 3-4 5 10z"/><path d="M7.3 11.8L9.5 13l2-1.2"/>' },
  { id: 'tazza', label: 'Tazza', svg: '<path d="M4 9h12v6a5 5 0 0 1-5 5H9a5 5 0 0 1-5-5z"/><path d="M16 11h1.5a2.5 2.5 0 0 1 0 5H16"/><path d="M8 3.5v2.5M12 3.5v2.5"/>' },
  { id: 'gelato', label: 'Gelato', svg: '<path d="M7 11l5 10.5L17 11"/><path d="M6.5 11a5.5 5.5 0 1 1 11 0z"/>' },
  { id: 'gioco', label: 'Videogiochi', svg: '<path d="M7 7h10a5 5 0 0 1 4.8 6.4l-.9 3.2a2.5 2.5 0 0 1-4.2 1.1L14 15h-4l-2.7 2.7a2.5 2.5 0 0 1-4.2-1.1l-.9-3.2A5 5 0 0 1 7 7z"/><path d="M8 9.8v3.4M6.3 11.5h3.4M15.5 10.5v.5M17.5 12.5v.5"/>' },
  { id: 'pallone', label: 'Pallone', svg: '<circle cx="12" cy="12" r="9"/><path d="M12 7.5l4 2.9-1.5 4.6h-5L8 10.4z"/><path d="M12 3v4.5M16 10.4l4.4-1.5M14.5 15l2.6 3.8M9.5 15l-2.6 3.8M8 10.4L3.6 8.9"/>' },
  { id: 'ciak', label: 'Ciak', svg: '<path d="M4 10h16v10H4z"/><path d="M3.6 10l-.7-3.9 15.8-2.8.7 3.9"/><path d="M7.8 5.6l2.1 3.3M12.9 4.7L15 8"/>' },
];

const byId = new Map(profileIcons.map((i) => [i.id, i]));

/** Il disegno di un'icona, o undefined se il nome è vuoto o sconosciuto (si mostra l'iniziale). */
export const profileIcon = (id: string | null | undefined) => (id ? byId.get(id) : undefined);
