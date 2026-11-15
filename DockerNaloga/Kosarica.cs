using MySql.Data.MySqlClient;

namespace DockerNaloga;

// Razred za nakup: košarica, zaključek naročila, preklic in brisanje knjig.
// Isti slog kot Uporabnik: statične metode, povezava iz Uporabnik.Povezava.
public static class Kosarica
{
    // Odpre novo povezavo z bazo. Vsaka metoda ima svojo povezavo (using jo zapre).
    private static MySqlConnection Odpri()
    {
        var c = new MySqlConnection(Uporabnik.Povezava);
        c.Open();
        return c;
    }

    // Vrne Id košarice (naročilo s statusom 'kosarica') ali null, če je uporabnik nima.
    private static int? NajdiKosarico(MySqlConnection c, MySqlTransaction? tx, int uid)
    {
        using var cmd = new MySqlCommand(
            "SELECT Id FROM Narocilo WHERE UporabnikId=@u AND Status='kosarica' LIMIT 1", c, tx);
        cmd.Parameters.AddWithValue("@u", uid);
        var r = cmd.ExecuteScalar();
        return r == null ? null : Convert.ToInt32(r);
    }

    // ---------------------------------------------------------
    // NAKUP: dodaj knjigo v košarico
    // ---------------------------------------------------------
    public static (bool uspeh, string sporocilo) DodajVKosarico(int uid, int knjigaId, int kolicina)
    {
        if (kolicina < 1) return (false, "Količina mora biti vsaj 1.");

        using var c = Odpri();
        using var tx = c.BeginTransaction();

        // 1. Preverimo, ali knjiga obstaja in koliko je na zalogi.
        decimal cena;
        int zaloga;
        using (var cmd = new MySqlCommand("SELECT Cena, Kosov FROM Knjiga WHERE Id=@k", c, tx))
        {
            cmd.Parameters.AddWithValue("@k", knjigaId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return (false, "Knjiga ne obstaja.");
            cena = r.GetDecimal("Cena");
            zaloga = r.GetInt32("Kosov");
        }
        if (zaloga < 1) return (false, "Knjiga je razprodana.");

        // 2. Poiščemo košarico, če je ni, jo ustvarimo.
        var kosaricaId = NajdiKosarico(c, tx, uid);
        if (kosaricaId == null)
        {
            using var ins = new MySqlCommand("INSERT INTO Narocilo (UporabnikId) VALUES (@u)", c, tx);
            ins.Parameters.AddWithValue("@u", uid);
            ins.ExecuteNonQuery();
            kosaricaId = (int)ins.LastInsertedId;
        }

        // 3. Ali je ta knjiga že v košarici? Če je, povečamo količino.
        int? postavkaId = null;
        int obstojeca = 0;
        using (var cmd = new MySqlCommand(
            "SELECT Id, Kolicina FROM NarociloPostavka WHERE NarociloId=@n AND KnjigaId=@k", c, tx))
        {
            cmd.Parameters.AddWithValue("@n", kosaricaId);
            cmd.Parameters.AddWithValue("@k", knjigaId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                postavkaId = r.GetInt32("Id");
                obstojeca = r.GetInt32("Kolicina");
            }
        }

        var skupaj = obstojeca + kolicina;
        if (skupaj > zaloga) return (false, $"Na zalogi je samo še {zaloga} kosov.");

        if (postavkaId == null)
        {
            using var ins = new MySqlCommand(
                "INSERT INTO NarociloPostavka (NarociloId, KnjigaId, Kolicina, Cena) VALUES (@n,@k,@q,@c)", c, tx);
            ins.Parameters.AddWithValue("@n", kosaricaId);
            ins.Parameters.AddWithValue("@k", knjigaId);
            ins.Parameters.AddWithValue("@q", kolicina);
            ins.Parameters.AddWithValue("@c", cena);
            ins.ExecuteNonQuery();
        }
        else
        {
            using var upd = new MySqlCommand("UPDATE NarociloPostavka SET Kolicina=@q WHERE Id=@id", c, tx);
            upd.Parameters.AddWithValue("@q", skupaj);
            upd.Parameters.AddWithValue("@id", postavkaId);
            upd.ExecuteNonQuery();
        }

        tx.Commit();
        return (true, "Knjiga dodana v košarico.");
    }

    // ---------------------------------------------------------
    // PREGLED KOŠARICE: vrne postavke in skupno ceno
    // ---------------------------------------------------------
    public static (List<object> postavke, decimal skupaj) PregledKosarice(int uid)
    {
        var seznam = new List<object>();
        decimal skupaj = 0;

        using var c = Odpri();
        using var cmd = new MySqlCommand(
            @"SELECT k.Id, k.Naslov, k.Avtor, p.Kolicina, p.Cena
              FROM Narocilo n
              JOIN NarociloPostavka p ON p.NarociloId = n.Id
              JOIN Knjiga k ON k.Id = p.KnjigaId
              WHERE n.UporabnikId=@u AND n.Status='kosarica'", c);
        cmd.Parameters.AddWithValue("@u", uid);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var kolicina = r.GetInt32("Kolicina");
            var cena = r.GetDecimal("Cena");
            var vsota = kolicina * cena;
            skupaj += vsota;
            seznam.Add(new
            {
                KnjigaId = r.GetInt32("Id"),
                Naslov = r.GetString("Naslov"),
                Avtor = r.GetString("Avtor"),
                Kolicina = kolicina,
                Cena = cena,
                Vsota = vsota
            });
        }
        return (seznam, skupaj);
    }

    // ---------------------------------------------------------
    // Odstrani knjigo iz košarice
    // ---------------------------------------------------------
    public static (bool uspeh, string sporocilo) OdstraniIzKosarice(int uid, int knjigaId)
    {
        using var c = Odpri();
        using var cmd = new MySqlCommand(
            @"DELETE p FROM NarociloPostavka p
              JOIN Narocilo n ON n.Id = p.NarociloId
              WHERE n.UporabnikId=@u AND n.Status='kosarica' AND p.KnjigaId=@k", c);
        cmd.Parameters.AddWithValue("@u", uid);
        cmd.Parameters.AddWithValue("@k", knjigaId);
        return cmd.ExecuteNonQuery() > 0
            ? (true, "Knjiga odstranjena iz košarice.")
            : (false, "Knjige ni v košarici.");
    }

    // ---------------------------------------------------------
    // NAKUP: zaključi naročilo. Zmanjša zalogo, status gre na 'oddano'.
    // ---------------------------------------------------------
    public static (bool uspeh, string sporocilo, int? narociloId) ZakljuciNarocilo(int uid)
    {
        using var c = Odpri();
        using var tx = c.BeginTransaction();

        var kosaricaId = NajdiKosarico(c, tx, uid);
        if (kosaricaId == null) return (false, "Košarica je prazna.", null);

        // Postavke preberemo v seznam, da lahko reader zapremo pred posodabljanjem.
        var postavke = new List<(int knjigaId, int kolicina)>();
        using (var cmd = new MySqlCommand(
            "SELECT KnjigaId, Kolicina FROM NarociloPostavka WHERE NarociloId=@n", c, tx))
        {
            cmd.Parameters.AddWithValue("@n", kosaricaId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) postavke.Add((r.GetInt32("KnjigaId"), r.GetInt32("Kolicina")));
        }
        if (postavke.Count == 0) return (false, "Košarica je prazna.", null);

        // Zmanjšamo zalogo. Pogoj Kosov >= @q prepreči negativno zalogo.
        foreach (var (knjigaId, kolicina) in postavke)
        {
            using var upd = new MySqlCommand(
                "UPDATE Knjiga SET Kosov = Kosov - @q WHERE Id=@k AND Kosov >= @q", c, tx);
            upd.Parameters.AddWithValue("@q", kolicina);
            upd.Parameters.AddWithValue("@k", knjigaId);
            if (upd.ExecuteNonQuery() == 0)
            {
                tx.Rollback();
                return (false, $"Knjige z Id {knjigaId} ni več dovolj na zalogi.", null);
            }
        }

        using (var st = new MySqlCommand(
            "UPDATE Narocilo SET Status='oddano', Datum=NOW() WHERE Id=@n", c, tx))
        {
            st.Parameters.AddWithValue("@n", kosaricaId);
            st.ExecuteNonQuery();
        }

        tx.Commit();
        return (true, "Naročilo oddano.", kosaricaId);
    }

    // ---------------------------------------------------------
    // PREKLIC NAROČILA: samo naročilo v statusu 'oddano'. Zalogo vrne.
    // ---------------------------------------------------------
    public static (bool uspeh, string sporocilo) PreklicNarocila(int uid, int narociloId)
    {
        using var c = Odpri();
        using var tx = c.BeginTransaction();

        string? status;
        using (var cmd = new MySqlCommand(
            "SELECT Status FROM Narocilo WHERE Id=@n AND UporabnikId=@u", c, tx))
        {
            cmd.Parameters.AddWithValue("@n", narociloId);
            cmd.Parameters.AddWithValue("@u", uid);
            status = cmd.ExecuteScalar() as string;
        }
        if (status == null) return (false, "Naročilo ne obstaja.");
        if (status != "oddano") return (false, $"Naročila s statusom '{status}' ni mogoče preklicati.");

        using (var vrni = new MySqlCommand(
            @"UPDATE Knjiga k JOIN NarociloPostavka p ON p.KnjigaId = k.Id
              SET k.Kosov = k.Kosov + p.Kolicina WHERE p.NarociloId=@n", c, tx))
        {
            vrni.Parameters.AddWithValue("@n", narociloId);
            vrni.ExecuteNonQuery();
        }
        using (var st = new MySqlCommand("UPDATE Narocilo SET Status='preklicano' WHERE Id=@n", c, tx))
        {
            st.Parameters.AddWithValue("@n", narociloId);
            st.ExecuteNonQuery();
        }

        tx.Commit();
        return (true, "Naročilo preklicano.");
    }

    // ---------------------------------------------------------
    // BRISANJE KNJIG
    // ---------------------------------------------------------
    // Ali ima uporabnik vlogo 'zaposleni'? (Vrednost se dogovorite v ekipi.)
    public static bool JeZaposleni(int uid)
    {
        using var c = Odpri();
        using var cmd = new MySqlCommand("SELECT Vloga FROM Uporabnik WHERE Id=@u", c);
        cmd.Parameters.AddWithValue("@u", uid);
        return (cmd.ExecuteScalar() as string) == "zaposleni";
    }

    public static (bool uspeh, string sporocilo) BrisanjeKnjige(int knjigaId)
    {
        using var c = Odpri();

        // Knjige, ki je trenutno izposojena (ni vrnjena), ne brišemo.
        using (var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM Izposoja WHERE KnjigaId=@k AND DatumVrnitve IS NULL", c))
        {
            cmd.Parameters.AddWithValue("@k", knjigaId);
            if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                return (false, "Knjige ni mogoče izbrisati, ker je trenutno izposojena.");
        }

        // Knjige, ki se pojavlja v naročilih, tudi ne (tuji ključ brez ON DELETE).
        using (var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM NarociloPostavka WHERE KnjigaId=@k", c))
        {
            cmd.Parameters.AddWithValue("@k", knjigaId);
            if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                return (false, "Knjige ni mogoče izbrisati, ker je že v naročilih.");
        }

        using var del = new MySqlCommand("DELETE FROM Knjiga WHERE Id=@k", c);
        del.Parameters.AddWithValue("@k", knjigaId);
        return del.ExecuteNonQuery() > 0
            ? (true, "Knjiga izbrisana.")
            : (false, "Knjiga ne obstaja.");
    }
}
