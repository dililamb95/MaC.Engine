using System;
using System.Windows;

namespace MaCProFunding.Preview
{
    /// <summary>
    /// Standalone visual preview for Dashboard V2.
    /// It does not connect to live trading services.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        // Ajusta la ventana al area disponible sin maximizar ni guardar
        // dimensiones del equipo de desarrollo.
        private void OnCompactWindowLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnCompactWindowLoaded;
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(1060d, Math.Max(600d, workArea.Width - 48d));
            Height = Math.Min(740d, Math.Max(450d, workArea.Height - 48d));
            MinWidth = Math.Min(760d, Width);
            MinHeight = Math.Min(520d, Height);
            Left = workArea.Left + (workArea.Width - Width) / 2d;
            Top = workArea.Top + (workArea.Height - Height) / 2d;
        }

    }
}