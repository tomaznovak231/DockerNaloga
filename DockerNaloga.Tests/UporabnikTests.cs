using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace DockerNaloga.Tests;

[TestClass]
public class UporabnikTests
{
    private static string NovEmail() => $"{Guid.NewGuid()}@test.si";

    // ---------- Registracija ----------

    [DataTestMethod]
    [DataRow("", "Novak", "ana@test.com", "geslo1234")]
    [DataRow("Ana", "", "ana@test.com", "geslo1234")]
    [DataRow("Ana", "Novak", "", "geslo1234")]
    [DataRow("Ana", "Novak", "ana@test.com", "")]
    public void Test_Registracija_ManjkajocaPolja(string ime, string priimek, string email, string geslo)
    {
        var (uspeh, sporocilo) = Uporabnik.Registracija(ime, priimek, email, geslo);
        Assert.IsFalse(uspeh);
        Assert.AreEqual("Vsa polja so obvezna.", sporocilo);
    }

    [TestMethod]
    public void Test_Registracija_KratkoGeslo()
    {
        var (uspeh, sporocilo) = Uporabnik.Registracija("Ana", "Novak", NovEmail(), "1234567");
        Assert.IsFalse(uspeh);
        Assert.AreEqual("Geslo mora imeti vsaj 8 znakov.", sporocilo);
    }

    [TestMethod]
    public void Test_Registracija_Uspesna()
    {
        var (uspeh, sporocilo) = Uporabnik.Registracija("Ana", "Novak", NovEmail(), "geslo1234");
        Assert.IsTrue(uspeh);
        Assert.AreEqual("Registracija uspešna.", sporocilo);
    }

    [TestMethod]
    public void Test_Registracija_PodvojenEmail()
    {
        var email = NovEmail();
        Uporabnik.Registracija("Ana", "Novak", email, "geslo1234");
        var (uspeh, sporocilo) = Uporabnik.Registracija("Jan", "Kos", email, "geslo1234");
        Assert.IsFalse(uspeh);
        Assert.AreEqual("Email je že registriran.", sporocilo);
    }

    // ---------- Prijava ----------

    [TestMethod]
    public void Test_Prijava_Uspesna()
    {
        var email = NovEmail();
        Uporabnik.Registracija("Ana", "Novak", email, "geslo1234");
        var u = Uporabnik.Prijava(email, "geslo1234");
        Assert.IsNotNull(u);
        Assert.AreEqual(email, u!.Email);
    }

    [TestMethod]
    public void Test_Prijava_NapacnoGeslo()
    {
        var email = NovEmail();
        Uporabnik.Registracija("Ana", "Novak", email, "geslo1234");
        var u = Uporabnik.Prijava(email, "napacno1");
        Assert.IsNull(u);
    }

    [TestMethod]
    public void Test_Prijava_NeobstojecUporabnik()
    {
        var u = Uporabnik.Prijava(NovEmail(), "geslo1234");
        Assert.IsNull(u);
    }

    [TestMethod]
    public void Test_Prijava_PoskodovanHash()
    {
        var email = NovEmail();
        using (var con = new SqliteConnection(Uporabnik.Povezava))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "INSERT INTO Uporabnik (Ime, Priimek, Email, GesloHash) VALUES ('Ana','Novak',@e,'brezpike')";
            cmd.Parameters.AddWithValue("@e", email);
            cmd.ExecuteNonQuery();
        }
        var u = Uporabnik.Prijava(email, "karkoli123");
        Assert.IsNull(u);
    }

    // ---------- Odjava ----------

    [TestMethod]
    public void Test_Odjava()
    {
        var seja = new TestnaSeja();
        seja.Set("uid", System.Text.Encoding.UTF8.GetBytes("5"));
        Uporabnik.Odjava(seja);
        Assert.AreEqual(0, seja.SteviloKljucev);
    }

    // ---------- UrejanjeProfila ----------

    [TestMethod]
    public void Test_UrejanjeProfila_PrazniPodatki()
    {
        var email = NovEmail();
        Uporabnik.Registracija("Ana", "Novak", email, "geslo1234");
        var u = Uporabnik.NajdiPoEmailu(email)!;
        var (uspeh, sporocilo) = Uporabnik.UrejanjeProfila(u.Id, "", "Novak", email);
        Assert.IsFalse(uspeh);
        Assert.AreEqual("Vsa polja so obvezna.", sporocilo);
    }

    [TestMethod]
    public void Test_UrejanjeProfila_Uspesno()
    {
        var email = NovEmail();
        Uporabnik.Registracija("Ana", "Novak", email, "geslo1234");
        var u = Uporabnik.NajdiPoEmailu(email)!;
        var (uspeh, sporocilo) = Uporabnik.UrejanjeProfila(u.Id, "Ana", "Kos", NovEmail());
        Assert.IsTrue(uspeh);
        Assert.AreEqual("Profil posodobljen.", sporocilo);
    }

    [TestMethod]
    public void Test_UrejanjeProfila_PodvojenEmail()
    {
        var email1 = NovEmail();
        var email2 = NovEmail();
        Uporabnik.Registracija("Ana", "Novak", email1, "geslo1234");
        Uporabnik.Registracija("Jan", "Kos", email2, "geslo1234");
        var u2 = Uporabnik.NajdiPoEmailu(email2)!;
        var (uspeh, sporocilo) = Uporabnik.UrejanjeProfila(u2.Id, "Jan", "Kos", email1);
        Assert.IsFalse(uspeh);
        Assert.AreEqual("Email že uporablja drug uporabnik.", sporocilo);
    }

    // ---------- PonastavitevGesla ----------

    [TestMethod]
    public void Test_PonastavitevGesla_KratkoGeslo()
    {
        var (uspeh, sporocilo) = Uporabnik.PonastavitevGesla(NovEmail(), "kratko");
        Assert.IsFalse(uspeh);
        Assert.AreEqual("Geslo mora imeti vsaj 8 znakov.", sporocilo);
    }

    [TestMethod]
    public void Test_PonastavitevGesla_NeobstojecUporabnik()
    {
        var (uspeh, sporocilo) = Uporabnik.PonastavitevGesla(NovEmail(), "novogeslo1");
        Assert.IsFalse(uspeh);
        Assert.AreEqual("Uporabnik ne obstaja.", sporocilo);
    }

    [TestMethod]
    public void Test_PonastavitevGesla_Uspesno()
    {
        var email = NovEmail();
        Uporabnik.Registracija("Ana", "Novak", email, "geslo1234");
        var (uspeh, sporocilo) = Uporabnik.PonastavitevGesla(email, "novogeslo1");
        Assert.IsTrue(uspeh);
        Assert.AreEqual("Geslo spremenjeno.", sporocilo);
        Assert.IsNotNull(Uporabnik.Prijava(email, "novogeslo1"));
    }

    // ---------- BrisanjeRacuna ----------

    [TestMethod]
    public void Test_BrisanjeRacuna_NeobstojecId()
    {
        var (uspeh, sporocilo) = Uporabnik.BrisanjeRacuna(int.MaxValue, "karkoli12");
        Assert.IsFalse(uspeh);
        Assert.AreEqual("Napačno geslo.", sporocilo);
    }

    [TestMethod]
    public void Test_BrisanjeRacuna_NapacnoGeslo()
    {
        var email = NovEmail();
        Uporabnik.Registracija("Ana", "Novak", email, "geslo1234");
        var u = Uporabnik.NajdiPoEmailu(email)!;
        var (uspeh, sporocilo) = Uporabnik.BrisanjeRacuna(u.Id, "napacno1");
        Assert.IsFalse(uspeh);
        Assert.AreEqual("Napačno geslo.", sporocilo);
    }

    [TestMethod]
    public void Test_BrisanjeRacuna_Uspesno()
    {
        var email = NovEmail();
        Uporabnik.Registracija("Ana", "Novak", email, "geslo1234");
        var u = Uporabnik.NajdiPoEmailu(email)!;
        var (uspeh, sporocilo) = Uporabnik.BrisanjeRacuna(u.Id, "geslo1234");
        Assert.IsTrue(uspeh);
        Assert.AreEqual("Račun izbrisan.", sporocilo);
        Assert.IsNull(Uporabnik.NajdiPoId(u.Id));
    }

    // ---------- NajdiPoId / NajdiPoEmailu ----------

    [TestMethod]
    public void Test_NajdiPoId_NeobstojecId()
    {
        Assert.IsNull(Uporabnik.NajdiPoId(int.MaxValue));
    }

    [TestMethod]
    public void Test_NajdiPoEmailu_NeobstojecEmail()
    {
        Assert.IsNull(Uporabnik.NajdiPoEmailu(NovEmail()));
    }
}

internal class TestnaSeja : ISession
{
    private readonly Dictionary<string, byte[]> _podatki = new();

    public bool IsAvailable => true;
    public string Id => "test-seja";
    public IEnumerable<string> Keys => _podatki.Keys;
    public int SteviloKljucev => _podatki.Count;

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Clear() => _podatki.Clear();
    public void Remove(string key) => _podatki.Remove(key);
    public void Set(string key, byte[] value) => _podatki[key] = value;
    public bool TryGetValue(string key, out byte[] value) => _podatki.TryGetValue(key, out value!);
}