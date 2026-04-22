namespace BengiDevTools.Models;

public enum TestfallActionType { Sql, Swagger }

public class TestfallAction
{
    public TestfallActionType Type       { get; set; }
    public string             Label      { get; set; } = "";
    public string             SqlScript  { get; set; } = "";
    public string             Url        { get; set; } = "";
    public string             HttpMethod { get; set; } = "POST";
    public string             Body       { get; set; } = "";
}

public class TestDataRow
{
    public int    DataSetId                         { get; set; }
    public string Databeskrivning                   { get; set; } = "";

    // Subjekt
    public string PersonId                          { get; set; } = "";
    public string SubjektPersonNrOrgNr              { get; set; } = "";
    public string SubjektNamn                       { get; set; } = "";
    public string SubjektAdress                     { get; set; } = "";
    public string SubjektCoAdress                   { get; set; } = "";
    public string SubjektPostnummer                 { get; set; } = "";
    public string SubjektPostort                    { get; set; } = "";
    public string SubjektLand                       { get; set; } = "";
    public string SubjektUrsprung                   { get; set; } = "";
    public string Subjekttyp                        { get; set; } = "";
    public string SubjektMetadata                   { get; set; } = "";

    // Inbetalare
    public string InbetalarePersonOrgNr             { get; set; } = "";
    public string InbetalareNamn                    { get; set; } = "";
    public string InbetalareAdress1                 { get; set; } = "";
    public string InbetalareAdress2                 { get; set; } = "";
    public string InbetalarePostnummer              { get; set; } = "";
    public string InbetalareOrt                     { get; set; } = "";
    public string InbetalareLand                    { get; set; } = "";

    // Inbetalning / Fordran
    public string InbetalningsspecifikationReferens { get; set; } = "";
    public string FordranReferens                   { get; set; } = "";
    public string InbetalningBetalreferens          { get; set; } = "";
    public string FordranBetalreferens              { get; set; } = "";
    public string Inbetalningskanal                 { get; set; } = "";
    public string FordranUppbordsomrade             { get; set; } = "";
    public string InbetalningKontogrupp             { get; set; } = "";
    public string FordranSaldo                      { get; set; } = "";
    public string FordranGrundbelopp               { get; set; } = "";
    public string InbetalningBelopp                 { get; set; } = "";
    public string FordranStatus                     { get; set; } = "";

    // Igun
    public string IgunSignalPost                    { get; set; } = "";
    public string IgunSignalResultat                { get; set; } = "";

    public List<TestfallAction> Actions             { get; set; } = [];
}
