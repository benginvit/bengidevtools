namespace BengiDevTools.Models;

public class TestCase
{
    public int         DataSetId   { get; set; }
    public string      Beskrivning { get; set; } = "";
    public string      Tag         { get; set; } = "";
    public string      Sql         { get; set; } = "";
    public List<int>   DataRows    { get; set; } = [];
}
