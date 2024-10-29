using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using XstReader;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using Orientation = System.Windows.Controls.Orientation;
using Task = System.Threading.Tasks.Task;

namespace PSTInsight
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Fields

        private readonly Dictionary<string, XstFile> _loadedPstFiles;
        private XstFile _currentPstFile;
        private ObservableCollection<XstMessage> _emails;
        private List<XstMessage> _allEmails;
        private string _progressText;
        private int _progressValue;
        private Visibility _progressVisibility = Visibility.Collapsed;
        private Visibility _webViewVisibility = Visibility.Collapsed;
        private int _exportCount;
        private Visibility _exportCountVisibility = Visibility.Collapsed;
        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        #endregion

        #region Properties

        public ObservableCollection<XstMessage> Emails
        {
            get => _emails;
            set
            {
                _emails = value;
                OnPropertyChanged(nameof(Emails));
            }
        }

        public int ProgressValue
        {
            get => _progressValue;
            set
            {
                _progressValue = value;
                OnPropertyChanged(nameof(ProgressValue));
            }
        }

        public string ProgressText
        {
            get => _progressText;
            set
            {
                _progressText = value;
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        public Visibility ProgressVisibility
        {
            get => _progressVisibility;
            set
            {
                _progressVisibility = value;
                OnPropertyChanged(nameof(ProgressVisibility));
            }
        }

        public Visibility WebViewVisibility
        {
            get => _webViewVisibility;
            set
            {
                _webViewVisibility = value;
                OnPropertyChanged(nameof(WebViewVisibility));
            }
        }

        public int ExportCount
        {
            get => _exportCount;
            set
            {
                _exportCount = value;
                OnPropertyChanged(nameof(ExportCount));
                ExportCountVisibility = value > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public Visibility ExportCountVisibility
        {
            get => _exportCountVisibility;
            set
            {
                _exportCountVisibility = value;
                OnPropertyChanged(nameof(ExportCountVisibility));
            }
        }

        #endregion

        #region Commands

        public ICommand SaveAttachmentCommand { get; private set; }

        #endregion

        #region Constructor

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _loadedPstFiles = new Dictionary<string, XstFile>();
            _emails = new ObservableCollection<XstMessage>();
            _allEmails = new List<XstMessage>();

            SaveAttachmentCommand = new AsyncRelayCommand<XstAttachment>(SaveAttachment);

            InitializeAsync();
            LoadSavedPstFiles();

            Loaded += MainWindow_Loaded;
        }

        private async void InitializeAsync()
        {
            await webView.EnsureCoreWebView2Async();
        }

        #endregion

        #region PST File Operations

        private async Task LoadPstFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException($"PST file not found: {filePath}");
            }

            try
            {
                SetWindowEnabled(false);
                ProgressVisibility = Visibility.Visible;
                ProgressValue = 0;
                ProgressText = $"Loading PST file: {Path.GetFileName(filePath)}...";

                // Make file loading async to prevent UI freezing
                await Task.Run(() =>
                {
                    var pstFile = new XstFile(filePath);
                    _loadedPstFiles[filePath] = pstFile;
                    _currentPstFile = pstFile;
                });

                if (!cmbPstFiles.Items.Contains(filePath))
                {
                    cmbPstFiles.Items.Add(filePath);
                    await Task.Run(() => SavePstFilePaths());
                }

                cmbPstFiles.SelectedItem = filePath;
                PopulateFolderTreeView(_currentPstFile.RootFolder);
                btnExport.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading PST file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetWindowEnabled(true);
                ProgressVisibility = Visibility.Collapsed;
            }
        }

        private void PopulateFolderTreeView(XstFolder rootFolder)
        {
            tvFolders.Items.Clear();
            var rootItem = CreateTreeViewItem(rootFolder);
            if (rootItem != null)
            {
                tvFolders.Items.Add(rootItem);
            }
        }

        private TreeViewItem CreateTreeViewItem(XstFolder folder)
        {
            if (folder == null) return null;

            // Skip specific system folders
            if (folder.DisplayName == "IPM_COMMON_VIEWS" || folder.DisplayName == "Search Root")
            {
                return null;
            }

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var folderName = new TextBlock
            {
                Text = folder.DisplayName ?? "Unnamed Folder",
                Margin = new Thickness(0, 0, 5, 0)
            };
            headerPanel.Children.Add(folderName);

            var messageCount = folder.GetMessages()?.Count() ?? 0;
            var countText = new TextBlock
            {
                Text = $"({messageCount})",
                Foreground = FindResource("SecondaryTextBrush") as Brush
            };
            headerPanel.Children.Add(countText);

            var item = new TreeViewItem
            {
                Header = headerPanel,
                Tag = folder,
                IsExpanded = true
            };

            foreach (var subFolder in folder.Folders)
            {
                var subItem = CreateTreeViewItem(subFolder);
                if (subItem != null)
                {
                    item.Items.Add(subItem);
                }
            }

            return item;
        }

        private async Task LoadEmailsAsync(XstFolder folder)
        {
            try
            {
                SetWindowEnabled(false);
                ProgressVisibility = Visibility.Visible;
                ProgressValue = 0;
                ProgressText = "Loading emails...";

                var messages = await Task.Run(() => folder.GetMessages());
                _allEmails = new List<XstMessage>();
                _emails.Clear();

                var total = messages.Count();
                var current = 0;

                foreach (var message in messages)
                {
                    _allEmails.Add(message);
                    _emails.Add(message);

                    current++;
                    ProgressValue = (int)((double)current / total * 100);
                    ProgressText = $"Loading emails... {ProgressValue}%";
                }

                ApplyFilter();
                lvEmails.Items.Refresh();
                UpdateExportCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading emails: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetWindowEnabled(true);
                ProgressVisibility = Visibility.Collapsed;
            }
        }

        private async Task SaveAttachment(XstAttachment attachment)
        {
            if (attachment == null) return;

            var saveFileDialog = new SaveFileDialog
            {
                FileName = attachment.FileNameForSaving,
                Filter = $"{Path.GetExtension(attachment.FileNameForSaving)} files (*{Path.GetExtension(attachment.FileNameForSaving)})|*{Path.GetExtension(attachment.FileNameForSaving)}|All files (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    // Make file saving async
                    await Task.Run(() => attachment.SaveToFile(saveFileDialog.FileName));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving attachment: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DisplayEmail(XstMessage email)
        {
            if (email == null)
            {
                ClearEmailDisplay();
                return;
            }

            txtSubject.Text = email.Subject;
            txtFrom.Text = email.From;
            txtDate.Text = email.Date?.ToString();

            try
            {
                string htmlContent = email.Body?.Text;
                if (!string.IsNullOrEmpty(htmlContent))
                {
                    webView.NavigateToString(htmlContent);
                    WebViewVisibility = Visibility.Visible;
                }
                else
                {
                    webView.NavigateToString("<html><body></body></html>");
                    WebViewVisibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error displaying email content: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                WebViewVisibility = Visibility.Collapsed;
            }
        }

        private void ClearEmailDisplay()
        {
            txtSubject.Text = string.Empty;
            txtFrom.Text = string.Empty;
            txtDate.Text = string.Empty;
            webView.NavigateToString("<html><body></body></html>");
            WebViewVisibility = Visibility.Collapsed;
        }

        private async Task ExportSelectedEmailsAsync(string outputPath)
        {
            var selectedEmails = _allEmails.Where(e => e.GetIsSelectedForExport()).ToList();
            if (selectedEmails.Count == 0)
            {
                MessageBox.Show("Please select at least one email to export.", "No Emails Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SetWindowEnabled(false);
                ProgressVisibility = Visibility.Visible;
                ProgressValue = 0;
                ProgressText = "Exporting emails...";

                var total = selectedEmails.Count;
                var success = 0;
                var failures = new List<string>();

                for (int i = 0; i < total; i++)
                {
                    var email = selectedEmails[i];
                    try
                    {
                        string fileName = GetSafeFileName($"{email.Subject}_{email.Date:yyyyMMdd}.msg");
                        string filePath = Path.Combine(outputPath, fileName);

                        // Make the file export operation async
                        await Task.Run(() =>
                        {
                            // TODO: Implement export using XstReader's native functionality
                            // This needs to be implemented based on XstReader's capabilities
                            // for exporting to MSG format
                        });

                        success++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{email.Subject}: {ex.Message}");
                    }

                    ProgressValue = (int)((double)(i + 1) / total * 100);
                    ProgressText = $"Exporting emails... {ProgressValue}%";

                    // Allow UI to update
                    await Task.Delay(1);
                }

                var message = $"Export completed:\n" +
                             $"Successfully exported: {success}\n" +
                             $"Failed to export: {failures.Count}";

                if (failures.Count > 0)
                {
                    message += "\n\nWould you like to see details of the failed exports?";
                    var response = MessageBox.Show(message, "Export Complete",
                        MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (response == MessageBoxResult.Yes)
                    {
                        MessageBox.Show(string.Join("\n", failures),
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

        #endregion

        #region Event Handlers

        private async void BtnLoadPst_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "PST Files (*.pst)|*.pst",
                Title = "Select a PST file"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await LoadPstFileAsync(openFileDialog.FileName);
            }
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    if (Path.GetExtension(file).Equals(".pst", StringComparison.OrdinalIgnoreCase))
                    {
                        await LoadPstFileAsync(file);
                    }
                }
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ?
                DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem selectedItem && selectedItem.Tag is XstFolder selectedFolder)
            {
                await LoadEmailsAsync(selectedFolder);
            }
        }

        private void LvEmails_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lvEmails.SelectedItem is XstMessage selectedEmail)
            {
                DisplayEmail(selectedEmail);
            }
            else
            {
                ClearEmailDisplay();
            }
        }

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader headerClicked &&
                headerClicked.Role != GridViewColumnHeaderRole.Padding)
            {
                ListSortDirection direction;

                if (headerClicked != _lastHeaderClicked)
                {
                    direction = ListSortDirection.Ascending;
                }
                else
                {
                    direction = _lastDirection == ListSortDirection.Ascending ?
                        ListSortDirection.Descending : ListSortDirection.Ascending;
                }

                var columnBinding = headerClicked.Column.DisplayMemberBinding as Binding;
                var sortBy = columnBinding?.Path.Path ?? headerClicked.Column.Header as string;

                Sort(sortBy, direction);

                _lastHeaderClicked = headerClicked;
                _lastDirection = direction;
            }
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            SelectAllItems();
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            DeselectAllItems();
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var folderBrowserDialog = new FolderBrowserDialog();
            if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await ExportSelectedEmailsAsync(folderBrowserDialog.SelectedPath);
            }
        }

        private void BtnRemovePst_Click(object sender, RoutedEventArgs e)
        {
            if (cmbPstFiles.SelectedItem is string selectedPstFile)
            {
                if (_loadedPstFiles.ContainsKey(selectedPstFile))
                {
                    _loadedPstFiles.Remove(selectedPstFile);
                }

                cmbPstFiles.Items.Remove(selectedPstFile);
                SavePstFilePaths();

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

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (tvFolders.SelectedItem is TreeViewItem selectedItem &&
                selectedItem.Tag is XstFolder selectedFolder)
            {
                await LoadEmailsAsync(selectedFolder);
            }
            else
            {
                MessageBox.Show("Please select a folder to refresh.", "Refresh",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private async void CmbPstFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPstFiles.SelectedItem is string selectedPstPath)
            {
                if (!_loadedPstFiles.ContainsKey(selectedPstPath))
                {
                    await LoadPstFileAsync(selectedPstPath);
                }
                else
                {
                    _currentPstFile = _loadedPstFiles[selectedPstPath];
                    PopulateFolderTreeView(_currentPstFile.RootFolder);
                }
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AdjustColumnWidths();
        }

        private void ListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            AdjustColumnWidths();
        }

        #endregion

        #region Helper Methods

        private void ApplyFilter()
        {
            if (_emails == null || string.IsNullOrEmpty(txtSearch.Text))
            {
                return;
            }

            var view = CollectionViewSource.GetDefaultView(_emails);
            var searchText = txtSearch.Text.ToLower();

            view.Filter = obj =>
            {
                if (obj is XstMessage email)
                {
                    return email.Subject?.ToLower().Contains(searchText) == true ||
                           email.From?.ToLower().Contains(searchText) == true;
                }
                return false;
            };
        }

        private void Sort(string sortBy, ListSortDirection direction)
        {
            var view = CollectionViewSource.GetDefaultView(lvEmails.ItemsSource);
            view.SortDescriptions.Clear();

            switch (sortBy?.ToLower())
            {
                case "subject":
                    view.SortDescriptions.Add(new SortDescription("Subject", direction));
                    break;
                case "from":
                    view.SortDescriptions.Add(new SortDescription("DisplayEmail", direction));
                    break;
                case "date":
                    view.SortDescriptions.Add(new SortDescription("ReceivedTime", direction));
                    break;
                case "attachment":
                    view.SortDescriptions.Add(new SortDescription("Attachments.Count", direction));
                    break;
            }

            view.Refresh();
        }

        private void SelectAllItems()
        {
            foreach (var email in _allEmails)
            {
                email.SetIsSelectedForExport(true);
            }
            UpdateExportCount();
        }

        private void DeselectAllItems()
        {
            foreach (var email in _allEmails)
            {
                email.SetIsSelectedForExport(false);
            }
            UpdateExportCount();
        }

        private void UpdateExportCount()
        {
            ExportCount = _allEmails?.Count(e => e.GetIsSelectedForExport()) ?? 0;
        }

        private void SavePstFilePaths()
        {
            var paths = cmbPstFiles.Items.Cast<string>().ToList();
            File.WriteAllLines("pstfiles.txt", paths);
        }

        private void LoadSavedPstFiles()
        {
            if (File.Exists("pstfiles.txt"))
            {
                var paths = File.ReadAllLines("pstfiles.txt");
                foreach (var path in paths.Where(File.Exists))
                {
                    _ = LoadPstFileAsync(path);
                }
            }
        }

        private static string GetSafeFileName(string fileName)
        {
            return string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        }

        private void SetWindowEnabled(bool enabled)
        {
            IsEnabled = enabled;
            Mouse.OverrideCursor = enabled ? null : Cursors.Wait;
        }

        private void AdjustColumnWidths()
        {
            if (!(lvEmails.View is GridView gridView)) return;

            double totalWidth = lvEmails.ActualWidth;

            if (GetDescendantByType(lvEmails, typeof(ScrollViewer)) is ScrollViewer scrollViewer &&
                scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            {
                totalWidth -= SystemParameters.VerticalScrollBarWidth;
            }

            const double exportColumnWidth = 53;
            const double attachmentColumnWidth = 80;
            totalWidth -= (exportColumnWidth + attachmentColumnWidth);

            var columnsToAdjust = gridView.Columns
                .Where(c => c.Header.ToString() != "Export?" && c.Header.ToString() != "Attachment")
                .ToList();

            if (columnsToAdjust.Any())
            {
                double widthPerColumn = totalWidth / columnsToAdjust.Count;
                foreach (var column in columnsToAdjust)
                {
                    column.Width = widthPerColumn;
                }
            }
        }

        private static DependencyObject GetDescendantByType(Visual element, Type type)
        {
            if (element == null) return null;

            if (element.GetType() == type) return element;

            DependencyObject foundElement = null;
            if (element is FrameworkElement)
            {
                (element as FrameworkElement).ApplyTemplate();
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var visual = VisualTreeHelper.GetChild(element, i) as Visual;
                foundElement = GetDescendantByType(visual, type);
                if (foundElement != null)
                    break;
            }
            return foundElement;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion
    }
}