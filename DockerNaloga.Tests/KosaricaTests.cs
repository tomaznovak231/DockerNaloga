using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Data.Sqlite;

namespace DockerNaloga.Tests;

// Testi za razred Kosarica. Vsak test si ustvari svojega uporabnika in svoje knjige,
// zato so testi med seboj neodvisni.
[TestClass]
public class KosaricaTests
{
    // ----- pomožne metode -----
private static SqliteConnection Odpri()
{
    var c = new SqliteConnection(Uporabnik.Povezava);
    c.Open();
    return c;
}

    // Izvede SQL in vrne Id vstavljene vrstice.
private static long Izvedi(string sql, params object[] p)
{
    using var c = Odpri();
    using var cmd = new SqliteCommand(sql, c);
    for (int i = 0; i < p.Length; i++) cmd.Parameters.AddWithValue("@p" + i, p[i]);
    cmd.ExecuteNonQuery();

    using var idCmd = new SqliteCommand("SELECT last_insert_rowid();", c);
    return (long)(idCmd.ExecuteScalar() ?? 0L);
}

private static object? Skalar(string sql, params object[] p)
{
    using var c = Odpri();
    using var cmd = new SqliteCommand(sql, c);
    for (int i = 0; i < p.Length; i++) cmd.Parameters.AddWithValue("@p" + i, p[i]);
    return cmd.ExecuteScalar();
}

    private static int NovUporabnik(string vloga = "stranka") =>
        (int)Izvedi(
            "INSERT INTO Uporabnik (Ime, Priimek, Email, GesloHash, Vloga) VALUES ('T','T',@p0,'x',@p1)",
            $"{Guid.NewGuid()}@test.si", vloga);

    private static int NovaKnjiga(int kosov, decimal cena = 10m) =>
        (int)Izvedi("INSERT INTO Knjiga (Naslov, Avtor, Cena, Kosov) VALUES ('T','A',@p0,@p1)", cena, kosov);

    private static int Zaloga(int knjigaId) =>
        Convert.ToInt32(Skalar("SELECT Kosov FROM Knjiga WHERE Id=@p0", knjigaId));

    private static int KosaricaId(int uid) =>
        Convert.ToInt32(Skalar("SELECT Id FROM Narocilo WHERE UporabnikId=@p0 AND Status='kosarica'", uid));

    // ----- DodajVKosarico -----
    [TestMethod]
    public void Test_DodajVKosarico_NeveljavnaKolicina()
    {
        var r = Kosarica.DodajVKosarico(NovUporabnik(), NovaKnjiga(3), 0);
        Assert.IsFalse(r.uspeh);
        Assert.AreEqual("Količina mora biti vsaj 1.", r.sporocilo);
    }

    [TestMethod]
    public void Test_DodajVKosarico_KnjigaNeObstaja()
    {
        var r = Kosarica.DodajVKosarico(NovUporabnik(), int.MaxValue, 1);
        Assert.IsFalse(r.uspeh);
        Assert.AreEqual("Knjiga ne obstaja.", r.sporocilo);
    }

    [TestMethod]
    public void Test_DodajVKosarico_Razprodana()
    {
        var r = Kosarica.DodajVKosarico(NovUporabnik(), NovaKnjiga(0), 1);
        Assert.IsFalse(r.uspeh);
        Assert.AreEqual("Knjiga je razprodana.", r.sporocilo);
    }

    [TestMethod]
    public void Test_DodajVKosarico_Uspeh()
    {
        var u = NovUporabnik();
        var r = Kosarica.DodajVKosarico(u, NovaKnjiga(3), 2);
        Assert.IsTrue(r.uspeh);

        var (postavke, skupaj) = Kosarica.PregledKosarice(u);
        Assert.AreEqual(1, postavke.Count);
        Assert.AreEqual(20m, skupaj);
    }

    [TestMethod]
    public void Test_DodajVKosarico_IstaKnjigaPovecaKolicino()
    {
        var u = NovUporabnik();
        var k = NovaKnjiga(5);
        Assert.IsTrue(Kosarica.DodajVKosarico(u, k, 1).uspeh);
        Assert.IsTrue(Kosarica.DodajVKosarico(u, k, 1).uspeh);

        var (postavke, skupaj) = Kosarica.PregledKosarice(u);
        Assert.AreEqual(1, postavke.Count);
        Assert.AreEqual(20m, skupaj);
    }

    [TestMethod]
    public void Test_DodajVKosarico_PresezeZalogo()
    {
        var r = Kosarica.DodajVKosarico(NovUporabnik(), NovaKnjiga(2), 3);
        Assert.IsFalse(r.uspeh);
        Assert.AreEqual("Na zalogi je samo še 2 kosov.", r.sporocilo);
    }

    // ----- PregledKosarice -----
    [TestMethod]
    public void Test_PregledKosarice_Prazna()
    {
        var (postavke, skupaj) = Kosarica.PregledKosarice(NovUporabnik());
        Assert.AreEqual(0, postavke.Count);
        Assert.AreEqual(0m, skupaj);
    }

    // ----- OdstraniIzKosarice -----
    [TestMethod]
    public void Test_OdstraniIzKosarice_Uspeh()
    {
        var u = NovUporabnik();
        var k = NovaKnjiga(3);
        Kosarica.DodajVKosarico(u, k, 1);

        var r = Kosarica.OdstraniIzKosarice(u, k);
        Assert.IsTrue(r.uspeh);
        Assert.AreEqual(0, Kosarica.PregledKosarice(u).postavke.Count);
    }

    [TestMethod]
    public void Test_OdstraniIzKosarice_KnjigeNiVKosarici()
    {
        var r = Kosarica.OdstraniIzKosarice(NovUporabnik(), NovaKnjiga(3));
        Assert.IsFalse(r.uspeh);
        Assert.AreEqual("Knjige ni v košarici.", r.sporocilo);
    }

    // ----- ZakljuciNarocilo -----
    [TestMethod]
    public void Test_ZakljuciNarocilo_BrezKosarice()
    {
        var r = Kosarica.ZakljuciNarocilo(NovUporabnik());
        Assert.IsFalse(r.uspeh);
        Assert.AreEqual("Košarica je prazna.", r.sporocilo);
        Assert.IsNull(r.narociloId);
    }

    [TestMethod]
    public void Test_ZakljuciNarocilo_KosaricaBrezPostavk()
    {
        var u = NovUporabnik();
        var k = NovaKnjiga(3);
        Kosarica.DodajVKosarico(u, k, 1);
        Kosarica.OdstraniIzKosarice(u, k); // košarica obstaja, a je prazna

        var r = Kosarica.ZakljuciNarocilo(u);
        Assert.IsFalse(r.uspeh);
        Assert.AreEqual("Košarica je prazna.", r.sporocilo);
    }

    [TestMethod]
    public void Test_ZakljuciNarocilo_ZalogeNiVecDovolj()
    {
        var u = NovUporabnik();
        var k = NovaKnjiga(2);
        Kosarica.DodajVKosarico(u, k, 2);
        Izvedi("UPDATE Knjiga SET Kosov=0 WHERE Id=@p0", k); // zalogo "pobere" nekdo drug

        var r = Kosarica.ZakljuciNarocilo(u);
        Assert.IsFalse(r.uspeh);
        Assert.IsTrue(r.sporocilo.Contains("ni več dovolj"));
    }

    [TestMethod]
    public void Test_ZakljuciNarocilo_Uspeh()
    {
        var u = NovUporabnik();
        var k = NovaKnjiga(3);
        Kosarica.DodajVKosarico(u, k, 2);

        var r = Kosarica.ZakljuciNarocilo(u);
        Assert.IsTrue(r.uspeh);
        Assert.IsNotNull(r.narociloId);
        Assert.AreEqual(1, Zaloga(k)); // 3 - 2
    }

    // ----- PreklicNarocila -----
    [TestMethod]
    public void Test_PreklicNarocila_NeObstaja()
    {
        var r = Kosarica.PreklicNarocila(NovUporabnik(), int.MaxValue);
        Assert.IsFalse(r.uspeh);
        Assert.AreEqual("Naročilo ne obstaja.", r.sporocilo);
    }

    [TestMethod]
    public void Test_PreklicNarocila_NiOddano()
    {
        var u = NovUporabnik();
        Kosarica.DodajVKosarico(u, NovaKnjiga(3), 1);

        var r = Kosarica.PreklicNarocila(u, KosaricaId(u)); // status je še 'kosarica'
        Assert.IsFalse(r.uspeh);
        Assert.IsTrue(r.sporocilo.Contains("ni mogoče preklicati"));
    }

    [TestMethod]
    public void Test_PreklicNarocila_Uspeh()
    {
        var u = NovUporabnik();
        var k = NovaKnjiga(3);
        Kosarica.DodajVKosarico(u, k, 2);
        var narociloId = Kosarica.ZakljuciNarocilo(u).narociloId!.Value;
        Assert.AreEqual(1, Zaloga(k));

        var r = Kosarica.PreklicNarocila(u, narociloId);
        Assert.IsTrue(r.uspeh);
        Assert.AreEqual(3, Zaloga(k)); // zaloga se vrne
    }

    // ----- JeZaposleni -----
    [TestMethod]
    public void Test_JeZaposleni_Da()
    {
        Assert.IsTrue(Kosarica.JeZaposleni(NovUporabnik("zaposleni")));
    }

    [TestMethod]
    public void Test_JeZaposleni_Ne()
    {
        Assert.IsFalse(Kosarica.JeZaposleni(NovUporabnik()));
    }

    // ----- BrisanjeKnjige -----
    [TestMethod]
    public void Test_BrisanjeKnjige_Izposojena()
    {
        var u = NovUporabnik();
        var k = NovaKnjiga(1);
        Izvedi("INSERT INTO Izposoja (UporabnikId, KnjigaId, DatumIzposoje, Rok) VALUES (@p0,@p1,date('now'),date('now'))", u, k);

        var r = Kosarica.BrisanjeKnjige(k);
        Assert.IsFalse(r.uspeh);
        Assert.IsTrue(r.sporocilo.Contains("izposojena"));
    }

    [TestMethod]
    public void Test_BrisanjeKnjige_VNarocilih()
    {
        var u = NovUporabnik();
        var k = NovaKnjiga(3);
        Kosarica.DodajVKosarico(u, k, 1);

        var r = Kosarica.BrisanjeKnjige(k);
        Assert.IsFalse(r.uspeh);
        Assert.IsTrue(r.sporocilo.Contains("naročilih"));
    }

    [TestMethod]
    public void Test_BrisanjeKnjige_Uspeh()
    {
        var k = NovaKnjiga(1);
        var r = Kosarica.BrisanjeKnjige(k);
        Assert.IsTrue(r.uspeh);
        Assert.IsNull(Skalar("SELECT Id FROM Knjiga WHERE Id=@p0", k));
    }

    [TestMethod]
    public void Test_BrisanjeKnjige_NeObstaja()
    {
        var r = Kosarica.BrisanjeKnjige(int.MaxValue);
        Assert.IsFalse(r.uspeh);
        Assert.AreEqual("Knjiga ne obstaja.", r.sporocilo);
    }
}
