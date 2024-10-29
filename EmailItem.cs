using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using XstReader;

namespace PSTInsight
{
    /// <summary>
    /// Represents an email item with its properties and attachments.
    /// Implements INotifyPropertyChanged for UI binding.
    /// </summary>
    public class EmailItem : INotifyPropertyChanged
    {
        #region Private Fields

        private ObservableCollection<AttachmentItem> _attachments;
        private bool _hasAttachment;
        private bool _isSelectedForExport;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the EmailItem class.
        /// </summary>
        /// <param name="message">The XstMessage to create the EmailItem from.</param>
        public EmailItem(XstMessage message)
        {
            MessageView = new MessageView(message) ?? throw new ArgumentNullException(nameof(message));
            Subject = MessageView.Subject;
            FromName = MessageView.FromName;
            FromAddress = MessageView.SenderEmail;
            ToName = MessageView.ToName;
            ToAddress = MessageView.ToAddress;
            CcName = MessageView.CcName;
            CcAddress = MessageView.CcAddress;
            Date = MessageView.Date ?? DateTime.MinValue;
            Body = MessageView.Body;
            BodyHtml = MessageView.BodyHtml;

            DateValue = Date.ToString("g");
            DateView = Date.ToString("g");
            DateString = Date.ToString("g");

            EnsureMessageDetailsLoaded();
            InitializeAttachments();

            SaveAttachmentCommand = new RelayCommand<AttachmentItem>(SaveAttachment);
        }


        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the subject of the email.
        /// </summary>
        public string Subject { get; set; }

        /// <summary>
        /// Gets or sets the sender's name.
        /// </summary>
        public string FromName { get; set; }

        /// <summary>
        /// Gets or sets the sender's email address.
        /// </summary>
        public string FromAddress { get; set; }

        /// <summary>
        /// Gets the display string for the sender, combining name and address.
        /// </summary>
        public string FromDisplay => string.IsNullOrWhiteSpace(FromAddress) ? FromName : $"{FromName} <{FromAddress}>";
        /// <summary>
        /// Gets or sets the recipient's name.
        /// </summary>
        public string ToName { get; set; }

        /// <summary>
        /// Gets or sets the recipient's email address.
        /// </summary>
        public string ToAddress { get; set; }

        /// <summary>
        /// Gets or sets the recipient's name.
        /// </summary>
        public string CcName { get; set; }

        /// <summary>
        /// Gets or sets the recipient's email address.
        /// </summary>
        public string CcAddress { get; set; }

        /// <summary>
        /// Gets or sets the date of the email.
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Gets or sets the body of the email.
        /// </summary>
        public string Body { get; set; }

        /// <summary>
        /// Gets or sets the body of the email as HTML.
        /// </summary>
        public string BodyHtml { get; set; }

        /// <summary>
        /// Gets or sets the collection of attachments for this email.
        /// </summary>
        public ObservableCollection<AttachmentItem> Attachments
        {
            get => _attachments;
            private set
            {
                if (_attachments != value)
                {
                    _attachments = value;
                    OnPropertyChanged();
                    HasAttachment = _attachments?.Count > 0;
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this email has attachments.
        /// </summary>
        public bool HasAttachment
        {
            get => _hasAttachment;
            set
            {
                if (_hasAttachment != value)
                {
                    _hasAttachment = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this email is selected for export.
        /// </summary>
        public bool IsSelectedForExport
        {
            get => _isSelectedForExport;
            set
            {
                if (_isSelectedForExport != value)
                {
                    _isSelectedForExport = value;
                    OnPropertyChanged();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Gets or sets the date of the email as a string.
        /// </summary>
        public string DateValue { get; set; }

        /// <summary>
        /// Gets or sets the date of the email as a formatted string.
        /// </summary>
        public string DateView { get; set; }

        /// <summary>
        /// Gets or sets the date of the email as a formatted string.
        /// </summary>
        public string DateString { get; set; }

        /// <summary>
        /// Gets the MessageView object for this email.
        /// </summary>
        public MessageView MessageView { get; }

        /// <summary>
        /// Gets the command for saving attachments.
        /// </summary>
        public ICommand SaveAttachmentCommand { get; private set; }

        public event EventHandler SelectionChanged;

        #endregion

        #region INotifyPropertyChanged Implementation

        /// <summary>
        /// Event handler for property changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the PropertyChanged event for the specified property.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Ensures that the message details are loaded, including body and attachments.
        /// </summary>
        private void EnsureMessageDetailsLoaded()
        {
            try
            {
                Debug.WriteLine($"[EmailItem] Starting load for email: {Subject ?? "No Subject"}");

                if (MessageView?.Message == null)
                {
                    Debug.WriteLine("[EmailItem] MessageView or Message is null");
                    return;
                }

                try
                {
                    Debug.WriteLine("[EmailItem] Starting attachment processing");
                    var attachmentList = new List<XstAttachment>();

                    if (MessageView.Message.Attachments != null)
                    {
                        Debug.WriteLine("[EmailItem] Attachments collection exists");

                        // Try to get count
                        int attachmentCount = 0;
                        try
                        {
                            Debug.WriteLine("[EmailItem] Attempting to get attachment count");
                            attachmentCount = MessageView.Message.Attachments.Count();
                            Debug.WriteLine($"[EmailItem] Successfully got attachment count: {attachmentCount}");
                        }
                        catch (NotSupportedException nse)
                        {
                            Debug.WriteLine($"[EmailItem] NotSupportedException getting count: {nse.Message}");
                            try
                            {
                                attachmentCount = MessageView.Message.Attachments.ToList().Count;
                                Debug.WriteLine($"[EmailItem] Got count via ToList(): {attachmentCount}");
                            }
                            catch (NotSupportedException nse2)
                            {
                                Debug.WriteLine($"[EmailItem] NotSupportedException on ToList(): {nse2.Message}");
                            }
                        }

                        // Try to access attachments directly
                        try
                        {
                            Debug.WriteLine("[EmailItem] Attempting direct attachment enumeration");
                            foreach (var attachment in MessageView.Message.Attachments)
                            {
                                if (attachment != null)
                                {
                                    attachmentList.Add(attachment);
                                    Debug.WriteLine($"[EmailItem] Added attachment: {attachment.FileName ?? "unnamed"}");
                                }
                            }
                        }
                        catch (NotSupportedException nse)
                        {
                            Debug.WriteLine($"[EmailItem] NotSupportedException during enumeration: {nse.Message}");

                            try
                            {
                                Debug.WriteLine("[EmailItem] Attempting alternative access via ToArray");
                                var attachmentsArray = MessageView.Message.Attachments.ToArray();
                                foreach (var attachment in attachmentsArray)
                                {
                                    if (attachment != null)
                                    {
                                        attachmentList.Add(attachment);
                                        Debug.WriteLine($"[EmailItem] Added attachment via array: {attachment.FileName ?? "unnamed"}");
                                    }
                                }
                            }
                            catch (NotSupportedException nse2)
                            {
                                Debug.WriteLine($"[EmailItem] NotSupportedException on ToArray(): {nse2.Message}");
                            }
                        }
                    }
                    else
                    {
                        Debug.WriteLine("[EmailItem] Attachments collection is null");
                    }

                    Attachments = new ObservableCollection<AttachmentItem>(
                        attachmentList.Select(att => new AttachmentItem(att)).ToList()
                    );
                    Debug.WriteLine($"[EmailItem] Created ObservableCollection with {attachmentList.Count} attachments");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[EmailItem] Error in attachment processing: {ex.GetType().Name} - {ex.Message}");
                    Attachments = new ObservableCollection<AttachmentItem>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EmailItem] Critical error: {ex.GetType().Name} - {ex.Message}");
                Attachments = new ObservableCollection<AttachmentItem>();
            }
        }


        /// <summary>
        /// Initializes the Attachments collection and sets the HasAttachment property.
        /// </summary>
        private void InitializeAttachments()
        {
            Attachments = new ObservableCollection<AttachmentItem>(
                MessageView.Message.Attachments.Select(a => new AttachmentItem(a)));
            HasAttachment = Attachments.Count > 0;
            Debug.WriteLine($"Attachments initialized: Count={Attachments.Count}, HasAttachment={HasAttachment}");
        }

        /// <summary>
        /// Saves the specified attachment to a user-selected location.
        /// </summary>
        /// <param name="attachment">The attachment to save.</param>
        private void SaveAttachment(AttachmentItem attachment)
        {
            try
            {
                EnsureMessageDetailsLoaded();

                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    FileName = attachment.FileName,
                    Filter = "All files (*.*)|*.*"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    using (FileStream fs = new FileStream(saveFileDialog.FileName, FileMode.Create, FileAccess.Write))
                    {
                        attachment.OriginalAttachment.SaveToStream(fs);
                    }
                    Debug.WriteLine($"Attachment saved: {saveFileDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving attachment: {ex.Message}");
                // You might want to show an error message to the user here
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets the email body as an HTML string.
        /// </summary>
        /// <returns>The email body as an HTML string.</returns>
        public string GetBodyAsHtmlString()
        {
            try
            {
                EnsureMessageDetailsLoaded();
                Debug.WriteLine($"MessageView.Body length: {MessageView.Body?.Length ?? 0}");
                Debug.WriteLine($"MessageView.BodyHtml length: {MessageView.BodyHtml?.Length ?? 0}");
                string content = MessageView.GetBodyAsHtmlString();
                Debug.WriteLine($"GetBodyAsHtmlString: Content length = {content?.Length ?? 0}");
                return content;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetBodyAsHtmlString: {ex.Message}");
                throw;
            }
        }

        #endregion
    }

    /// <summary>
    /// Represents an email attachment item.
    /// Implements INotifyPropertyChanged for UI binding.
    /// </summary>
    public class AttachmentItem : INotifyPropertyChanged
    {
        #region Private Fields

        private string _fileName;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the AttachmentItem class.
        /// </summary>
        /// <param name="attachment">The XstAttachment to create the AttachmentItem from.</param>
        public AttachmentItem(XstAttachment attachment)
        {
            FileName = attachment.LongFileName;
            ContentId = attachment.ContentId;
            OriginalAttachment = attachment;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the file name of the attachment.
        /// </summary>
        public string FileName
        {
            get => _fileName;
            set
            {
                if (_fileName != value)
                {
                    _fileName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the content ID of the attachment.
        /// </summary>
        public string ContentId { get; set; }

        /// <summary>
        /// Gets the original XstAttachment object.
        /// </summary>  
        public XstAttachment OriginalAttachment { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets the content of the attachment as a byte array.
        /// </summary>
        /// <returns>The attachment content as a byte array.</returns>
        public byte[] GetContent()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                OriginalAttachment.SaveToStream(ms);
                return ms.ToArray();
            }
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        /// <summary>
        /// Event handler for property changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the PropertyChanged event for the specified property.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// A generic implementation of ICommand for relay commands.
    /// </summary>
    /// <typeparam name="T">The type of the command parameter.</typeparam>
    public class RelayCommand<T> : ICommand
    {
        #region Private Fields

        private readonly Func<T, bool> _canExecute;
        private readonly Action<T> _execute;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the RelayCommand class.
        /// </summary>
        /// <param name="execute">The action to execute when the command is invoked.</param>
        /// <param name="canExecute">The function to determine if the command can be executed.</param>
        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        #endregion

        #region ICommand Implementation

        /// <summary>
        /// Determines whether the command can be executed.
        /// </summary>
        /// <param name="parameter">The command parameter.</param>
        /// <returns>True if the command can be executed; otherwise, false.</returns>
        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute((T)parameter);
        }

        /// <summary>
        /// Executes the command.
        /// </summary>
        /// <param name="parameter">The command parameter.</param>
        public void Execute(object parameter)
        {
            _execute((T)parameter);
        }

        /// <summary>
        /// Event that is raised when the ability to execute the command changes.
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        #endregion
    }
}