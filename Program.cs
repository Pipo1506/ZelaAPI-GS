using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Zela.SLAApi.Data;
using Zela.SLAApi.DTOs;
using Zela.SLAApi.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Banco de Dados Oracle via EF Core
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseOracle(connectionString));

// 2. Tratamento global padronizado de erros (ProblemDetails)
builder.Services.AddProblemDetails();

// 3. OpenAPI / Swagger com suporte a JWT Bearer
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Zela SLA API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Informe o token JWT gerado no endpoint /api/auth/login"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

// 4. Health Checks 
builder.Services.AddHealthChecks()
    .AddCheck("API_Liveness", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API operacional."), tags: ["live"])
    .AddDbContextCheck<AppDbContext>("Database_Health_Check", tags: ["ready"]);

// 5. Rate Limiting (proteção contra uso excessivo -> HTTP 429)
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Request.Headers.Host.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 2
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// 6. Autenticação e Autorização JWT
var jwtKey = builder.Configuration["Jwt:Key"] ?? "ZelaSlaApiSuperSecretKey2026SecureTokenValidation";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Zela.SLAApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "Zela.CidadaoGestor";
var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

// Pipeline HTTP
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Endpoints de Health Checks
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// ENDPOINT DE AUTENTICAÇÃO JWT
app.MapPost("/api/auth/login", (LoginRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Usuario))
        return Results.BadRequest(new { mensagem = "Usuário é obrigatório." });

    var role = string.IsNullOrWhiteSpace(request.Papel) ? "Gestor" : request.Papel.Trim();

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, request.Usuario.Trim()),
        new Claim(ClaimTypes.Role, role),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

    var credentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

    var tokenDescriptor = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(2),
        signingCredentials: credentials
    );

    var tokenString = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);

    return Results.Ok(new
    {
        accessToken = tokenString,
        tokenType = "Bearer",
        expiresInSeconds = 7200,
        usuario = request.Usuario.Trim(),
        papel = role
    });
})
.WithName("LoginJwt")
.WithOpenApi();

// ENDPOINTS MINIMAL API (Domínio SLA - Zela)

app.MapGet("/api/sla", async (AppDbContext db) =>
    Results.Ok(await db.SlaRecords.ToListAsync()))
    .RequireAuthorization()
    .WithName("GetSlaRecords")
    .WithOpenApi();

app.MapGet("/api/sla/{id:int}", async (int id, AppDbContext db) =>
{
    var sla = await db.SlaRecords.FindAsync(id);
    return sla is null
        ? Results.NotFound(new { mensagem = "Registro de SLA não encontrado." })
        : Results.Ok(sla);
})
.RequireAuthorization()
.WithName("GetSlaById")
.WithOpenApi();

app.MapPost("/api/sla", async (HttpContext context, AppDbContext db, CreateSlaRequest request) =>
{
    var validationContext = new ValidationContext(request);
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true))
    {
        var errors = validationResults
            .GroupBy(v => v.MemberNames.FirstOrDefault() ?? "Geral")
            .ToDictionary(g => g.Key, g => g.Select(v => v.ErrorMessage ?? "Valor inválido.").ToArray());

        return Results.ValidationProblem(errors);
    }

    if (context.Request.Headers.TryGetValue("X-Idempotency-Key", out var idempotencyKey) &&
        !string.IsNullOrWhiteSpace(idempotencyKey))
    {
        var existente = await db.IdempotencyRequests
            .FirstOrDefaultAsync(i => i.Id == idempotencyKey.ToString());

        if (existente != null)
            return Results.StatusCode(existente.StatusCode);
    }

    var agora = DateTime.UtcNow;
    var novoSla = new SlaRecord
    {
        OcorrenciaId = request.OcorrenciaId.Trim(),
        Categoria = request.Categoria.Trim(),
        PrazoHoras = request.PrazoHoras,
        ExecutorResponsavel = request.ExecutorResponsavel.Trim(),
        DataAbertura = agora,
        DataLimite = agora.AddHours(request.PrazoHoras),
        Status = "Pendente"
    };

    db.SlaRecords.Add(novoSla);
    await db.SaveChangesAsync();

    if (!string.IsNullOrWhiteSpace(idempotencyKey))
    {
        db.IdempotencyRequests.Add(new IdempotencyRequest
        {
            Id = idempotencyKey.ToString(),
            StatusCode = StatusCodes.Status201Created,
            ResponseBody = $"Criado com ID {novoSla.Id}"
        });
        await db.SaveChangesAsync();
    }

    return Results.Created($"/api/sla/{novoSla.Id}", novoSla);
})
.RequireAuthorization()
.WithName("CreateSlaRecord")
.WithOpenApi();

app.MapPatch("/api/sla/{id:int}/escalar", async (int id, HttpContext context, AppDbContext db) =>
{
    if (context.Request.Headers.TryGetValue("X-Idempotency-Key", out var idempotencyKey) &&
        !string.IsNullOrWhiteSpace(idempotencyKey))
    {
        var key = $"escalar:{id}:{idempotencyKey}";
        var existente = await db.IdempotencyRequests.FirstOrDefaultAsync(i => i.Id == key);
        if (existente != null)
            return Results.StatusCode(existente.StatusCode);
    }

    var sla = await db.SlaRecords.FindAsync(id);
    if (sla is null)
        return Results.NotFound(new { mensagem = "Registro de SLA não encontrado." });

    sla.Status = "Escalado";
    await db.SaveChangesAsync();

    if (!string.IsNullOrWhiteSpace(idempotencyKey))
    {
        db.IdempotencyRequests.Add(new IdempotencyRequest
        {
            Id = $"escalar:{id}:{idempotencyKey}",
            StatusCode = StatusCodes.Status200OK,
            ResponseBody = $"SLA {sla.Id} escalado."
        });
        await db.SaveChangesAsync();
    }

    return Results.Ok(sla);
})
.RequireAuthorization(policy => policy.RequireRole("Gestor", "Supervisor"))
.WithName("EscalateSlaRecord")
.WithOpenApi();

app.Run();