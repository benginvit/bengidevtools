-- Sätter upp en inbetalning kopplad till testdataraden
DECLARE @DataSetId INT = 1  -- ersätts av testramverket

INSERT INTO dbo.Inbetalning (Betalreferens, Belopp, Kanal, Datum)
VALUES ('TEST-' + CAST(@DataSetId AS VARCHAR), 1000.00, 1, GETDATE())
GO
