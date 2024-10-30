using HtmlAgilityPack;
using MsgKit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
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
            try
            {
                await webView.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Error initializing WebView: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region PST File Operations

        private async Task LoadPstFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException($"PST file not found: {filePath}");
            }

            // Check if file is already loaded
            if (_loadedPstFiles.ContainsKey(filePath))
            {
                return; // Skip if already loaded
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
            catch (Exception)
            {
                // Remove from dictionary if loading failed
                if (_loadedPstFiles.ContainsKey(filePath))
                {
                    _ = _loadedPstFiles.Remove(filePath);
                }

                // Remove from combo box if loading failed
                if (cmbPstFiles.Items.Contains(filePath))
                {
                    cmbPstFiles.Items.Remove(filePath);
                }

                throw; // Rethrow the exception to be handled by caller
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

            txtSubject.Text = email.Subject ?? string.Empty;
            txtFrom.Text = email.From ?? string.Empty;
            txtDate.Text = email.Date?.ToString("g") ?? string.Empty;

            try
            {
                string htmlContent = email.Body?.Text;
                if (!string.IsNullOrEmpty(htmlContent))
                {
                    // If content doesn't have HTML structure, wrap it
                    if (!htmlContent.ToLower().Contains("<html"))
                    {
                        htmlContent = $@"<!DOCTYPE html>
                                                <html>
                                                <head>
                                                    <meta charset='utf-8'>
                                                    <style>
                                                        body {{ 
                                                            font-family: Calibri, Arial, sans-serif; 
                                                            font-size: 12pt; 
                                                            margin: 20px;
                                                        }}
                                                    </style>
                                                </head>
                                                <body>
                                                    {htmlContent}
                                                </body>
                                                </html>";
                    }

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
            List<XstMessage> selectedEmails = _allEmails.Where(e => e.GetIsSelectedForExport()).ToList();
            if (selectedEmails.Count == 0)
            {
                _ = MessageBox.Show("Please select at least one email to export.", "No Emails Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SetWindowEnabled(false);
                ProgressVisibility = Visibility.Visible;
                ProgressValue = 0;
                ProgressText = "Exporting emails...";

                int total = selectedEmails.Count;
                int success = 0;
                List<string> failures = new List<string>();

                for (int i = 0; i < total; i++)
                {
                    XstMessage email = selectedEmails[i];
                    try
                    {
                        string baseFileName = GetSafeFileName(email.Subject);
                        string filePath = GetUniqueFilePath(outputPath, baseFileName, ".msg");

                        // Parse sender information
                        (string fromName, string fromAddress) = ParseEmailAddress(email.From ?? string.Empty);

                        using (var msg = new Email(new Sender(fromName, fromAddress), email.Subject ?? string.Empty))
                        {
                            // Set email properties including sent date
                            msg.SentOn = email.Date ?? DateTime.Now;
                            msg.Subject = email.Subject ?? string.Empty;

                            // Handle recipients
                            if (!string.IsNullOrEmpty(email.To))
                            {
                                foreach (string to in email.To.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    (string name, string address) = ParseEmailAddress(to);
                                    msg.Recipients.AddTo(address, name);
                                }
                            }

                            if (!string.IsNullOrEmpty(email.Cc))
                            {
                                foreach (string cc in email.Cc.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    (string name, string address) = ParseEmailAddress(cc);
                                    msg.Recipients.AddCc(address, name);
                                }
                            }

                            // Handle body content
                            string bodyText = email.Body?.Text ?? string.Empty;

                            if (IsHtmlContent(bodyText))
                            {
                                // Clean up the HTML content
                                var doc = new HtmlDocument();
                                doc.LoadHtml(bodyText);

                                // Convert HTML entities and clean up formatting
                                bodyText = WebUtility.HtmlDecode(doc.DocumentNode.InnerHtml)
                                    .Replace("&nbsp;", " ")
                                    .Replace("&#160;", " ")
                                    .Replace("\r\n", "\n")
                                    .Replace("\n\n\n", "\n\n");

                                // Set both HTML and plain text versions
                                msg.BodyHtml = $@"<!DOCTYPE html>
                                                <html>
                                                <head>
                                                    <meta charset='utf-8'>
                                                    <style>
                                                        body {{ 
                                                            font-family: Calibri, Arial, sans-serif; 
                                                            font-size: 11pt; 
                                                            line-height: 1.4;
                                                            margin: 10px;
                                                        }}
                                                        a {{ color: #0563C1; text-decoration: underline; }}
                                                    </style>
                                                </head>
                                                <body>{bodyText}</body>
                                                </html>";
                                msg.BodyText = HtmlToPlainText(bodyText);
                            }
                            else
                            {
                                // For plain text, create a simple HTML version
                                msg.BodyText = bodyText;
                                msg.BodyHtml = ConvertPlainTextToHtml(bodyText);
                            }

                            // Handle attachments
                            if (email.Attachments != null)
                            {
                                foreach (XstAttachment attachment in email.Attachments)
                                {
                                    // Skip inline/embedded images
                                    if (attachment.IsInlineAttachment)
                                    {
                                        continue;
                                    }

                                    try
                                    {
                                        // Create a unique temp file path for each attachment
                                        string tempPath = Path.Combine(
                                            Path.GetTempPath(),
                                            Path.GetRandomFileName(),
                                            attachment.FileNameForSaving
                                        );

                                        // Ensure directory exists
                                        Directory.CreateDirectory(Path.GetDirectoryName(tempPath));

                                        // Save attachment to temp location
                                        attachment.SaveToFile(tempPath);

                                        try
                                        {
                                            // Add attachment to MSG
                                            msg.Attachments.Add(tempPath);
                                        }
                                        finally
                                        {
                                            // Clean up temp file after adding to MSG
                                            try
                                            {
                                                if (File.Exists(tempPath))
                                                    File.Delete(tempPath);
                                                if (Directory.Exists(Path.GetDirectoryName(tempPath)))
                                                    Directory.Delete(Path.GetDirectoryName(tempPath), true);
                                            }
                                            catch (IOException)
                                            {
                                                // Log but continue if cleanup fails
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        failures.Add($"Attachment '{attachment.FileNameForSaving}' in email '{email.Subject}': {ex.Message}");
                                    }
                                }
                            }

                            // Save the message after all attachments are added
                            msg.Save(filePath);
                            success++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{email.Subject}: {ex.Message}");
                    }

                    ProgressValue = (int)((double)(i + 1) / total * 100);
                    ProgressText = $"Exporting emails... {ProgressValue}%";
                    await Task.Delay(1);
                }

                ShowExportResults(success, failures);
            }
            finally
            {
                SetWindowEnabled(true);
                ProgressVisibility = Visibility.Collapsed;
            }
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
                    try
                    {
                        if (Path.GetExtension(file).Equals(".pst", StringComparison.OrdinalIgnoreCase))
                        {
                            // Load each dropped file sequentially and wait for completion
                            await LoadPstFileAsync(file);

                            // Add a small delay between loads to prevent overwhelming the system
                            await Task.Delay(100);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log the error but continue loading other files
                        _ = MessageBox.Show($"Error loading PST file '{file}': {ex.Message}",
                            "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Space)
            {
                if (lvEmails.SelectedItem is XstMessage selectedItem)
                {
                    // Toggle the export state
                    bool newState = !selectedItem.GetIsSelectedForExport();
                    selectedItem.SetIsSelectedForExport(newState);

                    // Find and update the checkbox
                    if (lvEmails.ItemContainerGenerator.ContainerFromItem(selectedItem) is ListViewItem container)
                    {
                        var checkbox = FindVisualChild<CheckBox>(container);
                        if (checkbox != null)
                        {
                            checkbox.IsChecked = newState;
                        }
                    }

                    UpdateExportCount();
                    e.Handled = true;
                }
            }
        }

        private void ListView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                if (lvEmails.SelectedItem is XstMessage selectedItem)
                {
                    if (lvEmails.ItemContainerGenerator.ContainerFromItem(selectedItem) is ListViewItem container)
                    {
                        var checkbox = FindVisualChild<CheckBox>(container);
                        checkbox?.Focus();
                    }
                }
            }
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
                if (obj is XstMessage email)
                {
                    string subject = email.Subject?.ToLower() ?? string.Empty;
                    string from = email.From?.ToLower() ?? string.Empty;

                    return subject.Contains(searchText) ||
                           from.Contains(searchText);
                }
                return false;
            };
        }

        private void Sort(string sortBy, ListSortDirection direction)
        {
            if (_emails == null) return;

            // Create a new list for sorting
            var sortedList = new List<XstMessage>(_emails);

            // Sort based on the column
            switch (sortBy?.ToLower())
            {
                case "export?":
                    if (direction == ListSortDirection.Ascending)
                        sortedList.Sort((a, b) => a.GetIsSelectedForExport().CompareTo(b.GetIsSelectedForExport()));
                    else
                        sortedList.Sort((a, b) => b.GetIsSelectedForExport().CompareTo(a.GetIsSelectedForExport()));
                    break;

                case "subject":
                    if (direction == ListSortDirection.Ascending)
                        sortedList.Sort((a, b) => CompareStrings(a.Subject, b.Subject));
                    else
                        sortedList.Sort((a, b) => CompareStrings(b.Subject, a.Subject));
                    break;

                case "from":
                    if (direction == ListSortDirection.Ascending)
                        sortedList.Sort((a, b) => CompareStrings(a.From, b.From));
                    else
                        sortedList.Sort((a, b) => CompareStrings(b.From, a.From));
                    break;

                case "date":
                    if (direction == ListSortDirection.Ascending)
                        sortedList.Sort((a, b) => Nullable.Compare(a.Date, b.Date));
                    else
                        sortedList.Sort((a, b) => Nullable.Compare(b.Date, a.Date));
                    break;

                case "attachment":
                case "attachments":
                    if (direction == ListSortDirection.Ascending)
                        sortedList.Sort((a, b) => GetAttachmentCount(a).CompareTo(GetAttachmentCount(b)));
                    else
                        sortedList.Sort((a, b) => GetAttachmentCount(b).CompareTo(GetAttachmentCount(a)));
                    break;
            }

            // Update the observable collection
            _emails.Clear();
            foreach (var email in sortedList)
            {
                _emails.Add(email);
            }
        }

        private int GetAttachmentCount(XstMessage message)
        {
            return message.Attachments?.Count(a => !a.IsInlineAttachment) ?? 0;
        }

        private static int CompareStrings(string a, string b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            return string.Compare(a, b, StringComparison.CurrentCulture);
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
                if (lvEmails.ItemContainerGenerator.ContainerFromIndex(i) is ListViewItem item)
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
                if (lvEmails.ItemContainerGenerator.ContainerFromIndex(i) is ListViewItem item)
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
                // Use using statement to ensure file handle is released
                using (StreamWriter writer = new StreamWriter(GetPstFilesPath(), false))
                {
                    foreach (string path in paths)
                    {
                        writer.WriteLine(path);
                    }
                }
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Error saving PST file paths: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LoadSavedPstFiles()
        {
            string pstFilesPath = GetPstFilesPath();
            if (File.Exists(pstFilesPath))
            {
                try
                {
                    // Read all paths but load them one at a time
                    string[] paths = File.ReadAllLines(pstFilesPath);
                    foreach (string path in paths.Where(File.Exists))
                    {
                        try
                        {
                            // Load each PST file sequentially and wait for completion
                            await LoadPstFileAsync(path);

                            // Add a small delay between loads to prevent overwhelming the system
                            await Task.Delay(100);
                        }
                        catch (Exception ex)
                        {
                            // Log the error but continue loading other files
                            _ = MessageBox.Show($"Error loading PST file '{path}': {ex.Message}",
                                "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _ = MessageBox.Show($"Error loading saved PST file paths: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private bool IsHtmlContent(string content)
        {
            return !string.IsNullOrEmpty(content) &&
                   (content.ToLower().Contains("<html") ||
                    content.ToLower().Contains("<!doctype") ||
                    content.ToLower().Contains("<body"));
        }

        private string HtmlToPlainText(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Handle links specially - add URL in parentheses after link text
            var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
            if (linkNodes != null)
            {
                foreach (var link in linkNodes)
                {
                    string href = link.GetAttributeValue("href", string.Empty);
                    string text = link.InnerText.Trim();

                    // Only add the URL if it's different from the text
                    if (!string.IsNullOrEmpty(href) && !href.Equals(text, StringComparison.OrdinalIgnoreCase))
                    {
                        link.ParentNode.ReplaceChild(
                            doc.CreateTextNode($"{text} ({href})"),
                            link
                        );
                    }
                }
            }

            // Replace <br> and <p> with newlines
            var brNodes = doc.DocumentNode.SelectNodes("//br");
            if (brNodes != null)
            {
                foreach (var node in brNodes)
                {
                    node.ParentNode.ReplaceChild(doc.CreateTextNode("\n"), node);
                }
            }

            var pNodes = doc.DocumentNode.SelectNodes("//p");
            if (pNodes != null)
            {
                foreach (var node in pNodes)
                {
                    node.ParentNode.ReplaceChild(doc.CreateTextNode("\n" + node.InnerText + "\n"), node);
                }
            }

            // Get plain text and clean up whitespace
            string plainText = doc.DocumentNode.InnerText;

            // Clean up excessive whitespace while preserving paragraph structure
            plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"\n{3,}", "\n\n");
            plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"\s+", " ");

            return plainText.Trim();
        }

        private string ConvertPlainTextToHtml(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;

            // Encode HTML special characters
            string htmlEncodedText = WebUtility.HtmlEncode(plainText);

            // Convert newlines to <br> tags
            htmlEncodedText = htmlEncodedText.Replace(Environment.NewLine, "<br>");

            return $@"<!DOCTYPE HTML PUBLIC ""-//W3C//DTD HTML 4.0 Transitional//EN"">
<html>
<head>
<meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"">
<style>
body {{ font-family: Calibri, Arial, Helvetica, sans-serif; font-size: 11pt; }}
</style>
</head>
<body>
{htmlEncodedText}
</body>
</html>";
        }

        #endregion
    }

    public class ExportStateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is XstMessage message)
            {
                return message.GetIsSelectedForExport();
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }

    public class AttachmentFilterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IEnumerable<XstAttachment> attachments)
            {
                return attachments?.Where(a => !a.IsInlineAttachment);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HasNonInlineAttachmentsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is XstMessage message)
            {
                return message.Attachments?.Any(a => !a.IsInlineAttachment) ?? false;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}