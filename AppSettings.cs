using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PSTInsight
{
    /// <summary>
    /// Static class to manage application settings, specifically for handling PST file paths.
    /// </summary>
    public static class AppSettings
    {
        #region Constants

        /// <summary>
        /// The file name used to store settings.
        /// </summary>
        private const string SettingsFileName = "settings.txt";

        #endregion

        #region Fields

        /// <summary>
        /// The full path to the settings file.
        /// </summary>
        private static readonly string SettingsFilePath;

        #endregion

        #region Constructor

        /// <summary>
        /// Static constructor to initialize the SettingsFilePath.
        /// </summary>
        static AppSettings()
        {
            // Combine the path elements to create the full settings file path
            SettingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PSTInsight",
                SettingsFileName);

            Console.WriteLine($"Settings file path: {SettingsFilePath}");
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Loads the list of saved PST file paths from the settings file.
        /// </summary>
        /// <returns>A List of strings containing the saved PST file paths.</returns>
        public static List<string> LoadSavedPstFiles()
        {
            // Check if the settings file exists
            if (File.Exists(SettingsFilePath))
            {
                Console.WriteLine("Loading saved PST files from settings.");
                // Read all lines from the file and convert to a List<string>
                return File.ReadAllLines(SettingsFilePath).ToList();
            }
            else
            {
                Console.WriteLine("Settings file not found. Returning empty list.");
                // If the file doesn't exist, return an empty list
                return new List<string>();
            }
        }

        /// <summary>
        /// Saves the provided PST file paths to the settings file.
        /// </summary>
        /// <param name="pstFiles">An IEnumerable<string> containing the PST file paths to save.</param>
        public static void SavePstFiles(IEnumerable<string> pstFiles)
        {
            Console.WriteLine("Saving PST files to settings.");

            // Ensure the directory exists
            string directory = Path.GetDirectoryName(SettingsFilePath);
            _ = Directory.CreateDirectory(directory);

            // Write all lines to the settings file
            File.WriteAllLines(SettingsFilePath, pstFiles);

            Console.WriteLine($"Saved {pstFiles.Count()} PST file paths to settings.");
        }

        #endregion
    }
}