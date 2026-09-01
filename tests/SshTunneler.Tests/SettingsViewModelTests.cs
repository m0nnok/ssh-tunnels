using SshTunneler.Models;
using SshTunneler.Services;
using SshTunneler.ViewModels;
using Xunit;

namespace SshTunneler.Tests;

public class SettingsViewModelTests
{
    private static AppConfig ConfigWithServer(out SshServer server)
    {
        var config = new AppConfig();
        server = new SshServer { Name = "prod", Host = "10.0.0.5", User = "deploy" };
        config.Servers.Add(server);
        return config;
    }

    [Fact]
    public void FreshlyOpened_HasNoChanges()
    {
        var config = ConfigWithServer(out _);
        config.Resources.Add(new TunnelResource { Name = "Grafana", RemotePort = 3000, Order = 0 });
        using var askPass = new AskPassServer();

        var vm = new SettingsViewModel(config, askPass);

        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void AddingAResource_CountsAsAChange()
    {
        var config = ConfigWithServer(out _);
        using var askPass = new AskPassServer();
        var vm = new SettingsViewModel(config, askPass);

        vm.AddResourceCommand.Execute(null);

        Assert.True(vm.HasChanges);
    }

    [Fact]
    public void EditingAFieldOfAnExistingResource_CountsAsAChange()
    {
        var config = ConfigWithServer(out var server);
        config.Resources.Add(new TunnelResource { Name = "Grafana", ServerId = server.Id, RemotePort = 3000 });
        using var askPass = new AskPassServer();
        var vm = new SettingsViewModel(config, askPass);

        vm.Resources[0].RemotePort = 3001;

        Assert.True(vm.HasChanges);
    }

    [Fact]
    public void Editing_LeavesTheOriginalConfigUntouchedUntilSave()
    {
        var config = ConfigWithServer(out var server);
        config.Resources.Add(new TunnelResource { Name = "Grafana", ServerId = server.Id, RemotePort = 3000 });
        using var askPass = new AskPassServer();
        var vm = new SettingsViewModel(config, askPass);

        vm.Resources[0].Name = "Renamed";

        Assert.Equal("Grafana", config.Resources[0].Name);
        Assert.Equal("Renamed", vm.BuildResult().Resources[0].Name);
    }

    [Fact]
    public void BuildResult_NumbersResourcesByTheirOrderInTheList()
    {
        var config = ConfigWithServer(out var server);
        config.Resources.Add(new TunnelResource { Name = "A", ServerId = server.Id, Order = 0 });
        config.Resources.Add(new TunnelResource { Name = "B", ServerId = server.Id, Order = 1 });
        using var askPass = new AskPassServer();
        var vm = new SettingsViewModel(config, askPass);

        vm.SelectedResource = vm.Resources[1];
        vm.MoveResourceUpCommand.Execute(null);
        var result = vm.BuildResult();

        Assert.Equal(["B", "A"], result.Resources.OrderBy(r => r.Order).Select(r => r.Name));
    }

    [Fact]
    public void Validate_RejectsAResourceWithoutAServer()
    {
        var config = ConfigWithServer(out _);
        using var askPass = new AskPassServer();
        var vm = new SettingsViewModel(config, askPass);
        vm.AddResourceCommand.Execute(null);
        vm.Resources[0].ServerId = Guid.NewGuid();

        Assert.Contains("не выбран сервер", vm.Validate());
    }
}
