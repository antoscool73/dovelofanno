# Componenti di terzi

DoveLoFanno è distribuito con licenza GPL-3.0-or-later (file [LICENSE](LICENSE)).
Usa i componenti qui sotto, ciascuno con la propria licenza. Le versioni sono quelle fissate nel repository.

## Inclusi nel programma distribuito

| Componente | Versione | Licenza | Dove |
| --- | --- | --- | --- |
| .NET runtime e ASP.NET Core | 10.0 | MIT | https://github.com/dotnet/runtime, https://github.com/dotnet/aspnetcore |
| Microsoft.Data.Sqlite | 10.0.14 | MIT | https://github.com/dotnet/efcore |
| SQLitePCLRaw (usato da Microsoft.Data.Sqlite) | come da Microsoft.Data.Sqlite | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| SQLite | come da SQLitePCLRaw | Pubblico dominio | https://sqlite.org/copyright.html |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.14 | MIT | https://github.com/dotnet/runtime |
| Microsoft.AspNetCore.OpenApi (e Microsoft.OpenApi) | 10.0.14 | MIT | https://github.com/dotnet/aspnetcore |
| Svelte | 5.57.3 | MIT | https://github.com/sveltejs/svelte |
| Inter (carattere, @fontsource-variable/inter) | 5.3.0 | OFL-1.1 | https://github.com/rsms/inter |
| Space Grotesk (carattere, @fontsource/space-grotesk) | 5.3.0 | OFL-1.1 | https://github.com/floriankarsten/space-grotesk |

## Programmi esterni

**ffmpeg / ffprobe** non fanno parte del codice di DoveLoFanno: il server li avvia come programmi separati.

- Immagine Docker: contiene il pacchetto `ffmpeg` di Ubuntu, compilato con componenti GPL (licenza complessiva GPL-2.0-or-later). Sorgenti: pacchetto `ffmpeg` della stessa versione di Ubuntu (`apt-get source ffmpeg`, oppure https://launchpad.net/ubuntu/+source/ffmpeg). Licenze dettagliate nell'immagine in `/usr/share/doc/ffmpeg*/copyright`.
- Windows: ffmpeg non è incluso; lo installa l'utente (per esempio `winget install Gyan.FFmpeg`) con la licenza della build scelta.

## Solo per compilare (non distribuiti)

Vite (MIT), TypeScript (Apache-2.0), svelte-check (MIT), openapi-typescript (MIT), xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), Microsoft.AspNetCore.Mvc.Testing (MIT).

## Dati e servizi

Dati e servizi esterni (TMDB, voti IMDb, guide XMLTV, playlist M3U) sono scelti e configurati dall'utente; le loro condizioni sono descritte nel documento di progetto, sezione "Licenze dei dati".
