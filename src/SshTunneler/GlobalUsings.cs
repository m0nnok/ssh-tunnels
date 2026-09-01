// WPF and WinForms are both referenced (WinForms only for the tray icon and the askpass dialog),
// so the overlapping type names are pinned to their WPF meaning once, here.
global using Application = System.Windows.Application;
global using Binding = System.Windows.Data.Binding;
global using Clipboard = System.Windows.Clipboard;
global using MessageBox = System.Windows.MessageBox;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
