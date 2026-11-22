
using Microsoft.Data.Sqlite;

namespace DockerNaloga;

public static class Knjiznica
{
    private static SqliteConnection Odpri()
    {
        var povezava = new SqliteConnection(Uporabnik.Povezava);
        povezava.Open();
        return povezava;
    }

    // PREGLED IZPOSOJ IN ZGODOVINA
    public static List<object> PregledIzposoj(int uporabnikId)
    {
        var izposoje = new List<object>();

        using var povezava = Odpri();
        using var ukaz = new SqliteCommand(
            @"SELECT i.Id, i.KnjigaId, k.Naslov, k.Avtor,
                     i.DatumIzposoje, i.Rok, i.DatumVrnitve, i.Podaljsano
              FROM Izposoja i
              JOIN Knjiga k ON k.Id = i.KnjigaId
              WHERE i.UporabnikId = @u
              ORDER BY i.DatumIzposoje DESC", povezava);

        ukaz.Parameters.AddWithValue("@u", uporabnikId);

        using var bralnik = ukaz.ExecuteReader();

        while (bralnik.Read())
        {
            izposoje.Add(new
            {
                Id = bralnik.GetInt32(bralnik.GetOrdinal("Id")),
                KnjigaId = bralnik.GetInt32(bralnik.GetOrdinal("KnjigaId")),
                Naslov = bralnik.GetString(bralnik.GetOrdinal("Naslov")),
                Avtor = bralnik.GetString(bralnik.GetOrdinal("Avtor")),
                DatumIzposoje = Convert.ToDateTime(bralnik["DatumIzposoje"]),
                Rok = Convert.ToDateTime(bralnik["Rok"]),
                DatumVrnitve = bralnik.IsDBNull(
                    bralnik.GetOrdinal("DatumVrnitve"))
                    ? (DateTime?)null
                    : Convert.ToDateTime(bralnik["DatumVrnitve"]),
                Podaljsano = Convert.ToBoolean(bralnik["Podaljsano"])
            });
        }

        return izposoje;
    }

    // OPOMNIK O POTEKU ROKA
    public static List<object> PregledObvestil(int uporabnikId)
    {
        var obvestila = new List<object>();

        using var povezava = Odpri();
        using var ukaz = new SqliteCommand(
            @"SELECT i.Id, i.KnjigaId, k.Naslov, k.Avtor,
                     i.Rok,
                     CAST(
                         julianday(date(i.Rok)) -
                         julianday(date('now', 'localtime'))
                         AS INTEGER
                     ) AS DniDoRoka
              FROM Izposoja i
              JOIN Knjiga k ON k.Id = i.KnjigaId
              WHERE i.UporabnikId = @u
                AND i.DatumVrnitve IS NULL
                AND date(i.Rok) <= date('now', 'localtime', '+3 days')
              ORDER BY i.Rok ASC", povezava);

        ukaz.Parameters.AddWithValue("@u", uporabnikId);

        using var bralnik = ukaz.ExecuteReader();

        while (bralnik.Read())
        {
            int dniDoRoka = Convert.ToInt32(bralnik["DniDoRoka"]);

            obvestila.Add(new
            {
                IzposojaId = bralnik.GetInt32(bralnik.GetOrdinal("Id")),
                KnjigaId = bralnik.GetInt32(bralnik.GetOrdinal("KnjigaId")),
                Naslov = bralnik.GetString(bralnik.GetOrdinal("Naslov")),
                Avtor = bralnik.GetString(bralnik.GetOrdinal("Avtor")),
                Rok = Convert.ToDateTime(bralnik["Rok"]),
                DniDoRoka = dniDoRoka,
                Sporocilo = dniDoRoka < 0
                    ? "Rok za vračilo knjige je potekel."
                    : dniDoRoka == 0
                        ? "Rok za vračilo knjige je danes."
                        : "Rok za vračilo knjige se bliža."
            });
        }

        return obvestila;
    }

    // PREGLED SEZNAMA ŽELIM PREBRATI
    public static List<object> PregledSeznama(int uporabnikId)
    {
        var seznam = new List<object>();

        using var povezava = Odpri();
        using var ukaz = new SqliteCommand(
            @"SELECT s.id_seznam, s.KnjigaId, k.Naslov, k.Avtor,
                     k.Zanr, k.Leto, k.Cena
              FROM Seznam s
              JOIN Knjiga k ON k.Id = s.KnjigaId
              WHERE s.UporabnikId = @u
                AND s.Tip = 'zelim_prebrati'
              ORDER BY s.id_seznam DESC", povezava);

        ukaz.Parameters.AddWithValue("@u", uporabnikId);

        using var bralnik = ukaz.ExecuteReader();

        while (bralnik.Read())
        {
            seznam.Add(new
            {
                SeznamId = bralnik.GetInt32(
                    bralnik.GetOrdinal("id_seznam")),
                KnjigaId = bralnik.GetInt32(
                    bralnik.GetOrdinal("KnjigaId")),
                Naslov = bralnik.GetString(
                    bralnik.GetOrdinal("Naslov")),
                Avtor = bralnik.GetString(
                    bralnik.GetOrdinal("Avtor")),
                Zanr = bralnik.IsDBNull(
                    bralnik.GetOrdinal("Zanr"))
                    ? null
                    : bralnik.GetString(bralnik.GetOrdinal("Zanr")),
                Leto = bralnik.IsDBNull(
                    bralnik.GetOrdinal("Leto"))
                    ? (int?)null
                    : Convert.ToInt32(bralnik["Leto"]),
                Cena = Convert.ToDecimal(bralnik["Cena"])
            });
        }

        return seznam;
    }

    // DODAJANJE KNJIGE NA SEZNAM
    public static (bool Uspeh, string Sporocilo) DodajNaSeznam(
        int uporabnikId, int knjigaId)
    {
        using var povezava = Odpri();

        using (var preveriKnjigo = new SqliteCommand(
            "SELECT COUNT(*) FROM Knjiga WHERE Id = @k", povezava))
        {
            preveriKnjigo.Parameters.AddWithValue("@k", knjigaId);

            if (Convert.ToInt32(preveriKnjigo.ExecuteScalar()) == 0)
            {
                return (false, "Knjiga ne obstaja.");
            }
        }

        using (var preveriSeznam = new SqliteCommand(
            @"SELECT COUNT(*) FROM Seznam
              WHERE UporabnikId = @u
                AND KnjigaId = @k
                AND Tip = 'zelim_prebrati'", povezava))
        {
            preveriSeznam.Parameters.AddWithValue("@u", uporabnikId);
            preveriSeznam.Parameters.AddWithValue("@k", knjigaId);

            if (Convert.ToInt32(preveriSeznam.ExecuteScalar()) > 0)
            {
                return (false, "Knjiga je že na seznamu.");
            }
        }

        using var ukaz = new SqliteCommand(
            @"INSERT INTO Seznam (UporabnikId, KnjigaId, Tip)
              VALUES (@u, @k, 'zelim_prebrati')", povezava);

        ukaz.Parameters.AddWithValue("@u", uporabnikId);
        ukaz.Parameters.AddWithValue("@k", knjigaId);

        ukaz.ExecuteNonQuery();

        return (true, "Knjiga je dodana na seznam.");
    }

    // ODSTRANJEVANJE KNJIGE S SEZNAMA
    public static bool OdstraniIzSeznama(int uporabnikId, int seznamId)
    {
        using var povezava = Odpri();

        using var ukaz = new SqliteCommand(
            @"DELETE FROM Seznam
              WHERE id_seznam = @s
                AND UporabnikId = @u
                AND Tip = 'zelim_prebrati'", povezava);

        ukaz.Parameters.AddWithValue("@s", seznamId);
        ukaz.Parameters.AddWithValue("@u", uporabnikId);

        return ukaz.ExecuteNonQuery() > 0;
    }

    // PODALJŠANJE ROKA IZPOSOJE
    public static (bool Uspeh, string Sporocilo) PodaljsajRok(
        int uporabnikId, int izposojaId)
    {
        using var povezava = Odpri();

        DateTime? datumVrnitve;
        DateTime rok;
        bool podaljsano;

        using (var preveri = new SqliteCommand(
            @"SELECT DatumVrnitve, Rok, Podaljsano
              FROM Izposoja
              WHERE Id = @i AND UporabnikId = @u", povezava))
        {
            preveri.Parameters.AddWithValue("@i", izposojaId);
            preveri.Parameters.AddWithValue("@u", uporabnikId);

            using var bralnik = preveri.ExecuteReader();

            if (!bralnik.Read())
            {
                return (false, "Izposoja ne obstaja.");
            }

            datumVrnitve = bralnik.IsDBNull(
                bralnik.GetOrdinal("DatumVrnitve"))
                ? null
                : Convert.ToDateTime(bralnik["DatumVrnitve"]);

            rok = Convert.ToDateTime(bralnik["Rok"]);
            podaljsano = Convert.ToBoolean(bralnik["Podaljsano"]);
        }

        if (datumVrnitve != null)
        {
            return (false, "Izposoja je že zaključena.");
        }

        if (rok.Date < DateTime.Today)
        {
            return (false, "Rok za vračilo je že potekel.");
        }

        if (podaljsano)
        {
            return (false, "Rok izposoje je bil že podaljšan.");
        }

        using var ukaz = new SqliteCommand(
            @"UPDATE Izposoja
              SET Rok = date(Rok, '+14 days'),
                  Podaljsano = 1
              WHERE Id = @i
                AND UporabnikId = @u
                AND DatumVrnitve IS NULL
                AND Podaljsano = 0
                AND date(Rok) >= date('now', 'localtime')",
            povezava);

        ukaz.Parameters.AddWithValue("@i", izposojaId);
        ukaz.Parameters.AddWithValue("@u", uporabnikId);

        if (ukaz.ExecuteNonQuery() == 0)
        {
            return (false, "Roka izposoje ni bilo mogoče podaljšati.");
        }

        return (true, "Rok izposoje je podaljšan za 14 dni.");
    }
}
