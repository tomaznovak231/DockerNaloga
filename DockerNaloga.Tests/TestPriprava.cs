using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DockerNaloga.Tests;

// Zažene se enkrat pred vsemi testi. Nastavi povezavo na testno bazo.
// Lokalno: baza "knjige" na localhost (root, prazno geslo).
// V GitHub Action: povezava pride iz spremenljivke okolja TEST_CONNECTION.
[TestClass]
public class TestPriprava
{
    [AssemblyInitialize]
    public static void Zacetek(TestContext context)
    {
        Uporabnik.Povezava = Environment.GetEnvironmentVariable("TEST_CONNECTION")
            ?? "Server=localhost;Port=3306;Database=knjige;Uid=root;Pwd=;";
    }
}
