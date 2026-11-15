
// PREGLED IZPOSOJ IN ZGODOVINA
app.MapGet("/izposoje", (HttpContext context) =>
{
    var uporabnikId = context.Session.GetInt32("uid");

    if (uporabnikId == null)
    {
        return Results.Unauthorized();
    }

    var izposoje = Knjiznica.PregledIzposoj(uporabnikId.Value);
    return Results.Ok(izposoje);
});

// OPOMNIK O POTEKU ROKA
app.MapGet("/opomniki/roki", (HttpContext context) =>
{
    var uporabnikId = context.Session.GetInt32("uid");

    if (uporabnikId == null)
    {
        return Results.Unauthorized();
    }

    var obvestila = Knjiznica.PregledObvestil(uporabnikId.Value);
    return Results.Ok(obvestila);
});

// PREGLED SEZNAMA ŽELIM PREBRATI
app.MapGet("/seznam-zelim-prebrati", (HttpContext context) =>
{
    var uporabnikId = context.Session.GetInt32("uid");

    if (uporabnikId == null)
    {
        return Results.Unauthorized();
    }

    var seznam = Knjiznica.PregledSeznama(uporabnikId.Value);
    return Results.Ok(seznam);
});

// DODAJANJE KNJIGE NA SEZNAM
app.MapPost("/seznam-zelim-prebrati/{knjigaId}", (int knjigaId, HttpContext context) =>
{
    var uporabnikId = context.Session.GetInt32("uid");

    if (uporabnikId == null)
    {
        return Results.Unauthorized();
    }

    var rezultat = Knjiznica.DodajNaSeznam(uporabnikId.Value, knjigaId);

    if (rezultat.Uspeh == false)
    {
        return Results.BadRequest(rezultat.Sporocilo);
    }

    return Results.Ok(rezultat.Sporocilo);
});

// ODSTRANJEVANJE KNJIGE S SEZNAMA
app.MapDelete("/seznam-zelim-prebrati/{seznamId}", (int seznamId, HttpContext context) =>
{
    var uporabnikId = context.Session.GetInt32("uid");

    if (uporabnikId == null)
    {
        return Results.Unauthorized();
    }

    bool uspeh = Knjiznica.OdstraniIzSeznama(uporabnikId.Value, seznamId);

    if (uspeh == false)
    {
        return Results.NotFound("Knjige ni na seznamu.");
    }

    return Results.Ok("Knjiga je odstranjena s seznama.");
});

// PODALJŠANJE ROKA IZPOSOJE
app.MapPut("/izposoje/{izposojaId}/podaljsaj", (int izposojaId, HttpContext context) =>
{
    var uporabnikId = context.Session.GetInt32("uid");

    if (uporabnikId == null)
    {
        return Results.Unauthorized();
    }

    var rezultat = Knjiznica.PodaljsajRok(uporabnikId.Value, izposojaId);

    if (rezultat.Uspeh == false)
    {
        return Results.BadRequest(rezultat.Sporocilo);
    }

    return Results.Ok(rezultat.Sporocilo);
});
