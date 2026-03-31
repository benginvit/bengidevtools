using BengiDevTools.Models;

namespace BengiDevTools.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void Save();
    void Load();
}
