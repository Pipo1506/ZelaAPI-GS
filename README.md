
# Zela — SLA API (`Zela.SLAApi`)

API REST desenvolvida em **.NET 10 (ASP.NET Core Minimal APIs)** para gerenciamento, monitoramento e escalonamento de **Acordos de Nível de Serviço (SLAs)** de ocorrências urbanas, garantindo rastreabilidade de prazos, resiliência operacional e controle de acesso por perfil.

---

## Tecnologias Utilizadas

- **Framework:** .NET 10 (`ASP.NET Core Minimal APIs`)
- **Linguagem:** C#
- **ORM:** Entity Framework Core 10
- **Banco de Dados:** Oracle Database (`Oracle.EntityFrameworkCore`)
- **Autenticação e Autorização:** JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`)
- **Documentação:** Swagger / OpenAPI (`Swashbuckle.AspNetCore`)
- **Resiliência e Observabilidade:** Rate Limiting nativo (`AddRateLimiter`), Idempotência (`X-Idempotency-Key`), `ProblemDetails` e Health Checks (`AddHealthChecks`)
- **Testes Automatizados:** xUnit, `WebApplicationFactory` e `EntityFrameworkCore.InMemory`

---

## Estrutura do Domínio

O domínio da aplicação está estruturado nas seguintes entidades e contratos de entrada (`Models` e `DTOs`):

- **`SlaRecord`:** Entidade principal que armazena o identificador da ocorrência (`OcorrenciaId`), categoria (`Categoria`), prazo estipulado em horas (`PrazoHoras`), equipe/executor responsável (`ExecutorResponsavel`), timestamps de abertura e limite (`DataAbertura`, `DataLimite`) e status atual (`Pendente`, `Escalado`).
- **`IdempotencyRequest`:** Registro de controle de idempotência vinculado à chave `X-Idempotency-Key`, armazenando o `StatusCode` e o corpo de resposta para evitar duplicidade em reenvios de requisições críticas.
- **`LoginRequest`:** DTO utilizado para emissão de tokens JWT a partir de `Usuario` e `Papel` (`Gestor`, `Supervisor`, `Cidadao`).
- **`CreateSlaRequest`:** DTO validado via `DataAnnotations` para abertura de novos registros de SLA.

---

## Como Executar o Projeto Localmente

Siga as instruções abaixo para clonar, configurar e rodar a API em sua máquina.

### Pré-requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download) instalado
- Acesso a uma instância **Oracle Database** (opcional para consulta de `/health` e execução dos testes automatizados em memória)
- Git

### Passo a passo

1. **Clone o repositório e acesse a pasta do projeto:**
   ```bash
   git clone https://github.com/Pipo1506/ZelaAPI-GS.git
   cd Zela.SLAApi

```

2. **Configure a conexão com o Oracle e as chaves JWT:**
No arquivo `appsettings.Development.json` (ou via variáveis de ambiente), configure:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=<HOST>:<PORTA>/<SERVICO>;User Id=<USUARIO>;Password=<SENHA>;"
  },
  "Jwt": {
    "Key": "ZelaSlaApiSuperSecretKey2026SecureTokenValidation",
    "Issuer": "Zela.SLAApi",
    "Audience": "Zela.CidadaoGestor"
  }
}

```


3. **Restaure as dependências e execute a API:**
```bash
dotnet restore
dotnet run

```


4. **Acesse a documentação interativa (Swagger UI) e o Health Check:**
Com a aplicação em execução em modo `Development`, abra no navegador:
```text
http://localhost:5241/swagger
http://localhost:5241/health

```


> **Observação sobre o Safari:** Ao acessar o Swagger UI ou os endpoints de Health Check localmente, o **Safari** pode não conseguir conectar ao endereço `localhost`. Caso isso aconteça, utilize outro navegador — como o **Google Chrome**, utilizado nos testes deste projeto.

<img width="763" height="278" alt="Captura de Tela 2026-11-19 às 13 52 12" src="https://github.com/user-attachments/assets/213aeb36-3de5-4dee-a04d-4ea3701d5b46" />

<img width="1468" height="401" alt="Captura de Tela 2026-11-19 às 13 53 06" src="https://github.com/user-attachments/assets/07ccc2bf-b97d-4629-8565-8b3fffb1b9a6" />

---

## Documentação dos Endpoints

### Autenticação (`JWT Bearer`)

* **`POST /api/auth/login`** — Gera um token JWT válido por 2 horas contendo as claims de nome (`ClaimTypes.Name`) e perfil (`ClaimTypes.Role`).

### Gestão de SLAs (`/api/sla`)

Todos os endpoints abaixo exigem autenticação via header `Authorization: Bearer <seu_token>`:

* **`GET /api/sla`** — Lista todos os registros de SLA cadastrados.
* **`GET /api/sla/{id:int}`** — Consulta os detalhes de um SLA específico por ID (retorna `404 Not Found` caso não exista).
* **`POST /api/sla`** — Cria um novo registro de SLA (`Status = "Pendente"` e cálculo automático de `DataLimite = DataAbertura + PrazoHoras`). Suporta o header opcional `X-Idempotency-Key` para evitar duplicação de registros.
* **`PATCH /api/sla/{id:int}/escalar`** — Atualiza o status do SLA para `"Escalado"`. Exige perfil **`Gestor`** ou **`Supervisor`** (`403 Forbidden` para outros perfis) e suporta idempotência via `X-Idempotency-Key`.

<img width="1450" height="369" alt="Captura de Tela 2026-11-19 às 13 29 57" src="https://github.com/user-attachments/assets/919af2d7-c2f9-4e44-b4f5-8989b5b42281" />

---

## Segurança, Resiliência e Governança

* **Controle de Acesso Baseado em Perfis (RBAC):** Separação de permissões via claims JWT, restringindo operações críticas de escalonamento aos perfis `Gestor` e `Supervisor`.
* **Idempotência (`X-Idempotency-Key`):** Garante que retentativas de rede nos endpoints `POST /api/sla` e `PATCH /api/sla/{id}/escalar` não criem registros duplicados nem processem a operação duas vezes.
* **Rate Limiting (`HTTP 429`):** Política global de janela fixa (`10` requisições a cada `10` segundos por usuário/host), protegendo a API contra sobrecarga e abuso.
* **Padronização de Erros (`ProblemDetails`):** Respostas de erro estruturadas seguindo a RFC 7807.

---

## Health Checks e Monitoramento da Aplicação

A API expõe endpoints dedicados de observabilidade para monitoramento de disponibilidade e dependências:

* **`GET /health` (`live`):** Verifica a disponibilidade imediata da aplicação (`API_Liveness`), respondendo `Healthy` (`200 OK`) independentemente de dependências externas.
* **`GET /health/ready` (`ready`):** Verifica a conectividade e prontidão do banco de dados Oracle (`Database_Health_Check`).

---

## Testes e Qualidade

A solução conta com uma suíte de **11 testes automatizados** (unitários e de integração) implementados com **xUnit** e **`WebApplicationFactory`**, utilizando banco em memória (`InMemoryDatabase`) para execução isolada e determinística:

1. Validação de falha no `CreateSlaRequest` quando `PrazoHoras` é inválido (`<= 0`).
2. Cálculo exato de `DataLimite` (`DataAbertura + PrazoHoras`) no `SlaRecord`.
3. Emissão de token JWT válido em `POST /api/auth/login`.
4. Bloqueio de acesso não autenticado (`401 Unauthorized`) em `GET /api/sla`.
5. Criação de SLA (`201 Created`) e garantia de não duplicação com `X-Idempotency-Key` em `POST /api/sla`.
6. Bloqueio por perfil sem permissão (`403 Forbidden`) ao tentar escalar SLA com papel `Cidadao`.
7. Escalonamento bem-sucedido (`200 OK`, status `"Escalado"`) com perfil `Gestor`.
8. Retorno de erro de validação (`400 Bad Request`) para payload inválido em `POST /api/sla`.
9. Retorno de `404 Not Found` ao consultar SLA inexistente em `GET /api/sla/{id}`.
10. Respeito à chave `X-Idempotency-Key` em chamadas repetidas de `PATCH /api/sla/{id}/escalar`.
11. Disparo de `429 Too Many Requests` ao exceder o limite configurado no Rate Limiter.

Para executar toda a suíte de testes:

```bash
dotnet test --verbosity normal

```
<img width="694" height="57" alt="Captura de Tela 2026-11-19 às 13 46 32" src="https://github.com/user-attachments/assets/92331c6f-b0d7-48c7-9b09-11bc5493c31f" />


---

## Uso de IA Generativa

Em conformidade com as diretrizes da **Global Solution 2026/2 · CITIZENTECH (FIAP)**, declaramos o uso de assistente de IA generativa (**Gemini**) como apoio técnico durante o desenvolvimento, sempre acompanhado de revisão, validação local e compreensão de todo o código pela equipe:

* **Simplificação de soluções:** Estruturação de testes de integração com `WebApplicationFactory` e `InMemoryDatabase`, organização dos endpoints Minimal API e padronização da estrutura desta documentação (`README.md`).
* **Correções de bugs:** Diagnóstico e resolução de falhas de build e execução no `.NET 10` (como conflitos de provedores do Entity Framework Core em testes, erros `MSB3030`/`MSB3539`, falhas de inicialização em runtime com pacotes de UI incompatíveis e ajuste de concorrência para validar o `HTTP 429` no Rate Limiter).
* **Sugestões de melhoria:** Separação dos Health Checks por tags (`live` em `/health` e `ready` em `/health/ready`) para desacoplar a checagem de disponibilidade da API da prontidão do Oracle, além da ampliação da cobertura de cenários de teste (`400`, `401`, `403`, `404`, `429` e idempotência).

---

## Autores

Desenvolvido para a **Global Solution 2026 — FIAP (Análise e Desenvolvimento de Sistemas)**:

* **Phillipo da Silva Barbosa** — RM565399
* **Eduardo Martins** — RM562259
* **João Victor Alcântra** — RM562707
* **Leonardo Aragaki Rodrigues** — RM562944

