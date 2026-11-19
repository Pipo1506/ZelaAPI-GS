namespace Zela.SLAApi.Models;

public class SlaRecord
{
    public int Id { get; set; }
    public string OcorrenciaId { get; set; } = string.Empty; 
    public string Categoria { get; set; } = string.Empty;     
    public int PrazoHoras { get; set; }                       
    public DateTime DataAbertura { get; set; } = DateTime.UtcNow;
    public DateTime DataLimite { get; set; }                 
    public string Status { get; set; } = "Pendente";         
    public string ExecutorResponsavel { get; set; } = string.Empty;
}