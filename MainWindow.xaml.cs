using MsgKit;
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
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
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
                    XstFile pstFile = new XstFile(filePath);
                    _loadedPstFiles[filePath] = pstFile;
                    _currentPstFile = pstFile;
                });

                if (!cmbPstFiles.Items.Contains(filePath))
                {
                    _ = cmbPstFiles.Items.Add(filePath);
                    await Task.Run(() => SavePstFilePaths());
                }

                cmbPstFiles.SelectedItem = filePath;
                PopulateFolderTreeView(_currentPstFile.RootFolder);
                btnExport.IsEnabled = true;
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Error loading PST file: {ex.Message}", "Error",
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
            TreeViewItem rootItem = CreateTreeViewItem(rootFolder);
            if (rootItem != null)
            {
                _ = tvFolders.Items.Add(rootItem);
            }
        }

        private TreeViewItem CreateTreeViewItem(XstFolder folder)
        {
            if (folder == null)
            {
                return null;
            }

            // Skip specific system folders
            if (folder.DisplayName == "IPM_COMMON_VIEWS" || folder.DisplayName == "Search Root")
            {
                return null;
            }

            StackPanel headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            TextBlock folderName = new TextBlock
            {
                Text = folder.DisplayName ?? "Unnamed Folder",
                Margin = new Thickness(0, 0, 5, 0)
            };
            _ = headerPanel.Children.Add(folderName);

            int messageCount = folder.GetMessages()?.Count() ?? 0;
            TextBlock countText = new TextBlock
            {
                Text = $"({messageCount})",
                Foreground = FindResource("SecondaryTextBrush") as Brush
            };
            _ = headerPanel.Children.Add(countText);

            TreeViewItem item = new TreeViewItem
            {
                Header = headerPanel,
                Tag = folder,
                IsExpanded = true
            };

            foreach (XstFolder subFolder in folder.Folders)
            {
                TreeViewItem subItem = CreateTreeViewItem(subFolder);
                if (subItem != null)
                {
                    _ = item.Items.Add(subItem);
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

                IEnumerable<XstMessage> messages = await Task.Run(() => folder.GetMessages());
                _allEmails = new List<XstMessage>();
                _emails.Clear();

                int total = messages.Count();
                int current = 0;

                foreach (XstMessage message in messages)
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
                _ = MessageBox.Show($"Error loading emails: {ex.Message}", "Error",
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
            if (attachment == null)
            {
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
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
                    _ = MessageBox.Show($"Error saving attachment: {ex.Message}", "Error",
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
                _ = MessageBox.Show($"Error displaying email content: {ex.Message}", "Error",
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
            // Get selected emails
            List<XstMessage> selectedEmails = _allEmails.Where(e => e.GetIsSelectedForExport()).ToList();
            if (selectedEmails.Count == 0)
            {
                _ = MessageBox.Show("Please select at least one email to export.", "No Emails Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Setup progress tracking
                SetWindowEnabled(false);
                ProgressVisibility = Visibility.Visible;
                ProgressValue = 0;
                ProgressText = "Exporting emails...";

                int total = selectedEmails.Count;
                int success = 0;
                List<string> failures = new List<string>();

                // Process each email
                for (int i = 0; i < total; i++)
                {
                    XstMessage email = selectedEmails[i];
                    try
                    {
                        // Generate unique filename
                        string baseFileName = GetSafeFileName(email.Subject);
                        string filePath = GetUniqueFilePath(outputPath, baseFileName, ".msg");

                        // Export email asynchronously
                        await Task.Run(() =>
                        {
                            // Parse sender information
                            (string fromName, string fromAddress) = ParseEmailAddress(email.From ?? string.Empty);

                            // Create MSG file
                            using (Email msg = new Email(new Sender(fromName, fromAddress), email.Subject ?? string.Empty))
                            {
                                // Set basic properties
                                if (email.Date.HasValue)
                                {
                                    msg.SentOn = email.Date.Value;
                                }

                                // Add recipients (To, CC, BCC)
                                AddRecipients(msg, email);

                                // Set message body
                                SetMessageBody(msg, email);

                                // Add attachments
                                if (email.Attachments != null)
                                {
                                    foreach (XstAttachment attachment in email.Attachments)
                                    {
                                        string tempFile = null;
                                        try
                                        {
                                            // Create temp file with original filename
                                            string tempPath = Path.GetTempPath();
                                            string originalFileName = attachment.FileNameForSaving;
                                            tempFile = Path.Combine(tempPath, originalFileName);

                                            // Save attachment to temp file
                                            attachment.SaveToFile(tempFile);

                                            // Add the attachment using the original filename
                                            msg.Attachments.Add(
                                                tempFile,           // Full path to temp file
                                                -1,                 // Default rendering position
                                                false,              // Not inline
                                                string.Empty        // No content ID
                                            );
                                        }
                                        finally
                                        {
                                            // Clean up temp file
                                            if (tempFile != null && File.Exists(tempFile))
                                            {
                                                try
                                                {
                                                    File.Delete(tempFile);
                                                }
                                                catch
                                                {
                                                    // Log or handle deletion failure if needed
                                                }
                                            }
                                        }
                                    }
                                }

                                // Save the MSG file
                                msg.Save(filePath);
                            }
                        });

                        success++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{email.Subject}: {ex.Message}");
                    }

                    // Update progress
                    ProgressValue = (int)((double)(i + 1) / total * 100);
                    ProgressText = $"Exporting emails... {ProgressValue}%";
                    await Task.Delay(1); // Allow UI update
                }

                // Show results
                ShowExportResults(success, failures);
            }
            finally
            {
                SetWindowEnabled(true);
                ProgressVisibility = Visibility.Collapsed;
            }
        }

        private void AddRecipients(Email msg, XstMessage email)
        {
            // Add To recipients
            if (!string.IsNullOrEmpty(email.To))
            {
                foreach (string to in email.To.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    (string name, string address) = ParseEmailAddress(to);
                    msg.Recipients.AddTo(name, address);
                }
            }

            // Add CC recipients
            if (!string.IsNullOrEmpty(email.Cc))
            {
                foreach (string cc in email.Cc.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    (string name, string address) = ParseEmailAddress(cc);
                    msg.Recipients.AddCc(name, address);
                }
            }

            // Add BCC recipients
            if (!string.IsNullOrEmpty(email.Bcc))
            {
                foreach (string bcc in email.Bcc.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    (string name, string address) = ParseEmailAddress(bcc);
                    msg.Recipients.AddBcc(name, address);
                }
            }
        }

        private void SetMessageBody(Email msg, XstMessage email)
        {
            if (email.Body == null)
            {
                return;
            }

            string bodyText = email.Body.Text ?? string.Empty;
            string plainText = bodyText;
            string htmlText = bodyText;

            // If content is HTML
            if (bodyText.Contains("<html") || bodyText.Contains("<HTML"))
            {
                // Strip HTML for plain text
                plainText = System.Text.RegularExpressions.Regex.Replace(
                    bodyText,
                    "<[^>]*>",
                    string.Empty
                ).Trim();

                // Ensure HTML has proper structure
                if (!bodyText.Contains("<body"))
                {
                    htmlText = $@"<!DOCTYPE HTML PUBLIC ""-//W3C//DTD HTML 3.2//EN"">
<html>
<head>
<meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8"">
</head>
<body style='font-family: Calibri, Arial, sans-serif; font-size: 11pt;'>
{bodyText}
</body>
</html>";
                }
            }
            else
            {
                // Convert plain text to HTML
                htmlText = $@"<!DOCTYPE HTML PUBLIC ""-//W3C//DTD HTML 3.2//EN"">
<html>
<head>
<meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8"">
</head>
<body style='font-family: Calibri, Arial, sans-serif; font-size: 11pt;'>
{plainText.Replace(Environment.NewLine, "<br/>").Replace(" ", "&nbsp;")}
</body>
</html>";
            }

            // Set body in all formats
            msg.BodyText = plainText;
            msg.BodyHtml = htmlText;
        }

        private void ShowExportResults(int success, List<string> failures)
        {
            string message = $"Export completed:\n" +
                         $"Successfully exported: {success}\n" +
                         $"Failed to export: {failures.Count}";

            if (failures.Count > 0)
            {
                message += "\n\nWould you like to see details of the failed exports?";
                MessageBoxResult response = MessageBox.Show(message, "Export Complete",
                    MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (response == MessageBoxResult.Yes)
                {
                    _ = MessageBox.Show(string.Join("\n", failures),
                        "Failed Exports", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                _ = MessageBox.Show(message, "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion

        #region Event Handlers

        private void ExportCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkbox && checkbox.DataContext is XstMessage message)
            {
                message.SetIsSelectedForExport(checkbox.IsChecked ?? false);
                UpdateExportCount();
            }
        }

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

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string file in files)
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
                ListSortDirection direction = headerClicked != _lastHeaderClicked
                    ? ListSortDirection.Ascending
                    : _lastDirection == ListSortDirection.Ascending ?
                        ListSortDirection.Descending : ListSortDirection.Ascending;
                Binding columnBinding = headerClicked.Column.DisplayMemberBinding as Binding;
                string sortBy = columnBinding?.Path.Path ?? headerClicked.Column.Header as string;

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
            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
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
                    _ = _loadedPstFiles.Remove(selectedPstFile);
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
                _ = MessageBox.Show("Please select a folder to refresh.", "Refresh",
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


        /// <summary>
        /// Helper method to parse an email address string into name and address components
        /// </summary>
        private (string name, string address) ParseEmailAddress(string emailString)
        {
            if (string.IsNullOrEmpty(emailString))
            {
                return (string.Empty, string.Empty);
            }

            // Check if the email contains a display name
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(emailString, @"(.*?)\s*<(.+?)>");
            if (match.Success)
            {
                return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
            }

            // If no display name, return empty name and the email address
            return (string.Empty, emailString.Trim());
        }

        private string GetUniqueFilePath(string folderPath, string baseFileName, string extension)
        {
            string filePath = Path.Combine(folderPath, baseFileName + extension);

            if (!File.Exists(filePath))
            {
                return filePath;
            }

            int counter = 1;
            string newFilePath;

            do
            {
                newFilePath = Path.Combine(folderPath, $"{baseFileName}_{counter++}{extension}");
            } while (File.Exists(newFilePath));

            return newFilePath;
        }


        private void ApplyFilter()
        {
            if (_emails == null || string.IsNullOrEmpty(txtSearch.Text))
            {
                return;
            }

            ICollectionView view = CollectionViewSource.GetDefaultView(_emails);
            string searchText = txtSearch.Text.ToLower();

            view.Filter = obj =>
            {
                return obj is XstMessage email
&& (email.Subject?.ToLower().Contains(searchText) == true ||
                           email.From?.ToLower().Contains(searchText) == true);
            };
        }

        private void Sort(string sortBy, ListSortDirection direction)
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(lvEmails.ItemsSource);
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
            // First update the data
            foreach (XstMessage email in _emails)
            {
                email.SetIsSelectedForExport(true);
            }

            // Then find and update all checkboxes
            var items = lvEmails.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = lvEmails.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (item != null)
                {
                    var checkbox = FindVisualChild<CheckBox>(item);
                    if (checkbox != null)
                    {
                        checkbox.IsChecked = true;
                    }
                }
            }
            
            UpdateExportCount();
        }

        private void DeselectAllItems()
        {
            // First update the data
            foreach (XstMessage email in _emails)
            {
                email.SetIsSelectedForExport(false);
            }

            // Then find and update all checkboxes
            var items = lvEmails.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = lvEmails.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (item != null)
                {
                    var checkbox = FindVisualChild<CheckBox>(item);
                    if (checkbox != null)
                    {
                        checkbox.IsChecked = false;
                    }
                }
            }
            
            UpdateExportCount();
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T found)
                {
                    return found;
                }
                
                T childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }

        public void UpdateExportCount()
        {
            ExportCount = _allEmails?.Count(e => e.GetIsSelectedForExport()) ?? 0;
        }

        private string GetPstFilesPath()
        {
            // Get the AppData Roaming path and create PSTInsight directory if it doesn't exist
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PSTInsight"
            );
            Directory.CreateDirectory(appDataPath);
            return Path.Combine(appDataPath, "pst_paths.txt");
        }

        private void SavePstFilePaths()
        {
            try
            {
                List<string> paths = cmbPstFiles.Items.Cast<string>().ToList();
                File.WriteAllLines(GetPstFilesPath(), paths);
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Error saving PST file paths: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSavedPstFiles()
        {
            string pstFilesPath = GetPstFilesPath();
            if (File.Exists(pstFilesPath))
            {
                try
                {
                    string[] paths = File.ReadAllLines(pstFilesPath);
                    foreach (string path in paths.Where(File.Exists))
                    {
                        _ = LoadPstFileAsync(path);
                    }
                }
                catch (Exception ex)
                {
                    _ = MessageBox.Show($"Error loading saved PST file paths: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (!(lvEmails.View is GridView gridView))
            {
                return;
            }

            double totalWidth = lvEmails.ActualWidth;

            if (GetDescendantByType(lvEmails, typeof(ScrollViewer)) is ScrollViewer scrollViewer &&
                scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            {
                totalWidth -= SystemParameters.VerticalScrollBarWidth;
            }

            const double exportColumnWidth = 53;
            const double attachmentColumnWidth = 80;
            totalWidth -= exportColumnWidth + attachmentColumnWidth;

            List<GridViewColumn> columnsToAdjust = gridView.Columns
                .Where(c => c.Header.ToString() != "Export?" && c.Header.ToString() != "Attachment")
                .ToList();

            if (columnsToAdjust.Any())
            {
                double widthPerColumn = totalWidth / columnsToAdjust.Count;
                foreach (GridViewColumn column in columnsToAdjust)
                {
                    column.Width = widthPerColumn;
                }
            }
        }

        private static DependencyObject GetDescendantByType(Visual element, Type type)
        {
            if (element == null)
            {
                return null;
            }

            if (element.GetType() == type)
            {
                return element;
            }

            DependencyObject foundElement = null;
            if (element is FrameworkElement)
            {
                _ = (element as FrameworkElement).ApplyTemplate();
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                Visual visual = VisualTreeHelper.GetChild(element, i) as Visual;
                foundElement = GetDescendantByType(visual, type);
                if (foundElement != null)
                {
                    break;
                }
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