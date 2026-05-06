namespace BengiDevTools.Models;

public class Scenario
{
    public string Id     { get; set; } = Guid.NewGuid().ToString();
    public string Name   { get; set; } = "";
    public string AppId  { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string Url    { get; set; } = "";
    public string Body   { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();
}
