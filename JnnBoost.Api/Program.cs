using JnnBoost.Api.Data;
using JnnBoost.Api.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Lê a connection string de variável de ambiente (nunca hardcoded).
// Configure no docker-compose ou no ambiente do sistema:
//   DB_CONNECTION_STRING=Host=postgres-licencas;Database=licencas_db;Username=appuser;Password=...
var connectionString = builder.Configuration["DB_CONNECTION_STRING"]
    ?? Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
    ?? throw new InvalidOperationException("DB_CONNECTION_STRING não configurada.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Chave secreta usada para proteger os endpoints administrativos
// (ex.: os comandos do bot do Discord). Nunca deixe hardcoded - vem do .env.
var adminApiKey = builder.Configuration["ADMIN_API_KEY"]
    ?? Environment.GetEnvironmentVariable("ADMIN_API_KEY")
    ?? throw new InvalidOperationException("ADMIN_API_KEY não configurada.");

var app = builder.Build();

// Cria as tabelas automaticamente com base nos Models, caso ainda não existam.
// Simples e direto para este projeto. Se no futuro você precisar alterar o
// schema (adicionar uma coluna, por exemplo), essa abordagem não gera um
// histórico de mudanças — nesse ponto vale migrar para EF Migrations de verdade.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Função auxiliar simples para validar a chave de admin em cada requisição
bool AdminAutorizado(HttpRequest request)
{
    return request.Headers.TryGetValue("X-Admin-Key", out var valor)
        && valor.ToString() == adminApiKey;
}

app.MapPost("/api/validar", async (ValidarRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Senha) || string.IsNullOrWhiteSpace(req.Hwid))
        return Results.Ok(new { status = "erro_parametros" });

    var licenca = await db.Licencas
        .Include(l => l.TentativasBloqueadas)
        .FirstOrDefaultAsync(l => l.ChaveLicenca == req.Senha);

    if (licenca is null)
        return Results.Ok(new { status = "invalida" });

    // Checa expiração ANTES de qualquer outra lógica. Se venceu, revoga
    // o acesso nesse exato momento (marca como inativa) e informa o app.
    if (licenca.DataExpiracao is not null && DateTime.UtcNow > licenca.DataExpiracao)
    {
        if (licenca.Ativa)
        {
            licenca.Ativa = false;
            await db.SaveChangesAsync();
        }
        return Results.Ok(new { status = "expirada" });
    }

    bool naoAtivada = !licenca.Ativa || string.IsNullOrEmpty(licenca.Hwid);

    if (naoAtivada)
    {
        licenca.Hwid = req.Hwid;
        licenca.DataAtivacao = DateTime.UtcNow;
        licenca.Ativa = true;
        await db.SaveChangesAsync();
        return Results.Ok(new { status = "ativado" });
    }

    if (licenca.Hwid == req.Hwid)
        return Results.Ok(new { status = "autorizado" });

    // HWID diferente do cadastrado — registra a tentativa bloqueada
    db.TentativasBloqueadas.Add(new TentativaBloqueada
    {
        LicencaId = licenca.Id,
        HwidTentativa = req.Hwid,
        TentativaEm = DateTime.UtcNow
    });
    await db.SaveChangesAsync();

    return Results.Ok(new { status = "bloqueado" });
});

// -----------------------------------------------------------------
// ENDPOINTS ADMINISTRATIVOS (usados pelo bot do Discord)
// Todos exigem o header X-Admin-Key com o valor configurado no .env
// -----------------------------------------------------------------

app.MapPost("/api/admin/criar-licenca", async (CriarLicencaRequest req, HttpRequest http, AppDbContext db) =>
{
    if (!AdminAutorizado(http))
        return Results.Json(new { erro = "não autorizado" }, statusCode: 401);

    string chave = string.IsNullOrWhiteSpace(req.Chave)
        ? Guid.NewGuid().ToString("N")[..19].ToUpper()
        : req.Chave.Trim();

    bool jaExiste = await db.Licencas.AnyAsync(l => l.ChaveLicenca == chave);
    if (jaExiste)
        return Results.Json(new { erro = "chave já existe" }, statusCode: 409);

    var licenca = new Licenca
    {
        ChaveLicenca = chave,
        Ativa = false,
        CriadaEm = DateTime.UtcNow,
        DataExpiracao = req.Dias is not null
            ? DateTime.UtcNow.AddDays(req.Dias.Value)
            : null
    };

    db.Licencas.Add(licenca);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        chave = licenca.ChaveLicenca,
        expira_em = licenca.DataExpiracao
    });
});

app.MapPost("/api/admin/renovar-licenca", async (RenovarLicencaRequest req, HttpRequest http, AppDbContext db) =>
{
    if (!AdminAutorizado(http))
        return Results.Json(new { erro = "não autorizado" }, statusCode: 401);

    var licenca = await db.Licencas.FirstOrDefaultAsync(l => l.ChaveLicenca == req.Chave);
    if (licenca is null)
        return Results.Json(new { erro = "licença não encontrada" }, statusCode: 404);

    licenca.Ativa = true;
    licenca.DataExpiracao = DateTime.UtcNow.AddDays(req.Dias);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        chave = licenca.ChaveLicenca,
        expira_em = licenca.DataExpiracao
    });
});

app.MapGet("/api/admin/licenca/{chave}", async (string chave, HttpRequest http, AppDbContext db) =>
{
    if (!AdminAutorizado(http))
        return Results.Json(new { erro = "não autorizado" }, statusCode: 401);

    var licenca = await db.Licencas
        .Include(l => l.TentativasBloqueadas)
        .FirstOrDefaultAsync(l => l.ChaveLicenca == chave);

    if (licenca is null)
        return Results.Json(new { erro = "licença não encontrada" }, statusCode: 404);

    return Results.Ok(new
    {
        chave = licenca.ChaveLicenca,
        ativa = licenca.Ativa,
        hwid = licenca.Hwid,
        data_ativacao = licenca.DataAtivacao,
        expira_em = licenca.DataExpiracao,
        tentativas_bloqueadas = licenca.TentativasBloqueadas.Count
    });
});

// Endpoint simples de health check (útil pro Zabbix monitorar essa API depois!)
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();

record ValidarRequest(string Senha, string Hwid);
record CriarLicencaRequest(string? Chave, int? Dias);
record RenovarLicencaRequest(string Chave, int Dias);
