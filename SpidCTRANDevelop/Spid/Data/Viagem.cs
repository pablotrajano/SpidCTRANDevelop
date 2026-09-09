namespace Spid.Data;

public class Viagem
{
    public int Id { get; set; }

    public DateOnly DataViagem { get; set; }
    public string Origem { get; set; } = null!;
    public string Destino { get; set; } = null!;

    public decimal ValorCotado { get; set; }
    public decimal ValorFinal { get; set; }
    public string StatusOrigem { get; set; } = null!;

    public TimeOnly? HoraSolicitacao { get; set; }
    public TimeOnly HoraInicio { get; set; }
    public TimeOnly HoraFim { get; set; }
    public double DistanciaKm { get; set; }
    public int Avaliacao { get; set; }
    public string Motivo { get; set; } = null!;
    public string IdViagemParceiro { get; set; } = null!;

    public int ColaboradorId { get; set; }
    public Colaborador Colaborador { get; set; } = null!;

    public int CentroCustoId { get; set; }
    public CentroCusto CentroCusto { get; set; } = null!;

    public int ParceiroViagemId { get; set; }
    public ParceiroViagem Parceiro { get; set; } = null!;

    // Conferência do gestor
    public string StatusConferenciaGestor { get; set; } = "Pendente"; // Pendente/OK/Contestada

    public DateTime? DataConferencia { get; set; }
    public string? MotivoContestacao { get; set; }

    public int? ConferidoPorUsuarioId { get; set; }
    public Usuario? ConferidoPorUsuario { get; set; }
}