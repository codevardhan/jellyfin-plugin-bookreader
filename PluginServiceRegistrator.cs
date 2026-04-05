using System.Threading.Channels;
using JellyfinBookReader.Data;
using JellyfinBookReader.Dto;
using JellyfinBookReader.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace JellyfinBookReader;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        //  Data layer (unchanged) 
        services.AddSingleton<BookReaderDbContext>();
        services.AddSingleton<ProgressRepository>();
        services.AddSingleton<SessionRepository>();
        services.AddSingleton<ClientDataRepository>();

        //  Existing services (unchanged) 
        services.AddSingleton<BookService>();
        services.AddSingleton<CoverService>();
        services.AddSingleton<ProgressService>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<ClientDataService>();

        //  Streaming services 
        // Registered as IBookStreamingService so IEnumerable<IBookStreamingService>
        // injection into StreamingServiceFactory works automatically.
        // Resolution order here determines priority in the factory.
        services.AddSingleton<IBookStreamingService, CbzStreamingService>();
        services.AddSingleton<IBookStreamingService, CbrStreamingService>();
        services.AddSingleton<IBookStreamingService, EpubStreamingService>();
        services.AddSingleton<StreamingServiceFactory>();

        //  Adaptive page cache 
        services.AddSingleton<BookPageCache>();

        //  Warm-up channel 
        // Bounded with DropOldest so a burst of session starts never grows memory
        // unboundedly. Capacity is read from config at registration time.
        var capacity = Plugin.Instance?.Configuration?.WarmUpChannelCapacity ?? 20;
        var channel = Channel.CreateBounded<WarmUpRequest>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = true,
            });
        services.AddSingleton(channel);

        //  Background warm-up worker 
        services.AddHostedService<WarmUpBackgroundService>();
    }
}