using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace DockerNaloga;

public class Uporabnik
{
    public int Id { get; set; }
    public string Ime { get; set; } = "";
    public string Priimek { get; set; } = "";
    public string Email { get; set; } = "";
    public string GesloHash { get; set; } = "";
    public string Vloga { get; set; } = "stranka";
    public DateTime DatumRegistracije { get; set; }

    // Povezava z bazo. Nastavi se enkrat ob zagonu v Program.cs.
    public static string Povezava { get; set; } = "";

    // Ustvari tabelo Uporabnik v SQLite datoteki, če še ne obstaja.
    // Kliče se enkrat ob zagonu (Program.cs), ker baze ne gostimo ločeno.
    public static void Inicializiraj()
{
    using var con = new SqliteConnection(Povezava);
    con.Open();

    using (var pragma = con.CreateCommand())
    {
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        pragma.ExecuteNonQuery();
    }

    using var cmd = con.CreateCommand();
    cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS Uporabnik (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Ime TEXT NOT NULL,
            Priimek TEXT NOT NULL,
            Email TEXT NOT NULL UNIQUE,
            GesloHash TEXT NOT NULL,
            Vloga TEXT NOT NULL DEFAULT 'stranka',
            DatumRegistracije TEXT NOT NULL DEFAULT (datetime('now'))
        );";
    cmd.ExecuteNonQuery();
}

    // 1. Registracija: preveri podatke, geslo zgosti in vstavi uporabnika v bazo.
    // Podvojen email ujamemo prek UNIQUE indeksa (SQLite koda 2067 = UNIQUE constraint).
    public static (bool Uspeh, string Sporocilo) Registracija(string ime, string priimek, string email, string geslo)
    {
        if (string.IsNullOrWhiteSpace(ime) || string.IsNullOrWhiteSpace(priimek) ||
            string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(geslo))
            return (false, "Vsa polja so obvezna.");
        if (geslo.Length < 8)
            return (false, "Geslo mora imeti vsaj 8 znakov.");

        try
        {
            using var con = new SqliteConnection(Povezava);
            con.Open();
            using var cmd = new SqliteCommand(
                "INSERT INTO Uporabnik (Ime, Priimek, Email, GesloHash) VALUES (@ime, @priimek, @email, @hash)", con);
            cmd.Parameters.AddWithValue("@ime", ime.Trim());
            cmd.Parameters.AddWithValue("@priimek", priimek.Trim());
            cmd.Parameters.AddWithValue("@email", email.Trim().ToLower());
            cmd.Parameters.AddWithValue("@hash", Zgosti(geslo));
            cmd.ExecuteNonQuery();
            return (true, "Registracija uspešna.");
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            return (false, "Email je že registriran.");
        }
    }

    // 2. Prijava: vrne uporabnika, če geslo ustreza hashu v bazi. Sicer vrne null.
    public static Uporabnik? Prijava(string email, string geslo)
    {
        var u = NajdiPoEmailu(email);
        if (u == null || !Preveri(geslo, u.GesloHash))
            return null;
        return u;
    }

    // 3. Odjava: pobriše sejo.
    public static void Odjava(ISession seja)
    {
        seja.Clear();
    }

    // 4. Urejanje profila: posodobi ime, priimek in email za podan Id.
    public static (bool Uspeh, string Sporocilo) UrejanjeProfila(int id, string ime, string priimek, string email)
    {
        if (string.IsNullOrWhiteSpace(ime) || string.IsNullOrWhiteSpace(priimek) || string.IsNullOrWhiteSpace(email))
            return (false, "Vsa polja so obvezna.");

        try
        {
            using var con = new SqliteConnection(Povezava);
            con.Open();
            using var cmd = new SqliteCommand(
                "UPDATE Uporabnik SET Ime=@ime, Priimek=@priimek, Email=@email WHERE Id=@id", con);
            cmd.Parameters.AddWithValue("@ime", ime.Trim());
            cmd.Parameters.AddWithValue("@priimek", priimek.Trim());
            cmd.Parameters.AddWithValue("@email", email.Trim().ToLower());
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            return (true, "Profil posodobljen.");
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            return (false, "Email že uporablja drug uporabnik.");
        }
    }

    // 5. Ponastavitev gesla (poenostavljeno: email + novo geslo). Shrani nov hash.
    public static (bool Uspeh, string Sporocilo) PonastavitevGesla(string email, string novoGeslo)
    {
        if (string.IsNullOrWhiteSpace(novoGeslo) || novoGeslo.Length < 8)
            return (false, "Geslo mora imeti vsaj 8 znakov.");

        using var con = new SqliteConnection(Povezava);
        con.Open();
        using var cmd = new SqliteCommand("UPDATE Uporabnik SET GesloHash=@hash WHERE Email=@email", con);
        cmd.Parameters.AddWithValue("@hash", Zgosti(novoGeslo));
        cmd.Parameters.AddWithValue("@email", email.Trim().ToLower());
        return cmd.ExecuteNonQuery() > 0
            ? (true, "Geslo spremenjeno.")
            : (false, "Uporabnik ne obstaja.");
    }

    // 6. Brisanje računa: najprej preveri geslo, nato izbriše uporabnika.
    public static (bool Uspeh, string Sporocilo) BrisanjeRacuna(int id, string geslo)
    {
        var u = NajdiPoId(id);
        if (u == null || !Preveri(geslo, u.GesloHash))
            return (false, "Napačno geslo.");

        using var con = new SqliteConnection(Povezava);
        con.Open();
        using var cmd = new SqliteCommand("DELETE FROM Uporabnik WHERE Id=@id", con);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        return (true, "Račun izbrisan.");
    }

    // Pomožne metode
    public static Uporabnik? NajdiPoId(int id) => Najdi("Id=@p", id);

    public static Uporabnik? NajdiPoEmailu(string email) => Najdi("Email=@p", email.Trim().ToLower());

    // Skupna iskalna metoda za NajdiPoId in NajdiPoEmailu. Parametri preprečijo SQL injection.
    private static Uporabnik? Najdi(string pogoj, object vrednost)
    {
        using var con = new SqliteConnection(Povezava);
        con.Open();
        using var cmd = new SqliteCommand(
            "SELECT Id, Ime, Priimek, Email, GesloHash, Vloga, DatumRegistracije FROM Uporabnik WHERE " + pogoj, con);
        cmd.Parameters.AddWithValue("@p", vrednost);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Uporabnik
        {
            Id = r.GetInt32(0),
            Ime = r.GetString(1),
            Priimek = r.GetString(2),
            Email = r.GetString(3),
            GesloHash = r.GetString(4),
            Vloga = r.GetString(5),
            DatumRegistracije = DateTime.Parse(r.GetString(6))
        };
    }

    // Geslo zgostimo s PBKDF2 in naključno soljo. V bazo gre zapis "sol.hash" (Base64).
    private static string Zgosti(string geslo)
    {
        var sol = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(geslo, sol, 100000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(sol) + "." + Convert.ToBase64String(hash);
    }

    // Iz shranjenega zapisa vzame sol, zgosti vneseno geslo in primerja rezultata.
    private static bool Preveri(string geslo, string shranjen)
    {
        var deli = shranjen.Split('.');
        if (deli.Length != 2) return false;
        var sol = Convert.FromBase64String(deli[0]);
        var pricakovan = Convert.FromBase64String(deli[1]);
        var hash = Rfc2898DeriveBytes.Pbkdf2(geslo, sol, 100000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, pricakovan);
    }
}