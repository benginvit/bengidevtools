-- Nollar saldo på alla testfordringar
UPDATE dbo.Fordran
SET    Saldo = 0,
       Grundbelopp = 0
WHERE  Referens LIKE 'TEST-%'
