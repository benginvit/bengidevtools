namespace BengiDevTools.Models;

public enum TestCaseStepType { TestfallData, Sql, Swagger, Sleep, SqlForeach, SqlPoll }

public class TestCaseStep
{
    public TestCaseStepType Type         { get; set; }
    public string           Label        { get; set; } = "";
    public int              DataSetId    { get; set; }
    public string           SqlScript    { get; set; } = "";
    public string           HttpMethod            { get; set; } = "GET";
    public string           Url                   { get; set; } = "";
    public string           Body                  { get; set; } = "";
    public string           ContentType           { get; set; } = "application/json";
    public bool             UseDefaultCredentials { get; set; } = true;
    public int              SleepSeconds         { get; set; } = 2;
    public string           PollCriteria         { get; set; } = "RowCountGte";
    public string           PollCriteriaColumn   { get; set; } = "";
    public string           PollCriteriaValue    { get; set; } = "1";
    public int              PollTimeoutSeconds   { get; set; } = 30;
}

public class TestCase
{
    public int                DataSetId   { get; set; }
    public string             Beskrivning { get; set; } = "";
    public string             Tag         { get; set; } = "";
    public List<TestCaseStep> Steps       { get; set; } = [];
}
