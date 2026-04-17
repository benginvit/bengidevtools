using BengiDevTools.Models;

namespace BengiDevTools.Services;

public interface ITestDataService
{
    IReadOnlyList<TestDataRow> Rows { get; }
    void Load();
    void Save();
    void Add(TestDataRow row);
    void Remove(TestDataRow row);
    void Replace(TestDataRow old, TestDataRow updated);
    void Clear();
    string GenerateSql(IEnumerable<int> dataSetIds);
    string ExportCsv(IEnumerable<TestDataRow> rows);
    List<TestDataRow> ImportCsv(string csv);
}
