-- Totais Horse (lsidemo) no mesmo criterio do replicador.
-- 1 = aberto, 2 = pago. Ano atual em diante.
-- Execute no SQL Server do Click (GESTOR).

DECLARE @anoAtual int = YEAR(GETDATE());
DECLARE @situacaoAberto int = 1;
DECLARE @situacaoPago int = 2;

;WITH contratos_elegiveis AS (
    SELECT DISTINCT p.ID_CONTRATO
    FROM CONTAS_PARCELAS p
    INNER JOIN CONTAS_DETALHE d ON d.ID_CONTAS_PARCELAS = p.ID
    INNER JOIN CONTRATOS c ON c.ID = p.ID_CONTRATO
    WHERE p.ID_CONTRATO IS NOT NULL
      AND ISNULL(p.CANCELADO, 'N') <> 'S'
      AND ISNULL(d.CANCELADO, 'N') <> 'S'
      AND d.ID_SITUACAO_PARCELA = @situacaoAberto
      AND YEAR(ISNULL(d.DATA_VENCIMENTO, p.PRIMEIRO_VENCIMENTO)) >= @anoAtual
),
parcelas AS (
    SELECT
        d.ID,
        d.ID_SITUACAO_PARCELA,
        d.VALOR,
        ISNULL(d.VALOR_PAGO, 0) AS VALOR_PAGO,
        d.ID_ORIGINAL
    FROM CONTAS_DETALHE d
    INNER JOIN CONTAS_PARCELAS p ON p.ID = d.ID_CONTAS_PARCELAS
    WHERE p.ID_CONTRATO IN (SELECT ID_CONTRATO FROM contratos_elegiveis)
      AND ISNULL(p.CANCELADO, 'N') <> 'S'
      AND ISNULL(d.CANCELADO, 'N') <> 'S'
      AND YEAR(ISNULL(d.DATA_VENCIMENTO, p.PRIMEIRO_VENCIMENTO)) >= @anoAtual
      AND (
            d.ID_SITUACAO_PARCELA = @situacaoAberto
            OR (
                YEAR(d.DATA_VENCIMENTO) = @anoAtual
                AND d.ID_SITUACAO_PARCELA = @situacaoPago
                AND EXISTS (
                    SELECT 1
                    FROM CONTAS_DETALHE d2
                    INNER JOIN CONTAS_PARCELAS p2 ON p2.ID = d2.ID_CONTAS_PARCELAS
                    WHERE p2.ID_CONTRATO = p.ID_CONTRATO
                      AND ISNULL(d2.CANCELADO, 'N') <> 'S'
                      AND d2.ID_SITUACAO_PARCELA = @situacaoAberto
                      AND YEAR(d2.DATA_VENCIMENTO) > @anoAtual
                )
            )
      )
)
SELECT
    ID_SITUACAO_PARCELA AS status,
    CASE ID_SITUACAO_PARCELA WHEN 1 THEN 'aberto' WHEN 2 THEN 'pago' ELSE 'outro' END AS situacao,
    COUNT(*) AS qtd,
    SUM(VALOR) AS valor,
    SUM(VALOR_PAGO) AS valor_pago,
    SUM(CASE WHEN ID_ORIGINAL IS NOT NULL THEN 1 ELSE 0 END) AS com_id_cobra
FROM parcelas
GROUP BY ID_SITUACAO_PARCELA
ORDER BY ID_SITUACAO_PARCELA;
