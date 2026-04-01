using JellyfinBookReader.Configuration;
using Xunit;

namespace JellyfinBookReader.Tests.Configuration;

public class PluginConfigurationTests
{
    [Fact]
    public void DefaultValues_AreReasonable()
    {
        var config = new PluginConfiguration();

        Assert.Equal(30, config.StaleSessionTimeoutMinutes);
        Assert.Equal(100, config.MaxPageSize);
        Assert.Equal(20, config.DefaultPageSize);
    }

    [Fact]
    public void Values_AreSettable()
    {
        var config = new PluginConfiguration
        {
            StaleSessionTimeoutMinutes = 60,
            MaxPageSize = 200,
            DefaultPageSize = 50,
        };

        Assert.Equal(60, config.StaleSessionTimeoutMinutes);
        Assert.Equal(200, config.MaxPageSize);
        Assert.Equal(50, config.DefaultPageSize);
    }
}
