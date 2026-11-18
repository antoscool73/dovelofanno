using System.Text;
using DoveLoFanno.Data;
using DoveLoFanno.Epg;
using DoveLoFanno.Sources;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

/// <summary>Preferenze e proposte, avvisi, notifiche, abbonamenti (18/11/2026).</summary>
public sealed class PreferencesTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-pref-" + Guid.NewGuid().ToString("N"));
    string Db => Path.Combine(dir, "dlf.db");
    static readonly DateTimeOffset Now = new(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    sealed record World(GuideStore Guide, PreferenceStore Prefs, AlertStore Alerts, SubscriptionStore Subs, RecordingStore Recs,
        long Anna, long Bimbo, long Padrino, long CittaAperta, long Canale5);

    World Setup()
    {
        var src = new SourceStore(Db);
        long s = src.AddSource("Prova", "https://example.org/x.m3u", Now);
        src.Import(s, M3uParser.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"Canale5.it\",Canale 5\nhttp://x/c5.m3u8\n"), Now);
        var guide = new GuideStore(Db);
        guide.Save(guide.AddSource("https://example.org/g.xml"), XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(GuideStoreTests.Xmltv(Now))), Now.LocalDateTime), Now);
        var profiles = new ProfileStore(Db);
        long a = profiles.Create("Anna", false, null, null, Now).Id;
        long b = profiles.Create("Bimbo", true, null, null, Now).Id;
        var all = guide.Query(new GuideQuery(Now, Now.AddHours(6)));
        var padrino = all.Single(p => p.Title == "Il Padrino");
        long citta = all.Single(p => p.Title == "Città aperta").WorkId!.Value;
        var subs = new SubscriptionStore(Db);
        return new World(guide, new PreferenceStore(Db, guide), new AlertStore(Db, guide, subs), subs, new RecordingStore(Db),
            a, b, padrino.WorkId!.Value, citta, padrino.ChannelId!.Value);
    }

    [Fact]
    public void PropostePerPreferenzaUnaPerOpera()
    {
        var w = Setup();
        Assert.Equal("", PreferenceStore.Validate("Film", "", "Film", 0));
        Assert.NotEqual("", PreferenceStore.Validate("Vuota", "", "", 0));
        Assert.NotEqual("", PreferenceStore.Validate("X", "", "Fantascienza", 0));
        w.Prefs.Add(w.Anna, "Film", "", "Film", "", 0, true);
        w.Prefs.Add(w.Anna, "Pacino", "pacino", "", "", 0, true);          // trova di nuovo Il Padrino: resta una proposta
        var list = w.Prefs.Proposals(w.Anna, Now, 2, 3, 2, 10)!;
        Assert.Equal(["Città aperta", "Il Padrino"], list.Select(p => p.Programme.Title));
        Assert.Equal("Film", list.Single(p => p.Programme.Title == "Il Padrino").Preference);
        Assert.Null(w.Prefs.Proposals(424242, Now, 2, 3, 2, 10));

        // la preferenza spenta non propone
        foreach (var p in w.Prefs.List(w.Anna)!) w.Prefs.Update(w.Anna, p.Id, p.Name, p.Text, p.Group, p.Sub, p.MinRating, p.Name == "Pacino");
        Assert.Equal(["Il Padrino"], w.Prefs.Proposals(w.Anna, Now, 2, 3, 2, 10)!.Select(p => p.Programme.Title));
    }

    [Fact]
    public void NonProporrePiuVistiInProgrammaEGeneriEsclusi()
    {
        var w = Setup();
        w.Prefs.Add(w.Anna, "Film", "", "Film", "", 0, true);
        Assert.True(w.Prefs.Ignore(w.Anna, w.CittaAperta, Now));
        Assert.Equal(["Il Padrino"], w.Prefs.Proposals(w.Anna, Now, 2, 3, 2, 10)!.Select(p => p.Programme.Title));
        var ign = Assert.Single(w.Prefs.Ignored(w.Anna)!);
        Assert.Equal("Città aperta", ign.Title);
        Assert.True(w.Prefs.Unignore(w.Anna, ign.WorkKey));

        // in programma: non si propone
        var padrino = w.Guide.Query(new GuideQuery(Now, Now.AddHours(6))).Single(p => p.Title == "Il Padrino");
        long rec = w.Recs.Schedule(w.Canale5, "Il Padrino", padrino.Start, padrino.Stop, 2, 10, "mp4", Now, w.Padrino);
        Assert.Equal(["Città aperta"], w.Prefs.Proposals(w.Anna, Now, 2, 3, 2, 10)!.Select(p => p.Programme.Title));
        w.Recs.Cancel(rec);

        // già visto dal profilo
        new TonightStore(Db, w.Guide).Watched(w.Anna, w.Padrino, Now);
        Assert.Equal(["Città aperta"], w.Prefs.Proposals(w.Anna, Now, 2, 3, 2, 10)!.Select(p => p.Programme.Title));

        // genere escluso
        Assert.True(w.Prefs.SetExcludedGroups(w.Anna, ["Film", "Inventato"]));
        Assert.Equal(["Film"], w.Prefs.ExcludedGroups(w.Anna));
        Assert.Empty(w.Prefs.Proposals(w.Anna, Now, 2, 3, 2, 10)!);

        // profilo bambino: solo programmi per bambini
        w.Prefs.Add(w.Bimbo, "Film", "", "Film", "", 0, true);
        Assert.Empty(w.Prefs.Proposals(w.Bimbo, Now, 2, 3, 2, 10)!);
    }

    [Fact]
    public void ConflittoConIlLimiteEProposteNuoveUnaVolta()
    {
        var w = Setup();
        w.Prefs.Add(w.Anna, "Pacino", "pacino", "", "", 0, true);
        // una registrazione sovrapposta e limite 1: Il Padrino è in conflitto
        w.Recs.Schedule(w.Canale5, "Altro", Now.AddMinutes(90), Now.AddMinutes(100), 0, 0, "mp4", Now);
        var p = Assert.Single(w.Prefs.Proposals(w.Anna, Now, 2, 1, 2, 10)!);
        Assert.False(p.Programme.Scheduled);
        Assert.True(p.Conflict);
        Assert.False(Assert.Single(w.Prefs.Proposals(w.Anna, Now, 2, 3, 2, 10)!).Conflict);

        var list = w.Prefs.Proposals(w.Anna, Now, 2, 3, 2, 10)!;
        Assert.Empty(w.Prefs.TakeNew(w.Anna, list, Now));                 // la prima volta si segnano senza notificare
        Assert.Empty(w.Prefs.TakeNew(w.Anna, list, Now));
        w.Prefs.Add(w.Anna, "Film", "", "Film", "", 0, true);
        var fresh = w.Prefs.TakeNew(w.Anna, w.Prefs.Proposals(w.Anna, Now, 2, 3, 2, 10)!, Now);
        Assert.Equal("Città aperta", Assert.Single(fresh).Programme.Title);
    }

    [Fact]
    public void AvvisoTvScattaUnaVoltaERegistraDaSolo()
    {
        var w = Setup();
        Assert.NotEqual("", AlertStore.Validate(new AlertBody(w.Padrino, "boh", null, null, null, null, null, null)));
        long id = w.Alerts.Save(w.Anna, new AlertBody(w.Padrino, "tv", null, null, true, ["appear", "soon"], null, null))!.Value;
        Assert.Null(w.Alerts.Save(424242, new AlertBody(w.Padrino, "tv", null, null, null, null, null, null)));
        var row = Assert.Single(w.Alerts.List(w.Anna, w.Padrino, Now)!);
        Assert.Equal((id, "all", true), (row.Id, row.Channels, row.AutoRecord));
        Assert.StartsWith("Prossimo passaggio", row.Status);

        var scheduled = new List<long>();
        long? Schedule(GuideProgramme p, long profile)
        {
            long r = w.Recs.Schedule(p.ChannelId!.Value, p.Title, p.Start, p.Stop, 2, 10, "mp4", Now, p.WorkId, profile, p.Id);
            scheduled.Add(r);
            return r;
        }
        var fired = w.Alerts.Check(Now, TimeZoneInfo.Utc, Schedule);
        Assert.Equal(2, fired.Count);                                     // compare in guida + registrazione automatica
        Assert.Contains(fired, f => f.Title.Contains("passa in TV") && f.Text.Contains("Canale 5"));
        Assert.Contains(fired, f => f.RecordingId == scheduled.Single());
        Assert.Empty(w.Alerts.Check(Now, TimeZoneInfo.Utc, Schedule));   // una volta sola
        Assert.Single(scheduled);

        // 30 minuti prima
        var soon = w.Alerts.Check(Now.AddMinutes(40), TimeZoneInfo.Utc, Schedule);
        Assert.Contains(soon, f => f.Title.StartsWith("Tra poco"));

        // "la sera prima" non scatta per un passaggio dello stesso giorno (Il Padrino è alle 21 di oggi)
        w.Alerts.Save(w.Anna, new AlertBody(w.Padrino, "tv", null, null, null, ["eve"], null, null));
        Assert.Empty(w.Alerts.Check(Now.AddMinutes(41), TimeZoneInfo.Utc, Schedule));
        // ...e scatta dalle 21 del giorno prima per un passaggio di domani. Con il fuso a -3 ore il film è alle 18 di oggi: no;
        // a +3:15 "adesso" sono le 23:56 e il film è alle 0:15 di domani: sì
        Assert.Empty(w.Alerts.Check(Now.AddMinutes(41), TimeZoneInfo.CreateCustomTimeZone("m3", TimeSpan.FromHours(-3), "m3", "m3"), Schedule));
        Assert.Contains(w.Alerts.Check(Now.AddMinutes(41), TimeZoneInfo.CreateCustomTimeZone("p315", new TimeSpan(3, 15, 0), "p315", "p315"), Schedule),
            f => f.Title.StartsWith("Domani"));

        // spento: non scatta più nulla
        w.Alerts.Save(w.Anna, new AlertBody(w.Padrino, "tv", false, null, null, ["appear", "eve", "soon"], null, null));
        Assert.Empty(w.Alerts.Check(Now.AddMinutes(45), TimeZoneInfo.Utc, Schedule));
        Assert.True(w.Alerts.Delete(w.Anna, id));
        Assert.Empty(w.Alerts.List(w.Anna, null, Now)!);
    }

    [Fact]
    public void AvvisoSoloCanaliRegistrabiliEAbbonamenti()
    {
        var w = Setup();
        // Città aperta passa solo su TV2000, che non è nelle playlist
        w.Alerts.Save(w.Anna, new AlertBody(w.CittaAperta, "tv", null, "playlist", false, ["appear"], null, null));
        Assert.Empty(w.Alerts.Check(Now, TimeZoneInfo.Utc, (_, _) => null));
        w.Alerts.Save(w.Anna, new AlertBody(w.CittaAperta, "tv", null, "all", null, null, null, null));
        Assert.Single(w.Alerts.Check(Now, TimeZoneInfo.Utc, (_, _) => null));

        // abbonamenti: la famiglia ha Netflix, il film arriva su Netflix e a noleggio su Apple
        w.Subs.SetFamily(["Netflix", " ", "netflix"]);
        Assert.Equal(["Netflix"], w.Subs.Family());
        Assert.Equal(["Netflix"], w.Subs.OfProfile(w.Anna)!.Services);
        w.Alerts.Save(w.Anna, new AlertBody(w.Padrino, "subscription", null, null, null, null, null, null));
        w.Alerts.Save(w.Anna, new AlertBody(w.Padrino, "rent_buy", null, null, null, null, null, ["rent"]));
        Assert.Empty(w.Alerts.Check(Now, TimeZoneInfo.Utc, (_, _) => null));
        using (var c = Database.Open(Db))
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO availability (work_id, platform, kind, checked_at) VALUES ($w, 'Netflix', 'subscription', 0), ($w, 'Disney Plus', 'subscription', 0),
                ($w, 'Apple TV', 'rent', 0), ($w, 'Apple TV', 'buy', 0)
                """;
            cmd.Parameters.AddWithValue("$w", w.Padrino);
            cmd.ExecuteNonQuery();
        }
        var fired = w.Alerts.Check(Now, TimeZoneInfo.Utc, (_, _) => null);
        Assert.Equal(["«Il Padrino» è su Apple TV", "«Il Padrino» è su Netflix"], fired.Select(f => f.Title).Order());
        Assert.Contains("Netflix", w.Alerts.List(w.Anna, w.Padrino, Now)!.Single(a => a.Kind == "subscription").Status);

        // il profilo con una scelta sua
        Assert.True(w.Subs.SetProfile(w.Bimbo, false, ["Disney Plus"]));
        Assert.Equal((false, "Disney Plus"), (w.Subs.OfProfile(w.Bimbo)!.Inherits, w.Subs.OfProfile(w.Bimbo)!.Services.Single()));
        w.Alerts.Save(w.Bimbo, new AlertBody(w.Padrino, "subscription", null, null, null, null, null, null));
        Assert.Equal("«Il Padrino» è su Disney Plus", Assert.Single(w.Alerts.Check(Now, TimeZoneInfo.Utc, (_, _) => null)).Title);
        Assert.Contains("Disney Plus", w.Subs.Known());
    }

    [Fact]
    public void NotifichePerProfiloEFamiglia()
    {
        var w = Setup();
        var n = new NotificationStore(Db);
        NotificationRow? forwarded = null;
        n.Added += r => forwarded = r;
        long f = n.Add(null, "recording", "Registrata", "", "/registrazioni", Now);
        n.Add(w.Anna, "alert", "Solo per Anna", "", "", Now);
        Assert.Equal("Solo per Anna", forwarded!.Title);
        var anna = n.List(w.Anna)!;
        Assert.Equal((2, 2), (anna.Unread, anna.Items.Count));
        Assert.Equal(1, n.List(w.Bimbo)!.Unread);
        Assert.True(n.MarkRead(w.Anna, f));
        Assert.Equal(1, n.List(w.Anna)!.Unread);
        Assert.True(n.List(w.Anna)!.Items.Single(i => i.Id == f).Read);
        Assert.Null(n.List(424242));
    }
}
