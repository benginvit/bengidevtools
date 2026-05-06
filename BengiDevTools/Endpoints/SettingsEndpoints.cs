using BengiDevTools.Models;
using BengiDevTools.Services;

namespace BengiDevTools.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settings", (ISettingsService s) => s.Settings);

        app.MapPut("/api/settings", (AppSettings body, ISettingsService s) =>
        {
            s.Settings.RepoRootPath        = body.RepoRootPath;
            s.Settings.SqlConnectionString = body.SqlConnectionString;
            s.Settings.DebugScriptsPath    = body.DebugScriptsPath;
            s.Save();
            return Results.Ok(s.Settings);
        });
    }
}
