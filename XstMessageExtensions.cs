using System;
using System.Runtime.CompilerServices;
using System.Windows;
using XstReader;

namespace PSTInsight
{
    /// <summary>
    /// Provides extension methods for the XstMessage class to manage selection state for export.
    /// </summary>
    public static class XstMessageExtensions
    {
        // ConditionalWeakTable to store selection state for each XstMessage instance.
        private static readonly ConditionalWeakTable<XstMessage, SelectionState> SelectionStorage =
            new ConditionalWeakTable<XstMessage, SelectionState>();

        // Property to get the MainWindow instance from the current application.
        private static MainWindow MainWindow =>
            Application.Current.MainWindow as MainWindow;

        /// <summary>
        /// Gets whether the XstMessage is selected for export.
        /// </summary>
        /// <param name="message">The XstMessage instance.</param>
        /// <returns>True if selected for export, otherwise false.</returns>
        public static bool GetIsSelectedForExport(this XstMessage message)
        {
            // Retrieve or create the selection state for the message.
            SelectionState state = SelectionStorage.GetOrCreateValue(message);
            return state.IsSelected;
        }

        /// <summary>
        /// Sets whether the XstMessage is selected for export.
        /// </summary>
        /// <param name="message">The XstMessage instance.</param>
        /// <param name="value">True to select for export, otherwise false.</param>
        public static void SetIsSelectedForExport(this XstMessage message, bool value)
        {
            // Retrieve or create the selection state for the message.
            SelectionState state = SelectionStorage.GetOrCreateValue(message);
            state.IsSelected = value;

            // Update the export count through the main window if the dispatcher is available.
            if (Application.Current?.Dispatcher != null)
            {
                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    MainWindow?.UpdateExportCount();
                }));
            }
        }

        /// <summary>
        /// Represents the selection state of an XstMessage.
        /// </summary>
        private class SelectionState
        {
            public bool IsSelected { get; set; }
        }
    }
}
