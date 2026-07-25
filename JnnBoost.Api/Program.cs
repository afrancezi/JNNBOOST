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

app.MapPost("/api/validar", async (ValidarRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Senha) || string.IsNullOrWhiteSpace(req.Hwid))
        return Results.Ok(new { status = "erro_parametros" });

    var licenca = await db.Licencas
        .Include(l => l.TentativasBloqueadas)
        .FirstOrDefaultAsync(l => l.ChaveLicenca == req.Senha);

    if (licenca is null)
        return Results.Ok(new { status = "invalida" });

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

// Endpoint simples de health check (útil pro Zabbix monitorar essa API depois!)
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();

record ValidarRequest(string Senha, string Hwid);
