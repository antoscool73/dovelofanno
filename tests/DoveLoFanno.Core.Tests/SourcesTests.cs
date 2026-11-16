using DoveLoFanno.Data;
using DoveLoFanno.Recording;
using DoveLoFanno.Sources;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

public class SourcesTests
{
    // stesso stile della playlist predefinita di RegistraTV: attributi attaccati, user agent in due modi, DRM con KODIPROP
    const string Playlist = """
        #EXTM3U x-tvg-url="https://epgshare01.online/epgshare01/epg_ripper_IT1.xml.gz"
        #EXTINF:-1 tvg-id="Rai1.it"tvg-logo="https://x/rai1.png"group-title="Rai",Rai 1
        https://mediapolis.rai.it/relinker/relinkerServlet.htm?cont=2606
        #EXTINF:-1 tvg-id="Tv8.it" http-user-agent="Mozilla/5.0 (Prova)" group-title="Sky",TV8
        #EXTVLCOPT:http-referrer=https://tv8.it/
        https://tv8-lh.akamaihd.net/i/tv8_1@1/master.m3u8?hdnea=token
        #EXTINF:-1 tvg-id="" group-title="Discovery",NOVE
        #KODIPROP:inputstream.adaptive.license_type=com.widevine.alpha
        #KODIPROP:inputstream.adaptive.license_key=https://lic.example/
        https://example.org/nove.mpd
        #EXTINF:-1 group-title="Locali",Canale Senza Id
        #EXTVLCOPT:http-user-agent=VLC/3.0
        http://example.org/locale.m3u8
        """;

    // il file può avere fine riga CRLF (git su Windows): le sostituzioni sotto lavorano su LF
    static string Pl => Playlist.ReplaceLineEndings("\n");

    [Fact]
    public void PlaylistComeQuellaDiRegistraTV()
    {
        var p = M3uParser.Parse(Pl.Replace("\n", "\r\n"));
        Assert.Equal("https://epgshare01.online/epgshare01/epg_ripper_IT1.xml.gz", p.EpgUrl);
        Assert.Equal(4, p.Entries.Count);
        var rai = p.Entries[0];
        Assert.Equal(("Rai 1", "Rai1.it", "https://x/rai1.png", "Rai"), (rai.Name, rai.TvgId, rai.Logo, rai.Group));
        var tv8 = p.Entries[1];
        Assert.Equal(("Mozilla/5.0 (Prova)", "https://tv8.it/", false), (tv8.UserAgent, tv8.Referrer, tv8.Drm));
        Assert.True(p.Entries[2].Drm);
        Assert.Equal("VLC/3.0", p.Entries[3].UserAgent);
    }

    [Theory]
    [InlineData("https://x/live.m3u8", "application/vnd.apple.mpegurl", "")]
    [InlineData("https://x/live", "application/vnd.apple.mpegurl", "hls")]
    [InlineData("https://x/live.php?id=1", "application/x-mpegURL", "hls")]
    [InlineData("https://x/manifest", "application/dash+xml", "dash")]
    [InlineData("https://x/live", "video/mp2t", "")]
    public void FormatoForzatoSoloSeServe(string url, string ctype, string expected) =>
        Assert.Equal(expected, StreamResolver.FormatFor(url, ctype));

    [Fact]
    public void Ffprobe_Funziona()
    {
        const string json = """{"streams":[{"codec_type":"video","codec_name":"h264","width":854,"height":480},{"codec_type":"audio","codec_name":"aac"}],"format":{"format_name":"hls"}}""";
        var r = StreamProber.Classify(0, json, "");
        Assert.Equal((StreamState.Ok, "hls", 854, 480, "h264", "aac"), (r.State, r.Kind, r.Width, r.Height, r.VideoCodec, r.AudioCodec));
    }

    [Theory]
    [InlineData("Server returned 403 Forbidden (access denied)", StreamState.Geo)]
    [InlineData("HTTP error 451 Unavailable For Legal Reasons", StreamState.Geo)]
    [InlineData("Server returned 429 Too Many Requests", StreamState.Limit)]
    [InlineData("Connection refused", StreamState.Unreachable)]
    [InlineData("timeout di ffprobe", StreamState.Unreachable)]
    public void Ffprobe_Errori(string stderr, string expected) =>
        Assert.Equal(expected, StreamProber.Classify(1, "", stderr).State);

    static StreamCandidate C(long id, long source, string state, int sp = 0, int stp = 0, int? max = null) =>
        new(id, source, "u" + id, "", "", state, stp, sp, max);

    [Fact]
    public void OrdineDeiFlussi()
    {
        var list = StreamSelector.Order(
        [
            C(1, 1, StreamState.Unreachable),
            C(2, 2, StreamState.Ok, sp: 1),
            C(3, 1, StreamState.Drm),
            C(4, 1, StreamState.Ok, stp: 1),
            C(5, 1, StreamState.Unchecked),
            C(6, 3, StreamState.Ok, max: 1),
        ], new Dictionary<long, int> { [3] = 1 });
        // DRM escluso, sorgente 3 piena, quelli che non andavano in fondo; poi priorità sorgente, flusso, stato
        Assert.Equal(new long[] { 5, 4, 2, 1 }, list.Select(x => x.StreamId));
    }

    [Fact]
    public void Nfo_FilmEdEpisodio()
    {
        var film = new MediaInfo("Il Padrino", 1972, "Una famiglia...", ["Drammatico"], 238, "tt0068646", "Rete 4");
        Assert.Equal(("Il Padrino (1972)", "Il Padrino (1972)"), Nfo.LibraryPath(film));
        var x = Nfo.Build(film).Root!;
        Assert.Equal("movie", x.Name.LocalName);
        Assert.Equal("tt0068646", x.Elements("uniqueid").Single(e => (string?)e.Attribute("type") == "imdb").Value);
        var ep = new MediaInfo("Accecato dalla luce", SeriesTitle: "Walker", Season: 3, Episode: 10);
        Assert.Equal((Path.Combine("Walker", "Stagione 03"), "Walker - S03E10 - Accecato dalla luce"), Nfo.LibraryPath(ep));
        Assert.Equal("episodedetails", Nfo.Build(ep).Root!.Name.LocalName);
        Assert.Equal("Lui_ lei e l'altro_", Nfo.SafeName("Lui: lei e l'altro?."));
    }

    static string NewDb() => Path.Combine(Path.GetTempPath(), "dlf-src-" + Guid.NewGuid().ToString("N"), "dlf.db");

    [Fact]
    public void Archivio_ImportazioneEStatoConservato()
    {
        string db = NewDb();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var s = new SourceStore(db);
            long id = s.AddSource("TV Italia", "https://example.org/it.m3u", now);
            Assert.Equal((4, 0, 1), s.Import(id, M3uParser.Parse(Pl), now));
            var tv8 = s.Streams().Single(x => x.Url.Contains("tv8"));
            s.SaveProbe(tv8.Id, new ProbeResult(StreamState.Ok, "hls", 854, 480, "h264", "aac", ""), now);
            Assert.Equal(StreamState.Drm, s.Streams().Single(x => x.Url.EndsWith("nove.mpd")).State);

            // la playlist cambia: TV8 resta (stato conservato), Rai 1 cambia URL, sparisce il canale locale
            string changed = Pl.Replace("cont=2606", "cont=9999").Replace("#EXTINF:-1 group-title=\"Locali\",Canale Senza Id\n#EXTVLCOPT:http-user-agent=VLC/3.0\nhttp://example.org/locale.m3u8", "");
            Assert.Equal((1, 2, 1), s.Import(id, M3uParser.Parse(changed), now));
            Assert.Equal(StreamState.Ok, s.Streams().Single(x => x.Url.Contains("tv8")).State);
            Assert.Equal(4, s.Channels().Count);                       // i canali restano (registrazioni e storico)
            var cand = s.Candidates(tv8.ChannelId).Single();
            Assert.Equal(("Mozilla/5.0 (Prova)", "https://tv8.it/"), (cand.UserAgent, cand.Referrer));
            Assert.Equal("https://epgshare01.online/epgshare01/epg_ripper_IT1.xml.gz", s.Source(id)!.XmltvUrl);
            s.UpdateSource(id, enabled: false);
            Assert.Empty(s.Candidates(tv8.ChannelId));
        }
        finally { Cleanup(db); }
    }

    [Fact]
    public void Archivio_Registrazioni()
    {
        string db = NewDb();
        try
        {
            var now = new DateTimeOffset(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);
            var s = new SourceStore(db);
            long src = s.AddSource("x", "https://example.org/it.m3u", now);
            s.Import(src, M3uParser.Parse(Pl), now);
            long ch = s.Channels().First().Id;
            var r = new RecordingStore(db);
            long a = r.Schedule(ch, "Film", now.AddMinutes(30), now.AddMinutes(150), 2, 10, "mp4", now);
            long b = r.Schedule(ch, "Dopo", now.AddHours(5), now.AddHours(6), 2, 10, "mp3", now);
            Assert.Throws<ArgumentException>(() => r.Schedule(ch, "x", now, now, 0, 0, "mp4", now));

            Assert.Empty(r.Due(now, TimeSpan.FromMinutes(2)));
            Assert.Equal(new[] { a }, r.Due(now.AddMinutes(26), TimeSpan.FromMinutes(2)).Select(x => x.Id));   // 30 - 2 margine - 2 anticipo
            Assert.Equal(1, r.Overlapping(now.AddMinutes(100), now.AddMinutes(200)));
            Assert.Equal(2, r.Overlapping(now, now.AddDays(1)));

            r.SetState(a, "recording");
            Assert.False(r.Cancel(a));                                   // partita: si ferma, non si annulla
            Assert.True(r.Cancel(b));
            Assert.Equal("cancelled", r.Get(b)!.State);

            // server fermato durante la registrazione: riparte se non è finita, altrimenti non riuscita
            Assert.Equal(1, r.RecoverAfterRestart(now.AddMinutes(60)));
            Assert.Equal("scheduled", r.Get(a)!.State);
            r.SetState(a, "recording");
            Assert.Equal(0, r.RecoverAfterRestart(now.AddHours(4)));
            Assert.Equal("failed", r.Get(a)!.State);

            // esito con parti perse: tornano come lista di intervalli
            var lost = (now.AddMinutes(40), now.AddMinutes(45));
            r.Finish(a, new RecordingOutcome("incomplete", "x.mp4", false, 5, [lost], [], "a.log"));
            var row = r.Get(a)!;
            Assert.Equal(("incomplete", 5), (row.State, row.LostMinutes));
            Assert.Equal([new LostRange(lost.Item1, lost.Item2)], row.LostRanges);
        }
        finally { Cleanup(db); }
    }

    static void Cleanup(string db)
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path.GetDirectoryName(db)!, true); } catch (IOException) { }
    }
}
