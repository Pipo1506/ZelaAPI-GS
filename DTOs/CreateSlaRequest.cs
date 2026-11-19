using System.ComponentModel.DataAnnotations;

namespace Zela.SLAApi.DTOs;

public record CreateSlaRequest(
    [property: Required(ErrorMessage = "OcorrenciaId é obrigatório.")]
    [property: StringLength(100, MinimumLength = 2, ErrorMessage = "OcorrenciaId deve ter entre 2 e 100 caracteres.")]
    string OcorrenciaId,

    [property: Required(ErrorMessage = "Categoria é obrigatória.")]
    [property: StringLength(80, MinimumLength = 2, ErrorMessage = "Categoria deve ter entre 2 e 80 caracteres.")]
    string Categoria,

    [property: Range(1, 720, ErrorMessage = "PrazoHoras deve estar entre 1 e 720 horas.")]
    int PrazoHoras,

    [property: Required(ErrorMessage = "ExecutorResponsavel é obrigatório.")]
    [property: StringLength(120, MinimumLength = 2, ErrorMessage = "ExecutorResponsavel deve ter entre 2 e 120 caracteres.")]
    string ExecutorResponsavel
);