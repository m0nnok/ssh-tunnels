using System.Drawing;
using System.Windows.Forms;

namespace SshTunneler.Services;

/// <summary>
/// Fallback prompt shown by the askpass helper when the app has no stored secret,
/// which also covers one-time codes and host key confirmations.
/// </summary>
internal static class AskPassPromptWindow
{
    public static string? Ask(string prompt, string? label)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware);

        using var form = new Form
        {
            Text = string.IsNullOrWhiteSpace(label) ? "SSH Tunneler" : $"SSH Tunneler — {label}",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = true,
            TopMost = true,
            ClientSize = new Size(420, 132),
            BackColor = Color.FromArgb(28, 28, 32),
            ForeColor = Color.Gainsboro
        };

        var promptLabel = new Label
        {
            Text = prompt,
            AutoSize = false,
            Bounds = new Rectangle(14, 12, 392, 40)
        };

        var isYesNo = prompt.Contains("yes/no", StringComparison.OrdinalIgnoreCase);
        var input = new TextBox
        {
            Bounds = new Rectangle(14, 58, 392, 26),
            UseSystemPasswordChar = !isYesNo,
            BackColor = Color.FromArgb(40, 40, 46),
            ForeColor = Color.Gainsboro,
            BorderStyle = BorderStyle.FixedSingle
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(240, 94, 80, 26) };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(326, 94, 80, 26) };

        form.Controls.AddRange([promptLabel, input, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) => { form.Activate(); input.Focus(); };

        return form.ShowDialog() == DialogResult.OK ? input.Text : null;
    }
}
