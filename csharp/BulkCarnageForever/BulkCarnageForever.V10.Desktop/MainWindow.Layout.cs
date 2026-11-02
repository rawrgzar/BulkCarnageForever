using System;
using System.Windows;

namespace BulkCarnageForever.V10.Desktop;

public partial class MainWindow
{
    private void QuickLogSection_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (QuickLogLayout is null || QuickLogHeader is null || QuickLogFooter is null) return;
        // Fill tall viewports, but retain a useful results area and reachable inputs on smaller windows.
        double minimum = Math.Max(480, QuickLogHeader.ActualHeight + QuickLogFooter.ActualHeight + 100);
        if (Math.Abs(QuickLogLayout.MinHeight - minimum) > .5) QuickLogLayout.MinHeight = minimum;
    }
}
