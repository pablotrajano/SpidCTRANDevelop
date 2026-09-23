-- =========================================================================
-- SCRIPT DE CORREÇÃO DAS MÉTRICAS NA TABELA ConferenciasMensais
-- Objetivo: Recalcular QtdViagensOk, QtdViagensContestadas e QtdViagensOutroGestor
--           respeitando a regra por Centro de Custo e as viagens reais do mês.
-- =========================================================================

-- PASSO 1: (OPCIONAL) Visualizar o ANTES vs. DEPOIS sem alterar nada:
/*
SELECT 
    c.Id, 
    cc.Nome AS CentroCusto,
    c.Ano, 
    c.Mes, 
    u.Nome AS ConfirmadoPor,
    c.QtdViagensOk AS Ok_Atual,
    c.QtdViagensOutroGestor AS OutroGestor_Atual,
    (
        SELECT COUNT(*) 
        FROM Viagens v 
        WHERE v.CentroCustoId = c.CentroCustoId 
          AND v.DataViagem >= DATEFROMPARTS(c.Ano, c.Mes, 1) 
          AND v.DataViagem < DATEADD(month, 1, DATEFROMPARTS(c.Ano, c.Mes, 1))
          AND v.StatusConferenciaGestor IN ('OK', 'Considerado')
    ) AS Ok_Novo,
    (
        SELECT COUNT(*) 
        FROM Viagens v 
        INNER JOIN Colaboradores col ON v.ColaboradorId = col.Id
        WHERE v.CentroCustoId = c.CentroCustoId 
          AND v.DataViagem >= DATEFROMPARTS(c.Ano, c.Mes, 1) 
          AND v.DataViagem < DATEADD(month, 1, DATEFROMPARTS(c.Ano, c.Mes, 1))
          AND v.StatusConferenciaGestor IN ('OK', 'Considerado', 'Contestada')
          AND col.Cpf IN (
              SELECT gestor.Cpf 
              FROM Usuarios gestor 
              WHERE gestor.CentroCustoId = c.CentroCustoId 
                AND gestor.Ativo = 1 
                AND gestor.IsTeste = 0 
                AND gestor.Perfil IN ('Gestor Titular', 'Gestor Substituto')
                AND gestor.Id <> c.UsuarioId
          )
    ) AS OutroGestor_Novo
FROM ConferenciasMensais c
INNER JOIN CentrosCusto cc ON c.CentroCustoId = cc.Id
INNER JOIN Usuarios u ON c.UsuarioId = u.Id
ORDER BY c.Id;
*/

-- PASSO 2: EXECUÇÃO DO UPDATE
UPDATE c
SET 
    -- 1. Recalcula viagens OK/Consideradas reais do centro naquele mês
    c.QtdViagensOk = (
        SELECT COUNT(*) 
        FROM Viagens v 
        WHERE v.CentroCustoId = c.CentroCustoId 
          AND v.DataViagem >= DATEFROMPARTS(c.Ano, c.Mes, 1) 
          AND v.DataViagem < DATEADD(month, 1, DATEFROMPARTS(c.Ano, c.Mes, 1))
          AND v.StatusConferenciaGestor IN ('OK', 'Considerado')
    ),

    -- 2. Recalcula viagens contestadas reais do centro naquele mês
    c.QtdViagensContestadas = (
        SELECT COUNT(*) 
        FROM Viagens v 
        WHERE v.CentroCustoId = c.CentroCustoId 
          AND v.DataViagem >= DATEFROMPARTS(c.Ano, c.Mes, 1) 
          AND v.DataViagem < DATEADD(month, 1, DATEFROMPARTS(c.Ano, c.Mes, 1))
          AND v.StatusConferenciaGestor = 'Contestada'
    ),

    -- 3. Recalcula viagens de OUTRO GESTOR:
    --    Passageiro cujo CPF pertence a um dos gestores (Titular ou Substituto) 
    --    desse mesmo Centro de Custo, excluindo quem está confirmando.
    c.QtdViagensOutroGestor = (
        SELECT COUNT(*) 
        FROM Viagens v 
        INNER JOIN Colaboradores col ON v.ColaboradorId = col.Id
        WHERE v.CentroCustoId = c.CentroCustoId 
          AND v.DataViagem >= DATEFROMPARTS(c.Ano, c.Mes, 1) 
          AND v.DataViagem < DATEADD(month, 1, DATEFROMPARTS(c.Ano, c.Mes, 1))
          AND v.StatusConferenciaGestor IN ('OK', 'Considerado', 'Contestada')
          AND col.Cpf IN (
              SELECT gestor.Cpf 
              FROM Usuarios gestor 
              WHERE gestor.CentroCustoId = c.CentroCustoId 
                AND gestor.Ativo = 1 
                AND gestor.IsTeste = 0 
                AND gestor.Perfil IN ('Gestor Titular', 'Gestor Substituto')
                AND gestor.Id <> c.UsuarioId
          )
    )
FROM ConferenciasMensais c;
