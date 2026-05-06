-- Tar bort testdata kopplad till det konfigurerade DataSetId
DELETE FROM dbo.Inbetalning         WHERE Betalreferens LIKE 'TEST-%'
DELETE FROM dbo.Fordran             WHERE Referens      LIKE 'TEST-%'
DELETE FROM dbo.Subjekt             WHERE Metadata      LIKE '%"test":true%'
