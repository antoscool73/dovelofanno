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

    // 19/11/2026: una seconda playlist (forgejo) scrive i tvg-id in un altro modo e creava un secondo «Rai 1»;
    // la griglia della guida, con due righe dallo stesso nome, si bloccava su «Un momento…»
    const string AltraPlaylist = """
        #EXTM3U
        #EXTINF:-1 tvg-id="Rai.1.HD..101.it" group-title="Rai",Rai 1 HD
        https://altra.example/rai1.m3u8
        #EXTINF:-1 tvg-id="" group-title="Sky",TV8
        https://altra.example/tv8.m3u8
        #EXTINF:-1 tvg-id="Top.Crime.it" group-title="Mediaset",Top Crime
        https://altra.example/topcrime.mpd
        #EXTINF:-1 tvg-id="Tv8Alt" group-title="Sky",TV8 HD
        https://altra.example/tv8-alt.m3u8
        """;

    // 23/11/2026: tvg-id e nome diversi in tutte e due le playlist, ma lo stesso numero del digitale terrestre (26)
    [Fact]
    public void Archivio_StessoNumeroStessoCanale()
    {
        string db = NewDb();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var s = new SourceStore(db);
            long a = s.AddSource("Prima", "https://example.org/a.m3u", now);
            long b = s.AddSource("Seconda", "https://example.org/b.m3u", now);
            s.Import(a, M3uParser.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"cielotv\",Cielo\nhttps://a.example/cielo.m3u8\n#EXTINF:-1 tvg-id=\"mediaset27twentyseven\",Twenty Seven\nhttps://a.example/27.m3u8\n"), now);
            s.Import(b, M3uParser.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"Cielo.it\",Cielo TV\nhttps://b.example/cielo.m3u8\n#EXTINF:-1 tvg-id=\"27twentyseven\",27 Twentyseven\nhttps://b.example/27.m3u8\n#EXTINF:-1 tvg-id=\"Locale.it\",Rete Locale\nhttps://b.example/loc.m3u8\n"), now);
            var ch = s.Channels();
            Assert.Equal(3, ch.Count);
            Assert.Equal(2, ch.Single(c => c.Name == "Cielo").Streams);
            Assert.Equal(2, ch.Single(c => c.Name == "Twenty Seven").Streams);
            Assert.Equal(0, s.MergeDuplicateChannels());
        }
        finally { Cleanup(db); }
    }

    [Theory]
    [InlineData("Rai1.it", "id:rai1")]
    [InlineData("Rai.1.HD..101.it", "id:rai1")]
    [InlineData("Top.Crime.it", "id:topcrime")]
    [InlineData("", "name:canalesenzaid")]
    public void ChiaveDelCanaleNormalizzata(string tvgId, string expected) =>
        Assert.Equal(expected, SourceStore.ChannelKey(new M3uEntry("Canale Senza Id", "", tvgId, "", "https://x/y", "", "", false)));

    [Fact]
    public void Archivio_DuePlaylistStessoCanale()
    {
        string db = NewDb();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var s = new SourceStore(db);
            long a = s.AddSource("TV Italia", "https://example.org/it.m3u", now);
            long b = s.AddSource("Altra", "https://example.org/altra.m3u", now);
            s.Import(a, M3uParser.Parse(Pl), now);
            s.Import(b, M3uParser.Parse(AltraPlaylist.ReplaceLineEndings("\n")), now);
            var ch = s.Channels();
            // Rai 1 (tvg-id scritto diverso) e TV8 (per nome: senza tvg-id e con un tvg-id diverso) sono gli stessi canali
            Assert.Single(ch, c => c.Name.StartsWith("Rai 1"));
            Assert.Single(ch, c => c.Name == "TV8");
            Assert.Equal(2, ch.Single(c => c.Name.StartsWith("Rai 1")).Streams);
            Assert.Equal(3, ch.Single(c => c.Name == "TV8").Streams);
            Assert.Equal(5, ch.Count);                                  // 4 della prima + Top Crime
        }
        finally { Cleanup(db); }
    }

    [Fact]
    public void Archivio_TogliereUnaPlaylistToglieICanaliRimastiVuoti()
    {
        string db = NewDb();
        try
        {
            var now = new DateTimeOffset(2026, 11, 20, 20, 0, 0, TimeSpan.Zero);
            var s = new SourceStore(db);
            long a = s.AddSource("TV Italia", "https://example.org/it.m3u", now);
            long b = s.AddSource("Altra", "https://example.org/altra.m3u", now);
            s.Import(a, M3uParser.Parse(Pl), now);
            s.Import(b, M3uParser.Parse(AltraPlaylist.ReplaceLineEndings("\n")), now);
            Assert.Equal(5, s.Channels().Count);
            // Top Crime c'è solo nella seconda playlist: con una registrazione resta, senza sparirebbe
            long top = s.Channels().Single(c => c.Name == "Top Crime").Id;
            new RecordingStore(db).Schedule(top, "Giallo", now.AddHours(1), now.AddHours(2), 2, 10, "mp4", now);

            Assert.Equal(0, s.DeleteSource(b));                          // Rai 1 e TV8 hanno ancora la prima playlist
            Assert.Equal(5, s.Channels().Count);
            Assert.Equal(1, s.Channels().Single(c => c.Name.StartsWith("Rai 1")).Streams);

            Assert.Equal(4, s.DeleteSource(a));                          // Rai 1, TV8, NOVE e il canale locale; resta Top Crime
            Assert.Equal(new[] { "Top Crime" }, s.Channels().Select(c => c.Name));
            Assert.Empty(s.Sources());
        }
        finally { Cleanup(db); }
    }

    [Fact]
    public void Archivio_DoppioniGiaCreatiUnitiSenzaPerdereRegistrazioni()
    {
        string db = NewDb();
        try
        {
            var now = new DateTimeOffset(2026, 11, 19, 20, 0, 0, TimeSpan.Zero);
            var s = new SourceStore(db);
            long a = s.AddSource("TV Italia", "https://example.org/it.m3u", now);
            s.Import(a, M3uParser.Parse(Pl), now);
            long rai = s.Channels().Single(c => c.Name == "Rai 1").Id;
            // il doppione come lo creava la versione di prima: chiave grezza, un flusso, una registrazione programmata
            long dup;
            using (var c = new SqliteConnection($"Data Source={db}"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "INSERT INTO channels (key, name) VALUES ('id:rai.1.hd..101.it', 'Rai 1'); SELECT last_insert_rowid();";
                dup = (long)cmd.ExecuteScalar()!;
                cmd.CommandText = $"INSERT INTO streams (channel_id, source_id, url, kind, state) VALUES ({dup}, {a}, 'https://altra.example/rai1.m3u8', 'hls', 'ok')";
                cmd.ExecuteNonQuery();
                // e un doppione con un tvg-id proprio diverso ma lo stesso nome
                cmd.CommandText = "INSERT INTO channels (key, name) VALUES ('id:20', 'TV8 HD')";
                cmd.ExecuteNonQuery();
            }
            long rec = new RecordingStore(db).Schedule(dup, "Film", now.AddHours(1), now.AddHours(3), 2, 10, "mp4", now);

            Assert.Equal(2, s.MergeDuplicateChannels());
            Assert.Single(s.Channels(), c => c.Name.StartsWith("TV8"));
            Assert.Single(s.Channels(), c => c.Name == "Rai 1");
            Assert.Equal(2, s.Channels().Single(c => c.Id == rai).Streams);
            Assert.Equal(rai, new RecordingStore(db).Get(rec)!.ChannelId);
            Assert.Equal(0, s.MergeDuplicateChannels());                  // la seconda volta non c'è più niente da unire
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

            // registrazione finita cancellata (26/11/2026): sparisce dall'elenco ma resta nel database, senza file
            Assert.True(r.MarkDeleted(a, now));
            Assert.DoesNotContain(r.List(), x => x.Id == a);
            Assert.Equal(("incomplete", ""), (r.Get(a)!.State, r.Get(a)!.File));
            Assert.False(r.MarkDeleted(a, now));                          // già cancellata
            long c = r.Schedule(ch, "Ancora dopo", now.AddHours(7), now.AddHours(8), 2, 10, "mp4", now);
            Assert.False(r.MarkDeleted(c, now));                          // in programma: si annulla, non si cancella
            Assert.Contains(r.List(), x => x.Id == c);
        }
        finally { Cleanup(db); }
    }

    static void Cleanup(string db)
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path.GetDirectoryName(db)!, true); } catch (IOException) { }
    }
}
