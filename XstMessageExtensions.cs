using System;
using System.Runtime.CompilerServices;
using System.Windows;
using XstReader;

namespace PSTInsight
{
    public static class XstMessageExtensions
    {
        private static readonly ConditionalWeakTable<XstMessage, SelectionState> SelectionStorage =
            new ConditionalWeakTable<XstMessage, SelectionState>();

        private static MainWindow MainWindow =>
            Application.Current.MainWindow as MainWindow;

        public static bool GetIsSelectedForExport(this XstMessage message)
        {
            SelectionState state = SelectionStorage.GetOrCreateValue(message);
            return state.IsSelected;
        }

        public static void SetIsSelectedForExport(this XstMessage message, bool value)
        {
            SelectionState state = SelectionStorage.GetOrCreateValue(message);
            state.IsSelected = value;

            // Update the export count through the main window
            if (Application.Current?.Dispatcher != null)
            {
                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    MainWindow?.UpdateExportCount();
                }));
            }
        }

        private class SelectionState
        {
            public bool IsSelected { get; set; }
        }
    }
}