// Resolve the WinForms ↔ WPF type name collisions that show up once
// <UseWindowsForms>true</UseWindowsForms> is enabled for the NotifyIcon.
// We prefer the WPF types by default; WinForms gets the "WinForms" alias
// in places that actually need it.
global using Application  = System.Windows.Application;
global using Clipboard    = System.Windows.Clipboard;
global using MessageBox   = System.Windows.MessageBox;
global using MessageBoxButton  = System.Windows.MessageBoxButton;
global using MessageBoxResult  = System.Windows.MessageBoxResult;
global using MessageBoxImage   = System.Windows.MessageBoxImage;
global using RadioButton  = System.Windows.Controls.RadioButton;
global using Button       = System.Windows.Controls.Button;
global using TextBox      = System.Windows.Controls.TextBox;
global using CheckBox     = System.Windows.Controls.CheckBox;
global using ComboBox     = System.Windows.Controls.ComboBox;
global using ListBox      = System.Windows.Controls.ListBox;
global using UserControl  = System.Windows.Controls.UserControl;
global using Brush        = System.Windows.Media.Brush;
global using Color        = System.Windows.Media.Color;
global using Brushes      = System.Windows.Media.Brushes;
global using Point        = System.Windows.Point;
global using Size         = System.Windows.Size;
