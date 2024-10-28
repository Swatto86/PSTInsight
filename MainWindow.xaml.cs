using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using XstReader;
using CheckBox = System.Windows.Controls.CheckBox;
using Cursors = System.Windows.Input.Cursors;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using ListView = System.Windows.Controls.ListView;
using ListViewItem = System.Windows.Controls.ListViewItem;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace PSTInsight
{
    /// <summary>
    /// Main window of the PSTInsight application.
    /// Handles PST file loading, email display, and export functionality.
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Command Implementation

        /// <summary>
        /// Asynchronous implementation of ICommand for handling asynchronous operations.
        /// </summary>
        public class AsyncRelayCommand<T> : ICommand
        {
            private readonly Func<T, bool> _canExecute;
            private readonly Func<T, Task> _execute;

            public AsyncRelayCommand(Func<T, Task> execute, Func<T, bool> canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public bool CanExecute(object parameter)
            {
                return _canExecute == null || _canExecute((T)parameter);
            }

            public async void Execute(object parameter)
            {
                await _execute((T)parameter);
            }

            public event EventHandler CanExecuteChanged
            {
                add => CommandManager.RequerySuggested += value;
                remove => CommandManager.RequerySuggested -= value;
            }
        }

        #endregion

        #region Fields and Properties

        private readonly PstService _pstService;
        private XstFile _currentPstFile;
        private ObservableCollection<EmailItem> _emails;
        private List<EmailItem> _allEmails;
        private int _progressValue;
        private string _progressText;
        private Visibility _progressVisibility = Visibility.Collapsed;
        private GridViewColumnHeader _lastHeaderClicked = null;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        /// <summary>
        /// Gets or sets the current progress value.
        /// </summary>
        public int ProgressValue
        {
            get => _progressValue;
            set
            {
                if (_progressValue != value)
                {
                    _progressValue = value;
                    OnPropertyChanged(nameof(ProgressValue));
                }
            }
        }

        /// <summary>
        /// Gets or sets the current progress text.
        /// </summary>
        public string ProgressText
        {
            get => _progressText;
            set
            {
                if (_progressText != value)
                {
                    _progressText = value;
                    OnPropertyChanged(nameof(ProgressText));
                }
            }
        }

        /// <summary>
        /// Gets or sets the visibility of the progress bar.
        /// </summary>
        public Visibility ProgressVisibility
        {
            get => _progressVisibility;
            set
            {
                if (_progressVisibility != value)
                {
                    _progressVisibility = value;
                    OnPropertyChanged(nameof(ProgressVisibility));
                }
            }
        }

        private Visibility _webViewVisibility = Visibility.Collapsed;

        /// <summary>
        /// Gets or sets the visibility of the WebView control.
        /// </summary>
        public Visibility WebViewVisibility
        {
            get => _webViewVisibility;
            set
            {
                if (_webViewVisibility != value)
                {
                    _webViewVisibility = value;
                    OnPropertyChanged(nameof(WebViewVisibility));
                }
            }
        }

        /// <summary>
        /// Gets or sets the collection of email items to display.
        /// </summary>
        public ObservableCollection<EmailItem> Emails
        {
            get => _emails;
            set
            {
                _emails = value;
                OnPropertyChanged(nameof(Emails));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Commands

        public ICommand ToggleExportCommand { get; private set; }
        public ICommand SaveAttachmentCommand { get; private set; }

        #endregion

        #region Initialization and Setup

        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            _pstService = new PstService();
            _emails = new ObservableCollection<EmailItem>();
            DataContext = this;
            InitializeAsync();
            SaveAttachmentCommand = new AsyncRelayCommand<AttachmentItem>(SaveAttachment);
            LoadSavedPstFilesAsync();
            ToggleExportCommand = new RelayCommand<EmailItem>(ToggleExport);
            Loaded += MainWindow_Loaded;
        }

        /// <summary>
        /// Asynchronously initializes the WebView control.
        /// </summary>
        private async void InitializeAsync()
        {
            await webView.EnsureCoreWebView2Async();
        }

        /// <summary>
        /// Resizes ListView columns when the window size changes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            AdjustColumnWidths();
        }

        /// <summary>
        /// Adjusts the column widths of the ListView to fill the available space.
        /// </summary>
        private void AdjustColumnWidths()
        {
            ListView listView = lvEmails;

            if (listView.View is GridView gridView && gridView.Columns.Count > 0)
            {
                // Calculate total width (subtracting scrollbar width if present)
                double totalWidth = listView.ActualWidth;
                ScrollViewer scrollViewer = FindVisualChild<ScrollViewer>(listView);
                if (scrollViewer != null && scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible)
                {
                    totalWidth -= SystemParameters.VerticalScrollBarWidth;
                }

                // Subtract the width of 'Export?' and 'Attachment' columns
                double exportColumnWidth = 53;
                double attachmentColumnWidth = 80;
                totalWidth -= (exportColumnWidth + attachmentColumnWidth);

                // Exclude 'Export?' and 'Attachment' columns from width adjustment
                var columnsToAdjust = gridView.Columns.Where(c => c.Header.ToString() != "Export?" && c.Header.ToString() != "Attachment").ToList();
                double usedWidth = columnsToAdjust.Sum(c => c.ActualWidth);
                double remainingWidth = totalWidth - usedWidth;

                if (remainingWidth > 0)
                {
                    // Distribute remaining width among columns except 'Export?' and 'Attachment'
                    double additionalWidth = remainingWidth / columnsToAdjust.Count;
                    foreach (var column in columnsToAdjust)
                    {
                        column.Width = column.ActualWidth + additionalWidth;
                    }
                }
            }
        }

        #endregion

        #region PST File Handling

        /// <summary>
        /// Loads previously saved PST files asynchronously.
        /// </summary>
        private async void LoadSavedPstFilesAsync()
        {
            try
            {
                List<string> savedPstFiles = _pstService.LoadSavedPstPaths();
                List<string> failedPstFiles = new List<string>();

                foreach (string pstFile in savedPstFiles)
                {
                    if (File.Exists(pstFile))
                    {
                        try
                        {
                            await LoadPstFileAsync(pstFile);
                        }
                        catch (Exception ex)
                        {
                            failedPstFiles.Add(pstFile);
                            Debug.WriteLine($"Failed to load PST file: {pstFile}. Error: {ex.Message}");
                        }
                    }
                    else
                    {
                        failedPstFiles.Add(pstFile);
                        Debug.WriteLine($"PST file not found: {pstFile}");
                    }
                }

                if (failedPstFiles.Count > 0)
                {
                    string failedFiles = string.Join("\n", failedPstFiles);
                    _ = MessageBox.Show($"Failed to load the following PST files:\n{failedFiles}", "Load Errors",
                        MessageBoxButton.OK, MessageBoxImage.Warning);

                    // Remove failed PST files from the saved list
                    foreach (string failedFile in failedPstFiles)
                    {
                        _pstService.RemoveSavedPstPath(failedFile);
                    }
                    _pstService.SavePstPaths();
                }
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Error loading saved PST files: {ex.Message}", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Handles the click event of the Load PST button.
        /// </summary>
        private async void BtnLoadPst_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "PST Files (*.pst)|*.pst",
                Title = "Select a PST file"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await LoadPstFileAsync(openFileDialog.FileName);
            }
        }

        /// <summary>
        /// Loads a PST file asynchronously.
        /// </summary>
        /// <param name="pstFilePath">The path to the PST file to load.</param>
        private async Task LoadPstFileAsync(string pstFilePath)
        {
            if (string.IsNullOrEmpty(pstFilePath))
            {
                throw new ArgumentException("PST file path cannot be null or empty.", nameof(pstFilePath));
            }

            if (!File.Exists(pstFilePath))
            {
                throw new FileNotFoundException($"PST file not found: {pstFilePath}");
            }

            try
            {
                if (_pstService == null)
                {
                    throw new InvalidOperationException("PstService is not initialized.");
                }

                SetWindowEnabled(false);
                ProgressVisibility = Visibility.Visible;
                ProgressValue = 0;
                ProgressText = $"Loading PST file: {Path.GetFileName(pstFilePath)}...";

                bool success = await _pstService.LoadPstFileAsync(pstFilePath);
                if (success)
                {
                    _currentPstFile = _pstService.GetLoadedFile(pstFilePath);
                    if (_currentPstFile == null)
                    {
                        throw new Exception("Failed to retrieve loaded PST file.");
                    }

                    XstFolder rootFolder = _currentPstFile.RootFolder;
                    PopulateFolderTreeView(rootFolder);

                    if (!cmbPstFiles.Items.Contains(pstFilePath))
                    {
                        _ = cmbPstFiles.Items.Add(pstFilePath);
                        _pstService.SavePstPaths();
                    }

                    cmbPstFiles.SelectedItem = pstFilePath;

                    btnExport.IsEnabled = true;
                }
                else
                {
                    throw new Exception($"Failed to load PST file: {pstFilePath}");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new Exception(
                    $"Access denied when trying to load PST file: {pstFilePath}. Make sure you have the necessary permissions.",
                    ex);
            }
            catch (IOException ex)
            {
                throw new Exception(
                    $"I/O error occurred while loading PST file: {pstFilePath}. The file might be in use by another process.",
                    ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error occurred while loading PST file: {pstFilePath}", ex);
            }
            finally
            {
                SetWindowEnabled(true);
                ProgressVisibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Populates the folder TreeView with the folder structure of the loaded PST file.
        /// </summary>
        /// <param name="rootFolder">The root folder of the PST file.</param>
        private void PopulateFolderTreeView(XstFolder rootFolder)
        {
            tvFolders.Items.Clear();
            TreeViewItem rootItem = CreateTreeViewItem(rootFolder);
            _ = tvFolders.Items.Add(rootItem);
        }

        /// <summary>
        /// Creates a TreeViewItem for a given folder.
        /// </summary>
        /// <param name="folder">The folder to create a TreeViewItem for.</param>
        /// <returns>A TreeViewItem representing the folder.</returns>
        private TreeViewItem CreateTreeViewItem(XstFolder folder)
{
    try
    {
        // Skip null folders
        if (folder == null) return null;

        // Skip "IPM_COMMON_VIEWS" and "Search Root" folders
        if (folder.DisplayName == "IPM_COMMON_VIEWS" || folder.DisplayName == "Search Root")
        {
            return null;
        }

        // Safely get message count
        int messageCount = 0;
        try
        {
            messageCount = folder.GetMessages()?.Count() ?? 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting message count: {ex.Message}");
            // Continue with count as 0
        }

        // Create UI elements with null checks
        var headerPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal
        };

        var folderName = new TextBlock
        {
            Text = folder.DisplayName ?? "Unnamed Folder",
            Margin = new Thickness(0, 0, 5, 0)
        };
        headerPanel.Children.Add(folderName);

        var countText = new TextBlock
        {
            Text = $"({messageCount})",
            Foreground = FindResource("SecondaryTextBrush") as Brush ?? Brushes.Gray,
            FontSize = 11
        };
        headerPanel.Children.Add(countText);

        var item = new TreeViewItem
        {
            Header = headerPanel,
            Tag = folder,
            IsExpanded = true
        };

        // Safely add subfolders
        if (folder.Folders != null)
        {
            foreach (var subFolder in folder.Folders)
            {
                var subItem = CreateTreeViewItem(subFolder);
                if (subItem != null)
                {
                    item.Items.Add(subItem);
                }
            }
        }

        return item;
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"Error creating TreeViewItem: {ex.Message}");
        return null;
    }
}

        /// <summary>
        /// Handles the selection change event of the folder TreeView.
        /// </summary>
        private async void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem selectedItem && selectedItem.Tag is XstFolder selectedFolder)
            {
                await LoadEmailsAsync(selectedFolder);
            }
        }

        /// <summary>
        /// Loads emails from a selected folder asynchronously.
        /// </summary>
        /// <param name="selectedFolder">The selected folder to load emails from.</param>
        private async Task LoadEmailsAsync(XstFolder selectedFolder)
        {
            try
            {
                SetWindowEnabled(false);
                ProgressVisibility = Visibility.Visible;
                ProgressValue = 0;
                ProgressText = "Loading emails...";

                Progress<int> progress = new Progress<int>(value =>
                {
                    ProgressValue = value;
                    ProgressText = $"Loading emails... {value}%";
                });

                _allEmails = await _pstService.GetEmailsFromFolderAsync(selectedFolder, progress);

                if (_emails == null)
                {
                    _emails = new ObservableCollection<EmailItem>();
                }
                else
                {
                    _emails.Clear();
                }

                foreach (EmailItem email in _allEmails)
                {
                    _emails.Add(email);
                }

                Debug.WriteLine($"Loaded {_emails.Count} emails into ListView");

                ApplyFilter();
                lvEmails.Items.Refresh();
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Error loading emails: {ex.Message}", "Email Loading Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ProgressVisibility = Visibility.Collapsed;
                SetWindowEnabled(true);
            }
        }

        #endregion

        #region Email Display and Interaction

        /// <summary>
        /// Handles the selection change event of the email ListView.
        /// </summary>
        private async void LvEmails_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lvEmails.SelectedItem is EmailItem selectedEmail)
            {
                Debug.WriteLine($"Email selected: {selectedEmail.Subject}");
                txtSubject.Text = selectedEmail.MessageView.Subject;
                txtFrom.Text = selectedEmail.MessageView.SenderEmail;
                txtDate.Text = selectedEmail.MessageView.DisplayDate;

                try
                {
                    string htmlContent = await _pstService.GetEmailHtmlContentAsync(selectedEmail);
                    Debug.WriteLine($"HTML Content length: {htmlContent?.Length ?? 0}");
                    await webView.EnsureCoreWebView2Async();
                    webView.NavigateToString(htmlContent);
                    WebViewVisibility = Visibility.Visible;
                    Debug.WriteLine("WebView navigation completed");

                    // Focus the checkbox
                    if (lvEmails.ItemContainerGenerator.ContainerFromItem(selectedEmail) is ListViewItem item)
                    {
                        CheckBox checkbox = FindVisualChild<CheckBox>(item);
                        _ = (checkbox?.Focus());
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading email content: {ex.Message}");
                    _ = MessageBox.Show($"Error loading email content: {ex.Message}", "Error", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            else
            {
                Debug.WriteLine("No email selected");
                ClearEmailDisplay();
            }
        }

        /// <summary>
        /// Clears the email display area.
        /// </summary>
        private void ClearEmailDisplay()
        {
            txtSubject.Text = string.Empty;
            txtFrom.Text = string.Empty;
            txtDate.Text = string.Empty;
            webView.NavigateToString("<html><body></body></html>");
            WebViewVisibility = Visibility.Collapsed;
        }

        #endregion

        #region Email Filtering and Sorting

        /// <summary>
        /// Applies the current filter to the email list.
        /// </summary>
        private void ApplyFilter()
        {
            if (_emails == null || _emails.Count == 0)
            {
                return;
            }

            if (lvEmails.ItemsSource != null)
            {
                ICollectionView view = CollectionViewSource.GetDefaultView(lvEmails.ItemsSource);
                view.Filter = string.IsNullOrWhiteSpace(txtSearch.Text)
                    ? (Predicate<object>)null
                    : item =>
                    {
                        return item is EmailItem email
                               && (email.Subject?.IndexOf(txtSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0
                                   || email.FromAddress?.IndexOf(txtSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0);
                    };
            }
        }

        /// <summary>
        /// Handles the text changed event of the search TextBox.
        /// </summary>
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        /// <summary>
        /// Handles the click event of the GridViewColumnHeader for sorting.
        /// </summary>
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            ListSortDirection direction;

            if (e.OriginalSource is GridViewColumnHeader headerClicked)
            {
                if (headerClicked.Role != GridViewColumnHeaderRole.Padding)
                {
                    System.Windows.Data.Binding columnBinding = headerClicked.Column.DisplayMemberBinding as System.Windows.Data.Binding;
                    string sortBy = columnBinding?.Path.Path ?? headerClicked.Column.Header as string;

                    // Use same sorting as for the 'Attachment' column
                    if (sortBy == "Attachments")
                    {
                        sortBy = "Attachment";
                    }

                    direction = headerClicked != _lastHeaderClicked
                        ? ListSortDirection.Ascending
                        : _lastDirection == ListSortDirection.Ascending
                            ? ListSortDirection.Descending
                            : ListSortDirection.Ascending;

                    Sort(sortBy, direction);

                    _lastHeaderClicked = headerClicked;
                    _lastDirection = direction;
                }
            }
        }

        #endregion

        #region Email Export and Attachment Handling

        /// <summary>
        /// Handles the click event of the Export button.
        /// </summary>
        private async void BtnExport_Click(object sender, RoutedEventArgs e)
{
    try
    {
        List<EmailItem> selectedEmails = _allEmails.Where(email => email.IsSelectedForExport).ToList();
        if (selectedEmails.Count == 0)
        {
            _ = MessageBox.Show("Please select at least one email to export.", "No Emails Selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
        if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            try
            {
                SetWindowEnabled(false);
                ProgressVisibility = Visibility.Visible;
                ProgressValue = 0;
                ProgressText = "Exporting emails...";

                var result = await _pstService.ExportEmailsToMsgAsync(selectedEmails, folderBrowserDialog.SelectedPath,
                    progress =>
                    {
                        ProgressValue = progress;
                        ProgressText = $"Exporting emails... {progress}%";
                    });

                string message = $"Export completed:\n" +
                               $"Successfully exported: {result.SuccessCount}\n" +
                               $"Failed to export: {result.FailureCount}";

                if (result.FailureCount > 0)
                {
                    message += "\n\nWould you like to see details of the failed exports?";
                    var response = MessageBox.Show(message, "Export Complete", 
                        MessageBoxButton.YesNo, MessageBoxImage.Information);
                    
                    if (response == MessageBoxResult.Yes)
                    {
                        MessageBox.Show(string.Join("\n", result.FailedEmails), 
                            "Failed Exports", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show(message, "Export Complete", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                SetWindowEnabled(true);
                ProgressVisibility = Visibility.Collapsed;
            }
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Error during export: {ex.Message}", "Export Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

        /// <summary>
        /// Saves an attachment to a user-specified location.
        /// </summary>
        /// <param name="attachment">The attachment to save.</param>
        private async Task SaveAttachment(AttachmentItem attachment)
        {
            try
            {
                string fileExtension = Path.GetExtension(attachment.FileName);
                string filter = $"{fileExtension} files (*{fileExtension})|*{fileExtension}|All files (*.*)|*.*";

                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    FileName = attachment.FileName,
                    Filter = filter
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    await Task.Run(() => File.WriteAllBytes(saveFileDialog.FileName, attachment.GetContent()));
                    Debug.WriteLine($"Attachment saved: {saveFileDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving attachment: {ex.Message}");
                _ = MessageBox.Show($"Error saving attachment: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Sorts the email items in the ListView based on the specified column and direction.
        /// </summary>
        /// <param name="sortBy">The name of the column to sort by.</param>
        /// <param name="direction">The direction of the sort (ascending or descending).</param>
        private void Sort(string sortBy, ListSortDirection direction)
        {
            ICollectionView dataView = CollectionViewSource.GetDefaultView(lvEmails.ItemsSource);

            dataView.SortDescriptions.Clear();

            if (sortBy == "Attachment")
            {
                dataView.SortDescriptions.Add(new SortDescription("HasAttachment", direction));
            }
            else
            {
                SortDescription sd = new SortDescription(sortBy, direction);
                dataView.SortDescriptions.Add(sd);
            }

            dataView.Refresh();
        }

        /// <summary>
        /// Finds a visual child of a given type in the visual tree.
        /// </summary>
        /// <typeparam name="T">The type of the visual child to find.</typeparam>
        /// <param name="parent">The parent dependency object to search from.</param>
        /// <returns>The found visual child, or null if not found.</returns>
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                {
                    return result;
                }

                T childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }

            return null;
        }

        /// <summary>
        /// Updates the IsSelectedForExport property for all selected items in the ListView.
        /// </summary>
        private void UpdateSelectedItemsForExport()
        {
            foreach (EmailItem item in lvEmails.SelectedItems)
            {
                item.IsSelectedForExport = !item.IsSelectedForExport;
            }
        }

        /// <summary>
        /// Selects all items in the ListView for export.
        /// </summary>
        private void SelectAllItems()
        {
            lvEmails.SelectAll();
            UpdateSelectedItemsForExport();
        }

        /// <summary>
        /// Deselects all items in the ListView for export.
        /// </summary>
        private void DeselectAllItems()
        {
            lvEmails.UnselectAll();
            foreach (EmailItem item in _emails)
            {
                item.IsSelectedForExport = false;
            }
        }

        /// <summary>
        /// Raises the PropertyChanged event for a specified property.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Sets the enabled state of the window and updates the cursor accordingly.
        /// </summary>
        /// <param name="enabled">True to enable the window, false to disable it.</param>
        private void SetWindowEnabled(bool enabled)
        {
            IsEnabled = enabled;
            Mouse.OverrideCursor = enabled ? null : Cursors.Wait;
        }

        /// <summary>
        /// Toggles the export selection state of an email item.
        /// </summary>
        /// <param name="emailItem">The email item to toggle.</param>
        private void ToggleExport(EmailItem emailItem)
        {
            if (emailItem != null)
            {
                emailItem.IsSelectedForExport = !emailItem.IsSelectedForExport;
            }
        }

        #endregion

        #region Event Handlers

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AdjustColumnWidths();
            // Assuming your ListView is named 'EmailsListView'
            lvEmails.SizeChanged += ListView_SizeChanged;
        }

        /// <summary>
        /// Handles the drop event for drag-and-drop PST file loading.
        /// </summary>
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string file in files)
                {
                    if (Path.GetExtension(file).Equals(".pst", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = LoadPstFileAsync(file);
                    }
                }
            }
        }

        /// <summary>
        /// Handles the drag-over event for drag-and-drop PST file loading.
        /// </summary>
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// Handles the selection change event of the PST files ComboBox.
        /// </summary>
        private void CmbPstFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPstFiles.SelectedItem is string selectedPstPath)
            {
                _ = LoadPstFileAsync(selectedPstPath);
            }
        }

        /// <summary>
        /// Handles the click event of the Refresh button.
        /// </summary>
        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (tvFolders.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is XstFolder selectedFolder)
            {
                try
                {
                    SetWindowEnabled(false);
                    ProgressVisibility = Visibility.Visible;
                    ProgressValue = 0;
                    ProgressText = "Refreshing emails...";

                    await LoadEmailsAsync(selectedFolder);

                    _ = MessageBox.Show("Emails refreshed successfully.", "Refresh Complete", MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _ = MessageBox.Show($"Error refreshing emails: {ex.Message}", "Refresh Error", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    SetWindowEnabled(true);
                    ProgressVisibility = Visibility.Collapsed;
                }
            }
            else
            {
                _ = MessageBox.Show("Please select a folder to refresh.", "Refresh", MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Handles the click event of the Deselect All button.
        /// </summary>
        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            DeselectAllItems();
        }

        /// <summary>
        /// Handles the click event of the Select All button.
        /// </summary>
        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            SelectAllItems();
        }

        /// <summary>
        /// Handles the click event of the Remove PST button.
        /// </summary>
        private void BtnRemovePst_Click(object sender, RoutedEventArgs e)
        {
            string selectedPstFile = cmbPstFiles.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedPstFile))
            {
                _pstService.RemoveSavedPstPath(selectedPstFile);
                _pstService.UnloadPstFile(selectedPstFile);
                cmbPstFiles.Items.Remove(selectedPstFile);
                if (cmbPstFiles.Items.Count > 0)
                {
                    cmbPstFiles.SelectedIndex = 0;
                }
                else
                {
                    _currentPstFile = null;
                    tvFolders.Items.Clear();
                    _emails.Clear();
                    btnExport.IsEnabled = false;
                }
            }
        }

        #endregion
    }
}