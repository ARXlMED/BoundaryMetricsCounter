using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace BoundaryMetricCounter
{
    public partial class ResultsWindow : Window
    {
        public ResultsWindow(BoundaryAnalyzer.Result r)
        {
            InitializeComponent();

            var rows = new List<GilbRow>
            {
                new GilbRow { Metric = "Абсолютная граничная сложность (Sa)", Value = r.Sa.ToString(), Bold = true },
                new GilbRow { Metric = "Относительная граничная сложность (So)", Value = r.So.ToString("F3", CultureInfo.InvariantCulture) },
                new GilbRow { Metric = "Всего вершин графа (v)", Value = r.N.ToString() }
            };

            Grid.ItemsSource = rows;

            Closing += (s, e) =>
            {
                if (Owner is MainWindow mw && !mw.IsVisible) mw.ComeBack();
            };
        }

        void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is MainWindow mw) mw.ComeBack();
            Close();
        }
    }
}