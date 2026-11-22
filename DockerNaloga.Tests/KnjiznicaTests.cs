
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace DockerNaloga.Tests;

[TestClass]
public class KnjiznicaTests
{
    private int _uporabnikId;
    private int _knjigaId;

    private string Povezava => Uporabnik.Povezava;

    [TestInitialize]
    public void Zacetek()
    {
        using var povezava = new SqliteConnection(Povezava);
        povezava.Open();

        using var ukaz = povezava.CreateCommand();

        string email = $"test-{Guid.NewGuid()}@test.si";

        ukaz.CommandText = @"
            INSERT INTO Uporabnik
                (Ime, Priimek, Email, GesloHash, Vloga)
            VALUES
                ('Test', 'Uporabnik', @email, 'testhash', 'stranka');

            SELECT last_insert_rowid();
        ";

        ukaz.Parameters.AddWithValue("@email", email);
        _uporabnikId = Convert.ToInt32(ukaz.ExecuteScalar());

        using var knjiga = new SqliteCommand(
            @"INSERT INTO Knjiga
                (Naslov, Avtor, Zanr, Leto, Opis, Cena, Kosov)
              VALUES
                ('Testna knjiga', 'Testni avtor', 'Fantazija',
                 2024, 'Opis knjige', 15.50, 1);
              SELECT last_insert_rowid();",
            povezava);

        _knjigaId = Convert.ToInt32(knjiga.ExecuteScalar());
    }

    [TestCleanup]
    public void Konec()
    {
        using var povezava = new SqliteConnection(Povezava);
        povezava.Open();

        using var ukaz = new SqliteCommand(@"
            DELETE FROM Izposoja WHERE UporabnikId = @u;
            DELETE FROM Seznam WHERE UporabnikId = @u;
            DELETE FROM Knjiga WHERE Id = @k;
            DELETE FROM Uporabnik WHERE Id = @u;
        ", povezava);

        ukaz.Parameters.AddWithValue("@u", _uporabnikId);
        ukaz.Parameters.AddWithValue("@k", _knjigaId);
        ukaz.ExecuteNonQuery();
    }

    private int DodajIzposojo(
        DateTime rok,
        DateTime? datumVrnitve = null,
        bool podaljsano = false)
    {
        using var povezava = new SqliteConnection(Povezava);
        povezava.Open();

        using var ukaz = new SqliteCommand(@"
            INSERT INTO Izposoja
                (UporabnikId, KnjigaId, DatumIzposoje,
                 Rok, DatumVrnitve, Podaljsano)
            VALUES
                (@u, @k, @datum, @rok, @vrnitev, @podaljsano);

            SELECT last_insert_rowid();
        ", povezava);

        ukaz.Parameters.AddWithValue("@u", _uporabnikId);
        ukaz.Parameters.AddWithValue("@k", _knjigaId);
        ukaz.Parameters.AddWithValue("@datum", DateTime.Today.ToString("yyyy-MM-dd"));
        ukaz.Parameters.AddWithValue("@rok", rok.ToString("yyyy-MM-dd"));
        ukaz.Parameters.AddWithValue(
            "@vrnitev",
            datumVrnitve.HasValue
                ? datumVrnitve.Value.ToString("yyyy-MM-dd")
                : DBNull.Value);
        ukaz.Parameters.AddWithValue("@podaljsano", podaljsano ? 1 : 0);

        return Convert.ToInt32(ukaz.ExecuteScalar());
    }

    private int DodajNaSeznam(string tip)
    {
        using var povezava = new SqliteConnection(Povezava);
        povezava.Open();

        using var ukaz = new SqliteCommand(@"
            INSERT INTO Seznam (UporabnikId, KnjigaId, Tip)
            VALUES (@u, @k, @tip);

            SELECT last_insert_rowid();
        ", povezava);

        ukaz.Parameters.AddWithValue("@u", _uporabnikId);
        ukaz.Parameters.AddWithValue("@k", _knjigaId);
        ukaz.Parameters.AddWithValue("@tip", tip);

        return Convert.ToInt32(ukaz.ExecuteScalar());
    }

    [TestMethod]
    public void PregledIzposoj_VrneIzposojeUporabnika()
    {
        DodajIzposojo(DateTime.Today.AddDays(7));
        DodajIzposojo(
            DateTime.Today.AddDays(-2),
            DateTime.Today.AddDays(-1));

        var rezultat = Knjiznica.PregledIzposoj(_uporabnikId);

        Assert.AreEqual(2, rezultat.Count);
    }

    [TestMethod]
    public void PregledObvestil_VrneZapadleInBliznjeRoke()
    {
        DodajIzposojo(DateTime.Today.AddDays(-2));
        DodajIzposojo(DateTime.Today.AddDays(2));
        DodajIzposojo(DateTime.Today.AddDays(5));

        DodajIzposojo(
            DateTime.Today.AddDays(1),
            DateTime.Today);

        var rezultat = Knjiznica.PregledObvestil(_uporabnikId);

        Assert.AreEqual(2, rezultat.Count);
    }

    [TestMethod]
    public void PregledSeznama_VrneSamoZelimPrebrati()
    {
        DodajNaSeznam("zelim_prebrati");
        DodajNaSeznam("prebrano");

        var rezultat = Knjiznica.PregledSeznama(_uporabnikId);

        Assert.AreEqual(1, rezultat.Count);
    }

    [TestMethod]
    public void DodajNaSeznam_DodaKnjigo()
    {
        var rezultat = Knjiznica.DodajNaSeznam(
            _uporabnikId, _knjigaId);

        Assert.IsTrue(rezultat.Uspeh);
        Assert.AreEqual(
            "Knjiga je dodana na seznam.",
            rezultat.Sporocilo);
    }

    [TestMethod]
    public void DodajNaSeznam_PrepreciPodvojenoKnjigo()
    {
        Knjiznica.DodajNaSeznam(_uporabnikId, _knjigaId);

        var rezultat = Knjiznica.DodajNaSeznam(
            _uporabnikId, _knjigaId);

        Assert.IsFalse(rezultat.Uspeh);
        Assert.AreEqual(
            "Knjiga je že na seznamu.",
            rezultat.Sporocilo);
    }

    [TestMethod]
    public void DodajNaSeznam_NeObstojecaKnjiga()
    {
        var rezultat = Knjiznica.DodajNaSeznam(
            _uporabnikId, -1);

        Assert.IsFalse(rezultat.Uspeh);
        Assert.AreEqual(
            "Knjiga ne obstaja.",
            rezultat.Sporocilo);
    }

    [TestMethod]
    public void OdstraniIzSeznama_OdstraniKnjigo()
    {
        int seznamId = DodajNaSeznam("zelim_prebrati");

        bool rezultat = Knjiznica.OdstraniIzSeznama(
            _uporabnikId, seznamId);

        Assert.IsTrue(rezultat);

        bool ponovno = Knjiznica.OdstraniIzSeznama(
            _uporabnikId, seznamId);

        Assert.IsFalse(ponovno);
    }

    [TestMethod]
    public void PodaljsajRok_PodaljsaZa14Dni()
    {
        DateTime prvotniRok = DateTime.Today.AddDays(7);

        int izposojaId = DodajIzposojo(prvotniRok);

        var rezultat = Knjiznica.PodaljsajRok(
            _uporabnikId, izposojaId);

        Assert.IsTrue(rezultat.Uspeh);

        using var povezava = new SqliteConnection(Povezava);
        povezava.Open();

        using var ukaz = new SqliteCommand(
            "SELECT Rok, Podaljsano FROM Izposoja WHERE Id = @i",
            povezava);

        ukaz.Parameters.AddWithValue("@i", izposojaId);

        using var bralnik = ukaz.ExecuteReader();
        Assert.IsTrue(bralnik.Read());

        DateTime novRok = Convert.ToDateTime(bralnik["Rok"]);
        bool podaljsano = Convert.ToBoolean(bralnik["Podaljsano"]);

        Assert.AreEqual(prvotniRok.AddDays(14), novRok);
        Assert.IsTrue(podaljsano);
    }

    [TestMethod]
    public void PodaljsajRok_NeDovoliDrugegaPodaljsanja()
    {
        int izposojaId = DodajIzposojo(DateTime.Today.AddDays(7));

        Knjiznica.PodaljsajRok(_uporabnikId, izposojaId);

        var rezultat = Knjiznica.PodaljsajRok(
            _uporabnikId, izposojaId);

        Assert.IsFalse(rezultat.Uspeh);
        Assert.AreEqual(
            "Rok izposoje je bil že podaljšan.",
            rezultat.Sporocilo);
    }

    [TestMethod]
    public void PodaljsajRok_NeDovoliPodaljsanjaPoPotekuRoka()
    {
        int izposojaId = DodajIzposojo(DateTime.Today.AddDays(-2));

        var rezultat = Knjiznica.PodaljsajRok(
            _uporabnikId, izposojaId);

        Assert.IsFalse(rezultat.Uspeh);
        Assert.AreEqual(
            "Rok za vračilo je že potekel.",
            rezultat.Sporocilo);
    }

    [TestMethod]
    public void PodaljsajRok_NeDovoliZakljuceneIzposoje()
    {
        int izposojaId = DodajIzposojo(
            DateTime.Today.AddDays(7),
            DateTime.Today);

        var rezultat = Knjiznica.PodaljsajRok(
            _uporabnikId, izposojaId);

        Assert.IsFalse(rezultat.Uspeh);
        Assert.AreEqual(
            "Izposoja je že zaključena.",
            rezultat.Sporocilo);
    }

    [TestMethod]
    public void PodaljsajRok_NeObstojecaIzposoja()
    {
        var rezultat = Knjiznica.PodaljsajRok(
            _uporabnikId, -1);

        Assert.IsFalse(rezultat.Uspeh);
        Assert.AreEqual(
            "Izposoja ne obstaja.",
            rezultat.Sporocilo);
    }
}
