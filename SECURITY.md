# Sicurezza

## Versioni seguite

Durante la beta (versioni 0.x) le correzioni di sicurezza escono solo nell'ultima versione rilasciata: per averle si
aggiorna.

## Come segnalare un problema

**Non aprire una segnalazione pubblica.** Usa uno di questi canali privati:

- il pulsante **«Report a vulnerability»** nella scheda
  [Security](https://github.com/antoscool73/dovelofanno/security/advisories/new) del repository;
- oppure un'email a <info@dovelofanno.it> con oggetto «Sicurezza».

Scrivi la versione, il tipo di installazione (Docker, Windows), cosa si riesce a fare e i passi per riprodurlo.
Non mandare dati veri: niente playlist, chiavi TMDB, token di Telegram o password.

Riceverai una risposta entro 7 giorni. Il problema viene corretto in privato e reso pubblico insieme alla versione che
lo risolve, citando chi l'ha segnalato (se lo desidera).

## Cosa conta

DoveLoFanno è pensato per la rete di casa. Interessano soprattutto:

- accesso al server senza password da fuori casa, o da casa quando la password è impostata;
- lettura o modifica di file fuori dalle cartelle configurate (registrazioni, libreria, dati);
- esecuzione di comandi sul server, anche attraverso ffmpeg o i nomi dei file;
- furto della sessione o delle credenziali salvate (chiave TMDB, token di Telegram);
- installatore Windows o immagine Docker che danno più permessi del necessario.

Non sono problemi di sicurezza di DoveLoFanno: un server esposto su Internet senza password contro le avvertenze,
problemi di sorgenti e playlist di terzi, attacchi che richiedono già il controllo del computer o del NAS.

---

## English summary

Only the latest 0.x release gets security fixes. Report vulnerabilities privately via GitHub's "Report a
vulnerability" (Security tab) or by email to info@dovelofanno.it, never in a public issue. You will get an answer within
7 days; fixes are disclosed together with the release that contains them.
