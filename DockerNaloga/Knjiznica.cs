
using MySql.Data.MySqlClient;

namespace DockerNaloga;

public static class Knjiznica
{
    private static MySqlConnection Odpri()
    {
        var povezava = new MySqlConnection(Uporabnik.Povezava);
        povezava.Open();
        return povezava;
    }

    // PREGLED IZPOSOJ IN ZGODOVINA
    public static List<object> PregledIzposoj(int uporabnikId)
    {
        var izposoje = new List<object>();

        using var povezava = Odpri();
        using var ukaz = new MySqlCommand(
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
                Id = bralnik.GetInt32("Id"),
                KnjigaId = bralnik.GetInt32("KnjigaId"),
                Naslov = bralnik.GetString("Naslov"),
                Avtor = bralnik.GetString("Avtor"),
                DatumIzposoje = bralnik.GetDateTime("DatumIzposoje"),
                Rok = bralnik.GetDateTime("Rok"),
                DatumVrnitve = bralnik.IsDBNull(bralnik.GetOrdinal("DatumVrnitve"))
                    ? (DateTime?)null
                    : bralnik.GetDateTime("DatumVrnitve"),
                Podaljsano = bralnik.GetBoolean("Podaljsano")
            });
        }

        return izposoje;
    }

    // OPOMNIK O POTEKU ROKA
    public static List<object> PregledObvestil(int uporabnikId)
    {
        var obvestila = new List<object>();

        using var povezava = Odpri();
        using var ukaz = new MySqlCommand(
            @"SELECT i.Id, i.KnjigaId, k.Naslov, k.Avtor,
                     i.Rok,
                     DATEDIFF(i.Rok, CURDATE()) AS DniDoRoka
              FROM Izposoja i
              JOIN Knjiga k ON k.Id = i.KnjigaId
              WHERE i.UporabnikId = @u
                AND i.DatumVrnitve IS NULL
                AND i.Rok <= DATE_ADD(CURDATE(), INTERVAL 3 DAY)
              ORDER BY i.Rok ASC", povezava);

        ukaz.Parameters.AddWithValue("@u", uporabnikId);

        using var bralnik = ukaz.ExecuteReader();

        while (bralnik.Read())
        {
            int dniDoRoka = bralnik.GetInt32("DniDoRoka");

            obvestila.Add(new
            {
                IzposojaId = bralnik.GetInt32("Id"),
                KnjigaId = bralnik.GetInt32("KnjigaId"),
                Naslov = bralnik.GetString("Naslov"),
                Avtor = bralnik.GetString("Avtor"),
                Rok = bralnik.GetDateTime("Rok"),
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
        using var ukaz = new MySqlCommand(
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
                SeznamId = bralnik.GetInt32("id_seznam"),
                KnjigaId = bralnik.GetInt32("KnjigaId"),
                Naslov = bralnik.GetString("Naslov"),
                Avtor = bralnik.GetString("Avtor"),
                Zanr = bralnik.IsDBNull(bralnik.GetOrdinal("Zanr"))
                    ? null
                    : bralnik.GetString("Zanr"),
                Leto = bralnik.IsDBNull(bralnik.GetOrdinal("Leto"))
                    ? (int?)null
                    : bralnik.GetInt32("Leto"),
                Cena = bralnik.GetDecimal("Cena")
            });
        }

        return seznam;
    }

    // DODAJANJE KNJIGE NA SEZNAM
    public static (bool Uspeh, string Sporocilo) DodajNaSeznam(
        int uporabnikId, int knjigaId)
    {
        using var povezava = Odpri();

        using (var preveriKnjigo = new MySqlCommand(
            "SELECT COUNT(*) FROM Knjiga WHERE Id = @k", povezava))
        {
            preveriKnjigo.Parameters.AddWithValue("@k", knjigaId);

            if (Convert.ToInt32(preveriKnjigo.ExecuteScalar()) == 0)
            {
                return (false, "Knjiga ne obstaja.");
            }
        }

        using (var preveriSeznam = new MySqlCommand(
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

        using var ukaz = new MySqlCommand(
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

        using var ukaz = new MySqlCommand(
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

        string? datumVrnitve;
        DateTime rok;
        bool podaljsano;

        using (var preveri = new MySqlCommand(
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
                : bralnik.GetDateTime("DatumVrnitve").ToString("yyyy-MM-dd");

            rok = bralnik.GetDateTime("Rok");
            podaljsano = bralnik.GetBoolean("Podaljsano");
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

        using var ukaz = new MySqlCommand(
            @"UPDATE Izposoja
              SET Rok = DATE_ADD(Rok, INTERVAL 14 DAY),
                  Podaljsano = 1
              WHERE Id = @i
                AND UporabnikId = @u
                AND DatumVrnitve IS NULL
                AND Podaljsano = 0
                AND Rok >= CURDATE()", povezava);

        ukaz.Parameters.AddWithValue("@i", izposojaId);
        ukaz.Parameters.AddWithValue("@u", uporabnikId);

        if (ukaz.ExecuteNonQuery() == 0)
        {
            return (false, "Roka izposoje ni bilo mogoče podaljšati.");
        }

        return (true, "Rok izposoje je podaljšan za 14 dni.");
    }
}
