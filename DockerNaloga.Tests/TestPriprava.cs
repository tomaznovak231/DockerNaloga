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
    }

    [AssemblyCleanup]
    public static void Konec()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPot)) File.Delete(_dbPot);
    }
}