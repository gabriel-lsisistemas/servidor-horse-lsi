-- Acelera a checagem de pedido ja gravado (ID_PEDIDO_APP) no Click / Click Free.
-- Rode no GESTOR e no GESTOR_FREE se as bases forem diferentes.

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_PEDIDO_CABECALHO_ID_PEDIDO_APP'
      AND object_id = OBJECT_ID('dbo.PEDIDO_CABECALHO')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_PEDIDO_CABECALHO_ID_PEDIDO_APP
    ON dbo.PEDIDO_CABECALHO (ID_PEDIDO_APP)
    WHERE ID_PEDIDO_APP IS NOT NULL AND ID_PEDIDO_APP > 0;
END
