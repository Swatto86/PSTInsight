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
    /// <summary>
    /// Main window class for the PST Insight application.
    /// Handles PST file loading, email display, and file management operations.
    /// Implements INotifyPropertyChanged for UI binding updates.
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Fields

        // Dictionary to store loaded PST files with their file paths as keys
        private readonly Dictionary<string, XstFile> _loadedPstFiles;

        // Currently active PST file
        private XstFile _currentPstFile;

        // Observable collection of emails for UI binding
        private ObservableCollection<XstMessage> _emails;

        // Complete list of emails (unfiltered)
        private List<XstMessage> _allEmails;

        // Progress tracking properties
        private string _progressText;
        private int _progressValue;
        private Visibility _progressVisibility;

        // WebView visibility control
        private Visibility _webViewVisibility;

        // Export count tracking
        private int _exportCount;
        private Visibility _exportCountVisibility;

        // ListView sorting properties
        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection;

        #endregion

        #region Properties

        /// <summary>
        /// Observable collection of emails displayed in the ListView
        /// </summary>
        public ObservableCollection<XstMessage> Emails
        {
            get => _emails;
            set
            {
                _emails = value;
                OnPropertyChanged(nameof(Emails));
            }
        }

        /// <summary>
        /// Progress bar value (0-100)
        /// </summary>
        public int ProgressValue
        {
            get => _progressValue;
            set
            {
                _progressValue = value;
                OnPropertyChanged(nameof(ProgressValue));
            }
        }

        /// <summary>
        /// Progress status text
        /// </summary>
        public string ProgressText
        {
            get => _progressText;
            set
            {
                _progressText = value;
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        /// <summary>
        /// Progress bar visibility state
        /// </summary>
        public Visibility ProgressVisibility
        {
            get => _progressVisibility;
            set
            {
                _progressVisibility = value;
                OnPropertyChanged(nameof(ProgressVisibility));
            }
        }

        /// <summary>
        /// WebView control visibility state
        /// </summary>
        public Visibility WebViewVisibility
        {
            get => _webViewVisibility;
            set
            {
                _webViewVisibility = value;
                OnPropertyChanged(nameof(WebViewVisibility));
            }
        }

        /// <summary>
        /// Number of emails selected for export
        /// </summary>
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

        /// <summary>
        /// Export count label visibility state
        /// </summary>
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

        /// <summary>
        /// Command for saving email attachments
        /// </summary>
        public ICommand SaveAttachmentCommand { get; private set; }

        #endregion

        #region Constructor and Initialization

        /// <summary>
        /// Initializes the main window and sets up initial state
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // Initialize collections
            _loadedPstFiles = new Dictionary<string, XstFile>();
            _emails = new ObservableCollection<XstMessage>();
            _allEmails = new List<XstMessage>();

            // Initialize commands
            SaveAttachmentCommand = new AsyncRelayCommand<XstAttachment>(SaveAttachment);

            // Initialize WebView and load saved PST files
            InitializeAsync();
            LoadSavedPstFiles();

            // Subscribe to window loaded event
            Loaded += MainWindow_Loaded;

            // Set initial visibility states
            _progressVisibility = Visibility.Collapsed;
            _webViewVisibility = Visibility.Collapsed;
            _exportCountVisibility = Visibility.Collapsed;

            // Set initial sort direction
            _lastDirection = ListSortDirection.Ascending;
        }

        /// <summary>
        /// Initializes the WebView2 control asynchronously
        /// </summary>
        private async void InitializeAsync()
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show(
                    $"Error initializing WebView: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        #endregion
        #region PST File Operations

        /// <summary>
        /// Loads a PST file asynchronously from the specified file path.
        /// Handles file validation, loading, and UI updates.
        /// </summary>
        /// <param name="filePath">Path to the PST file to load</param>
        /// <returns>Asynchronous task</returns>
        private async Task LoadPstFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException($"PST file not found: {filePath}");
            }

            // Skip if file is already loaded
            if (_loadedPstFiles.ContainsKey(filePath))
            {
                return;
            }

            try
            {
                SetWindowEnabled(false);
                ProgressVisibility = Visibility.Visible;
                ProgressValue = 0;
                ProgressText = $"Loading PST file: {Path.GetFileName(filePath)}...";

                // Load PST file asynchronously
                await Task.Run(() =>
                {
                    XstFile pstFile = new XstFile(filePath);
                    _loadedPstFiles[filePath] = pstFile;
                    _currentPstFile = pstFile;
                });

                // Update UI with new PST file
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
                // Clean up failed loads
                if (_loadedPstFiles.ContainsKey(filePath))
                {
                    _ = _loadedPstFiles.Remove(filePath);
                }

                if (cmbPstFiles.Items.Contains(filePath))
                {
                    cmbPstFiles.Items.Remove(filePath);
                }

                throw; // Rethrow for caller handling
            }
            finally
            {
                SetWindowEnabled(true);
                ProgressVisibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Populates the folder TreeView with the PST file structure.
        /// Filters out system folders and creates a hierarchical view.
        /// </summary>
        /// <param name="rootFolder">Root folder of the PST file</param>
        private void PopulateFolderTreeView(XstFolder rootFolder)
        {
            tvFolders.Items.Clear();
            TreeViewItem rootItem = CreateTreeViewItem(rootFolder);
            if (rootItem != null)
            {
                _ = tvFolders.Items.Add(rootItem);
            }
        }

        /// <summary>
        /// Creates a TreeViewItem for a folder, including message count and subfolder hierarchy.
        /// Filters out system folders and creates the visual representation of the folder structure.
        /// </summary>
        /// <param name="folder">Folder to create TreeViewItem for</param>
        /// <returns>TreeViewItem representing the folder, or null if folder should be excluded</returns>
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

            // Create header panel with folder name and message count
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

            // Create TreeViewItem
            TreeViewItem item = new TreeViewItem
            {
                Header = headerPanel,
                Tag = folder,
                IsExpanded = true
            };

            // Add subfolders recursively
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

        /// <summary>
        /// Loads emails from a specified folder asynchronously.
        /// Updates the UI with progress and handles the email list display.
        /// </summary>
        /// <param name="folder">Folder containing emails to load</param>
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
                _ = MessageBox.Show(
                    $"Error loading emails: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                SetWindowEnabled(true);
                ProgressVisibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Saves an email attachment to a user-specified location.
        /// Handles file dialog and saving process.
        /// </summary>
        /// <param name="attachment">Attachment to save</param>
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
                    await Task.Run(() => attachment.SaveToFile(saveFileDialog.FileName));
                }
                catch (Exception ex)
                {
                    _ = MessageBox.Show(
                        $"Error saving attachment: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }

        /// <summary>
        /// Displays the selected email's content in the preview pane.
        /// Handles both HTML and plain text content with appropriate formatting.
        /// </summary>
        /// <param name="email">Email message to display</param>
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
                    // Wrap plain text in HTML structure if needed
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
                _ = MessageBox.Show(
                    $"Error displaying email content: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                WebViewVisibility = Visibility.Collapsed;
            }
        }
        /// <summary>
        /// Clears the email preview display.
        /// Resets all preview fields to empty state.
        /// </summary>
        private void ClearEmailDisplay()
        {
            txtSubject.Text = string.Empty;
            txtFrom.Text = string.Empty;
            txtDate.Text = string.Empty;
            webView.NavigateToString("<html><body></body></html>");
            WebViewVisibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Exports selected emails to MSG format.
        /// Handles the complete export process including attachments and metadata.
        /// </summary>
        /// <param name="outputPath">Directory path for exported files</param>
        private async Task ExportSelectedEmailsAsync(string outputPath)
        {
            List<XstMessage> selectedEmails = _allEmails.Where(e => e.GetIsSelectedForExport()).ToList();
            if (selectedEmails.Count == 0)
            {
                _ = MessageBox.Show(
                    "Please select at least one email to export.",
                    "No Emails Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
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

                        using (Email msg = new Email(new Sender(fromName, fromAddress), email.Subject ?? string.Empty))
                        {
                            // Set email properties
                            msg.SentOn = email.Date ?? DateTime.Now;
                            msg.Subject = email.Subject ?? string.Empty;

                            // Process recipients
                            ProcessRecipients(msg, email);

                            // Handle body content
                            ProcessEmailBody(msg, email.Body?.Text ?? string.Empty);

                            // Process attachments
                            await ProcessAttachments(msg, email.Attachments);

                            // Save the message
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
                    await Task.Delay(1); // Allow UI updates
                }

                ShowExportResults(success, failures);
            }
            finally
            {
                SetWindowEnabled(true);
                ProgressVisibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Processes recipients for an email message.
        /// Handles To and CC recipients with proper name/address parsing.
        /// </summary>
        /// <param name="msg">Email message being created</param>
        /// <param name="email">Source email message</param>
        private void ProcessRecipients(Email msg, XstMessage email)
        {
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
        }

        /// <summary>
        /// Processes email body content for export.
        /// Handles both HTML and plain text content with appropriate formatting.
        /// </summary>
        /// <param name="msg">Email message being created</param>
        /// <param name="bodyText">Source email body text</param>
        private void ProcessEmailBody(Email msg, string bodyText)
        {
            if (string.IsNullOrEmpty(bodyText))
            {
                return;
            }

            if (IsHtmlContent(bodyText))
            {
                // Clean up HTML content
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(bodyText);

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
                msg.BodyText = bodyText;
                msg.BodyHtml = ConvertPlainTextToHtml(bodyText);
            }
        }

        /// <summary>
        /// Processes attachments for an email message.
        /// Handles temporary file management and attachment addition.
        /// </summary>
        /// <param name="msg">Email message being created</param>
        /// <param name="attachments">Collection of attachments to process</param>
        /// <summary>
        /// Processes attachments for an email message asynchronously.
        /// Handles temporary file management and attachment addition.
        /// </summary>
        /// <param name="msg">Email message being created</param>
        /// <param name="attachments">Collection of attachments to process</param>
        private async Task ProcessAttachments(Email msg, IEnumerable<XstAttachment> attachments)
        {
            if (attachments == null)
            {
                return;
            }

            foreach (XstAttachment attachment in attachments)
            {
                if (attachment.IsInlineAttachment)
                {
                    continue;
                }

                try
                {
                    string tempPath = Path.Combine(
                        Path.GetTempPath(),
                        Path.GetRandomFileName(),
                        attachment.FileNameForSaving
                    );

                    // Create directory asynchronously
                    _ = await Task.Run(() => Directory.CreateDirectory(Path.GetDirectoryName(tempPath)));

                    // Save attachment to temp file asynchronously
                    await Task.Run(() => attachment.SaveToFile(tempPath));

                    try
                    {
                        // Add attachment to message
                        await Task.Run(() => msg.Attachments.Add(tempPath));
                    }
                    finally
                    {
                        // Clean up temp files asynchronously
                        await Task.Run(() =>
                        {
                            try
                            {
                                if (File.Exists(tempPath))
                                {
                                    File.Delete(tempPath);
                                }

                                if (Directory.Exists(Path.GetDirectoryName(tempPath)))
                                {
                                    Directory.Delete(Path.GetDirectoryName(tempPath), true);
                                }
                            }
                            catch (IOException)
                            {
                                // Log but continue if cleanup fails
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error processing attachment '{attachment.FileNameForSaving}': {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Displays the results of the email export operation.
        /// Shows success count and optionally displays failure details.
        /// </summary>
        /// <param name="success">Number of successfully exported emails</param>
        /// <param name="failures">List of failed exports with error messages</param>
        private void ShowExportResults(int success, List<string> failures)
        {
            string message = $"Export completed:\n" +
                           $"Successfully exported: {success}\n" +
                           $"Failed to export: {failures.Count}";

            if (failures.Count > 0)
            {
                message += "\n\nWould you like to see details of the failed exports?";
                MessageBoxResult response = MessageBox.Show(
                    message,
                    "Export Complete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );

                if (response == MessageBoxResult.Yes)
                {
                    _ = MessageBox.Show(
                        string.Join("\n", failures),
                        "Failed Exports",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
            }
            else
            {
                _ = MessageBox.Show(
                    message,
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }
        #region Event Handlers

        /// <summary>
        /// Handles the click event of the export checkbox.
        /// Updates the export selection state and export count.
        /// </summary>
        private void ExportCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkbox && checkbox.DataContext is XstMessage message)
            {
                message.SetIsSelectedForExport(checkbox.IsChecked ?? false);
                UpdateExportCount();
            }
        }

        /// <summary>
        /// Handles the click event for loading PST files.
        /// Opens file dialog and initiates PST file loading.
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
        /// Handles drag-drop events for PST files.
        /// Supports multiple file drops and handles errors individually.
        /// </summary>
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
                            await LoadPstFileAsync(file);
                            await Task.Delay(100); // Prevent system overload
                        }
                    }
                    catch (Exception ex)
                    {
                        _ = MessageBox.Show(
                            $"Error loading PST file '{file}': {ex.Message}",
                            "Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Handles drag-over events to show valid drop targets.
        /// Updates cursor to show when files can be dropped.
        /// </summary>
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ?
                DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// Handles folder selection changes in the TreeView.
        /// Loads emails from the selected folder.
        /// </summary>
        private async void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem selectedItem && selectedItem.Tag is XstFolder selectedFolder)
            {
                await LoadEmailsAsync(selectedFolder);
            }
        }

        /// <summary>
        /// Handles email selection changes in the ListView.
        /// Updates the email preview display.
        /// </summary>
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

        /// <summary>
        /// Handles column header clicks for sorting.
        /// Manages sort direction and column sorting.
        /// </summary>
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader headerClicked &&
                headerClicked.Role != GridViewColumnHeaderRole.Padding)
            {
                ListSortDirection direction = headerClicked != _lastHeaderClicked
                    ? ListSortDirection.Ascending
                    : _lastDirection == ListSortDirection.Ascending
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending;

                Binding columnBinding = headerClicked.Column.DisplayMemberBinding as Binding;
                string sortBy = columnBinding?.Path.Path ?? headerClicked.Column.Header as string;

                Sort(sortBy, direction);

                _lastHeaderClicked = headerClicked;
                _lastDirection = direction;
            }
        }

        /// <summary>
        /// Handles select all button click.
        /// Selects all visible emails for export.
        /// </summary>
        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            SelectAllItems();
        }

        /// <summary>
        /// Handles deselect all button click.
        /// Deselects all emails from export.
        /// </summary>
        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            DeselectAllItems();
        }

        /// <summary>
        /// Handles export button click.
        /// Opens folder dialog and initiates email export.
        /// </summary>
        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
            if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await ExportSelectedEmailsAsync(folderBrowserDialog.SelectedPath);
            }
        }

        /// <summary>
        /// Handles PST file removal button click.
        /// Removes selected PST file from the application.
        /// </summary>
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

        /// <summary>
        /// Handles refresh button click.
        /// Reloads emails from the selected folder.
        /// </summary>
        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (tvFolders.SelectedItem is TreeViewItem selectedItem &&
                selectedItem.Tag is XstFolder selectedFolder)
            {
                await LoadEmailsAsync(selectedFolder);
            }
            else
            {
                _ = MessageBox.Show(
                    "Please select a folder to refresh.",
                    "Refresh",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }

        /// <summary>
        /// Handles search text changes.
        /// Applies filtering to the email list.
        /// </summary>
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        /// <summary>
        /// Handles PST file selection changes in the combo box.
        /// Loads selected PST file if needed and updates UI.
        /// </summary>
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

        /// <summary>
        /// Handles window loaded event.
        /// Adjusts column widths when window is first loaded.
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AdjustColumnWidths();
        }

        /// <summary>
        /// Handles ListView size changes.
        /// Adjusts column widths when ListView size changes.
        /// </summary>
        private void ListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            AdjustColumnWidths();
        }

        /// <summary>
        /// Handles keyboard input for the window.
        /// Manages space bar functionality for email selection.
        /// </summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Space)
            {
                if (lvEmails.SelectedItem is XstMessage selectedItem)
                {
                    bool newState = !selectedItem.GetIsSelectedForExport();
                    selectedItem.SetIsSelectedForExport(newState);

                    if (lvEmails.ItemContainerGenerator.ContainerFromItem(selectedItem) is ListViewItem container)
                    {
                        CheckBox checkbox = FindVisualChild<CheckBox>(container);
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

        /// <summary>
        /// Handles keyboard preview events for the ListView.
        /// Manages focus for checkbox selection.
        /// </summary>
        private void ListView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                if (lvEmails.SelectedItem is XstMessage selectedItem)
                {
                    if (lvEmails.ItemContainerGenerator.ContainerFromItem(selectedItem) is ListViewItem container)
                    {
                        CheckBox checkbox = FindVisualChild<CheckBox>(container);
                        _ = (checkbox?.Focus());
                    }
                }
            }
        }

        #endregion
        #region Helper Methods

        /// <summary>
        /// Parses an email address string into name and address components.
        /// Handles various email address formats including display names.
        /// </summary>
        /// <param name="emailString">Email string to parse</param>
        /// <returns>Tuple containing name and address components</returns>
        private (string name, string address) ParseEmailAddress(string emailString)
        {
            if (string.IsNullOrEmpty(emailString))
            {
                return (string.Empty, string.Empty);
            }

            System.Text.RegularExpressions.Match match =
                System.Text.RegularExpressions.Regex.Match(emailString, @"(.*?)\s*<(.+?)>");

            return match.Success ? ((string name, string address))(match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim()) : ((string name, string address))(string.Empty, emailString.Trim());
        }

        /// <summary>
        /// Generates a unique file path for saving files.
        /// Appends numbering if filename already exists.
        /// </summary>
        /// <param name="folderPath">Target folder path</param>
        /// <param name="baseFileName">Base filename without extension</param>
        /// <param name="extension">File extension including dot</param>
        /// <returns>Unique file path</returns>
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

        /// <summary>
        /// Applies search filter to email list.
        /// Filters based on subject and from fields.
        /// </summary>
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

                    return subject.Contains(searchText) || from.Contains(searchText);
                }
                return false;
            };
        }

        /// <summary>
        /// Sorts the email list based on specified column and direction.
        /// Handles different types of sorting including dates and attachments.
        /// </summary>
        /// <param name="sortBy">Column to sort by</param>
        /// <param name="direction">Sort direction</param>
        private void Sort(string sortBy, ListSortDirection direction)
        {
            if (_emails == null)
            {
                return;
            }

            List<XstMessage> sortedList = new List<XstMessage>(_emails);

            switch (sortBy?.ToLower())
            {
                case "export?":
                    if (direction == ListSortDirection.Ascending)
                    {
                        sortedList.Sort((a, b) => a.GetIsSelectedForExport().CompareTo(b.GetIsSelectedForExport()));
                    }
                    else
                    {
                        sortedList.Sort((a, b) => b.GetIsSelectedForExport().CompareTo(a.GetIsSelectedForExport()));
                    }

                    break;

                case "subject":
                    if (direction == ListSortDirection.Ascending)
                    {
                        sortedList.Sort((a, b) => CompareStrings(a.Subject, b.Subject));
                    }
                    else
                    {
                        sortedList.Sort((a, b) => CompareStrings(b.Subject, a.Subject));
                    }

                    break;

                case "from":
                    if (direction == ListSortDirection.Ascending)
                    {
                        sortedList.Sort((a, b) => CompareStrings(a.From, b.From));
                    }
                    else
                    {
                        sortedList.Sort((a, b) => CompareStrings(b.From, a.From));
                    }

                    break;

                case "date":
                    if (direction == ListSortDirection.Ascending)
                    {
                        sortedList.Sort((a, b) => Nullable.Compare(a.Date, b.Date));
                    }
                    else
                    {
                        sortedList.Sort((a, b) => Nullable.Compare(b.Date, a.Date));
                    }

                    break;

                case "attachment":
                case "attachments":
                    if (direction == ListSortDirection.Ascending)
                    {
                        sortedList.Sort((a, b) => GetAttachmentCount(a).CompareTo(GetAttachmentCount(b)));
                    }
                    else
                    {
                        sortedList.Sort((a, b) => GetAttachmentCount(b).CompareTo(GetAttachmentCount(a)));
                    }

                    break;
            }

            _emails.Clear();
            foreach (XstMessage email in sortedList)
            {
                _emails.Add(email);
            }
        }

        /// <summary>
        /// Gets the count of non-inline attachments for an email.
        /// </summary>
        private int GetAttachmentCount(XstMessage message)
        {
            return message.Attachments?.Count(a => !a.IsInlineAttachment) ?? 0;
        }

        /// <summary>
        /// Compares two strings with null handling.
        /// </summary>
        private static int CompareStrings(string a, string b)
        {
            if (a == null && b == null)
            {
                return 0;
            }

            return a == null ? -1 : b == null ? 1 : string.Compare(a, b, StringComparison.CurrentCulture);
        }

        /// <summary>
        /// Selects all visible emails for export.
        /// Updates checkboxes and export count.
        /// </summary>
        private void SelectAllItems()
        {
            foreach (XstMessage email in _emails)
            {
                email.SetIsSelectedForExport(true);
            }

            ItemCollection items = lvEmails.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (lvEmails.ItemContainerGenerator.ContainerFromIndex(i) is ListViewItem item)
                {
                    CheckBox checkbox = FindVisualChild<CheckBox>(item);
                    if (checkbox != null)
                    {
                        checkbox.IsChecked = true;
                    }
                }
            }

            UpdateExportCount();
        }

        /// <summary>
        /// Deselects all emails from export.
        /// Updates checkboxes and export count.
        /// </summary>
        private void DeselectAllItems()
        {
            foreach (XstMessage email in _emails)
            {
                email.SetIsSelectedForExport(false);
            }

            ItemCollection items = lvEmails.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (lvEmails.ItemContainerGenerator.ContainerFromIndex(i) is ListViewItem item)
                {
                    CheckBox checkbox = FindVisualChild<CheckBox>(item);
                    if (checkbox != null)
                    {
                        checkbox.IsChecked = false;
                    }
                }
            }

            UpdateExportCount();
        }

        /// <summary>
        /// Recursively finds a child control of specified type.
        /// </summary>
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

        /// <summary>
        /// Updates the count of emails selected for export.
        /// </summary>
        public void UpdateExportCount()
        {
            ExportCount = _allEmails?.Count(e => e.GetIsSelectedForExport()) ?? 0;
        }

        /// <summary>
        /// Gets the path for storing PST file paths.
        /// Creates directory if it doesn't exist.
        /// </summary>
        private string GetPstFilesPath()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PSTInsight"
            );
            _ = Directory.CreateDirectory(appDataPath);
            return Path.Combine(appDataPath, "pst_paths.txt");
        }

        /// <summary>
        /// Saves the list of PST file paths to storage.
        /// </summary>
        private void SavePstFilePaths()
        {
            try
            {
                List<string> paths = cmbPstFiles.Items.Cast<string>().ToList();
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
                _ = MessageBox.Show(
                    $"Error saving PST file paths: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// Loads saved PST file paths and attempts to load the PST files.
        /// </summary>
        private async void LoadSavedPstFiles()
        {
            string pstFilesPath = GetPstFilesPath();
            if (File.Exists(pstFilesPath))
            {
                try
                {
                    string[] paths = File.ReadAllLines(pstFilesPath);
                    foreach (string path in paths.Where(File.Exists))
                    {
                        try
                        {
                            await LoadPstFileAsync(path);
                            await Task.Delay(100);
                        }
                        catch (Exception ex)
                        {
                            _ = MessageBox.Show(
                                $"Error loading PST file '{path}': {ex.Message}",
                                "Warning",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    _ = MessageBox.Show(
                        $"Error loading saved PST file paths: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }

        /// <summary>
        /// Creates a safe filename by removing invalid characters.
        /// </summary>
        private static string GetSafeFileName(string fileName)
        {
            return string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        }

        /// <summary>
        /// Sets the window enabled state and updates cursor.
        /// </summary>
        private void SetWindowEnabled(bool enabled)
        {
            IsEnabled = enabled;
            Mouse.OverrideCursor = enabled ? null : Cursors.Wait;
        }

        /// <summary>
        /// Adjusts ListView column widths based on available space.
        /// </summary>
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

        /// <summary>
        /// Finds a descendant control of specified type in the visual tree.
        /// </summary>
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

        /// <summary>
        /// Raises the PropertyChanged event for a property.
        /// </summary>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// PropertyChanged event for INotifyPropertyChanged implementation.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Checks if content is HTML format.
        /// </summary>
        private bool IsHtmlContent(string content)
        {
            return !string.IsNullOrEmpty(content) &&
                   (content.ToLower().Contains("<html") ||
                    content.ToLower().Contains("<!doctype") ||
                    content.ToLower().Contains("<body"));
        }

        /// <summary>
        /// Converts HTML content to plain text.
        /// Preserves links and basic formatting.
        /// </summary>
        private string HtmlToPlainText(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Handle links specially
            HtmlNodeCollection linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
            if (linkNodes != null)
            {
                foreach (HtmlNode link in linkNodes)
                {
                    string href = link.GetAttributeValue("href", string.Empty);
                    string text = link.InnerText.Trim();

                    if (!string.IsNullOrEmpty(href) && !href.Equals(text, StringComparison.OrdinalIgnoreCase))
                    {
                        _ = link.ParentNode.ReplaceChild(
                            doc.CreateTextNode($"{text} ({href})"),
                            link
                        );
                    }
                }
            }

            // Handle line breaks and paragraphs
            HtmlNodeCollection brNodes = doc.DocumentNode.SelectNodes("//br");
            if (brNodes != null)
            {
                foreach (HtmlNode node in brNodes)
                {
                    _ = node.ParentNode.ReplaceChild(doc.CreateTextNode("\n"), node);
                }
            }

            HtmlNodeCollection pNodes = doc.DocumentNode.SelectNodes("//p");
            if (pNodes != null)
            {
                foreach (HtmlNode node in pNodes)
                {
                    _ = node.ParentNode.ReplaceChild(doc.CreateTextNode("\n" + node.InnerText + "\n"), node);
                }
            }

            // Clean up whitespace while preserving structure
            string plainText = doc.DocumentNode.InnerText;
            plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"\n{3,}", "\n\n");
            plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"\s+", " ");

            return plainText.Trim();
        }

        /// <summary>
        /// Converts plain text content to HTML format.
        /// Adds basic HTML structure and styling.
        /// </summary>
        /// <param name="plainText">Plain text to convert to HTML</param>
        /// <returns>Formatted HTML string</returns>
        private string ConvertPlainTextToHtml(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return string.Empty;
            }

            // Encode HTML special characters
            string htmlEncodedText = WebUtility.HtmlEncode(plainText);

            // Convert newlines to <br> tags
            htmlEncodedText = htmlEncodedText.Replace(Environment.NewLine, "<br>");

            return $@"<!DOCTYPE HTML PUBLIC ""-//W3C//DTD HTML 4.0 Transitional//EN"">
<html>
<head>
    <meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"">
    <style>
        body {{
            font-family: Calibri, Arial, Helvetica, sans-serif;
            font-size: 11pt;
            line-height: 1.4;
            margin: 10px;
        }}
    </style>
</head>
<body>
    {htmlEncodedText}
</body>
</html>";
        }

        #endregion
    }

    #region Value Converters

    /// <summary>
    /// Converts between XstMessage and export state boolean value.
    /// Used for binding export checkboxes.
    /// </summary>
    public class ExportStateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is XstMessage message ? message.GetIsSelectedForExport() : (object)false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }

    /// <summary>
    /// Filters attachment collection to exclude inline attachments.
    /// Used for displaying attachment list.
    /// </summary>
    public class AttachmentFilterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is IEnumerable<XstAttachment> attachments ? (attachments?.Where(a => !a.IsInlineAttachment)) : (object)null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Determines if a message has any non-inline attachments.
    /// Used for displaying attachment indicators.
    /// </summary>
    public class HasNonInlineAttachmentsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is XstMessage message ? message.Attachments?.Any(a => !a.IsInlineAttachment) ?? false : (object)false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    #endregion
}
#endregion