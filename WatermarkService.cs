using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace PSTInsight
{
    /// <summary>
    /// Provides watermark functionality for TextBox controls in WPF applications.
    /// </summary>
    public static class WatermarkService
    {
        #region Dependency Property

        /// <summary>
        /// Dependency property for setting and getting the watermark text.
        /// </summary>
        public static readonly DependencyProperty WatermarkProperty = DependencyProperty.RegisterAttached(
            "Watermark",
            typeof(string),
            typeof(WatermarkService),
            new FrameworkPropertyMetadata(string.Empty, OnWatermarkChanged));

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets the watermark text for a dependency object.
        /// </summary>
        /// <param name="d">The dependency object to get the watermark from.</param>
        /// <returns>The watermark text.</returns>
        public static string GetWatermark(DependencyObject d)
        {
            return (string)d.GetValue(WatermarkProperty);
        }

        /// <summary>
        /// Sets the watermark text for a dependency object.
        /// </summary>
        /// <param name="d">The dependency object to set the watermark on.</param>
        /// <param name="value">The watermark text to set.</param>
        public static void SetWatermark(DependencyObject d, string value)
        {
            d.SetValue(WatermarkProperty, value);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Handles changes to the Watermark property.
        /// </summary>
        /// <param name="d">The dependency object that changed.</param>
        /// <param name="e">Event arguments containing the old and new values.</param>
        private static void OnWatermarkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TextBox textBox))
            {
                Debug.WriteLine("OnWatermarkChanged: The dependency object is not a TextBox.");
                return;
            }

            if (e.NewValue != null)
            {
                // Add event handlers when a new watermark is set
                textBox.Loaded += TextBox_Loaded;
                textBox.GotFocus += TextBox_GotFocus;
                textBox.LostFocus += TextBox_LostFocus;
                textBox.TextChanged += TextBox_TextChanged;
                Debug.WriteLine($"OnWatermarkChanged: Added event handlers for TextBox with watermark: {e.NewValue}");
            }
            else
            {
                // Remove event handlers when the watermark is cleared
                textBox.Loaded -= TextBox_Loaded;
                textBox.GotFocus -= TextBox_GotFocus;
                textBox.LostFocus -= TextBox_LostFocus;
                textBox.TextChanged -= TextBox_TextChanged;
                RemoveWatermark(textBox);
                Debug.WriteLine("OnWatermarkChanged: Removed event handlers and watermark from TextBox.");
            }
        }

        /// <summary>
        /// Handles the Loaded event of the TextBox.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private static void TextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is TextBox textBox))
            {
                Debug.WriteLine("TextBox_Loaded: Sender is not a TextBox.");
                return;
            }

            if (string.IsNullOrEmpty(textBox.Text))
            {
                ShowWatermark(textBox);
                Debug.WriteLine("TextBox_Loaded: Showed watermark for empty TextBox.");
            }
            else
            {
                RemoveWatermark(textBox);
                Debug.WriteLine("TextBox_Loaded: Removed watermark for non-empty TextBox.");
            }
        }

        /// <summary>
        /// Handles the GotFocus event of the TextBox.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private static void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (!(sender is TextBox textBox))
            {
                Debug.WriteLine("TextBox_GotFocus: Sender is not a TextBox.");
                return;
            }

            RemoveWatermark(textBox);
            Debug.WriteLine("TextBox_GotFocus: Removed watermark.");
        }

        /// <summary>
        /// Handles the LostFocus event of the TextBox.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private static void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!(sender is TextBox textBox))
            {
                Debug.WriteLine("TextBox_LostFocus: Sender is not a TextBox.");
                return;
            }

            if (string.IsNullOrEmpty(textBox.Text))
            {
                ShowWatermark(textBox);
                Debug.WriteLine("TextBox_LostFocus: Showed watermark for empty TextBox.");
            }
        }

        /// <summary>
        /// Handles the TextChanged event of the TextBox.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private static void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(sender is TextBox textBox))
            {
                Debug.WriteLine("TextBox_TextChanged: Sender is not a TextBox.");
                return;
            }

            if (!string.IsNullOrEmpty(textBox.Text))
            {
                RemoveWatermark(textBox);
                Debug.WriteLine("TextBox_TextChanged: Removed watermark for non-empty TextBox.");
            }
            else if (!textBox.IsFocused)
            {
                ShowWatermark(textBox);
                Debug.WriteLine("TextBox_TextChanged: Showed watermark for empty, unfocused TextBox.");
            }
        }

        /// <summary>
        /// Shows the watermark for the specified TextBox.
        /// </summary>
        /// <param name="textBox">The TextBox to show the watermark for.</param>
        private static void ShowWatermark(TextBox textBox)
        {
            if (textBox == null || textBox.Template == null)
            {
                Debug.WriteLine("ShowWatermark: TextBox or its template is null.");
                return;
            }

            if (textBox.Template.FindName("PART_Watermark", textBox) is TextBlock watermarkTextBlock)
            {
                watermarkTextBlock.Visibility = Visibility.Visible;
                Debug.WriteLine("ShowWatermark: Watermark visibility set to Visible.");
            }
            else
            {
                Debug.WriteLine("ShowWatermark: PART_Watermark not found in TextBox template.");
            }
        }

        /// <summary>
        /// Removes the watermark from the specified TextBox.
        /// </summary>
        /// <param name="textBox">The TextBox to remove the watermark from.</param>
        private static void RemoveWatermark(TextBox textBox)
        {
            if (textBox == null || textBox.Template == null)
            {
                Debug.WriteLine("RemoveWatermark: TextBox or its template is null.");
                return;
            }

            if (textBox.Template.FindName("PART_Watermark", textBox) is TextBlock watermarkTextBlock)
            {
                watermarkTextBlock.Visibility = Visibility.Collapsed;
                Debug.WriteLine("RemoveWatermark: Watermark visibility set to Collapsed.");
            }
            else
            {
                Debug.WriteLine("RemoveWatermark: PART_Watermark not found in TextBox template.");
            }
        }

        #endregion
    }
}