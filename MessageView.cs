using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using XstReader;
using XstReader.ElementProperties;

namespace PSTInsight
{
    /// <summary>
    /// Represents a view model for an email message, implementing INotifyPropertyChanged for UI binding.
    /// </summary>
    public class MessageView : INotifyPropertyChanged
    {
        #region Fields

        private bool isSelected;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the MessageView class.
        /// </summary>
        /// <param name="message">The XstMessage object to be wrapped.</param>
        /// <exception cref="Exception">Thrown when the message parameter is null.</exception>
        public MessageView(XstMessage message)
        {
            Message = message ?? throw new Exception("MessageView requires an XstMessage object");
            SenderEmail = message.Recipients[RecipientType.Sender].FirstOrDefault()?.Address;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the underlying XstMessage object.
        /// </summary>
        public XstMessage Message { get; }

        /// <summary>
        /// Gets the subject of the email.
        /// </summary>
        public string Subject => Message.Subject;

        /// <summary>
        /// Gets the date of the email.
        /// </summary>
        public DateTime? Date => Message.Date;

        /// <summary>
        /// Gets the formatted display date of the email.
        /// </summary>
        public string DisplayDate => Message.Date?.ToString("g") ?? string.Empty;

        /// <summary>
        /// Gets the plain text body of the email.
        /// </summary>
        public string Body => Message.Body?.Text;

        /// <summary>
        /// Gets the HTML body of the email.
        /// </summary>
        public string BodyHtml => Message.Body?.Bytes != null ? Encoding.UTF8.GetString(Message.Body.Bytes) : null;

        /// <summary>
        /// Gets the sender's name.
        /// </summary>
        public string FromName => Message?.From;

        /// <summary>
        /// Gets the recipients' names.
        /// </summary>
        public string ToName => string.Join("; ", Message.To);

        /// <summary>
        /// Gets the recipients' email addresses.
        /// </summary>
        public string ToAddress => string.Join("; ", Message.To);

        /// <summary>
        /// Gets the CC recipients' names.
        /// </summary>
        public string CcName => string.Join("; ", Message.Cc);

        /// <summary>
        /// Gets the CC recipients' email addresses.
        /// </summary>
        public string CcAddress => string.Join("; ", Message.Cc);

        /// <summary>
        /// Gets the sender's email address.
        /// </summary>
        public string SenderEmail { get; }

        /// <summary>
        /// Gets or sets whether the message is selected.
        /// </summary>
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (value != isSelected)
                {
                    isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Event that is raised when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Public Methods

        /// <summary>
        /// Retrieves the email body as an HTML string, embedding inline attachments if present.
        /// </summary>
        /// <returns>The email body as an HTML string.</returns>
        public string GetBodyAsHtmlString()
        {
            try
            {
                // Determine the content: use HTML if available, otherwise use plain text
                string content = BodyHtml ?? (!string.IsNullOrEmpty(Body) ? $"<html><body>{WebUtility.HtmlEncode(Body)}</body></html>" : null);

                // Embed inline attachments if content is not null
                if (content != null)
                {
                    content = EmbedInlineAttachments(content);
                }

                Debug.WriteLine($"Final HTML content length: {content?.Length ?? 0}");
                return content ?? "<p>Email content is empty.</p>";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetBodyAsHtmlString: {ex.Message}");
                return "<p>Error loading email content.</p>";
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Embeds inline attachments into the HTML content.
        /// </summary>
        /// <param name="htmlContent">The original HTML content.</param>
        /// <returns>HTML content with embedded inline attachments.</returns>
        private string EmbedInlineAttachments(string htmlContent)
        {
            foreach (XstAttachment attachment in Message.Attachments.Where(a => !string.IsNullOrEmpty(a.ContentId)))
            {
                try
                {
                    string mimeType = GetMimeType(attachment.FileName);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        attachment.SaveToStream(ms);
                        string base64Content = Convert.ToBase64String(ms.ToArray());
                        string dataUri = $"data:{mimeType};base64,{base64Content}";
                        htmlContent = htmlContent.Replace($"cid:{attachment.ContentId}", dataUri);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error embedding attachment {attachment.FileName}: {ex.Message}");
                }
            }
            return htmlContent;
        }

        /// <summary>
        /// Determines the MIME type based on the file extension.
        /// </summary>
        /// <param name="fileName">The name of the file.</param>
        /// <returns>The MIME type as a string.</returns>
        private string GetMimeType(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return "application/octet-stream";
            }

            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            switch (extension)
            {
                case ".txt":
                    return "text/plain";
                case ".pdf":
                    return "application/pdf";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".png":
                    return "image/png";
                default:
                    return "application/octet-stream";
            }
        }

        /// <summary>
        /// Raises the PropertyChanged event for a specific property.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}