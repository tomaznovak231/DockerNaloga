
using Microsoft.Data.Sqlite;

namespace DockerNaloga.Tests;

// Zažene se enkrat pred vsemi testi. Ustvari začasno SQLite bazo za cel test-run,
// tako da Uporabnik in Kosarica testi delijo isto bazo brez MySQL strežnika.
[TestClass]
public class TestPriprava
{
    private static string _dbPot = "";

    [AssemblyInitialize]
    public static void Zacetek(TestContext context)
    {
        _dbPot = Path.Combine(Path.GetTempPath(), $"testna-baza-{Guid.NewGuid()}.db");
        Uporabnik.Povezava = $"Data Source={_dbPot};Foreign Keys=True;Default Timeout=30";

        Uporabnik.Inicializiraj();
        Kosarica.Inicializiraj();

        using var povezava = new SqliteConnection(Uporabnik.Povezava);
        povezava.Open();

        using var ukaz = povezava.CreateCommand();

        ukaz.CommandText = @"
            CREATE TABLE IF NOT EXISTS Seznam (
                id_seznam INTEGER PRIMARY KEY AUTOINCREMENT,
                UporabnikId INTEGER NOT NULL,
                KnjigaId INTEGER NOT NULL,
                Tip TEXT NOT NULL,
                FOREIGN KEY (UporabnikId) REFERENCES Uporabnik(Id),
                FOREIGN KEY (KnjigaId) REFERENCES Knjiga(Id)
            );
        ";

        ukaz.ExecuteNonQuery();
    }

    [AssemblyCleanup]
    public static void Konec()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPot)) File.Delete(_dbPot);
    }
}
