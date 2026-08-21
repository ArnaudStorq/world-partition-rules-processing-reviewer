using System.Windows;
using System.Windows.Media;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.Views;

public partial class ReportViewerDialog : Window
{
    public ReportViewerDialog(SuspiciousReport report, bool approved)
    {
        InitializeComponent();
        DataContext = report;

        KindGlyph.Text = approved ? "\uE8FB" : "\uEB90";
        KindGlyph.Foreground = approved
            ? (TryFindResource("Brush.StatusExpected") as Brush ?? Brushes.Green)
            : new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));
        KindTitle.Text = approved ? "Approved report" : "Suspicious report";
        Title = approved ? "Approved report" : "Suspicious report";
    }
}
