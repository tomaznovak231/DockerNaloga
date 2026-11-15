using DockerNaloga;
using Microsoft.AspNetCore.Mvc;

// Ustvarimo spletno aplikacijo (ASP.NET minimal API).
var builder = WebApplication.CreateBuilder(args);

// Seja hrani podatke o prijavljenem uporabniku med zahtevki.
// Shranjuje se v pomnilnik strežnika. Po ponovnem zagonu so vsi odjavljeni.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();

var app = builder.Build();

// Povezavo z bazo preberemo iz appsettings.json (ConnectionStrings:Default).
// Na Azure jo nastavimo kot spremenljivko okolja ConnectionStrings__Default.
// Razred Uporabnik jo uporablja pri vseh klicih na bazo.
Uporabnik.Povezava = builder.Configuration.GetConnectionString("Default")
    ?? throw new Exception("Manjka ConnectionStrings:Default");

// Vklopi sejo. Mora biti pred endpointi.
app.UseSession();

// Testni endpoint. Preveri, ali aplikacija teče.
app.MapGet("/", () => "Hello World!");

// ---------------------------------------------------------
// 1. REGISTRACIJA
// Telo zahtevka (JSON): ime, priimek, email, geslo
// Vrne 200 ob uspehu, 400 če email že obstaja ali podatki niso veljavni.
// ---------------------------------------------------------
app.MapPost("/registracija", (RegistracijaDto d) =>
{
    var (uspeh, sporocilo) = Uporabnik.Registracija(d.Ime, d.Priimek, d.Email, d.Geslo);
    return uspeh ? Results.Ok(sporocilo) : Results.BadRequest(sporocilo);
});

// ---------------------------------------------------------
// 2. PRIJAVA
// Telo zahtevka (JSON): email, geslo
// Ob uspehu shrani Id uporabnika v sejo. Ostali endpointi iz seje preberejo, kdo je prijavljen.
// Vrne 401, če sta email ali geslo napačna.
// ---------------------------------------------------------
app.MapPost("/prijava", (PrijavaDto d, HttpContext ctx) =>
{
    var u = Uporabnik.Prijava(d.Email, d.Geslo);
    if (u == null) return Results.Unauthorized();

    ctx.Session.SetInt32("uid", u.Id);

    // Gesla (hasha) nikoli ne vračamo odjemalcu.
    return Results.Ok(new { u.Id, u.Ime, u.Priimek, u.Email, u.Vloga });
});

// ---------------------------------------------------------
// 3. ODJAVA
// Izbriše vse podatke iz seje, zato uporabnik ni več prijavljen.
// ---------------------------------------------------------
app.MapPost("/odjava", (HttpContext ctx) =>
{
    Uporabnik.Odjava(ctx.Session);
    return Results.Ok("Odjavljen.");
});

// ---------------------------------------------------------
// 4. UREJANJE PROFILA
// Telo zahtevka (JSON): ime, priimek, email
// Potrebna je prijava. Id vzamemo iz seje, tako da uporabnik ne more urejati tujega profila.
// Vrne 401, če ni prijavljen. Vrne 400, če email že uporablja drug uporabnik.
// ---------------------------------------------------------
app.MapPut("/profil", (ProfilDto d, HttpContext ctx) =>
{
    var id = ctx.Session.GetInt32("uid");
    if (id == null) return Results.Unauthorized();

    var (uspeh, sporocilo) = Uporabnik.UrejanjeProfila(id.Value, d.Ime, d.Priimek, d.Email);
    return uspeh ? Results.Ok(sporocilo) : Results.BadRequest(sporocilo);
});

// ---------------------------------------------------------
// 5. PONASTAVITEV GESLA
// Telo zahtevka (JSON): email, novoGeslo
// Poenostavljena različica. Prava različica bi poslala email z žetonom, za to bi potrebovali novo tabelo.
// ---------------------------------------------------------
app.MapPost("/ponastavitev-gesla", (PonastavitevDto d) =>
{
    var (uspeh, sporocilo) = Uporabnik.PonastavitevGesla(d.Email, d.NovoGeslo);
    return uspeh ? Results.Ok(sporocilo) : Results.BadRequest(sporocilo);
});

// ---------------------------------------------------------
// 6. BRISANJE RAČUNA
// Telo zahtevka (JSON): geslo
// Potrebna je prijava in potrditev z geslom.
// Povezani podatki (naročila, ocene, komentarji ...) se izbrišejo sami (ON DELETE CASCADE v bazi).
// Po brisanju uporabnika odjavimo.
// ---------------------------------------------------------
// [FromBody] je obvezen, ker DELETE telesa sicer ne prebere samodejno.
app.MapDelete("/racun", ([FromBody] BrisanjeDto d, HttpContext ctx) =>
{
    var id = ctx.Session.GetInt32("uid");
    if (id == null) return Results.Unauthorized();

    var (uspeh, sporocilo) = Uporabnik.BrisanjeRacuna(id.Value, d.Geslo);
    if (!uspeh) return Results.BadRequest(sporocilo);

    Uporabnik.Odjava(ctx.Session);
    return Results.Ok(sporocilo);
});

app.Run();

// DTO-ji so preprosti razredi za podatke, ki jih odjemalec pošlje v JSON telesu zahtevka.
record RegistracijaDto(string Ime, string Priimek, string Email, string Geslo);
record PrijavaDto(string Email, string Geslo);
record ProfilDto(string Ime, string Priimek, string Email);
record PonastavitevDto(string Email, string NovoGeslo);
record BrisanjeDto(string Geslo);
