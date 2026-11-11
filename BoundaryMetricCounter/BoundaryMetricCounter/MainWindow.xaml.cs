using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace BoundaryMetricCounter
{
    public partial class MainWindow : Window
    {
        string? _path = null;

        public MainWindow() { InitializeComponent(); }

        void Choose_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Rust files (*.rs)|*.rs|Все файлы (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
            {
                _path = dlg.FileName;
                FilePathBox.Text = _path;
                AnalyzeButton.IsEnabled = true;
                StatusText.Text = "Файл выбран.";
            }
        }

        void Analyze_Click(object sender, RoutedEventArgs e)
        {
            if (_path == null || !File.Exists(_path)) return;
            try
            {
                string code = File.ReadAllText(_path);
                var result = BoundaryAnalyzer.Analyze(code);

                var win = new ResultsWindow(result);
                win.Owner = this;
                win.Show();
                Hide();
            }
            catch (System.Exception ex) { StatusText.Text = "Ошибка: " + ex.Message; }
        }

        public void ComeBack() { Show(); Activate(); }
    }
}