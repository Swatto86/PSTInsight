using MsgKit;
using MsgKit.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XstReader;
using Task = System.Threading.Tasks.Task;

namespace PSTInsight
{
    /// <summary>
    /// Provides services for managing PST files, including loading, unloading, and processing emails.
    /// </summary>
    public class PstService
    {
        #region Fields

        private readonly Dictionary<string, XstFile> _loadedFiles;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the PstService class.
        /// </summary>
        public PstService()
        {
            _loadedFiles = new Dictionary<string, XstFile>();
        }

        #endregion

        #region PST File Management

        /// <summary>
        /// Asynchronously loads a PST file.
        /// </summary>
        /// <param name="filePath">The path to the PST file.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a boolean indicating success.</returns>
        public async Task<bool> LoadPstFileAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Only load the file if it's not already loaded
                    if (!_loadedFiles.ContainsKey(filePath))
                    {
                        XstFile pstFile = new XstFile(filePath);
                        _loadedFiles[filePath] = pstFile;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading PST file: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// Retrieves a loaded PST file.
        /// </summary>
        /// <param name="filePath">The path to the PST file.</param>
        /// <returns>The loaded XstFile object, or null if not found.</returns>
        public XstFile GetLoadedFile(string filePath)
        {
            return _loadedFiles.TryGetValue(filePath, out XstFile pstFile) ? pstFile : null;
        }

        /// <summary>
        /// Unloads a PST file from memory.
        /// </summary>
        /// <param name="filePath">The path to the PST file to unload.</param>
        public void UnloadPstFile(string filePath)
        {
            if (_loadedFiles.ContainsKey(filePath))
            {
                _ = _loadedFiles.Remove(filePath);
            }
        }

        #endregion

        #region Email Processing

        /// <summary>
        /// Asynchronously retrieves emails from a specified folder.
        /// </summary>
        /// <param name="folder">The XstFolder to retrieve emails from.</param>
        /// <param name="progress">An IProgress object to report progress.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a list of EmailItem objects.</returns>
        public async Task<List<EmailItem>> GetEmailsFromFolderAsync(XstFolder folder, IProgress<int> progress)
        {
            return await Task.Run(() =>
            {
                Debug.WriteLine($"Getting emails from folder: {folder.DisplayName}");
                IEnumerable<XstMessage> messages = folder.GetMessages();
                List<EmailItem> emails = new List<EmailItem>();
                int totalMessages = messages.Count();

                for (int i = 0; i < totalMessages; i++)
                {
                    EmailItem email = new EmailItem(messages.ElementAt(i));
                    emails.Add(email);
                    Debug.WriteLine(
                        $"Created EmailItem: Subject={email.Subject}, HasAttachment={email.HasAttachment}, Attachments Count={email.Attachments?.Count ?? 0}");

                    // Report progress
                    int percentComplete = (int)((i + 1) / (double)totalMessages * 100);
                    progress.Report(percentComplete);
                }

                Debug.WriteLine($"Found {emails.Count} emails in folder");
                return emails;
            });
        }

        public class ExportResult
        {
            public int SuccessCount { get; set; }
            public int FailureCount { get; set; }
            public List<string> FailedEmails { get; set; } = new List<string>();
        }
        
        /// <summary>
        /// Asynchronously exports emails to MSG format.
        /// </summary>
        /// <param name="emailsToExport">The list of EmailItem objects to export.</param>
        /// <param name="folderPath">The folder path to save the exported MSG files.</param>
        /// <param name="progressCallback">A callback to report progress.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task<ExportResult> ExportEmailsToMsgAsync(List<EmailItem> emailsToExport, string folderPath, Action<int> progressCallback)
        {
            return await Task.Run(() =>
            {
                var result = new ExportResult();
                var processedFileNames = new HashSet<string>();
                int totalCount = emailsToExport.Count;
                int processedCount = 0;

                foreach (EmailItem email in emailsToExport)
                {
                    List<string> tempFiles = new List<string>();
                    try
                    {
                        string baseFileName = ReplaceInvalidChars($"{email.Subject}.msg", '_');
                        string uniqueFileName = GetUniqueFileName(folderPath, baseFileName);
                        string filePath = Path.Combine(folderPath, uniqueFileName);

                        if (processedFileNames.Contains(filePath.ToLower()))
                        {
                            Debug.WriteLine($"Duplicate file path detected: {filePath}");
                            result.FailureCount++;
                            continue;
                        }

                        string fromAddress = email.FromAddress;
                        if (!IsValidEmail(fromAddress))
                        {
                            fromAddress = "none@example.com";
                        }

                        Sender sender = new Sender(fromAddress, fromAddress, AddressType.Smtp, MessageFormat.TextOnly);
                        Representing representing = new Representing(fromAddress, fromAddress);

                        using (Email msg = new Email(sender, representing, email.Subject))
                        {
                            msg.BodyText = email.Body;
                            msg.BodyHtml = email.GetBodyAsHtmlString();
                            msg.SentOn = email.Date;

                            AddRecipients(email.ToName, email.ToAddress, (name, address) => msg.Recipients.AddTo(name, address));
                            AddRecipients(email.CcName, email.CcAddress, (name, address) => msg.Recipients.AddCc(name, address));

                            if (email.Attachments.Any())
                            {
                                AddAttachments(msg, email.Attachments);
                            }

                            msg.Save(filePath);
                        }

                        processedFileNames.Add(filePath.ToLower());
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.FailureCount++;
                        result.FailedEmails.Add($"{email.Subject}: {ex.Message}");
                        Debug.WriteLine($"Error exporting email '{email.Subject}': {ex.Message}");
                    }
                    finally
                    {
                        // Cleanup temp files
                        foreach (string tempFile in tempFiles)
                        {
                            try
                            {
                                if (File.Exists(tempFile))
                                    File.Delete(tempFile);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error deleting temporary file {tempFile}: {ex.Message}");
                            }
                        }

                        // Update progress
                        processedCount++;
                        int progressPercentage = (processedCount * 100) / totalCount;
                        progressCallback(progressPercentage);
                    }
                }

                // Log final statistics
                Debug.WriteLine($"Total emails to export: {totalCount}");
                Debug.WriteLine($"Unique files created: {processedFileNames.Count}");
                Debug.WriteLine($"Reported success: {result.SuccessCount}");
                Debug.WriteLine($"Reported failures: {result.FailureCount}");

                return result;
            });
        }

        /// <summary>
        /// Asynchronously retrieves the HTML content of an email.
        /// </summary>
        /// <param name="emailItem">The EmailItem to retrieve HTML content from.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the HTML content as a string.</returns>
        public async Task<string> GetEmailHtmlContentAsync(EmailItem emailItem)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string htmlContent = emailItem.GetBodyAsHtmlString();
                    Debug.WriteLine($"GetEmailHtmlContentAsync: Content length = {htmlContent?.Length ?? 0}");
                    return htmlContent ?? "<p>Email content is empty.</p>";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in GetEmailHtmlContentAsync: {ex.Message}");
                    throw;
                }
            });
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Adds recipients to an email message.
        /// </summary>
        /// <param name="names">Semicolon-separated list of recipient names.</param>
        /// <param name="addresses">Semicolon-separated list of recipient addresses.</param>
        /// <param name="addRecipient">Action to add a recipient to the message.</param>
        private void AddRecipients(string names, string addresses, Action<string, string> addRecipient)
        {
            var recipients = names.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Zip(addresses.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                    (name, address) => new { Name = name.Trim(), Address = address.Trim() });

            foreach (var recipient in recipients)
            {
                addRecipient(recipient.Name, recipient.Address);
            }
        }

        /// <summary>
        /// Validates an email address.
        /// </summary>
        /// <param name="email">The email address to validate.</param>
        /// <returns>True if the email is valid, false otherwise.</returns>
        private bool IsValidEmail(string email)
        {
            try
            {
                System.Net.Mail.MailAddress addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Adds attachments to an email message.
        /// </summary>
        /// <param name="msg">The Email object to add attachments to.</param>
        /// <param name="attachments">The collection of AttachmentItem objects to add.</param>
        /// <returns>A list of temporary file paths created for the attachments.</returns>
        private List<string> AddAttachments(Email msg, IEnumerable<AttachmentItem> attachments)
        {
            List<string> attachmentPaths = new List<string>();
            foreach (AttachmentItem attachment in attachments)
            {
                try
                {
                    string tempFilePath = Path.Combine(Path.GetTempPath(), attachment.FileName);
                    File.WriteAllBytes(tempFilePath, attachment.GetContent());
                    msg.Attachments.Add(tempFilePath, -1, false, attachment.FileName);
                    attachmentPaths.Add(tempFilePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error adding attachment '{attachment.FileName}': {ex.Message}");
                }
            }
            return attachmentPaths;
        }

        /// <summary>
        /// Replaces invalid characters in a filename with a specified replacement character.
        /// </summary>
        /// <param name="filename">The filename to process.</param>
        /// <param name="replacementChar">The character to use as a replacement for invalid characters.</param>
        /// <returns>The processed filename with invalid characters replaced.</returns>
        private static string ReplaceInvalidChars(string filename, char replacementChar)
        {
            return string.Join("", filename.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? replacementChar : c));
        }

        private string GetUniqueFileName(string folderPath, string baseFileName)
        {
            string fileName = baseFileName;
            int counter = 1;
            while (File.Exists(Path.Combine(folderPath, fileName)))
            {
                fileName = Path.GetFileNameWithoutExtension(baseFileName) + $"_{counter}.msg";
                counter++;
            }
            return fileName;
        }

        #endregion

        #region PST Path Management

        /// <summary>
        /// Loads the list of saved PST file paths.
        /// </summary>
        /// <returns>A list of saved PST file paths.</returns>
        public List<string> LoadSavedPstPaths()
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PSTInsight");
            string filePath = Path.Combine(appDataPath, "pst_paths.txt");

            if (File.Exists(filePath))
            {
                try
                {
                    return File.ReadAllLines(filePath).ToList();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading PST paths: {ex.Message}");
                }
            }

            return new List<string>();
        }

        /// <summary>
        /// Saves the current list of loaded PST file paths.
        /// </summary>
        public void SavePstPaths()
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PSTInsight");
            _ = Directory.CreateDirectory(appDataPath);
            string filePath = Path.Combine(appDataPath, "pst_paths.txt");

            try
            {
                File.WriteAllLines(filePath, _loadedFiles.Keys);
                Debug.WriteLine($"Saved {_loadedFiles.Count} PST paths to {filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving PST paths: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes a PST file path from the saved list.
        /// </summary>
        /// <param name="pstPath">The PST file path to remove.</param>
        public void RemoveSavedPstPath(string pstPath)
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PSTInsight");
            string filePath = Path.Combine(appDataPath, "pst_paths.txt");

            try
            {
                if (File.Exists(filePath))
                {
                    HashSet<string> paths = new HashSet<string>(File.ReadAllLines(filePath));
                    if (paths.Remove(pstPath))
                    {
                        File.WriteAllLines(filePath, paths);
                        Debug.WriteLine($"Removed PST path from saved file: {pstPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing PST path from saved file: {ex.Message}");
            }
        }

        #endregion
    }
}