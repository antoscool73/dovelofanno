using DoveLoFanno.Data;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

public sealed class ProfilesTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-prof-" + Guid.NewGuid().ToString("N"));
    readonly ProfileStore store;
    static readonly DateTimeOffset Now = new(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);

    public ProfilesTests() => store = new ProfileStore(Path.Combine(dir, "dlf.db"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void CreazioneConListaFissa()
    {
        var (res, id) = store.Create("Antonio", false, null, "1234", Now);
        Assert.Equal(ProfileResult.Ok, res);
        var p = store.Get(id)!;
        Assert.Equal(("Antonio", false, true, "#6E9BFF"), (p.Name, p.IsChild, p.HasPin, p.Avatar));
        using var c = Database.Open(Path.Combine(dir, "dlf.db"));
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name FROM lists WHERE profile_id = $p AND is_tonight = 1";
        cmd.Parameters.AddWithValue("$p", id);
        Assert.Equal(ProfileStore.TonightList, cmd.ExecuteScalar());
        // il PIN non è salvato in chiaro
        cmd.CommandText = "SELECT pin_hash FROM profiles WHERE id = $p";
        string hash = (string)cmd.ExecuteScalar()!;
        Assert.StartsWith("pbkdf2$", hash);
        Assert.DoesNotContain("1234", hash);
    }

    [Fact]
    public void RegoleSuNomiEPin()
    {
        store.Create("Antonio", false, null, null, Now);
        Assert.Equal(ProfileResult.NameTaken, store.Create("antonio", false, null, null, Now).Result);
        Assert.Equal(ProfileResult.InvalidName, store.Create("  ", false, null, null, Now).Result);
        Assert.Equal(ProfileResult.InvalidPin, store.Create("Bimbi", true, null, "1234", Now).Result);   // niente PIN ai bambini
        Assert.Equal(ProfileResult.InvalidPin, store.Create("Mamma", false, null, "12a4", Now).Result);
        Assert.Equal(ProfileResult.Ok, store.Create("Bambini", true, null, null, Now).Result);
        Assert.Equal(["Antonio", "Bambini"], store.List().Select(p => p.Name));                // adulti prima
    }

    [Fact]
    public void PinEUltimoAdulto()
    {
        long a = store.Create("Antonio", false, null, "4321", Now).Id;
        long b = store.Create("Bambini", true, null, null, Now).Id;
        Assert.True(store.Unlock(a, "4321"));
        Assert.False(store.Unlock(a, "0000"));
        Assert.False(store.Unlock(a, null));
        Assert.True(store.Unlock(b, null));                       // senza PIN si entra
        Assert.Null(store.Unlock(999, "1"));

        Assert.Equal(ProfileResult.LastAdult, store.Delete(a));
        Assert.Equal(ProfileResult.LastAdult, store.Update(a, null, true, null, null, false));
        Assert.Equal(ProfileResult.Ok, store.Update(a, "Antonio S.", null, "#34C77B", null, true));
        var p = store.Get(a)!;
        Assert.Equal(("Antonio S.", "#34C77B", false), (p.Name, p.Avatar, p.HasPin));
        Assert.Equal(ProfileResult.Ok, store.Delete(b));
        Assert.Single(store.List());
    }
}
