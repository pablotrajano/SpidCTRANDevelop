namespace Spid.Data;

/// <summary>
/// Registro de confirmação mensal de um gestor.
/// Indica que ele verificou todas as viagens do seu centro de custo naquele mês/ano.
/// </summary>
public class ConferenciaMensal
{
    public int Id { get; set; }

    public int Ano { get; set; }
    public int Mes { get; set; }

    // Centro de Custo cujas viagens foram conferidas
    public int CentroCustoId { get; set; }
    public CentroCusto CentroCusto { get; set; } = null!;

    // Gestor que confirmou
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;

    public DateTime DataConfirmacao { get; set; }

    public int QtdViagensOk { get; set; }
    public int QtdViagensContestadas { get; set; }
    public int QtdViagensOutroGestor { get; set; }
}
