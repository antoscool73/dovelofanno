using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DoveLoFanno.Data;
using DoveLoFanno.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using DoveLoFanno.Epg;
using DoveLoFanno.Library;
using DoveLoFanno.Media;
using DoveLoFanno.Ratings;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

/// <summary>Riconoscimento dei nomi dei file, come Jellyfin e Kodi.</summary>
public sealed class MediaNameTests
{
    [Theory]
    [InlineData("/nas/Film/Il Padrino (1972)/Il Padrino (1972).mkv", "Il Padrino", 1972)]
    [InlineData("/nas/Film/The.Matrix.1999.1080p.BluRay.x264-GRP.mkv", "The Matrix", 1999)]
    [InlineData("/nas/Film/2001 Odissea nello spazio (1968).avi", "2001 Odissea nello spazio", 1968)]
    [InlineData("/nas/Film/Avatar [2009]/movie.mkv", "Avatar", 2009)]
    [InlineData("/nas/Film/Mr. Bean's Holiday 2007.mkv", "Mr. Bean's Holiday", 2007)]
    [InlineData("/nas/Film/La vita è bella.mp4", "La vita è bella", 0)]
    [InlineData("/nas/Film/C'era.una.volta.il.West.1968.ITA.ENG.AC3.mkv", "C'era una volta il West", 1968)]
    [InlineData("/nas/Film/Blade Runner 2049 (2017)/Blade Runner 2049 (2017).mkv", "Blade Runner 2049", 2017)]
    // nomi veri del NAS (17/11/2026)
    [InlineData("/nas/_FILM/About Time (2013) BDrip XviD ENG-ITA Ac3 subs - Questione Di tempo -Shiv@/time.avi", "About Time", 2013)]
    [InlineData("/nas/_FILM/Tutta Colpa di Freud (2014) BRRip XviD Ac3 Italian - sub ENG -Shiv@/Tutta colpa di Freud.avi", "Tutta Colpa di Freud", 2014)]
    [InlineData("/nas/_FILM/Il bambino con il pigiama a righe/Il bambino con il pigiama a righe - CD 1.avi", "Il bambino con il pigiama a righe", 0)]
    [InlineData("/nas/_FILM/Men In Black III/Men.In.Black.III.2012.iTALiAN.AC3.BRRip.XviD-T4P3.CD2.avi", "Men In Black III", 2012)]
    [InlineData("/nas/_FILM/Odissea (1968) Franco Rossi/Odissea - 1968 Franco Rossi Disco 2.mkv", "Odissea", 1968)]
    [InlineData("/nas/_FILM/SuperQuark - Odissea/SuperQuark - Odissea - Parte 1-2.avi", "SuperQuark - Odissea", 0)]
    [InlineData("/nas/_FILM/Ritorno al futuro/Ritorno al futuro Parte III.H264..ita.eng.sub.ita.iCV-MIRCrew.mkv", "Ritorno al futuro Parte III", 0)]
    [InlineData("/nas/_FILM/Rocky.Collection.1976-2006.iTALiAN.AC3.Bluray.1080p.x264/Rocky.2.1979.iTALiAN-DSS.mkv", "Rocky 2", 1979)]
    [InlineData("/nas/_FILM/Gomorra.2008/Gomorra.2008.iTALiAN.DVDRip.XviD-SVD-CD1.avi", "Gomorra", 2008)]
    public void Film(string path, string title, int year)
    {
        var g = MediaName.Parse(path);
        Assert.False(g.IsEpisode);
        Assert.Equal((title, year), (g.Title, g.Year));
    }

    [Theory]
    [InlineData("/nas/Serie/Breaking Bad/Stagione 01/Breaking Bad - S01E02 - Il gatto nel sacco.mkv", "Breaking Bad", 1, 2, "Il gatto nel sacco")]
    [InlineData("/nas/Serie/Lost/Season 2/lost.2x05.hdtv.avi", "lost", 2, 5, "")]
    [InlineData("/nas/Serie/Don Matteo/Stagione 3/Episodio 4.mp4", "Don Matteo", 3, 4, "")]
    [InlineData("/nas/Serie/Dark/S02E08.mkv", "Dark", 2, 8, "")]
    [InlineData("/nas/Serie/The.Office.US.S03E10E11.720p.WEB-DL.mkv", "The Office US", 3, 10, "")]
    public void Episodi(string path, string show, int season, int episode, string episodeTitle)
    {
        var g = MediaName.Parse(path);
        Assert.True(g.IsEpisode);
        Assert.Equal((show, season, episode, episodeTitle), (g.Title, g.Season!.Value, g.Episode!.Value, g.EpisodeTitle));
    }

    [Fact]
    public void FileECartelleDaSaltare()
    {
        Assert.True(MediaName.SkipFile("Il Padrino-sample.mkv"));
        Assert.True(MediaName.SkipFile(".nascosto.mp4"));
        Assert.False(MediaName.SkipFile("Sampled (2020).mkv"));
        Assert.True(MediaName.SkipFolder("@eaDir"));
        Assert.True(MediaName.SkipFolder("Extras"));
        Assert.True(MediaName.SkipFolder("Bonus"));
        Assert.True(MediaName.Skipped("/nas/_FILM", "/nas/_FILM/Ritorno al futuro/Bonus/Bonus/00300.mkv"));
        Assert.False(MediaName.Skipped("/nas/_FILM", "/nas/_FILM/Ritorno al futuro/Ritorno al futuro Parte II.mkv"));
        Assert.False(MediaName.SkipFolder("Stagione 01"));
        Assert.True(MediaName.IsVideo("a.MKV"));
        Assert.False(MediaName.IsVideo("a.nfo"));
    }
}

/// <summary>Cartelle e nomi delle registrazioni per Jellyfin, Plex ed Emby.</summary>
public sealed class LibraryLayoutTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 17, 20, 30, 0, TimeSpan.Zero);

    static Recording.RecordingMedia M(string kind, string title, int year = 0, string episode = "", string subtitle = "", string group = "") =>
        new(kind, title, year, kind == "film" ? 238 : null, kind == "film" ? "movie" : "", "tt0068646", "Trama", "", "[\"Film\",\"Drammatico\"]",
            "Francis Ford Coppola", "Marlon Brando", episode, subtitle, group);

    [Theory]
    [InlineData("S2 E5", 2, 5)]
    [InlineData("S02E05", 2, 5)]
    [InlineData("Stagione 3 Ep. 12", 3, 12)]
    [InlineData("1x04", 1, 4)]
    public void Episodi(string text, int season, int episode) =>
        Assert.Equal((season, episode), Recording.LibraryLayout.ParseEpisode(text)!.Value);

    [Fact]
    public void SenzaStagioneNonEUnEpisodio()
    {
        Assert.Null(Recording.LibraryLayout.ParseEpisode("Ep. 12"));
        Assert.Null(Recording.LibraryLayout.ParseEpisode(""));
    }

    [Fact]
    public void FilmSerieProgrammiAudio()
    {
        string root = Path.Combine(Path.GetTempPath(), "dlf-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (f, b, info) = Recording.LibraryLayout.For("Il Padrino", "Iris", Start, "mp4", M("film", "Il Padrino", 1972), root);
            Assert.Equal((Path.Combine("Film", "Il Padrino (1972)"), "Il Padrino (1972)"), (f, b));
            Assert.Equal((238, "tt0068646"), (info!.TmdbId!.Value, info.ImdbId));
            Assert.Contains("<uniqueid type=\"tmdb\" default=\"true\">238</uniqueid>", Recording.Nfo.Build(info).ToString());
            // un secondo passaggio dello stesso film: un'altra versione nella stessa cartella
            Directory.CreateDirectory(Path.Combine(root, f));
            File.WriteAllText(Path.Combine(root, f, b + ".mp4"), "");
            var (f2, b2, _) = Recording.LibraryLayout.For("Il Padrino", "Iris", Start, "mp4", M("film", "Il Padrino", 1972), root);
            Assert.Equal(f, f2);
            Assert.StartsWith("Il Padrino (1972) - Iris ", b2);

            var (fs, bs, es) = Recording.LibraryLayout.For("Dark", "Rai 4", Start, "mp4", M("episode", "Dark", episode: "S2 E5", subtitle: "Verità", group: "Serie TV"), root);
            Assert.Equal((Path.Combine("Serie TV", "Dark", "Stagione 02"), "Dark - S02E05 - Verità"), (fs, bs));
            Assert.True(es!.IsEpisode);

            var (fp, bp, ip) = Recording.LibraryLayout.For("4 Ristoranti", "TV8", Start, "mp4", M("programme", "4 Ristoranti", group: "Intrattenimento"), root);
            Assert.Equal(Path.Combine("Programmi", "4 Ristoranti"), fp);
            Assert.StartsWith("4 Ristoranti 2026-11-17 ", bp);
            Assert.Null(ip);

            var (fa, _, ia) = Recording.LibraryLayout.For("Radio Deejay", "Deejay", Start, "mp3", M("film", "X", 2000), root);
            Assert.Equal(Path.Combine("Audio", "Radio Deejay"), fa);
            Assert.Null(ia);

            // registrazione a mano, senza opera né guida
            var (fm, _, _) = Recording.LibraryLayout.For("Prova", "TV8", Start, "mp4", null, root);
            Assert.Equal(Path.Combine("Programmi", "Prova"), fm);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}

/// <summary>Cosa si guarda nel browser così com'è e cosa ha bisogno di una copia.</summary>
public sealed class MediaProbeTests
{
    static ProbeInfo P(string container, string v, params (string Codec, string Lang)[] audio) =>
        new(100, container, v, audio.Length > 0 ? audio[0].Codec : "", 1920, 1080,
            audio.Select((a, i) => new TrackInfo(i, a.Codec, a.Lang, "")).ToList(), []);

    [Fact]
    public void Decisioni()
    {
        Assert.Equal(Playback.Direct, MediaProbe.Decide("a.mp4", P("mov,mp4,m4a,3gp,3g2,mj2", "h264", ("aac", "ita"))));
        Assert.Equal(Playback.Remux, MediaProbe.Decide("a.mkv", P("matroska,webm", "h264", ("aac", "ita"))));
        Assert.Equal(Playback.Remux, MediaProbe.Decide("a.ts", P("mpegts", "h264", ("aac", ""))));
        Assert.Equal(Playback.Audio, MediaProbe.Decide("a.mkv", P("matroska,webm", "h264", ("ac3", "ita"))));
        Assert.Equal(Playback.Audio, MediaProbe.Decide("a.ts", P("mpegts", "h264", ("mp2", "ita"))));
        Assert.Equal(Playback.Full, MediaProbe.Decide("a.mp4", P("mov,mp4,m4a,3gp,3g2,mj2", "hevc", ("aac", "ita"))));
        Assert.Equal(Playback.Full, MediaProbe.Decide("a.avi", P("avi", "mpeg4", ("mp3", ""))));
        // inglese per prima e italiano dopo: la copia mette l'italiano davanti
        var two = P("mov,mp4,m4a,3gp,3g2,mj2", "h264", ("aac", "eng"), ("aac", "ita"));
        Assert.Equal(Playback.Remux, MediaProbe.Decide("a.mp4", two));
        var args = MediaProbe.CopyArgs("in.mp4", "out.mp4", two, Playback.Remux);
        int first = args.IndexOf("0:a:1"), second = args.IndexOf("0:a:0");
        Assert.True(first > 0 && second > first);
        // audio ricodificato sempre in stereo: un 5.1 in AAC usciva muto nel browser (Interstellar, 24/11/2026)
        var ac3 = MediaProbe.CopyArgs("in.mkv", "out.mp4", P("matroska,webm", "h264", ("ac3", "ita")), Playback.Audio);
        int ac = ac3.IndexOf("-ac");
        Assert.True(ac > 0 && ac3[ac + 1] == "2");
        Assert.DoesNotContain("-ac", MediaProbe.CopyArgs("in.mkv", "out.mp4", two, Playback.Remux));
        Assert.Equal(Playback.Direct, MediaProbe.Decide("a.mp3", new ProbeInfo(100, "mp3", "", "mp3", null, null, [new TrackInfo(0, "mp3", "", "")], [])));
    }

    [Fact]
    public void LetturaDiFfprobe()
    {
        const string json = """
            {"streams":[
              {"index":0,"codec_name":"h264","codec_type":"video","width":1280,"height":720},
              {"index":1,"codec_name":"ac3","codec_type":"audio","tags":{"language":"ita","title":"Stereo"}},
              {"index":2,"codec_name":"aac","codec_type":"audio","tags":{"language":"eng"}},
              {"index":3,"codec_name":"subrip","codec_type":"subtitle","tags":{"language":"ita"}},
              {"index":4,"codec_name":"hdmv_pgs_subtitle","codec_type":"subtitle"},
              {"index":5,"codec_name":"mjpeg","codec_type":"video","disposition":{"attached_pic":1}}],
             "format":{"format_name":"matroska,webm","duration":"5400.120000"}}
            """;
        var p = MediaProbe.Parse(json)!;
        Assert.Equal(("h264", "ac3", 1280, 720, 5400), (p.VideoCodec, p.AudioCodec, p.Width!.Value, p.Height!.Value, (int)p.DurationS));
        Assert.Equal(["ita", "eng"], p.Audio.Select(a => a.Language));
        Assert.Equal(2, p.Subtitles.Count);
        Assert.True(MediaProbe.IsTextSubtitle(p.Subtitles[0].Codec));
        Assert.False(MediaProbe.IsTextSubtitle(p.Subtitles[1].Codec));
        Assert.Null(MediaProbe.Parse("non json"));
    }

    [Fact]
    public void SottotitoliSrtInWebVtt()
    {
        string vtt = MediaProbe.SrtToVtt("﻿1\r\n00:00:01,500 --> 00:00:03,000\r\nCiao, come va?\r\n");
        Assert.StartsWith("WEBVTT\n\n1\n00:00:01.500 --> 00:00:03.000\nCiao, come va?", vtt);
    }
}

/// <summary>Seguiti numerati in modi diversi tra il nome del file e TMDB.</summary>
public sealed class SequelTests
{
    [Theory]
    [InlineData("Rocky 1", "Rocky")]
    [InlineData("Rocky 2", "Rocky II")]
    [InlineData("Men In Black III", "Men In Black 3")]
    [InlineData("Matrix 1", "Matrix")]
    public void Varianti(string q, string expected) => Assert.Contains(expected, LibraryScanner.SequelVariants(q));

    [Fact]
    public void SenzaNumeroNessunaVariante() => Assert.Empty(LibraryScanner.SequelVariants("Inception"));

    [Fact]
    public async Task TitoliDiUnaParolaSeguitiEFilmNoti()
    {
        var tmdb = new FakeLibraryTmdb();
        tmdb.Movies["Ribelle"] = [new TmdbMovie(62177, "Ribelle - The Brave", "Brave", "2012-06-21", 7.0, 15000)];
        tmdb.Movies["Odissea"] = [new TmdbMovie(62, "2001: Odissea nello spazio", "2001: A Space Odyssey", "1968-04-02", 8.1, 12000)];
        tmdb.Movies["Rocky II"] = [new TmdbMovie(1367, "Rocky II", "Rocky II", "1979-06-15", 6.9, 4500)];
        tmdb.Movies["Rapunzel"] = [new TmdbMovie(9001, "Rapunzel", "Rapunzel", "1983-01-01", 5.0, 30),
                                   new TmdbMovie(38757, "Rapunzel - L'intreccio della torre", "Tangled", "2010-11-24", 7.6, 12000)];
        var scanner = new LibraryScanner(null!, null!, null!, TimeProvider.System) { Pace = TimeSpan.Zero };
        Assert.Equal(62177, (await scanner.FindMovieAsync(tmdb, "Ribelle", 2012, CancellationToken.None))?.Id);
        Assert.Null(await scanner.FindMovieAsync(tmdb, "Odissea", 1968, CancellationToken.None));
        Assert.Equal(1367, (await scanner.FindMovieAsync(tmdb, "Rocky 2", 1979, CancellationToken.None))?.Id);
        Assert.Equal(38757, (await scanner.FindMovieAsync(tmdb, "Rapunzel", 0, CancellationToken.None))?.Id);
    }
}

/// <summary>Risposte di TMDB scritte a mano.</summary>
sealed class FakeLibraryTmdb : ILibraryTmdb
{
    public readonly Dictionary<string, List<TmdbMovie>> Movies = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, List<TmdbMovie>> Tv = new(StringComparer.OrdinalIgnoreCase);
    public int Calls;

    public Task<IReadOnlyList<TmdbMovie>> SearchMovieAsync(string title, int year, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<TmdbMovie>>(Movies.TryGetValue(title, out var l) ? l.Where(m => year == 0 || m.Year == year).ToList() : []);
    }

    public Task<IReadOnlyList<TmdbMovie>> SearchTvAsync(string title, int year, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<TmdbMovie>>(Tv.TryGetValue(title, out var l) ? l : []);
    }

    public Task<string> ImdbIdAsync(int tmdbId, CancellationToken ct) => Task.FromResult("tt" + tmdbId);
    public Task<string> TvImdbIdAsync(int tmdbId, CancellationToken ct) => Task.FromResult("tt" + tmdbId);
    public Task<MovieDetails> MovieDetailsAsync(int tmdbId, CancellationToken ct) =>
        Task.FromResult(new MovieDetails("Trama", $"/poster{tmdbId}.jpg", "", 100, "", "", "", []));
    public Task<MovieDetails> TvDetailsAsync(int tmdbId, CancellationToken ct) =>
        Task.FromResult(new MovieDetails("Trama della serie", $"/tv{tmdbId}.jpg", "", 45, "", "", "", [], [18]));
    public readonly Dictionary<(int, int), Dictionary<int, string>> Seasons = [];
    public Task<IReadOnlyDictionary<int, string>> SeasonAsync(int tmdbId, int season, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<int, string>>(Seasons.TryGetValue((tmdbId, season), out var d) ? d : new Dictionary<int, string>());
}

/// <summary>Scansione di una cartella vera con file fatti da ffmpeg, abbinamento, ripresa, visto.</summary>
[Collection("ffmpeg")]
public sealed class LibraryScanTests(ITestOutputHelper output) : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-lib-" + Guid.NewGuid().ToString("N"));
    string Db => Path.Combine(dir, "dlf.db");
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Un video di prova di 4 secondi: H.264 + audio nel codec indicato.</summary>
    internal static async Task MakeVideoAsync(MediaTools tools, string path, string audioCodec = "aac", int seconds = 4)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        int code = await tools.RunFfmpegAsync(
            ["-hide_banner", "-y", "-f", "lavfi", "-i", $"testsrc=size=320x180:rate=25:duration={seconds}", "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
             "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", audioCodec, "-shortest", path],
            path + ".log", CancellationToken.None);
        Assert.Equal(0, code);
        File.Delete(path + ".log");
    }

    [Fact]
    public async Task ScansioneAbbinamentoRipresaEVisto()
    {
        var tools = HlsSimulator.FindTools();
        if (tools == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }

        // guida con "Il Padrino" (1972) già abbinato a TMDB 238 dai voti: il file deve finire sulla stessa opera
        var guide = new GuideStore(Db);
        guide.Save(guide.AddSource("https://example.org/g.xml"), XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(GuideStoreTests.Xmltv(Now))), Now.LocalDateTime), Now);
        long padrinoGuida = guide.Query(new GuideQuery(Now, Now.AddHours(6))).Single(p => p.Title == "Il Padrino").WorkId!.Value;
        using (var c = Database.Open(Db))
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE works SET tmdb_id = 238, tmdb_kind = 'movie' WHERE id = $w";
            cmd.Parameters.AddWithValue("$w", padrinoGuida);
            cmd.ExecuteNonQuery();
        }

        string films = Path.Combine(dir, "Film"), series = Path.Combine(dir, "Serie");
        await MakeVideoAsync(tools, Path.Combine(films, "Il Padrino (1972)", "Il Padrino (1972).mp4"));
        await MakeVideoAsync(tools, Path.Combine(films, "Titolo Introvabile.mkv"), "ac3");
        await MakeVideoAsync(tools, Path.Combine(series, "Breaking Bad", "Stagione 01", "Breaking Bad - S01E01 - Pilota.mp4"));
        await MakeVideoAsync(tools, Path.Combine(series, "Breaking Bad", "Stagione 01", "Breaking Bad - S01E02.mp4"));
        File.Copy(Path.Combine(films, "Il Padrino (1972)", "Il Padrino (1972).mp4"), Path.Combine(films, "Il Padrino (1972)", "padrino-sample.mp4"));
        Directory.CreateDirectory(Path.Combine(films, "@eaDir"));
        File.Copy(Path.Combine(films, "Titolo Introvabile.mkv"), Path.Combine(films, "@eaDir", "x.mkv"));

        var lib = new LibraryStore(Db);
        var works = new WorkStore(Db, guide);
        Assert.NotNull(lib.AddFolder(films, "auto"));
        Assert.Null(lib.AddFolder(films, "auto"));
        lib.AddFolder(series, "series");
        var tmdb = new FakeLibraryTmdb();
        tmdb.Movies["Il Padrino"] = [new TmdbMovie(238, "Il padrino", "The Godfather", "1972-03-14", 8.7, 21000)];
        tmdb.Tv["Breaking Bad"] = [new TmdbMovie(1396, "Breaking Bad", "Breaking Bad", "2008-01-20", 8.9, 15000)];
        tmdb.Seasons[(1396, 1)] = new() { [1] = "Pilota (da TMDB)", [2] = "Il gatto nel sacco" };
        var scanner = new LibraryScanner(lib, works, tools, TimeProvider.System) { Pace = TimeSpan.Zero };

        var r = await scanner.ScanAsync(tmdb, CancellationToken.None);
        Assert.Equal((2, 4, 4, 0, 4, 3, 1, ""), (r.Folders, r.Files, r.New, r.Missing, r.Probed, r.Matched, r.Unmatched, r.Error));

        var film = Assert.Single(lib.Entries("films", null, "added", null));
        Assert.Equal((padrinoGuida, "Il Padrino"), (film.WorkId!.Value, film.Title));     // l'opera della guida, con il suo titolo
        Assert.EndsWith("/poster238.jpg", film.Poster);
        var show = Assert.Single(lib.Entries("series", null, "added", null));
        Assert.Equal(("Breaking Bad", 2, 2008, "TMDB"), (show.Title, show.Episodes, show.Year, show.RatingSource));
        var fix = Assert.Single(lib.Entries("unmatched", null, "added", null));
        Assert.Equal("Titolo Introvabile", fix.Title);
        Assert.Equal(4, lib.Entries("all", null, "title", null).Sum(e => e.Files) + 1);  // + il file da sistemare

        // il film si guarda subito, l'MKV con AC3 ha bisogno della copia
        var padrinoItem = lib.Item(film.PlayItemId!.Value, null)!;
        Assert.True(padrinoItem.Playable);
        Assert.Equal((320, 180), (padrinoItem.Width!.Value, padrinoItem.Height!.Value));
        Assert.InRange(padrinoItem.DurationS, 3, 5);
        var mkv = lib.Item(fix.ItemId!.Value, null)!;
        Assert.False(mkv.Playable);
        Assert.Equal("needed", mkv.CopyState);
        Assert.True(lib.QueueCopy(mkv.Id));
        Assert.Equal(mkv.Id, lib.NextCopy()!.Value.Id);

        // la copia per il browser: audio ricodificato in AAC, video copiato
        string copy = Path.Combine(dir, "copia.mp4");
        var probe = lib.StoredProbe(mkv.Id)!;
        Assert.Equal(0, await tools.RunFfmpegAsync(MediaProbe.CopyArgs(Path.Combine(films, "Titolo Introvabile.mkv"), copy, probe, MediaProbe.Decide("x.mkv", probe)), copy + ".log", CancellationToken.None));
        var copied = await MediaProbe.ProbeAsync(tools, copy, CancellationToken.None);
        Assert.Equal(("h264", "aac"), (copied!.VideoCodec, copied.AudioCodec));
        lib.SetCopy(mkv.Id, "done", copy);
        Assert.True(lib.Item(mkv.Id, null)!.Playable);
        Assert.Equal(copy, lib.StreamPath(mkv.Id)!.Value.Path);

        // la Scheda della serie mostra gli episodi in ordine
        var detail = works.Detail(show.WorkId!.Value, null, Now)!;
        Assert.Equal([1, 2], detail.Files.Select(f => f.Episode!.Value));
        Assert.Equal(("Pilota", "Il gatto nel sacco"), (detail.Files[0].EpisodeTitle, detail.Files[1].EpisodeTitle));  // il nome del file vince su TMDB

        // ripresa e visto per profilo; la serie diventa vista quando lo sono tutti gli episodi
        var profiles = new ProfileStore(Db);
        long anna = profiles.Create("Anna", false, null, null, Now).Id;
        long bimbi = profiles.Create("Bimbi", true, null, null, Now).Id;
        new TonightStore(Db, guide).Add(anna, show.WorkId.Value, Now);
        long ep1 = detail.Files[0].ItemId, ep2 = detail.Files[1].ItemId;
        Assert.Equal(ProgressResult.Saved, lib.Progress(anna, ep1, 2, 0, Now));
        Assert.Equal(2, lib.Item(ep1, anna)!.PositionS);
        Assert.Equal(0, lib.Item(ep1, bimbi)!.PositionS);
        Assert.Equal(ep2, lib.Item(ep1, anna)!.NextItemId);
        Assert.Equal(ProgressResult.Watched, lib.Progress(anna, ep1, 4, 0, Now));
        Assert.False(lib.Entries("series", anna, "added", null).Single().Watched);
        Assert.True(lib.MarkWatched(anna, ep2, Now));
        var seen = lib.Entries("series", anna, "added", null).Single();
        Assert.True(seen.Watched);
        Assert.Empty(new TonightStore(Db, guide).Items(anna, Now)!);          // vista: fuori dalla mia serata
        Assert.Empty(lib.Entries("series", bimbi, "added", null));                 // un dramma: non per il profilo bambino
        Assert.False(lib.Item(ep2, bimbi)!.Watched);                               // il visto è per profilo
        Assert.Equal(ProgressResult.NotFound, lib.Progress(999, ep1, 1, 0, Now));

        // visti e votati: la serie vista, il film votato senza averlo visto
        new WorkStore(Db, guide).SetStars(anna, padrinoGuida, 5, Now);
        Assert.Equal(["Il Padrino", "Breaking Bad"], lib.Entries("watched", anna, "stars", null).Select(e => e.Title));
        Assert.Empty(lib.Entries("watched", bimbi, "added", null));

        // abbinamento a mano del file da sistemare, a un cartone animato (genere TMDB 16)
        long work = lib.FilmWork("Titolo Ritrovato", 2001, 999, "", "", 0, 0, false, Now, [16, 10751]);
        lib.SetMatch(mkv.Id, work, "manual");
        Assert.Empty(lib.Entries("unmatched", null, "added", null));
        Assert.Equal(2, lib.Entries("films", null, "title", null).Count);
        // il profilo bambino vede solo il cartone: niente Padrino, niente Breaking Bad, niente file da sistemare
        Assert.Equal(["Titolo Ritrovato"], lib.Entries("all", bimbi, "title", null).Select(e => e.Title));
        Assert.Empty(lib.Entries("unmatched", bimbi, "added", null));

        // seconda scansione: niente di nuovo; un file cancellato risulta mancante (e un abbinamento a mano resta)
        File.Delete(Path.Combine(series, "Breaking Bad", "Stagione 01", "Breaking Bad - S01E02.mp4"));
        int calls = tmdb.Calls;
        r = await scanner.ScanAsync(tmdb, CancellationToken.None);
        Assert.Equal((3, 0, 1, 0), (r.Files, r.New, r.Missing, r.Probed));
        Assert.Equal(calls, tmdb.Calls);
        Assert.Equal(1, lib.Entries("series", null, "added", null).Single().Episodes);
        Assert.Equal("manual", lib.Item(mkv.Id, null)!.MatchState);
        Assert.Equal(1, lib.Counts().Missing);

        // togliere una cartella toglie i suoi file dalla libreria (non dal disco)
        Assert.True(lib.DeleteFolder(lib.Folders().Single(f => f.Path == series).Id));
        Assert.Empty(lib.Entries("series", null, "added", null));
        Assert.True(Directory.Exists(series));
    }

    [Fact]
    public async Task SenzaChiaveTmdbSiAbbinaDalNome()
    {
        var tools = HlsSimulator.FindTools();
        if (tools == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        string films = Path.Combine(dir, "Film");
        await MakeVideoAsync(tools, Path.Combine(films, "Nuovo Cinema Paradiso (1988).mp4"));
        var guide = new GuideStore(Db);
        var lib = new LibraryStore(Db);
        lib.AddFolder(films, "films");
        var r = await new LibraryScanner(lib, new WorkStore(Db, guide), tools, TimeProvider.System).ScanAsync(null, CancellationToken.None);
        Assert.Equal((1, 0), (r.Matched, r.Unmatched));
        var e = Assert.Single(lib.Entries("films", null, "added", null));
        Assert.Equal(("Nuovo Cinema Paradiso", 1988, ""), (e.Title, e.Year, e.Poster));
        Assert.Equal("local", lib.Item(e.PlayItemId!.Value, null)!.MatchState);
        // con la chiave, i file abbinati solo dal nome si ricercano su TMDB
        Assert.Single(lib.Pending(withTmdb: true));
        Assert.Empty(lib.Pending(withTmdb: false));
    }
}

/// <summary>Liste personali, di famiglia e la mia serata.</summary>
public sealed class ListTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-list-" + Guid.NewGuid().ToString("N"));
    string Db => Path.Combine(dir, "dlf.db");
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void ListePermessiEBambini()
    {
        var guide = new GuideStore(Db);
        guide.Save(guide.AddSource("https://example.org/g.xml"), XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(GuideStoreTests.Xmltv(Now))), Now.LocalDateTime), Now);
        var progs = guide.Query(new GuideQuery(Now, Now.AddHours(6)));
        long padrino = progs.Single(p => p.Title == "Il Padrino").WorkId!.Value;
        var profiles = new ProfileStore(Db);
        long a = profiles.Create("Anna", false, null, null, Now).Id;
        long b = profiles.Create("Bimbi", true, null, null, Now).Id;
        var lists = new ListStore(Db, guide);

        var mine = lists.Lists(a)!;
        var tonight = Assert.Single(mine);
        Assert.True(tonight.IsTonight);
        Assert.Equal(ListResult.Fixed, lists.Update(a, tonight.Id, "Altro nome", null));
        Assert.Equal(ListResult.Fixed, lists.Delete(a, tonight.Id));

        var (ok, rid) = lists.Create(a, "Da rivedere", false, Now);
        Assert.Equal(ListResult.Ok, ok);
        Assert.Equal(ListResult.Invalid, lists.Create(a, "  ", false, Now).Result);
        Assert.Equal(ListResult.Forbidden, lists.Create(b, "Dei bimbi per tutti", true, Now).Result);
        Assert.Equal(ListResult.Ok, lists.Create(b, "Cartoni", false, Now).Result);
        Assert.Equal(ListResult.Ok, lists.Add(a, rid, padrino, Now));
        Assert.Equal(ListResult.Ok, lists.Add(a, rid, padrino, Now));          // due volte: resta uno
        Assert.Equal(ListResult.NoWork, lists.Add(a, rid, 999999, Now));
        Assert.Equal(ListResult.NotFound, lists.Add(b, rid, padrino, Now));     // la lista personale di un altro non si vede

        var d = lists.Detail(a, rid, "title", Now)!;
        var it = Assert.Single(d.Items);
        Assert.Equal(("Il Padrino", "film"), (it.Title, it.Kind));
        Assert.NotNull(it.Next);
        Assert.Equal(["Da rivedere"], lists.ListsOf(a, padrino).Select(l => l.Name));

        // in famiglia: la vede anche il profilo bambino, ma non la cambia e non vede i film non per bambini
        Assert.Equal(ListResult.Ok, lists.Update(a, rid, "Da rivedere insieme", true));
        var fam = lists.Lists(b)!.Single(l => l.Family);
        Assert.Equal(("Da rivedere insieme", false), (fam.Name, fam.CanEdit));
        Assert.Equal(ListResult.Forbidden, lists.Remove(b, rid, padrino));
        Assert.Empty(lists.Detail(b, rid, "added", Now)!.Items);
        Assert.Single(lists.Detail(a, rid, "added", Now)!.Items);
        Assert.Equal(ListResult.Invalid, lists.Update(a, rid, null, false));    // da famiglia non si torna personale
        Assert.Equal(["Da rivedere insieme"], lists.ListsOf(b, padrino).Select(l => l.Name));

        Assert.Equal(ListResult.Ok, lists.Remove(a, rid, padrino));
        Assert.Equal(ListResult.Ok, lists.Delete(a, rid));
        Assert.Null(lists.Lists(424242));
        Assert.Equal(3, lists.Lists(b)!.Count + lists.Lists(a)!.Count);        // due serate + "Cartoni"
    }
}

/// <summary>Libreria attraverso l'API: cartella, scansione, video a intervalli per il player, ripresa, liste.</summary>
[Collection("ffmpeg")]
public sealed class LibraryApiTests(ITestOutputHelper output) : IAsyncLifetime
{
    readonly string data = Path.Combine(Path.GetTempPath(), "dlf-libapi-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    MediaTools? tools;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task InitializeAsync()
    {
        tools = HlsSimulator.FindTools();
        if (tools == null) return Task.CompletedTask;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DoveLoFanno:DataDir", data);
            b.UseSetting("DoveLoFanno:Ffmpeg", tools.FfmpegPath);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (factory != null) await factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(data, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task DallaCartellaAlPlayer()
    {
        if (factory == null || tools == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        string films = Path.Combine(data, "nas", "Film");
        await LibraryScanTests.MakeVideoAsync(tools, Path.Combine(films, "Nuovo Cinema Paradiso (1988).mp4"), seconds: 6);
        await File.WriteAllTextAsync(Path.Combine(films, "Nuovo Cinema Paradiso (1988).it.srt"), "1\n00:00:01,000 --> 00:00:02,000\nCiao\n");
        var http = factory.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/v1/library/folders", new { path = "relativo", kind = "auto" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/v1/library/folders", new { path = Path.Combine(data, "non-esiste"), kind = "auto" })).StatusCode);
        var add = await http.PostAsJsonAsync("/api/v1/library/folders", new { path = films, kind = "films" });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/v1/library/folders", new { path = films + Path.DirectorySeparatorChar, kind = "auto" })).StatusCode);

        // la scansione parte da sola: si aspetta che il file sia letto
        JsonElement entry = default;
        for (int i = 0; i < 150; i++)
        {
            var found = await http.GetFromJsonAsync<JsonElement>("/api/v1/library?section=films");
            if (found.GetArrayLength() == 1 && found[0].GetProperty("durationS").GetInt32() > 0) { entry = found[0]; break; }
            await Task.Delay(200);
        }
        Assert.Equal(JsonValueKind.Object, entry.ValueKind);
        Assert.Equal("Nuovo Cinema Paradiso", entry.GetProperty("title").GetString());
        long item = entry.GetProperty("playItemId").GetInt64();
        var status = await http.GetFromJsonAsync<JsonElement>("/api/v1/library/status");
        Assert.Equal(1, status.GetProperty("counts").GetProperty("files").GetInt32());

        // video a intervalli, come lo chiede il browser per saltare
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/library/items/{item}/stream"))
        {
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);
            using var res = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.PartialContent, res.StatusCode);
            Assert.Equal("video/mp4", res.Content.Headers.ContentType!.MediaType);
            Assert.Equal(100, (await res.Content.ReadAsByteArrayAsync()).Length);
        }
        var detail = await http.GetFromJsonAsync<JsonElement>($"/api/v1/library/items/{item}");
        var sub = detail.GetProperty("subtitles").EnumerateArray().Single();
        Assert.Equal(("f0", "Italiano"), (sub.GetProperty("id").GetString(), sub.GetProperty("label").GetString()));
        string vtt = await http.GetStringAsync($"/api/v1/library/items/{item}/subtitles/f0");
        Assert.StartsWith("WEBVTT", vtt);
        Assert.Contains("00:00:01.000 --> 00:00:02.000", vtt);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync($"/api/v1/library/items/{item}/prepare", null)).StatusCode);

        // ripresa del profilo e "visto" oltre il 90%
        var prof = await (await http.PostAsJsonAsync("/api/v1/profiles", new { name = "Anna", isChild = false })).Content.ReadFromJsonAsync<JsonElement>();
        long p = prof.GetProperty("id").GetInt64();
        var saved = await (await http.PutAsJsonAsync($"/api/v1/profiles/{p}/items/{item}/progress", new { positionS = 2, durationS = 6 })).Content.ReadFromJsonAsync<ProgressReply>(Json);
        Assert.False(saved!.Watched);
        var cont = await http.GetFromJsonAsync<JsonElement>($"/api/v1/profiles/{p}/continue");
        Assert.Equal(0, cont.GetArrayLength());                                 // sotto i 30 secondi non è "da continuare"
        Assert.Equal(2, (await http.GetFromJsonAsync<JsonElement>($"/api/v1/library/items/{item}?profileId={p}")).GetProperty("positionS").GetInt32());
        saved = await (await http.PutAsJsonAsync($"/api/v1/profiles/{p}/items/{item}/progress", new { positionS = 6, durationS = 6 })).Content.ReadFromJsonAsync<ProgressReply>(Json);
        Assert.True(saved!.Watched);

        // liste: nuova, con il film, che la Scheda mostra
        long work = entry.GetProperty("workId").GetInt64();
        var created = await http.PostAsJsonAsync($"/api/v1/profiles/{p}/lists", new { name = "Bellissimi", family = false });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        long list = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync($"/api/v1/profiles/{p}/lists/{list}/items", new { workId = work })).StatusCode);
        var scheda = await http.GetFromJsonAsync<JsonElement>($"/api/v1/works/{work}?profileId={p}");
        Assert.Equal("Bellissimi", scheda.GetProperty("lists").EnumerateArray().Single().GetProperty("name").GetString());
        Assert.True(scheda.GetProperty("watched").GetBoolean());
        Assert.Equal(item, scheda.GetProperty("files").EnumerateArray().Single().GetProperty("itemId").GetInt64());
        long tonight = (await http.GetFromJsonAsync<JsonElement>($"/api/v1/profiles/{p}/lists")).EnumerateArray()
            .Single(l => l.GetProperty("isTonight").GetBoolean()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict, (await http.DeleteAsync($"/api/v1/profiles/{p}/lists/{tonight}")).StatusCode);

        // togliere il file dalla libreria senza cancellarlo
        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/api/v1/library/items/{item}")).StatusCode);
        Assert.True(File.Exists(Path.Combine(films, "Nuovo Cinema Paradiso (1988).mp4")));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/v1/library/items/{item}/stream")).StatusCode);
    }
}
