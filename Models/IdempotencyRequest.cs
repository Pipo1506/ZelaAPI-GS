namespace Zela.SLAApi.Models;

public class IdempotencyRequest
{
    public string Id { get; set; } = string.Empty; // Chave de idempotência (Header X-Idempotency-Key)
    public string ResponseBody { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}