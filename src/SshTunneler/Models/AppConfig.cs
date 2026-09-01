using System.Text.Json;

namespace SshTunneler.Models;

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public List<SshServer> Servers { get; set; } = new();
    public List<TunnelResource> Resources { get; set; } = new();
    public UiSettings Ui { get; set; } = new();

    public SshServer? FindServer(Guid id) => Servers.FirstOrDefault(s => s.Id == id);

    public TunnelResource? FindResource(Guid id) => Resources.FirstOrDefault(r => r.Id == id);

    /// <summary>Deep copy used by the settings window so edits can be cancelled.</summary>
    public AppConfig Clone()
        => JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this))!;
}
