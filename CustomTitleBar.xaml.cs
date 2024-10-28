using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PSTInsight
{
    /// <summary>
    ///     Interaction logic for CustomTitleBar.xaml
    /// </summary>
    public partial class CustomTitleBar : UserControl
    {
        /// <summary>
        ///     Dependency property for the Title of the CustomTitleBar.
        /// </summary>
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(CustomTitleBar),
                new PropertyMetadata("SwatLauncher"));

        /// <summary>
        ///     Initializes a new instance of the CustomTitleBar class.
        /// </summary>
        public CustomTitleBar()
        {
            InitializeComponent();
            Loaded += CustomTitleBar_Loaded;
        }

        /// <summary>
        ///     Gets or sets the Title displayed in the CustomTitleBar.
        /// </summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>
        ///     Handles the MouseDown event on the TitleBar.
        ///     Allows dragging the window when the left mouse button is pressed.
        /// </summary>
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                TitleBar_MouseLeftButtonDown(sender, e);
            }
        }

        /// <summary>
        ///     Handles the Click event of the Minimize button.
        ///     Minimizes the parent window.
        /// </summary>
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).WindowState = WindowState.Minimized;
        }

        /// <summary>
        ///     Handles the Click event of the Maximize button.
        ///     Toggles between maximized and normal window state.
        /// </summary>
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).WindowState = Window.GetWindow(this).WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        /// <summary>
        ///     Handles the Click event of the Close button.
        ///     Closes the parent window.
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Window parentWindow = Window.GetWindow(this);
            parentWindow?.Close();
        }

        /// <summary>
        ///     Handles the Loaded event of the CustomTitleBar.
        ///     Sets up event handlers for window resizing and state changes.
        /// </summary>
        private void CustomTitleBar_Loaded(object sender, RoutedEventArgs e)
        {
            Window parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                parentWindow.SizeChanged += ParentWindow_SizeChanged;
                parentWindow.StateChanged += ParentWindow_StateChanged;
                UpdateTitleBarWidth(parentWindow.ActualWidth);
                UpdateMaximizeButtonContent(parentWindow.WindowState);
            }
        }

        /// <summary>
        ///     Handles the SizeChanged event of the parent window.
        ///     Updates the CustomTitleBar width when the window is resized.
        /// </summary>
        private void ParentWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTitleBarWidth(e.NewSize.Width);
        }

        /// <summary>
        ///     Handles the StateChanged event of the parent window.
        ///     Updates the maximize button content when the window state changes.
        /// </summary>
        private void ParentWindow_StateChanged(object sender, EventArgs e)
        {
            if (sender is Window parentWindow)
            {
                UpdateMaximizeButtonContent(parentWindow.WindowState);
            }
        }

        /// <summary>
        ///     Updates the width of the CustomTitleBar.
        /// </summary>
        private void UpdateTitleBarWidth(double width)
        {
            Width = width;
        }

        /// <summary>
        ///     Updates the content of the maximize button based on the window state.
        /// </summary>
        private void UpdateMaximizeButtonContent(WindowState windowState)
        {
            MaximizeButton.Content = windowState == WindowState.Maximized ? "❐" : "□";
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Window.GetWindow(this).WindowState = Window.GetWindow(this).WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                Window.GetWindow(this).DragMove();
            }
        }
    }
}