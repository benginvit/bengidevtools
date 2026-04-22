namespace BengiDevTools;

public record AppEntry(string Group, string Name, int Port, string RepoKey, string ProjectName);

public static class AppRegistry
{
    public static readonly IReadOnlyDictionary<string, string> RepoMap = new Dictionary<string, string>
    {
        ["support"]          = "USB.Support",
        ["uppbord"]          = "USB.Uppbord",
        ["redovexport"]      = "USB.RedovExport",
        ["redovstaging"]     = "USB.RedovStaging",
        ["sparbarhetslogg"]  = "USB.Sparbarhetslogg",
        ["subjekt"]          = "USB.Subjekt",
        ["uppbordsgui"]      = "USB.UppbordsGui",
        ["uppbordsapi"]      = "USB.UppbordsApi",
        ["audit"]            = "USB.Audit",
        ["avisering"]        = "USB.Avisering",
        ["gda"]              = "USB.GDA",
        ["gdagui"]           = "USB.GDAGui",
        ["mft"]              = "USB.MFT",
        ["mftgui"]           = "USB.MFTGui",
        ["mus"]              = "USB.MUS",
        ["musgui"]           = "USB.MUSGui",
        ["inbet"]            = "USB.Inbet",
        ["gatewayavisering"] = "USB.NSB.Gateway.Avisering",
        ["gatewayklass2"]    = "USB.NSB.Gateway.Klass2",
        ["gatewayklass3"]    = "USB.NSB.Gateway.Klass3",
        ["gatewayredov"]     = "USB.NSB.Gateway.Redov",
        ["gatewaysfh"]       = "USB.NSB.Gateway.SFH",
        ["gatewaysubjekt"]   = "USB.NSB.Gateway.Subjekt",
        ["gatewayaudit"]     = "USB.NSB.Gateway.Audit",
    };

    public static readonly IReadOnlyList<AppEntry> Apps = new List<AppEntry>
    {
        new("Gateways", "NSB.Gateway.Audit",      7217, "gatewayaudit",     "NSB.Gateway.Audit"),
        new("Gateways", "NSB.Gateway.Avisering",  7212, "gatewayavisering", "NSB.Gateway.Avisering"),
        new("Gateways", "NSB.Gateway.Klass2",     7261, "gatewayklass2",    "NSB.Gateway.Klass2"),
        new("Gateways", "NSB.Gateway.Klass3",     7091, "gatewayklass3",    "NSB.Gateway.Klass3"),
        new("Gateways", "NSB.Gateway.Redov",      7140, "gatewayredov",     "NSB.Gateway.Redov"),
        new("Gateways", "NSB.Gateway.SFH",        7111, "gatewaysfh",       "NSB.Gateway.SFH"),
        new("Gateways", "NSB.Gateway.Subjekt",    7191, "gatewaysubjekt",   "NSB.Gateway.Subjekt"),
        new("Support",  "Support.Host",            9021, "support",          "Support.Host"),
        new("Support",  "Support.IGUN",            9031, "support",          "Support.IGUN"),
        new("Tjänster", "USB.Audit",               7281, "audit",            "USB.Audit"),
        new("Tjänster", "USB.Avisering",           7171, "avisering",        "USB.Avisering"),
        new("Tjänster", "USB.GDA",                 7062, "gda",              "USB.GDA"),
        new("Tjänster", "USB.Inbet",               7007, "inbet",            "USB.Inbet"),
        new("Tjänster", "USB.MFT",                 7085, "mft",              "USB.MFT"),
        new("Tjänster", "USB.MUS",                 7115, "mus",              "USB.MUS"),
        new("Tjänster", "USB.RedovExport",         7070, "redovexport",      "USB.RedovExport"),
        new("Tjänster", "USB.RedovStaging",        7163, "redovstaging",     "USB.RedovStaging"),
        new("Tjänster", "USB.Spårbarhetslogg",     7099, "sparbarhetslogg",  "USB.Sparbarhetslogg"),
        new("Tjänster", "USB.Subjekt",             7272, "subjekt",          "USB.Subjekt"),
        new("Tjänster", "USB.Uppbord",             7245, "uppbord",          "USB.Uppbord"),
        new("Tjänster", "USB.UppbordsApi",         7274, "uppbordsapi",      "USB.UppbordsApi"),
        new("GUI",      "USB.GDAGui",              7081, "gdagui",           "USB.GDAGui"),
        new("GUI",      "USB.MFTGui",              7239, "mftgui",           "USB.MFTGui"),
        new("GUI",      "USB.MUSGui",              7063, "musgui",           "USB.MUSGui"),
        new("GUI",      "USB.UppbordsGui",         7133, "uppbordsgui",      "USB.UppbordsGui"),
    };
}
