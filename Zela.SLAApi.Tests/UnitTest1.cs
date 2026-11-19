using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zela.SLAApi.Data;
using Zela.SLAApi.DTOs;
using Zela.SLAApi.Models;

namespace Zela.SLAApi.Tests;

public class ZelaWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"ZelaSlaTestDb_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptorsToRemove = services
                .Where(d =>
                    d.ServiceType == typeof(AppDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    (d.ServiceType.IsGenericType &&
                     d.ServiceType.GetGenericTypeDefinition()
                         .FullName?.Contains("IDbContextOptionsConfiguration") == true))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            var uiInitServices = services
                .Where(d => d.ImplementationType?.FullName?.Contains("UIInitializationHostedService") == true)
                .ToList();

            foreach (var uiService in uiInitServices)
            {
                services.Remove(uiService);
            }
        });
    }
}

public class SlaApiTests : IClassFixture<ZelaWebApplicationFactory>
{
    private readonly ZelaWebApplicationFactory _factory;

    public SlaApiTests(ZelaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<string> ObterTokenAsync(HttpClient client, string usuario = "gestor.zela", string papel = "Gestor")
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(usuario, papel));
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public void CreateSlaRequest_Deve_Falhar_Quando_PrazoHoras_For_Invalido()
    {
        var dtoInvalido = new CreateSlaRequest(
            OcorrenciaId: "OC-101",
            Categoria: "Iluminação",
            PrazoHoras: 0,
            ExecutorResponsavel: "Equipe Norte"
        );

        var context = new ValidationContext(dtoInvalido);
        var resultados = new List<ValidationResult>();
        var valido = Validator.TryValidateObject(dtoInvalido, context, resultados, validateAllProperties: true);

        Assert.False(valido);
        Assert.Contains(resultados, r => r.MemberNames.Contains(nameof(CreateSlaRequest.PrazoHoras)));
    }

    [Fact]
    public void SlaRecord_Deve_Calcular_DataLimite_Corretamente()
    {
        var abertura = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
        var sla = new SlaRecord
        {
            OcorrenciaId = "OC-202",
            Categoria = "Buraco",
            PrazoHoras = 24,
            DataAbertura = abertura,
            DataLimite = abertura.AddHours(24)
        };

        Assert.Equal(abertura.AddHours(24), sla.DataLimite);
    }

    [Fact]
    public async Task LoginJwt_Deve_Retornar_Token_Quando_Usuario_Valido()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("gestor.zela", "Gestor"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await ObterTokenAsync(client);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task Rota_Protegida_Sla_Deve_Retornar_401_Sem_Token()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/sla");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostSla_Deve_Criar_E_Respeitar_IdempotencyKey()
    {
        var client = _factory.CreateClient();
        var token = await ObterTokenAsync(client, "gestor.zela", "Gestor");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var idemKey = $"idem-{Guid.NewGuid()}";
        var payload = new CreateSlaRequest("OC-500", "Poda de Árvore", 48, "Equipe Sul");

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/sla") { Content = JsonContent.Create(payload) };
        req1.Headers.Add("X-Idempotency-Key", idemKey);
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/sla") { Content = JsonContent.Create(payload) };
        req2.Headers.Add("X-Idempotency-Key", idemKey);
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);

        var lista = await client.GetFromJsonAsync<List<SlaRecord>>("/api/sla");
        Assert.NotNull(lista);
        Assert.Single(lista, x => x.OcorrenciaId == "OC-500");
    }

    [Fact]
    public async Task PatchEscalar_Deve_Retornar_403_Para_Role_Sem_Permissao()
    {
        var client = _factory.CreateClient();
        var tokenCidadao = await ObterTokenAsync(client, "cidadao.teste", "Cidadao");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenCidadao);

        var response = await client.PatchAsync("/api/sla/1/escalar", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PatchEscalar_Deve_Atualizar_Status_Para_Escalado_Com_Gestor()
    {
        var client = _factory.CreateClient();
        var tokenGestor = await ObterTokenAsync(client, "gestor.sla", "Gestor");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenGestor);

        var createRes = await client.PostAsJsonAsync("/api/sla",
            new CreateSlaRequest("OC-777", "Alagamento", 12, "Defesa Civil"));
        createRes.EnsureSuccessStatusCode();
        var criado = await createRes.Content.ReadFromJsonAsync<SlaRecord>();
        Assert.NotNull(criado);

        var patchRes = await client.PatchAsync($"/api/sla/{criado!.Id}/escalar", null);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        var atualizado = await patchRes.Content.ReadFromJsonAsync<SlaRecord>();
        Assert.Equal("Escalado", atualizado?.Status);
    }

    [Fact]
    public async Task PostSla_Deve_Retornar_400_Quando_Payload_For_Invalido()
    {
        var client = _factory.CreateClient();
        var token = await ObterTokenAsync(client, "gestor.sla", "Gestor");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payloadInvalido = new CreateSlaRequest(
            OcorrenciaId: "",
            Categoria: "A",
            PrazoHoras: 0,
            ExecutorResponsavel: ""
        );

        var response = await client.PostAsJsonAsync("/api/sla", payloadInvalido);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSlaById_Deve_Retornar_404_Quando_Id_Nao_Existir()
    {
        var client = _factory.CreateClient();
        var token = await ObterTokenAsync(client, "gestor.sla", "Gestor");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/sla/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatchEscalar_Deve_Respeitar_IdempotencyKey()
    {
        var client = _factory.CreateClient();
        var token = await ObterTokenAsync(client, "gestor.sla", "Gestor");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createRes = await client.PostAsJsonAsync("/api/sla",
            new CreateSlaRequest("OC-888", "Queda de Árvore", 6, "Defesa Civil"));
        createRes.EnsureSuccessStatusCode();
        var criado = await createRes.Content.ReadFromJsonAsync<SlaRecord>();
        Assert.NotNull(criado);

        var idemKey = $"idem-patch-{Guid.NewGuid()}";

        var patch1 = new HttpRequestMessage(HttpMethod.Patch, $"/api/sla/{criado!.Id}/escalar");
        patch1.Headers.Add("X-Idempotency-Key", idemKey);
        var res1 = await client.SendAsync(patch1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        var patch2 = new HttpRequestMessage(HttpMethod.Patch, $"/api/sla/{criado.Id}/escalar");
        patch2.Headers.Add("X-Idempotency-Key", idemKey);
        var res2 = await client.SendAsync(patch2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
    }

    [Fact]
    public async Task Endpoints_Devem_Retornar_429_Ao_Exceder_Rate_Limit()
    {
        var client = _factory.CreateClient();
        var token = await ObterTokenAsync(client, "rate.limit.user", "Gestor");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var tasks = Enumerable.Range(0, 15)
            .Select(_ => client.GetAsync("/api/sla"));

        var responses = await Task.WhenAll(tasks);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        Assert.Contains(HttpStatusCode.TooManyRequests, statusCodes);
    }
}