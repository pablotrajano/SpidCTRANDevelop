-- Este script retroativamente preenche as métricas de aprovação na tabela ConferenciasMensais
-- e atribui as viagens correspondentes ao gestor que realizou a conferência daquele mês.

BEGIN TRANSACTION;

-- 1. Atribuir o usuário que confirmou o mês como o conferente das viagens daquele mês
UPDATE v
SET v.ConferidoPorUsuarioId = c.UsuarioId
FROM [Viagens] v
INNER JOIN [ConferenciasMensais] c ON v.CentroCustoId = c.CentroCustoId
    AND YEAR(v.DataViagem) = c.Ano
    AND MONTH(v.DataViagem) = c.Mes
WHERE v.StatusConferenciaGestor IN ('OK', 'Contestada', 'Considerado')
  AND v.ConferidoPorUsuarioId IS NULL;

-- 2. Recalcular as métricas de viagens "OK" para todas as conferências que estão com 0
UPDATE c
SET QtdViagensOk = (
    SELECT COUNT(*)
    FROM [Viagens] v
    WHERE v.CentroCustoId = c.CentroCustoId
      AND YEAR(v.DataViagem) = c.Ano
      AND MONTH(v.DataViagem) = c.Mes
      AND v.StatusConferenciaGestor IN ('OK', 'Considerado')
)
FROM [ConferenciasMensais] c
WHERE c.QtdViagensOk = 0;

-- 3. Recalcular as métricas de viagens "Contestadas" para todas as conferências que estão com 0
UPDATE c
SET QtdViagensContestadas = (
    SELECT COUNT(*)
    FROM [Viagens] v
    WHERE v.CentroCustoId = c.CentroCustoId
      AND YEAR(v.DataViagem) = c.Ano
      AND MONTH(v.DataViagem) = c.Mes
      AND v.StatusConferenciaGestor = 'Contestada'
)
FROM [ConferenciasMensais] c
WHERE c.QtdViagensContestadas = 0;

COMMIT TRANSACTION;
