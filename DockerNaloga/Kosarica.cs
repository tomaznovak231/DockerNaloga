using Microsoft.Data.Sqlite;

namespace DockerNaloga;

public static class Kosarica
{
    private static SqliteConnection Odpri()
    {
        var c = new SqliteConnection(Uporabnik.Povezava);
        c.Open();
        return c;
    }

    // Ustvari tabele Knjiga, Narocilo, NarociloPostavka in Izposoja, če še ne obstajajo.
    // Kliče se enkrat ob zagonu, poleg Uporabnik.Inicializiraj().
    public static void Inicializiraj()
    {
        using var c = Odpri();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Knjiga (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Naslov TEXT NOT NULL,
                Avtor TEXT NOT NULL,
                Zanr TEXT,
                Leto INTEGER,
                Opis TEXT,
                Cena REAL NOT NULL,
                Slika TEXT,
                Kosov INTEGER NOT NULL DEFAULT 1,
                DodalUporabnikId INTEGER,
                FOREIGN KEY (DodalUporabnikId) REFERENCES Uporabnik(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS Narocilo (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UporabnikId INTEGER NOT NULL,
                Datum TEXT NOT NULL DEFAULT (datetime('now')),
                Status TEXT NOT NULL DEFAULT 'kosarica',
                FOREIGN KEY (UporabnikId) REFERENCES Uporabnik(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS NarociloPostavka (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NarociloId INTEGER NOT NULL,
                KnjigaId INTEGER NOT NULL,
                Kolicina INTEGER NOT NULL DEFAULT 1,
                Cena REAL NOT NULL,
                FOREIGN KEY (NarociloId) REFERENCES Narocilo(Id) ON DELETE CASCADE,
                FOREIGN KEY (KnjigaId) REFERENCES Knjiga(Id)
            );

            CREATE TABLE IF NOT EXISTS Izposoja (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UporabnikId INTEGER NOT NULL,
                KnjigaId INTEGER NOT NULL,
                DatumIzposoje TEXT NOT NULL,
                Rok TEXT NOT NULL,
                DatumVrnitve TEXT,
                Podaljsano INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (UporabnikId) REFERENCES Uporabnik(Id) ON DELETE CASCADE,
                FOREIGN KEY (KnjigaId) REFERENCES Knjiga(Id)
            );";
        cmd.ExecuteNonQuery();
    }

    private static int? NajdiKosarico(SqliteConnection c, SqliteTransaction? tx, int uid)
    {
        using var cmd = new SqliteCommand(
            "SELECT Id FROM Narocilo WHERE UporabnikId=@u AND Status='kosarica' LIMIT 1", c, tx);
        cmd.Parameters.AddWithValue("@u", uid);
        var r = cmd.ExecuteScalar();
        return r == null ? null : Convert.ToInt32(r);
    }

    public static (bool uspeh, string sporocilo) DodajVKosarico(int uid, int knjigaId, int kolicina)
    {
        if (kolicina < 1) return (false, "Količina mora biti vsaj 1.");

        using var c = Odpri();
        using var tx = c.BeginTransaction();

        decimal cena;
        int zaloga;
        using (var cmd = new SqliteCommand("SELECT Cena, Kosov FROM Knjiga WHERE Id=@k", c, tx))
        {
            cmd.Parameters.AddWithValue("@k", knjigaId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return (false, "Knjiga ne obstaja.");
            cena = r.GetDecimal(0);
            zaloga = r.GetInt32(1);
        }
        if (zaloga < 1) return (false, "Knjiga je razprodana.");
    
        var kosaricaId = NajdiKosarico(c, tx, uid);
        if (kosaricaId == null)
        {
           using var ins = new SqliteCommand("INSERT INTO Narocilo (UporabnikId) VALUES (@u)", c, tx);
            ins.Parameters.AddWithValue("@u", uid);
            ins.ExecuteNonQuery();

            using var idCmd = new SqliteCommand("SELECT last_insert_rowid();", c, tx);
            kosaricaId = Convert.ToInt32(idCmd.ExecuteScalar());
        }

        int? postavkaId = null;
        int obstojeca = 0;
        using (var cmd = new SqliteCommand(
            "SELECT Id, Kolicina FROM NarociloPostavka WHERE NarociloId=@n AND KnjigaId=@k", c, tx))
        {
            cmd.Parameters.AddWithValue("@n", kosaricaId);
            cmd.Parameters.AddWithValue("@k", knjigaId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                postavkaId = r.GetInt32(0);
                obstojeca = r.GetInt32(1);
            }
        }

        var skupaj = obstojeca + kolicina;
        if (skupaj > zaloga) return (false, $"Na zalogi je samo še {zaloga} kosov.");

        if (postavkaId == null)
        {
            using var ins = new SqliteCommand(
                "INSERT INTO NarociloPostavka (NarociloId, KnjigaId, Kolicina, Cena) VALUES (@n,@k,@q,@c)", c, tx);
            ins.Parameters.AddWithValue("@n", kosaricaId);
            ins.Parameters.AddWithValue("@k", knjigaId);
            ins.Parameters.AddWithValue("@q", kolicina);
            ins.Parameters.AddWithValue("@c", cena);
            ins.ExecuteNonQuery();
        }
        else
        {
            using var upd = new SqliteCommand("UPDATE NarociloPostavka SET Kolicina=@q WHERE Id=@id", c, tx);
            upd.Parameters.AddWithValue("@q", skupaj);
            upd.Parameters.AddWithValue("@id", postavkaId);
            upd.ExecuteNonQuery();
        }

        tx.Commit();
        return (true, "Knjiga dodana v košarico.");
    }

    public static (List<object> postavke, decimal skupaj) PregledKosarice(int uid)
    {
        var seznam = new List<object>();
        decimal skupaj = 0;

        using var c = Odpri();
        using var cmd = new SqliteCommand(
            @"SELECT k.Id, k.Naslov, k.Avtor, p.Kolicina, p.Cena
              FROM Narocilo n
              JOIN NarociloPostavka p ON p.NarociloId = n.Id
              JOIN Knjiga k ON k.Id = p.KnjigaId
              WHERE n.UporabnikId=@u AND n.Status='kosarica'", c);
        cmd.Parameters.AddWithValue("@u", uid);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var kolicina = r.GetInt32(3);
            var cena = r.GetDecimal(4);
            var vsota = kolicina * cena;
            skupaj += vsota;
            seznam.Add(new
            {
                KnjigaId = r.GetInt32(0),
                Naslov = r.GetString(1),
                Avtor = r.GetString(2),
                Kolicina = kolicina,
                Cena = cena,
                Vsota = vsota
            });
        }
        return (seznam, skupaj);
    }

    public static (bool uspeh, string sporocilo) OdstraniIzKosarice(int uid, int knjigaId)
    {
        using var c = Odpri();
        using var cmd = new SqliteCommand(
            @"DELETE FROM NarociloPostavka
              WHERE KnjigaId=@k
              AND NarociloId IN (SELECT Id FROM Narocilo WHERE UporabnikId=@u AND Status='kosarica')", c);
        cmd.Parameters.AddWithValue("@u", uid);
        cmd.Parameters.AddWithValue("@k", knjigaId);
        return cmd.ExecuteNonQuery() > 0
            ? (true, "Knjiga odstranjena iz košarice.")
            : (false, "Knjige ni v košarici.");
    }

    public static (bool uspeh, string sporocilo, int? narociloId) ZakljuciNarocilo(int uid)
    {
        using var c = Odpri();
        using var tx = c.BeginTransaction();

        var kosaricaId = NajdiKosarico(c, tx, uid);
        if (kosaricaId == null) return (false, "Košarica je prazna.", null);

        var postavke = new List<(int knjigaId, int kolicina)>();
        using (var cmd = new SqliteCommand(
            "SELECT KnjigaId, Kolicina FROM NarociloPostavka WHERE NarociloId=@n", c, tx))
        {
            cmd.Parameters.AddWithValue("@n", kosaricaId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) postavke.Add((r.GetInt32(0), r.GetInt32(1)));
        }
        if (postavke.Count == 0) return (false, "Košarica je prazna.", null);

        foreach (var (knjigaId, kolicina) in postavke)
        {
            using var upd = new SqliteCommand(
                "UPDATE Knjiga SET Kosov = Kosov - @q WHERE Id=@k AND Kosov >= @q", c, tx);
            upd.Parameters.AddWithValue("@q", kolicina);
            upd.Parameters.AddWithValue("@k", knjigaId);
            if (upd.ExecuteNonQuery() == 0)
            {
                tx.Rollback();
                return (false, $"Knjige z Id {knjigaId} ni več dovolj na zalogi.", null);
            }
        }

        using (var st = new SqliteCommand(
            "UPDATE Narocilo SET Status='oddano', Datum=datetime('now') WHERE Id=@n", c, tx))
        {
            st.Parameters.AddWithValue("@n", kosaricaId);
            st.ExecuteNonQuery();
        }

        tx.Commit();
        return (true, "Naročilo oddano.", kosaricaId);
    }

    public static (bool uspeh, string sporocilo) PreklicNarocila(int uid, int narociloId)
    {
        using var c = Odpri();
        using var tx = c.BeginTransaction();

        string? status;
        using (var cmd = new SqliteCommand(
            "SELECT Status FROM Narocilo WHERE Id=@n AND UporabnikId=@u", c, tx))
        {
            cmd.Parameters.AddWithValue("@n", narociloId);
            cmd.Parameters.AddWithValue("@u", uid);
            status = cmd.ExecuteScalar() as string;
        }
        if (status == null) return (false, "Naročilo ne obstaja.");
        if (status != "oddano") return (false, $"Naročila s statusom '{status}' ni mogoče preklicati.");

        using (var vrni = new SqliteCommand(
            @"UPDATE Knjiga
              SET Kosov = Kosov + (
                  SELECT COALESCE(SUM(p.Kolicina), 0) FROM NarociloPostavka p
                  WHERE p.NarociloId=@n AND p.KnjigaId = Knjiga.Id
              )
              WHERE Id IN (SELECT KnjigaId FROM NarociloPostavka WHERE NarociloId=@n)", c, tx))
        {
            vrni.Parameters.AddWithValue("@n", narociloId);
            vrni.ExecuteNonQuery();
        }
        using (var st = new SqliteCommand("UPDATE Narocilo SET Status='preklicano' WHERE Id=@n", c, tx))
        {
            st.Parameters.AddWithValue("@n", narociloId);
            st.ExecuteNonQuery();
        }

        tx.Commit();
        return (true, "Naročilo preklicano.");
    }

    public static bool JeZaposleni(int uid)
    {
        using var c = Odpri();
        using var cmd = new SqliteCommand("SELECT Vloga FROM Uporabnik WHERE Id=@u", c);
        cmd.Parameters.AddWithValue("@u", uid);
        return (cmd.ExecuteScalar() as string) == "zaposleni";
    }

    public static (bool uspeh, string sporocilo) BrisanjeKnjige(int knjigaId)
    {
        using var c = Odpri();

        using (var cmd = new SqliteCommand(
            "SELECT COUNT(*) FROM Izposoja WHERE KnjigaId=@k AND DatumVrnitve IS NULL", c))
        {
            cmd.Parameters.AddWithValue("@k", knjigaId);
            if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                return (false, "Knjige ni mogoče izbrisati, ker je trenutno izposojena.");
        }

        using (var cmd = new SqliteCommand(
            "SELECT COUNT(*) FROM NarociloPostavka WHERE KnjigaId=@k", c))
        {
            cmd.Parameters.AddWithValue("@k", knjigaId);
            if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                return (false, "Knjige ni mogoče izbrisati, ker je že v naročilih.");
        }

        using var del = new SqliteCommand("DELETE FROM Knjiga WHERE Id=@k", c);
        del.Parameters.AddWithValue("@k", knjigaId);
        return del.ExecuteNonQuery() > 0
            ? (true, "Knjiga izbrisana.")
            : (false, "Knjiga ne obstaja.");
    }
}