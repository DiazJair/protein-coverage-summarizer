﻿// -------------------------------------------------------------------------------
// Written by Matthew Monroe and Nikša Blonder for the Department of Energy (PNNL, Richland, WA)
// Started June 2005
//
// E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov
// Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/
// -------------------------------------------------------------------------------
//
// Licensed under the 2-Clause BSD License; you may not use this file except
// in compliance with the License.  You may obtain a copy of the License at
// https://opensource.org/licenses/BSD-2-Clause
//
// Copyright 2018 Battelle Memorial Institute

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using PRISM;
using ProteinFileReader;

namespace ProteinCoverageSummarizer
{
    public delegate void ProteinCachingStartEventHandler();
    public delegate void ProteinCachedEventHandler(int proteinsCached);
    public delegate void ProteinCachedWithProgressEventHandler(int proteinsCached, float percentFileProcessed);
    public delegate void ProteinCachingCompleteEventHandler();

    /// <summary>
    /// This class will read a protein FASTA file or delimited protein info file and
    /// store the proteins in memory
    /// </summary>
    [CLSCompliant(true)]
    public class clsProteinFileDataCache : EventNotifier
    {
        // Ignore Spelling: Nikša, udt, Uniquesequence

        public clsProteinFileDataCache()
        {
            mFileDate = "July 26, 2019";
            InitializeLocalVariables();
        }

        #region "Constants and Enums"

        protected const string SQL_LITE_PROTEIN_CACHE_FILENAME = "tmpProteinInfoCache.db3";

        #endregion

        #region "Structures"

        public struct udtProteinInfoType
        {
            public string Name;
            public string Description;
            public string Sequence;

            /// <summary>
            /// Unique sequence ID
            /// </summary>
            /// <remarks>
            /// Index number applied to the proteins stored in the SQLite DB; the first protein has UniqueSequenceID = 0
            /// </remarks>
            public int UniqueSequenceID;

            /// <summary>
            /// Percent coverage
            /// </summary>
            /// <remarks>Value between 0 and 1</remarks>
            public double PercentCoverage;
        }

        #endregion

        #region "Class wide Variables"

        protected string mFileDate;
        private string mStatusMessage;

        private char mDelimitedInputFileDelimiter;                              // Only used for delimited protein input files, not for fasta files

        public FastaFileOptionsClass FastaFileOptions;

        private int mProteinCount;
        private bool mParsedFileIsFastaFile;

        // SQLite Connection String and file path
        private string mSQLiteDBConnectionString = string.Empty;
        private string mSQLiteDBFilePath = SQL_LITE_PROTEIN_CACHE_FILENAME;

        private SQLiteConnection mSQLitePersistentConnection;

        public event ProteinCachingStartEventHandler ProteinCachingStart;
        public event ProteinCachedEventHandler ProteinCached;
        public event ProteinCachedWithProgressEventHandler ProteinCachedWithProgress;
        public event ProteinCachingCompleteEventHandler ProteinCachingComplete;

        #endregion

        #region "Processing Options Interface Functions"

        /// <summary>
        /// When True, assume the input file is a tab-delimited text file
        /// </summary>
        /// <returns></returns>
        /// <remarks>Ignored if AssumeFastaFile is True</remarks>
        public bool AssumeDelimitedFile { get; set; }

        /// <summary>
        /// When True, assume the input file is a FASTA text file
        /// </summary>
        /// <returns></returns>
        public bool AssumeFastaFile { get; set; }
        public bool ChangeProteinSequencesToLowercase { get; set; }
        public bool ChangeProteinSequencesToUppercase { get; set; }
        public DelimitedFileReader.eDelimitedFileFormatCode DelimitedFileFormatCode { get; set; }

        public char DelimitedFileDelimiter
        {
            get => mDelimitedInputFileDelimiter;
            set
            {
                if (value != default)
                {
                    mDelimitedInputFileDelimiter = value;
                }
            }
        }

        public bool DelimitedFileSkipFirstLine { get; set; }
        public bool IgnoreILDifferences { get; set; }

        /// <summary>
        /// When this is True, the SQLite Database will not be deleted after processing finishes
        /// </summary>
        public bool KeepDB { get; set; }

        public bool RemoveSymbolCharacters { get; set; }

        public string StatusMessage => mStatusMessage;

        #endregion

        public SQLiteConnection ConnectToSQLiteDB(bool disableJournaling)
        {
            if (string.IsNullOrWhiteSpace(mSQLiteDBConnectionString))
            {
                OnDebugEvent("ConnectToSQLiteDB: Unable to open the SQLite database because mSQLiteDBConnectionString is empty");
                return null;
            }

            OnDebugEvent("Connecting to SQLite DB: " + mSQLiteDBConnectionString);

            var sqlConnection = new SQLiteConnection(mSQLiteDBConnectionString, true);
            sqlConnection.Open();

            if (disableJournaling)
            {
                OnDebugEvent("Disabling Journaling and setting Synchronous mode to 0 (improves update speed)");

                using (var cmd = sqlConnection.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA journal_mode = OFF";
                    cmd.ExecuteNonQuery();
                    cmd.CommandText = "PRAGMA synchronous = 0";
                    cmd.ExecuteNonQuery();
                }
            }

            return sqlConnection;
        }

        private string DefineSQLiteDBPath(string SQLiteDBFileName)
        {
            string dbPath;
            var directoryPath = string.Empty;
            var filePath = string.Empty;

            bool success;

            try
            {
                // See if we can create files in the directory that contains this .Dll
                directoryPath = clsProteinCoverageSummarizer.GetAppDirectoryPath();

                filePath = Path.Combine(directoryPath, "TempFileToTestFileIOPermissions.tmp");
                OnDebugEvent("Checking for write permission by creating file " + filePath);

                using (var writer = new StreamWriter(new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read)))
                {
                    writer.WriteLine("Test");
                }

                success = true;
            }
            catch (Exception ex)
            {
                // Error creating file; user likely doesn't have write-access
                OnDebugEvent(" ... unable to create the file: " + ex.Message);
                success = false;
            }

            if (!success)
            {
                try
                {
                    // Create a randomly named file in the user's temp directory
                    filePath = Path.GetTempFileName();
                    OnDebugEvent("Creating file in user's temp directory: " + filePath);

                    directoryPath = Path.GetDirectoryName(filePath);
                    success = true;
                }
                catch (Exception ex)
                {
                    // Error creating temp file; user likely doesn't have write-access anywhere on the disk
                    OnDebugEvent(" ... unable to create the file: " + ex.Message);
                    success = false;
                }
            }

            if (success)
            {
                try
                {
                    // Delete the temporary file
                    OnDebugEvent("Deleting " + filePath);
                    File.Delete(filePath);
                }
                catch (Exception)
                {
                    // Ignore errors here
                }
            }

            if (success)
            {
                dbPath = Path.Combine(directoryPath ?? string.Empty, SQLiteDBFileName);
            }
            else
            {
                dbPath = SQLiteDBFileName;
            }

            OnDebugEvent(" SQLite DB Path defined: " + dbPath);

            return dbPath;
        }

        /// <summary>
        /// Delete the SQLite database file
        /// </summary>
        /// <param name="callingMethod">Calling method name</param>
        /// <param name="forceDelete">Force deletion (ignore KeepDB)</param>
        public void DeleteSQLiteDBFile(string callingMethod, bool forceDelete = false)
        {
            const int MAX_RETRY_ATTEMPT_COUNT = 3;
            try
            {
                if (mSQLitePersistentConnection != null)
                {
                    OnDebugEvent("Closing persistent SQLite connection; calling method: " + callingMethod);
                    mSQLitePersistentConnection.Close();
                }
            }
            catch (Exception ex)
            {
                // Ignore errors here
                OnDebugEvent(" ... exception: " + ex.Message);
            }

            try
            {
                if (string.IsNullOrEmpty(mSQLiteDBFilePath))
                {
                    OnDebugEvent("DeleteSQLiteDBFile: SQLiteDBFilePath is not defined or is empty; nothing to do; calling method: " + callingMethod);
                    return;
                }

                if (!File.Exists(mSQLiteDBFilePath))
                {
                    OnDebugEvent(string.Format("DeleteSQLiteDBFile: File doesn't exist; nothing to do ({0}); calling method: {1}", mSQLiteDBFilePath, callingMethod));
                    return;
                }

                // Call the garbage collector to dispose of the SQLite objects
                GC.Collect();
                Thread.Sleep(500);
            }
            catch (Exception)
            {
                // Ignore errors here
            }

            if (KeepDB & !forceDelete)
            {
                OnDebugEvent("DeleteSQLiteDBFile: KeepDB is true; not deleting " + mSQLiteDBFilePath);
                return;
            }

            for (var retryIndex = 0; retryIndex < MAX_RETRY_ATTEMPT_COUNT ; retryIndex++)
            {
                var retryHoldOffSeconds = retryIndex + 1;
                try
                {
                    if (!string.IsNullOrEmpty(mSQLiteDBFilePath))
                    {
                        if (File.Exists(mSQLiteDBFilePath))
                        {
                            OnDebugEvent("DeleteSQLiteDBFile: Deleting " + mSQLiteDBFilePath + "; calling method: " + callingMethod);
                            File.Delete(mSQLiteDBFilePath);
                        }
                    }

                    if (retryIndex > 1)
                    {
                        OnStatusEvent(" --> File now successfully deleted");
                    }

                    // If we get here, the delete succeeded
                    break;
                }
                catch (Exception ex)
                {
                    if (retryIndex > 0)
                    {
                        OnWarningEvent(string.Format("Error deleting {0} (calling method {1}): {2}", mSQLiteDBFilePath, callingMethod, ex.Message));
                        OnWarningEvent("  Waiting " + retryHoldOffSeconds + " seconds, then trying again");
                    }
                }

                GC.Collect();
                Thread.Sleep(retryHoldOffSeconds * 1000);
            }
        }

        public int GetProteinCountCached()
        {
            return mProteinCount;
        }

        public IEnumerable<udtProteinInfoType> GetCachedProteins(int startIndex = -1, int endIndex = -1)
        {
            if (mSQLitePersistentConnection == null ||
                mSQLitePersistentConnection.State == ConnectionState.Closed ||
                mSQLitePersistentConnection.State == ConnectionState.Broken)
            {
                mSQLitePersistentConnection = ConnectToSQLiteDB(false);
            }

            var sqlQuery =
                " SELECT UniqueSequenceID, Name, Description, Sequence, PercentCoverage" +
                " FROM udtProteinInfoType";

            if (startIndex >= 0 && endIndex < 0)
            {
                sqlQuery += " WHERE UniqueSequenceID >= " + Convert.ToString(startIndex);
            }
            else if (startIndex >= 0 && endIndex >= 0)
            {
                sqlQuery += " WHERE UniqueSequenceID BETWEEN " + Convert.ToString(startIndex) + " AND " + Convert.ToString(endIndex);
            }

            var cmd = mSQLitePersistentConnection.CreateCommand();
            cmd.CommandText = sqlQuery;

            OnDebugEvent("GetCachedProteinFromSQLiteDB: running query " + cmd.CommandText);

            var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                // Column names in table udtProteinInfoType:
                // Name TEXT,
                // Description TEXT,
                // Sequence TEXT,
                // UniqueSequenceID INTEGER,
                // PercentCoverage REAL,
                // NonUniquePeptideCount INTEGER,
                // UniquePeptideCount INTEGER

                var udtProteinInfo = new udtProteinInfoType
                {
                    UniqueSequenceID = Convert.ToInt32(reader["UniqueSequenceID"]),
                    Name = Convert.ToString(reader["Name"]),
                    PercentCoverage = Convert.ToDouble(reader["PercentCoverage"]),
                    Description = Convert.ToString(reader["Description"]),
                    Sequence = Convert.ToString(reader["Sequence"])
                };

                yield return udtProteinInfo;
            }

            // Close the SQL Reader
            reader.Close();
        }

        private void InitializeLocalVariables()
        {
            const int MAX_FILE_CREATE_ATTEMPTS = 10;

            AssumeDelimitedFile = false;
            AssumeFastaFile = false;

            mDelimitedInputFileDelimiter = '\t';
            DelimitedFileFormatCode = DelimitedFileReader.eDelimitedFileFormatCode.ProteinName_Description_Sequence;
            DelimitedFileSkipFirstLine = false;

            FastaFileOptions = new FastaFileOptionsClass();

            mProteinCount = 0;

            RemoveSymbolCharacters = true;

            ChangeProteinSequencesToLowercase = false;
            ChangeProteinSequencesToUppercase = false;

            IgnoreILDifferences = false;
            var fileAttemptCount = 0;
            var success = false;
            while (!success && fileAttemptCount < MAX_FILE_CREATE_ATTEMPTS)
            {
                // Define the path to the SQLite database
                if (fileAttemptCount == 0)
                {
                    mSQLiteDBFilePath = DefineSQLiteDBPath(SQL_LITE_PROTEIN_CACHE_FILENAME);
                }
                else
                {
                    mSQLiteDBFilePath = DefineSQLiteDBPath(Path.GetFileNameWithoutExtension(SQL_LITE_PROTEIN_CACHE_FILENAME) +
                                                           fileAttemptCount.ToString() +
                                                           Path.GetExtension(SQL_LITE_PROTEIN_CACHE_FILENAME));
                }

                try
                {
                    // If the file exists, we need to delete it
                    if (File.Exists(mSQLiteDBFilePath))
                    {
                        OnDebugEvent("InitializeLocalVariables: deleting " + mSQLiteDBFilePath);
                        File.Delete(mSQLiteDBFilePath);
                    }

                    if (!File.Exists(mSQLiteDBFilePath))
                    {
                        success = true;
                    }
                }
                catch (Exception ex)
                {
                    // Error deleting the file
                    OnWarningEvent("Exception in InitializeLocalVariables: " + ex.Message);
                }

                fileAttemptCount += 1;
            }

            mSQLiteDBConnectionString = "Data Source=" + mSQLiteDBFilePath + ";";
        }

        /// <summary>
        /// Examines the file's extension and true if it ends in .fasta or .fsa or .faa
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static bool IsFastaFile(string filePath)
        {
            var proteinFileExtension = Path.GetExtension(filePath).ToLower();

            if (proteinFileExtension == ".fasta" || proteinFileExtension == ".fsa" || proteinFileExtension == ".faa")
            {
                return true;
            }

            return false;
        }

        public bool ParseProteinFile(string proteinInputFilePath)
        {
            // If outputFileNameBaseOverride is defined, then uses that name for the protein output filename rather than auto-defining the name

            // Create the SQLite DB
            var sqlConnection = ConnectToSQLiteDB(true);

            // SQL query to Create the Table
            var cmd = sqlConnection.CreateCommand();
            cmd.CommandText = "CREATE TABLE udtProteinInfoType( " +
                              "Name TEXT, " +
                              "Description TEXT, "
                              + "sequence TEXT, "
                              + "UniquesequenceID INTEGER PRIMARY KEY, " +
                              "PercentCoverage REAL);"; // , NonUniquePeptideCount INTEGER, UniquePeptideCount INTEGER);"

            OnDebugEvent("ParseProteinFile: Creating table with " + cmd.CommandText);

            cmd.ExecuteNonQuery();

            // Define a RegEx to replace all of the non-letter characters
            var reReplaceSymbols = new Regex("[^A-Za-z]", RegexOptions.Compiled);

            ProteinFileReaderBaseClass proteinFileReader = null;

            bool success;

            try
            {
                if (string.IsNullOrWhiteSpace(proteinInputFilePath))
                {
                    ReportError("Empty protein input file path");
                    success = false;
                }
                else
                {
                    if (AssumeFastaFile || IsFastaFile(proteinInputFilePath))
                    {
                        mParsedFileIsFastaFile = true;
                    }
                    else if (AssumeDelimitedFile)
                    {
                        mParsedFileIsFastaFile = false;
                    }
                    else
                    {
                        mParsedFileIsFastaFile = true;
                    }

                    if (mParsedFileIsFastaFile)
                    {
                        proteinFileReader = new FastaFileReader()
                        {
                            ProteinLineStartChar = FastaFileOptions.ProteinLineStartChar,
                            ProteinLineAccessionEndChar = FastaFileOptions.ProteinLineAccessionEndChar
                        };
                    }
                    else
                    {
                        proteinFileReader = new DelimitedFileReader()
                        {
                            Delimiter = mDelimitedInputFileDelimiter,
                            DelimitedFileFormatCode = DelimitedFileFormatCode,
                            SkipFirstLine = DelimitedFileSkipFirstLine
                        };
                    }

                    // Verify that the input file exists
                    if (!File.Exists(proteinInputFilePath))
                    {
                        ReportError("Protein input file not found: " + proteinInputFilePath);
                        success = false;
                    }
                    // Attempt to open the input file
                    else if (!proteinFileReader.OpenFile(proteinInputFilePath))
                    {
                        ReportError("Error opening protein input file: " + proteinInputFilePath);
                        success = false;
                    }
                    else
                        success = true;
                }
            }
            catch (Exception ex)
            {
                ReportError("Error opening protein input file (" + proteinInputFilePath + "): " + ex.Message, ex);
                success = false;
            }

            // Abort processing if we couldn't successfully open the input file
            if (!success)
                return false;

            try
            {
                // Read each protein in the input file and process appropriately
                mProteinCount = 0;

                ProteinCachingStart?.Invoke();

                // Create a parameterized Insert query
                cmd.CommandText = " INSERT INTO udtProteinInfoType(Name, Description, sequence, UniquesequenceID, PercentCoverage) " +
                                  " VALUES (?, ?, ?, ?, ?)";

                var nameFld = cmd.CreateParameter();
                var descriptionFld = cmd.CreateParameter();
                var sequenceFld = cmd.CreateParameter();
                var uniqueSequenceIDFld = cmd.CreateParameter();
                var percentCoverageFld = cmd.CreateParameter();
                cmd.Parameters.Add(nameFld);
                cmd.Parameters.Add(descriptionFld);
                cmd.Parameters.Add(sequenceFld);
                cmd.Parameters.Add(uniqueSequenceIDFld);
                cmd.Parameters.Add(percentCoverageFld);

                // Begin a SQL Transaction
                var SQLTransaction = sqlConnection.BeginTransaction();

                var proteinsProcessed = 0;
                var inputFileLinesRead = 0;

                while (true)
                {
                    var inputProteinFound = proteinFileReader.ReadNextProteinEntry();
                    if (!inputProteinFound)
                    {
                        break;
                    }

                    proteinsProcessed += 1;
                    inputFileLinesRead = proteinFileReader.LinesRead;

                    var name = proteinFileReader.ProteinName;
                    var description = proteinFileReader.ProteinDescription;
                    string sequence;

                    if (RemoveSymbolCharacters)
                    {
                        sequence = reReplaceSymbols.Replace(proteinFileReader.ProteinSequence, string.Empty);
                    }
                    else
                    {
                        sequence = proteinFileReader.ProteinSequence;
                    }

                    if (ChangeProteinSequencesToLowercase)
                    {
                        if (IgnoreILDifferences)
                        {
                            // Replace all L characters with I
                            sequence = sequence.ToLower().Replace('l', 'i');
                        }
                        else
                        {
                            sequence = sequence.ToLower();
                        }
                    }
                    else if (ChangeProteinSequencesToUppercase)
                    {
                        if (IgnoreILDifferences)
                        {
                            // Replace all L characters with I
                            sequence = sequence.ToUpper().Replace('L', 'I');
                        }
                        else
                        {
                            sequence = sequence.ToUpper();
                        }
                    }
                    else if (IgnoreILDifferences)
                    {
                        // Replace all L characters with I
                        sequence = sequence.Replace('L', 'I').Replace('l', 'i');
                    }

                    // Store this protein in the SQLite DB
                    nameFld.Value = name;
                    descriptionFld.Value = description;
                    sequenceFld.Value = sequence;

                    // Use mProteinCount to assign UniqueSequenceID values
                    uniqueSequenceIDFld.Value = mProteinCount;

                    percentCoverageFld.Value = 0;

                    cmd.ExecuteNonQuery();

                    mProteinCount += 1;

                    ProteinCached?.Invoke(mProteinCount);

                    if (mProteinCount % 100 == 0)
                    {
                        ProteinCachedWithProgress?.Invoke(mProteinCount, proteinFileReader.PercentFileProcessed());
                    }

                    success = true;
                }


                // Finalize the SQL Transaction
                SQLTransaction.Commit();

                // Set Synchronous mode to 1   (this may not be truly necessary)
                cmd.CommandText = "PRAGMA synchronous=1";
                cmd.ExecuteNonQuery();

                // Close the SQLite DB
                cmd.Dispose();
                sqlConnection.Close();

                // Close the protein file
                proteinFileReader.CloseFile();

                ProteinCachingComplete?.Invoke();

                if (success)
                {
                    OnStatusEvent("Done: Processed " + proteinsProcessed.ToString("###,##0") + " proteins (" + inputFileLinesRead.ToString("###,###,##0") + " lines)");
                }
                else
                {
                    OnErrorEvent(mStatusMessage);
                }
            }
            catch (Exception ex)
            {
                ReportError("Error reading protein input file (" + proteinInputFilePath + "): " + ex.Message, ex);
                success = false;
            }

            return success;
        }

        private void ReportError(string errorMessage, Exception ex = null)
        {
            OnErrorEvent(errorMessage, ex);
            mStatusMessage = errorMessage;
        }

        // Options class
        public class FastaFileOptionsClass
        {
            public FastaFileOptionsClass()
            {
                mProteinLineStartChar = '>';
                mProteinLineAccessionEndChar = ' ';
            }

            #region "Class wide Variables"

            private char mProteinLineStartChar;
            private char mProteinLineAccessionEndChar;

            #endregion

            #region "Processing Options Interface Functions"

            public char ProteinLineStartChar
            {
                get => mProteinLineStartChar;
                set
                {
                    if (value != default)
                    {
                        mProteinLineStartChar = value;
                    }
                }
            }

            public char ProteinLineAccessionEndChar
            {
                get => mProteinLineAccessionEndChar;
                set
                {
                    if (value != default)
                    {
                        mProteinLineAccessionEndChar = value;
                    }
                }
            }

            #endregion
        }
    }
}