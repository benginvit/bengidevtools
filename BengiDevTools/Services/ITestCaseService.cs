using BengiDevTools.Models;

namespace BengiDevTools.Services;

public interface ITestCaseService
{
    IReadOnlyList<TestCase> Cases { get; }
    void Load();
    void Save();
    void Add(TestCase tc);
    void Remove(TestCase tc);
    void Replace(TestCase old, TestCase updated);
    Task RunAsync(IEnumerable<TestCase> cases, string connectionString, Action<string> progress, CancellationToken ct = default);
    string ExportSql(IEnumerable<TestCase> cases);
}
