using System.Security.Cryptography;
using MySql.Data.MySqlClient;

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

    // 1. Registracija: preveri podatke, geslo zgosti in vstavi uporabnika v bazo.
    // Podvojen email ujamemo prek UNIQUE indeksa (MySQL napaka 1062).
    public static (bool Uspeh, string Sporocilo) Registracija(string ime, string priimek, string email, string geslo)
    {
        if (string.IsNullOrWhiteSpace(ime) || string.IsNullOrWhiteSpace(priimek) ||
            string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(geslo))
            return (false, "Vsa polja so obvezna.");
        if (geslo.Length < 8)
            return (false, "Geslo mora imeti vsaj 8 znakov.");

        try
        {
            using var con = new MySqlConnection(Povezava);
            con.Open();
            using var cmd = new MySqlCommand(
                "INSERT INTO Uporabnik (Ime, Priimek, Email, GesloHash) VALUES (@ime, @priimek, @email, @hash)", con);
            cmd.Parameters.AddWithValue("@ime", ime.Trim());
            cmd.Parameters.AddWithValue("@priimek", priimek.Trim());
            cmd.Parameters.AddWithValue("@email", email.Trim().ToLower());
            cmd.Parameters.AddWithValue("@hash", Zgosti(geslo));
            cmd.ExecuteNonQuery();
            return (true, "Registracija uspešna.");
        }
        catch (MySqlException ex) when (ex.Number == 1062)
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
            using var con = new MySqlConnection(Povezava);
            con.Open();
            using var cmd = new MySqlCommand(
                "UPDATE Uporabnik SET Ime=@ime, Priimek=@priimek, Email=@email WHERE Id=@id", con);
            cmd.Parameters.AddWithValue("@ime", ime.Trim());
            cmd.Parameters.AddWithValue("@priimek", priimek.Trim());
            cmd.Parameters.AddWithValue("@email", email.Trim().ToLower());
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            return (true, "Profil posodobljen.");
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            return (false, "Email že uporablja drug uporabnik.");
        }
    }

    // 5. Ponastavitev gesla (poenostavljeno: email + novo geslo). Shrani nov hash.
    public static (bool Uspeh, string Sporocilo) PonastavitevGesla(string email, string novoGeslo)
    {
        if (string.IsNullOrWhiteSpace(novoGeslo) || novoGeslo.Length < 8)
            return (false, "Geslo mora imeti vsaj 8 znakov.");

        using var con = new MySqlConnection(Povezava);
        con.Open();
        using var cmd = new MySqlCommand("UPDATE Uporabnik SET GesloHash=@hash WHERE Email=@email", con);
        cmd.Parameters.AddWithValue("@hash", Zgosti(novoGeslo));
        cmd.Parameters.AddWithValue("@email", email.Trim().ToLower());
        return cmd.ExecuteNonQuery() > 0
            ? (true, "Geslo spremenjeno.")
            : (false, "Uporabnik ne obstaja.");
    }

    // 6. Brisanje računa: najprej preveri geslo, nato izbriše uporabnika.
    // Povezane vrstice izbriše baza sama (ON DELETE CASCADE).
    public static (bool Uspeh, string Sporocilo) BrisanjeRacuna(int id, string geslo)
    {
        var u = NajdiPoId(id);
        if (u == null || !Preveri(geslo, u.GesloHash))
            return (false, "Napačno geslo.");

        using var con = new MySqlConnection(Povezava);
        con.Open();
        using var cmd = new MySqlCommand("DELETE FROM Uporabnik WHERE Id=@id", con);
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
        using var con = new MySqlConnection(Povezava);
        con.Open();
        using var cmd = new MySqlCommand(
            "SELECT Id, Ime, Priimek, Email, GesloHash, Vloga, DatumRegistracije FROM Uporabnik WHERE " + pogoj, con);
        cmd.Parameters.AddWithValue("@p", vrednost);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Uporabnik
        {
            Id = r.GetInt32("Id"),
            Ime = r.GetString("Ime"),
            Priimek = r.GetString("Priimek"),
            Email = r.GetString("Email"),
            GesloHash = r.GetString("GesloHash"),
            Vloga = r.GetString("Vloga"),
            DatumRegistracije = r.GetDateTime("DatumRegistracije")
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
