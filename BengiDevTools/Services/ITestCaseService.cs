using BengiDevTools.Models;

namespace BengiDevTools.Services;

public interface ITestCaseService
{
    IReadOnlyList<TestCase> Cases { get; }
    void Load();
    void Save();
    void Add(TestCase tc);
    void AddRange(IEnumerable<TestCase> cases);
    void Remove(TestCase tc);
    void Replace(TestCase old, TestCase updated);
    Task RunAsync(IEnumerable<TestCase> cases, string connectionString, Action<string> progress, Func<TestCaseStep, CancellationToken, Task>? onBreakpoint = null, Action<TestCase, int>? onStep = null, CancellationToken ct = default);
    Task<(List<(List<string> Cols, List<Dictionary<string, string>> Rows)> Results, string? Error)> QueryAsync(string sql, string connectionString, CancellationToken ct = default);
    string ExportSql(IEnumerable<TestCase> cases);
}
