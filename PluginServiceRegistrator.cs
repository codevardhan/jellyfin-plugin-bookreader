using JellyfinBookReader.Data;
using JellyfinBookReader.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace JellyfinBookReader;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        // Data layer
        services.AddSingleton<BookReaderDbContext>();
        services.AddSingleton<ProgressRepository>();
        services.AddSingleton<SessionRepository>();

        // Services
        services.AddSingleton<BookService>();
        services.AddSingleton<CoverService>();
        services.AddSingleton<ProgressService>();
        services.AddSingleton<SessionService>();
    }
}