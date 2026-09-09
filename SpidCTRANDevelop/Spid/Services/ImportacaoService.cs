using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Spid.Data;
using System.Globalization;

namespace Spid.Services;

public class ImportacaoResult
{
    public int Importadas { get; set; }
    public int Ignoradas { get; set; } // duplicatas
    public int Atualizadas { get; set; } // antigas atualizadas com novos dados
    public List<string> Erros { get; set; } = new();
}

public class ImportacaoService
{
    private readonly AppDbContext _db;

    public ImportacaoService(AppDbContext db)
    {
        _db = db;
    }

    // -------------------------------------------------------------------------
    // ALIASES DE CENTROS DE CUSTO
    // Mapeamento temporário de nomes errados (vindos da planilha do fornecedor)
    // para os nomes corretos cadastrados no sistema.
    // Remover este bloco quando o fornecedor corrigir os nomes na planilha.
    // -------------------------------------------------------------------------
    private static readonly Dictionary<string, string> _aliasesCentroCusto =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["SIELE/COENG/DETEC"] = "SIND/COENG/DETEC",
        // Adicionar novos aliases aqui se necessário:
        // ["NOME_ERRADO"] = "NOME_CORRETO",
    };

    private static string NormalizarNomeCentroCusto(string nome)
    {
        return _aliasesCentroCusto.TryGetValue(nome, out var correto) ? correto : nome;
    }
    // -------------------------------------------------------------------------

    public async Task<ImportacaoResult> ImportarExcelAsync(Stream stream, int usuarioId)
    {
        var result = new ImportacaoResult();

        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();

        // Mapeamento dinâmico de colunas pelo cabeçalho
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerRow = ws.Row(1);
        int colCount = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (int i = 1; i <= colCount; i++)
        {
            var headerName = headerRow.Cell(i).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(headerName))
            {
                headerMap[headerName] = i;
            }
        }

        int GetColIndex(int fallback, params string[] possibleNames)
        {
            foreach(var name in possibleNames)
            {
                if (headerMap.TryGetValue(name, out int idx)) return idx;
            }
            return fallback;
        }

        int colData = GetColIndex(1, "Data da Viagem");
        int colNomeColab = GetColIndex(2, "Nome Colaborador");
        int colCpf = GetColIndex(3, "CPF/CPNPJ", "CPF/CNPJ", "CPF");
        int colCentroCusto = GetColIndex(4, "Centro de Custo");
        int colOrigem = GetColIndex(5, "Origem");
        int colDestino = GetColIndex(6, "Destino");
        int colParceiro = GetColIndex(7, "Parceiro");
        int colValorCotado = GetColIndex(8, "Valor Cotado");
        int colValorFinal = GetColIndex(9, "Valor Final");
        int colStatus = GetColIndex(10, "Status");
        int colHoraSolicitacao = GetColIndex(-1, "Solicitação da Viagem");
        int colHoraInicio = GetColIndex(11, "Início da Viagem");
        int colHoraFim = GetColIndex(12, "Fim da Viagem");
        int colDistancia = GetColIndex(13, "Distância (KM)", "Distância");
        int colAvaliacao = GetColIndex(14, "Avaliação da Viagem");
        int colMotivo = GetColIndex(15, "Motivo da Viagem");
        int colIdViagem = GetColIndex(16, "ID Viagem Parceiro");

        // Pegar IDs de viagens já existentes para deduplicação e saber se possuem HoraSolicitacao
        var viagensExistentesInfo = await _db.Viagens
            .Select(v => new { v.IdViagemParceiro, TemHora = v.HoraSolicitacao != null })
            .ToDictionaryAsync(v => v.IdViagemParceiro, v => v.TemHora, StringComparer.OrdinalIgnoreCase);

        // Cache de entidades para evitar consultas repetidas
        var centrosCustoCache = await _db.CentrosCusto.ToDictionaryAsync(s => s.Nome);
        var parceirosCache = await _db.Parceiros.ToDictionaryAsync(p => p.Nome);
        var colaboradoresCache = await _db.Colaboradores.ToDictionaryAsync(c => c.Cpf);

        // Percorrer linhas (pular cabeçalho na linha 1)
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        // Desabilitar detecção automática de mudanças para performance em bulk
        _db.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            // Primeira passada: resolver/criar entidades auxiliares
            var novosCentrosCusto = new List<CentroCusto>();
            var novosParceiros = new List<ParceiroViagem>();
            var novosColaboradores = new List<(int row, string cpf, string nome, CentroCusto centroCusto)>();

            for (int row = 2; row <= lastRow; row++)
            {
                try
                {
                    var idViagemParceiro = ws.Cell(row, colIdViagem).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(idViagemParceiro) || viagensExistentesInfo.ContainsKey(idViagemParceiro))
                        continue;

                    var centroCusto = NormalizarNomeCentroCusto(
                        ws.Cell(row, colCentroCusto).GetString().Trim());
                    var parceiroNome = ws.Cell(row, colParceiro).GetString().Trim();
                    var cpf = ws.Cell(row, colCpf).GetString().Trim();
                    var nomeColab = ws.Cell(row, colNomeColab).GetString().Trim();

                    if (!centrosCustoCache.ContainsKey(centroCusto))
                    {
                        var objCentroCusto = new CentroCusto { Nome = centroCusto };
                        novosCentrosCusto.Add(objCentroCusto);
                        centrosCustoCache[centroCusto] = objCentroCusto;
                    }

                    if (!parceirosCache.ContainsKey(parceiroNome))
                    {
                        var parceiro = new ParceiroViagem { Nome = parceiroNome };
                        novosParceiros.Add(parceiro);
                        parceirosCache[parceiroNome] = parceiro;
                    }

                    if (!colaboradoresCache.ContainsKey(cpf))
                    {
                        novosColaboradores.Add((row, cpf, nomeColab, centrosCustoCache[centroCusto]));
                        colaboradoresCache[cpf] = null!; // placeholder
                    }
                }
                catch (Exception ex)
                {
                    result.Erros.Add($"Linha {row} (pré-processamento): {ex.Message}");
                }
            }

            // Salvar entidades auxiliares em batch
            if (novosCentrosCusto.Count > 0)
            {
                _db.CentrosCusto.AddRange(novosCentrosCusto);
                await _db.SaveChangesAsync();
            }

            if (novosParceiros.Count > 0)
            {
                _db.Parceiros.AddRange(novosParceiros);
                await _db.SaveChangesAsync();
            }

            // Criar colaboradores (precisam do CentroCustoId já persistido)
            if (novosColaboradores.Count > 0)
            {
                var colabEntities = novosColaboradores.Select(c => new Colaborador
                {
                    Nome = c.nome,
                    Cpf = c.cpf,
                    CentroCustoId = c.centroCusto.Id
                }).ToList();

                _db.Colaboradores.AddRange(colabEntities);
                await _db.SaveChangesAsync();

                foreach (var colab in colabEntities)
                    colaboradoresCache[colab.Cpf] = colab;
            }

            // Segunda passada: criar viagens em lotes
            const int batchSize = 500;
            int pendingCount = 0;

            for (int row = 2; row <= lastRow; row++)
            {
                try
                {
                    var idViagemParceiro = ws.Cell(row, colIdViagem).GetString().Trim();

                    if (string.IsNullOrWhiteSpace(idViagemParceiro))
                    {
                        continue;
                    }

                    if (viagensExistentesInfo.TryGetValue(idViagemParceiro, out bool temHoraSolicitacao))
                    {
                        IXLCell? horaCell = colHoraSolicitacao != -1 ? ws.Cell(row, colHoraSolicitacao) : null;
                        if (!temHoraSolicitacao && horaCell != null && !string.IsNullOrWhiteSpace(horaCell.GetString()))
                        {
                            var viagemExistente = await _db.Viagens.FirstOrDefaultAsync(v => v.IdViagemParceiro == idViagemParceiro);
                            if (viagemExistente != null)
                            {
                                viagemExistente.HoraSolicitacao = ParseTimeOnlySafe(horaCell);
                                _db.Viagens.Update(viagemExistente);
                                result.Atualizadas++;
                                pendingCount++;
                                viagensExistentesInfo[idViagemParceiro] = true;
                            }
                        }
                        else
                        {
                            result.Ignoradas++;
                        }

                        if (pendingCount >= batchSize)
                        {
                            await _db.SaveChangesAsync();
                            pendingCount = 0;
                        }
                        continue;
                    }

                    var dataCell = ws.Cell(row, colData);
                    var cpf = ws.Cell(row, colCpf).GetString().Trim();
                    var centroCusto = NormalizarNomeCentroCusto(
                        ws.Cell(row, colCentroCusto).GetString().Trim());
                    var origem = ws.Cell(row, colOrigem).GetString().Trim();
                    var destino = ws.Cell(row, colDestino).GetString().Trim();
                    var parceiroNome = ws.Cell(row, colParceiro).GetString().Trim();
                    var valorCotadoCell = ws.Cell(row, colValorCotado);
                    var valorFinalCell = ws.Cell(row, colValorFinal);
                    var statusOrigem = ws.Cell(row, colStatus).GetString().Trim();
                    
                    IXLCell? horaSolicitacaoCell = colHoraSolicitacao != -1 ? ws.Cell(row, colHoraSolicitacao) : null;
                    
                    var horaInicioCell = ws.Cell(row, colHoraInicio);
                    var horaFimCell = ws.Cell(row, colHoraFim);
                    var distanciaCell = ws.Cell(row, colDistancia);
                    var avaliacaoStr = ws.Cell(row, colAvaliacao).GetString().Trim();
                    var motivo = ws.Cell(row, colMotivo).GetString().Trim();

                    var centroCustoObj = centrosCustoCache[centroCusto];
                    var parceiro = parceirosCache[parceiroNome];
                    var colaborador = colaboradoresCache[cpf];

                    var dataViagem = ParseDateSafe(dataCell);
                    var valorCotado = ParseDecimalSafe(valorCotadoCell);
                    var valorFinal = ParseDecimalSafe(valorFinalCell);
                    
                    TimeOnly? horaSolicitacao = null;
                    if (horaSolicitacaoCell != null && !string.IsNullOrWhiteSpace(horaSolicitacaoCell.GetString()))
                    {
                        horaSolicitacao = ParseTimeOnlySafe(horaSolicitacaoCell);
                    }
                    
                    var horaInicio = ParseTimeOnlySafe(horaInicioCell);
                    var horaFim = ParseTimeOnlySafe(horaFimCell);
                    var distancia = (double)ParseDecimalSafe(distanciaCell);
                    var avaliacao = int.TryParse(avaliacaoStr, out var av) ? av : 0;

                    var viagem = new Viagem
                    {
                        DataViagem = dataViagem,
                        Origem = origem,
                        Destino = destino,
                        ValorCotado = valorCotado,
                        ValorFinal = valorFinal,
                        StatusOrigem = statusOrigem,
                        HoraSolicitacao = horaSolicitacao,
                        HoraInicio = horaInicio,
                        HoraFim = horaFim,
                        DistanciaKm = distancia,
                        Avaliacao = avaliacao,
                        Motivo = motivo,
                        IdViagemParceiro = idViagemParceiro,
                        ColaboradorId = colaborador.Id,
                        CentroCustoId = centroCustoObj.Id,
                        ParceiroViagemId = parceiro.Id,
                        StatusConferenciaGestor = "Pendente"
                    };

                    _db.Viagens.Add(viagem);
                    viagensExistentesInfo[idViagemParceiro] = viagem.HoraSolicitacao.HasValue;
                    result.Importadas++;
                    pendingCount++;

                    if (pendingCount >= batchSize)
                    {
                        await _db.SaveChangesAsync();
                        pendingCount = 0;
                    }
                }
                catch (Exception ex)
                {
                    result.Erros.Add($"Linha {row}: {ex.Message}");
                }
            }

            // Salvar viagens restantes
            if (pendingCount > 0)
                await _db.SaveChangesAsync();

            if (result.Importadas > 0)
            {
                var log = new ImportacaoLog
                {
                    DataImportacao = DateTime.Now,
                    UsuarioId = usuarioId,
                    QuantidadeImportada = result.Importadas
                };
                _db.ImportacoesLog.Add(log);
                await _db.SaveChangesAsync();
            }
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = true;
        }

        return result;
    }

    private static DateOnly ParseDateSafe(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime)
        {
            return DateOnly.FromDateTime(cell.GetDateTime());
        }

        var str = cell.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(str)) return default;

        if (DateTime.TryParseExact(str, new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd" }, new CultureInfo("pt-BR"), DateTimeStyles.None, out var dt))
        {
            return DateOnly.FromDateTime(dt);
        }

        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtFallback))
        {
            return DateOnly.FromDateTime(dtFallback);
        }

        return default;
    }

    private static decimal ParseDecimalSafe(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Number)
        {
            return cell.GetValue<decimal>();
        }

        var str = cell.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(str)) return 0m;

        // Limpar possíveis símbolos de moeda e espaços
        str = str.Replace("R$", "").Replace("$", "").Trim();

        // Se usar vírgula, assumimos que é formato pt-BR (ex: "1.500,50" ou "12,95")
        if (str.Contains(','))
        {
            str = str.Replace(".", ""); // Remove separadores de milhar
            if (decimal.TryParse(str, NumberStyles.Any, new CultureInfo("pt-BR"), out decimal valBr))
            {
                return valBr;
            }
        }
        else
        {
            // Se não usar vírgula, pode ser Invariant "1500.50" ou apenas inteiro "1500"
            if (decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valInv))
            {
                return valInv;
            }

            // Fallback para pt-BR
            if (decimal.TryParse(str, NumberStyles.Any, new CultureInfo("pt-BR"), out decimal valPt))
            {
                return valPt;
            }
        }

        return 0m;
    }

    private static TimeOnly ParseTimeOnlySafe(IXLCell cell)
    {
        if (cell.DataType == XLDataType.TimeSpan)
        {
            var ts = cell.GetTimeSpan();
            // Evitar erro se passar de 24 horas
            return new TimeOnly(Math.Max(0, ts.Hours % 24), Math.Max(0, ts.Minutes), Math.Max(0, ts.Seconds));
        }
        else if (cell.DataType == XLDataType.DateTime)
        {
            return TimeOnly.FromDateTime(cell.GetDateTime());
        }

        var value = cell.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return new TimeOnly(0, 0);

        var parts = value.Split(':');
        if (parts.Length > 0 && int.TryParse(parts[0], out var hour))
        {
            var minute = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : 0;
            return new TimeOnly(Math.Clamp(hour % 24, 0, 23), Math.Clamp(minute % 60, 0, 59));
        }

        return new TimeOnly(0, 0);
    }
}
