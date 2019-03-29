Option Strict On

' -------------------------------------------------------------------------------
' Written by Matthew Monroe and Nik�a Blonder for the Department of Energy (PNNL, Richland, WA)
' Started June 2005
'
' E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov
' Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/
' -------------------------------------------------------------------------------
'
' Licensed under the 2-Clause BSD License; you may not use this file except
' in compliance with the License.  You may obtain a copy of the License at
' https://opensource.org/licenses/BSD-2-Clause
'
' Copyright 2018 Battelle Memorial Institute

Imports System.Data.SQLite
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Threading
Imports PRISM
Imports ProteinFileReader

''' <summary>
''' This class will read a protein FASTA file or delimited protein info file and
''' store the proteins in memory
''' </summary>
<CLSCompliant(True)>
Public Class clsProteinFileDataCache
    Inherits EventNotifier

    Public Sub New()
        mFileDate = "September 14, 2018"
        InitializeLocalVariables()
    End Sub

#Region "Constants and Enums"

    Protected Const SQL_LITE_PROTEIN_CACHE_FILENAME As String = "tmpProteinInfoCache.db3"

#End Region

#Region "Structures"
    Public Structure udtProteinInfoType
        Public Name As String
        Public Description As String
        Public Sequence As String

        ''' <summary>
        ''' Unique sequence ID
        ''' </summary>
        ''' <remarks>
        ''' Index number applied to the proteins stored in the SQL Lite DB; the first protein has UniqueSequenceID = 0
        ''' </remarks>
        Public UniqueSequenceID As Integer

        ''' <summary>
        ''' Percent coverage
        ''' </summary>
        ''' <remarks>Value between 0 and 1</remarks>
        Public PercentCoverage As Double
    End Structure

#End Region

#Region "Classwide Variables"
    Protected mFileDate As String
    Private mStatusMessage As String

    Private mDelimitedInputFileDelimiter As Char                              ' Only used for delimited protein input files, not for fasta files

    Public FastaFileOptions As FastaFileOptionsClass

    Private mProteinCount As Integer
    Private mParsedFileIsFastaFile As Boolean

    ' Sql Lite Connection String and filepath
    Private mSqlLiteDBConnectionString As String = String.Empty
    Private mSqlLiteDBFilePath As String = SQL_LITE_PROTEIN_CACHE_FILENAME

    Private mSqlLitePersistentConnection As SQLiteConnection

    Public Event ProteinCachingStart()
    Public Event ProteinCached(proteinsCached As Integer)
    Public Event ProteinCachedWithProgress(proteinsCached As Integer, percentFileProcessed As Single)
    Public Event ProteinCachingComplete()

#End Region

#Region "Processing Options Interface Functions"

    ''' <summary>
    ''' When True, assume the input file is a tab-delimited text file
    ''' </summary>
    ''' <returns></returns>
    ''' <remarks>Ignored if AssumeFastaFile is True</remarks>
    Public Property AssumeDelimitedFile As Boolean

    ''' <summary>
    ''' When True, assume the input file is a FASTA text file
    ''' </summary>
    ''' <returns></returns>
    Public Property AssumeFastaFile As Boolean

    Public Property ChangeProteinSequencesToLowercase As Boolean

    Public Property ChangeProteinSequencesToUppercase As Boolean

    Public Property DelimitedFileFormatCode As DelimitedFileReader.eDelimitedFileFormatCode

    Public Property DelimitedFileDelimiter As Char
        Get
            Return mDelimitedInputFileDelimiter
        End Get
        Set
            If Not Value = Nothing Then
                mDelimitedInputFileDelimiter = Value
            End If
        End Set
    End Property

    Public Property DelimitedFileSkipFirstLine As Boolean

    Public Property IgnoreILDifferences As Boolean

    ''' <summary>
    ''' When this is True, the SQLite Database will not be deleted after processing finishes
    ''' </summary>
    Public Property KeepDB As Boolean

    Public Property RemoveSymbolCharacters As Boolean

    Public ReadOnly Property StatusMessage As String
        Get
            Return mStatusMessage
        End Get
    End Property

#End Region

    Public Function ConnectToSqlLiteDB(disableJournaling As Boolean) As SQLiteConnection

        If mSqlLiteDBConnectionString Is Nothing OrElse mSqlLiteDBConnectionString.Length = 0 Then
            OnDebugEvent("ConnectToSqlLiteDB: Unable to open the SQLite database because mSqlLiteDBConnectionString is empty")
            Return Nothing
        End If

        OnDebugEvent("Connecting to SQLite DB: " + mSqlLiteDBConnectionString)

        Dim sqlConnection = New SQLiteConnection(mSqlLiteDBConnectionString, True)
        sqlConnection.Open()

        If disableJournaling Then
            OnDebugEvent("Disabling Journaling and setting Synchronous mode to 0 (improves update speed)")

            Using cmd As SQLiteCommand = sqlConnection.CreateCommand
                cmd.CommandText = "PRAGMA journal_mode = OFF"
                cmd.ExecuteNonQuery()
                cmd.CommandText = "PRAGMA synchronous = 0"
                cmd.ExecuteNonQuery()
            End Using
        End If

        Return sqlConnection

    End Function

    Private Function DefineSqlLiteDBPath(sqlLiteDBFileName As String) As String
        Dim dbPath As String
        Dim directoryPath As String = String.Empty
        Dim filePath As String = String.Empty

        Dim success As Boolean

        Try
            ' See if we can create files in the directory that contains this .Dll
            directoryPath = clsProteinCoverageSummarizer.GetAppDirectoryPath()

            filePath = Path.Combine(directoryPath, "TempFileToTestFileIOPermissions.tmp")
            OnDebugEvent("Checking for write permission by creating file " + filePath)

            Using writer = New StreamWriter(New FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                writer.WriteLine("Test")
            End Using

            success = True

        Catch ex As Exception
            ' Error creating file; user likely doesn't have write-access
            OnDebugEvent(" ... unable to create the file: " + ex.Message)
            success = False
        End Try

        If Not success Then
            Try
                ' Create a randomly named file in the user's temp directory
                filePath = Path.GetTempFileName
                OnDebugEvent("Creating file in user's temp directory: " + filePath)

                directoryPath = Path.GetDirectoryName(filePath)
                success = True

            Catch ex As Exception
                ' Error creating temp file; user likely doesn't have write-access anywhere on the disk
                OnDebugEvent(" ... unable to create the file: " + ex.Message)
                success = False
            End Try
        End If

        If success Then
            Try
                ' Delete the temporary file
                OnDebugEvent("Deleting " + filePath)
                File.Delete(filePath)
            Catch ex As Exception
                ' Ignore errors here
            End Try
        End If

        If success Then
            dbPath = Path.Combine(directoryPath, sqlLiteDBFileName)
        Else
            dbPath = sqlLiteDBFileName
        End If

        OnDebugEvent(" SQLite DB Path defined: " + dbPath)

        Return dbPath

    End Function

    ''' <summary>
    ''' Delete the SQLite database file
    ''' </summary>
    ''' <param name="callingMethod">Calling method name</param>
    ''' <param name="forceDelete">Force deletion (ignore KeepDB)</param>
    Public Sub DeleteSQLiteDBFile(callingMethod As String, Optional forceDelete As Boolean = False)
        Const MAX_RETRY_ATTEMPT_COUNT = 3

        Try
            If Not mSqlLitePersistentConnection Is Nothing Then
                OnDebugEvent("Closing persistent SQLite connection; calling method: " + callingMethod)
                mSqlLitePersistentConnection.Close()
            End If
        Catch ex As Exception
            ' Ignore errors here
            OnDebugEvent(" ... exception: " + ex.Message)
        End Try

        Try

            If String.IsNullOrEmpty(mSqlLiteDBFilePath) Then
                OnDebugEvent("DeleteSQLiteDBFile: SqlLiteDBFilePath is not defined or is empty; nothing to do; calling method: " + callingMethod)
                Exit Sub
            ElseIf Not File.Exists(mSqlLiteDBFilePath) Then
                OnDebugEvent(String.Format("DeleteSQLiteDBFile: File doesn't exist; nothing to do ({0}); calling method: {1}",
                                           mSqlLiteDBFilePath, callingMethod))
                Exit Sub
            End If

            ' Call the garbage collector to dispose of the SQLite objects
            GC.Collect()
            Thread.Sleep(500)

        Catch ex As Exception
            ' Ignore errors here
        End Try

        If KeepDB And Not forceDelete Then
            OnDebugEvent("DeleteSQLiteDBFile: KeepDB is true; not deleting " + mSqlLiteDBFilePath)
            Exit Sub
        End If

        For retryIndex = 0 To MAX_RETRY_ATTEMPT_COUNT - 1
            Dim retryHoldOffSeconds = (retryIndex + 1)

            Try
                If Not String.IsNullOrEmpty(mSqlLiteDBFilePath) Then
                    If File.Exists(mSqlLiteDBFilePath) Then
                        OnDebugEvent("DeleteSQLiteDBFile: Deleting " + mSqlLiteDBFilePath + "; calling method: " + callingMethod)
                        File.Delete(mSqlLiteDBFilePath)
                    End If
                End If

                If retryIndex > 1 Then
                    OnStatusEvent(" --> File now successfully deleted")
                End If

                ' If we get here, the delete succeeded
                Exit For

            Catch ex As Exception
                If retryIndex > 0 Then
                    OnWarningEvent(String.Format("Error deleting {0} (calling method {1}): {2}", mSqlLiteDBFilePath, callingMethod, ex.Message))
                    OnWarningEvent("  Waiting " & retryHoldOffSeconds & " seconds, then trying again")
                End If
            End Try

            GC.Collect()
            Thread.Sleep(retryHoldOffSeconds * 1000)
        Next

    End Sub

    Public Function GetProteinCountCached() As Integer
        Return mProteinCount
    End Function

    Public Iterator Function GetCachedProteins(Optional startIndex As Integer = -1, Optional endIndex As Integer = -1) As IEnumerable(Of udtProteinInfoType)

        If mSqlLitePersistentConnection Is Nothing Then
            mSqlLitePersistentConnection = ConnectToSqlLiteDB(False)
        End If

        Dim sqlQuery =
            " SELECT UniqueSequenceID, Name, Description, Sequence, PercentCoverage" &
            " FROM udtProteinInfoType"

        If startIndex >= 0 AndAlso endIndex < 0 Then
            sqlQuery &= " WHERE UniqueSequenceID >= " & CStr(startIndex)
        ElseIf startIndex >= 0 AndAlso endIndex >= 0 Then
            sqlQuery &= " WHERE UniqueSequenceID BETWEEN " & CStr(startIndex) & " AND " & CStr(endIndex)
        End If

        Dim cmd As SQLiteCommand
        cmd = mSqlLitePersistentConnection.CreateCommand
        cmd.CommandText = sqlQuery

        OnDebugEvent("GetCachedProteinFromSQLiteDB: running query " + cmd.CommandText)

        Dim reader As SQLiteDataReader
        reader = cmd.ExecuteReader()

        While reader.Read()
            ' Column names in table udtProteinInfoType:
            '  Name TEXT,
            '  Description TEXT,
            '  Sequence TEXT,
            '  UniqueSequenceID INTEGER,
            '  PercentCoverage REAL,
            '  NonUniquePeptideCount INTEGER,
            '  UniquePeptideCount INTEGER

            Dim udtProteinInfo = New udtProteinInfoType()

            With udtProteinInfo
                .UniqueSequenceID = CInt(reader("UniqueSequenceID"))

                .Name = CStr(reader("Name"))
                .PercentCoverage = CDbl(reader("PercentCoverage"))
                .Description = CStr(reader("Description"))

                .Sequence = CStr(reader("Sequence"))
            End With

            Yield udtProteinInfo
        End While

        ' Close the SQL Reader
        reader.Close()

    End Function

    Private Sub InitializeLocalVariables()
        Const MAX_FILE_CREATE_ATTEMPTS = 10

        AssumeDelimitedFile = False
        AssumeFastaFile = False

        mDelimitedInputFileDelimiter = ControlChars.Tab
        DelimitedFileFormatCode = DelimitedFileReader.eDelimitedFileFormatCode.ProteinName_Description_Sequence
        DelimitedFileSkipFirstLine = False

        FastaFileOptions = New FastaFileOptionsClass

        mProteinCount = 0

        RemoveSymbolCharacters = True

        ChangeProteinSequencesToLowercase = False
        ChangeProteinSequencesToUppercase = False

        IgnoreILDifferences = False

        Dim fileAttemptCount = 0
        Dim success = False
        Do While Not success AndAlso fileAttemptCount < MAX_FILE_CREATE_ATTEMPTS

            ' Define the path to the Sql Lite database
            If fileAttemptCount = 0 Then
                mSqlLiteDBFilePath = DefineSqlLiteDBPath(SQL_LITE_PROTEIN_CACHE_FILENAME)
            Else
                mSqlLiteDBFilePath = DefineSqlLiteDBPath(Path.GetFileNameWithoutExtension(SQL_LITE_PROTEIN_CACHE_FILENAME) &
                                                         fileAttemptCount.ToString &
                                                         Path.GetExtension(SQL_LITE_PROTEIN_CACHE_FILENAME))
            End If

            Try
                ' If the file exists, we need to delete it
                If File.Exists(mSqlLiteDBFilePath) Then
                    OnDebugEvent("InitializeLocalVariables: deleting " + mSqlLiteDBFilePath)
                    File.Delete(mSqlLiteDBFilePath)
                End If

                If Not File.Exists(mSqlLiteDBFilePath) Then
                    success = True
                End If

            Catch ex As Exception
                ' Error deleting the file
                OnWarningEvent("Exception in InitializeLocalVariables: " + ex.Message)
            End Try

            fileAttemptCount += 1
        Loop

        mSqlLiteDBConnectionString = "Data Source=" & mSqlLiteDBFilePath & ";"

    End Sub

    ''' <summary>
    ''' Examines the file's extension and true if it ends in .fasta or .fsa or .faa
    ''' </summary>
    ''' <param name="filePath"></param>
    ''' <returns></returns>
    Public Shared Function IsFastaFile(filePath As String) As Boolean

        Dim proteinFileExtension = Path.GetExtension(filePath).ToLower()

        If proteinFileExtension = ".fasta" OrElse proteinFileExtension = ".fsa" OrElse proteinFileExtension = ".faa" Then
            Return True
        Else
            Return False
        End If

    End Function

    Public Function ParseProteinFile(proteinInputFilePath As String) As Boolean
        ' If outputFileNameBaseOverride is defined, then uses that name for the protein output filename rather than auto-defining the name

        ' Create the SQL Lite DB
        Dim sqlConnection = ConnectToSqlLiteDB(True)

        ' SQL query to Create the Table
        Dim cmd = sqlConnection.CreateCommand
        cmd.CommandText = "CREATE TABLE udtProteinInfoType( " &
                                    "Name TEXT, " &
                                    "Description TEXT, " &
                                    "sequence TEXT, " &
                                    "UniquesequenceID INTEGER PRIMARY KEY, " &
                                    "PercentCoverage REAL);" ', NonUniquePeptideCount INTEGER, UniquePeptideCount INTEGER);"

        OnDebugEvent("ParseProteinFile: Creating table with " + cmd.CommandText)

        cmd.ExecuteNonQuery()

        ' Define a RegEx to replace all of the non-letter characters
        Dim reReplaceSymbols = New Regex("[^A-Za-z]", RegexOptions.Compiled)

        Dim proteinFileReader As ProteinFileReaderBaseClass = Nothing

        Dim success As Boolean

        Try

            If proteinInputFilePath Is Nothing OrElse proteinInputFilePath.Length = 0 Then
                ReportError("Empty protein input file path")
                success = False
            Else

                If AssumeFastaFile OrElse IsFastaFile(proteinInputFilePath) Then
                    mParsedFileIsFastaFile = True
                Else
                    If AssumeDelimitedFile Then
                        mParsedFileIsFastaFile = False
                    Else
                        mParsedFileIsFastaFile = True
                    End If
                End If

                If mParsedFileIsFastaFile Then
                    proteinFileReader = New FastaFileReader() With {
                        .ProteinLineStartChar = FastaFileOptions.ProteinLineStartChar,
                        .ProteinLineAccessionEndChar = FastaFileOptions.ProteinLineAccessionEndChar
                    }
                Else
                    proteinFileReader = New DelimitedFileReader With {
                        .Delimiter = mDelimitedInputFileDelimiter,
                        .DelimitedFileFormatCode = DelimitedFileFormatCode,
                        .SkipFirstLine = DelimitedFileSkipFirstLine
                    }
                End If

                ' Verify that the input file exists
                If Not File.Exists(proteinInputFilePath) Then
                    ReportError("Protein input file not found: " & proteinInputFilePath)
                    success = False
                    Exit Try
                End If

                ' Attempt to open the input file
                If Not proteinFileReader.OpenFile(proteinInputFilePath) Then
                    ReportError("Error opening protein input file: " & proteinInputFilePath)
                    success = False
                    Exit Try
                End If

                success = True
            End If

        Catch ex As Exception
            ReportError("Error opening protein input file (" & proteinInputFilePath & "): " & ex.Message, ex)
            success = False
        End Try

        ' Abort processing if we couldn't successfully open the input file
        If Not success Then Return False

        Try
            ' Read each protein in the input file and process appropriately
            mProteinCount = 0

            RaiseEvent ProteinCachingStart()

            ' Create a parameterized Insert query
            cmd.CommandText = " INSERT INTO udtProteinInfoType(Name, Description, sequence, UniquesequenceID, PercentCoverage) " &
                                     " VALUES (?, ?, ?, ?, ?)"

            Dim nameFld As SQLiteParameter = cmd.CreateParameter
            Dim descriptionFld As SQLiteParameter = cmd.CreateParameter
            Dim sequenceFld As SQLiteParameter = cmd.CreateParameter
            Dim uniqueSequenceIDFld As SQLiteParameter = cmd.CreateParameter
            Dim percentCoverageFld As SQLiteParameter = cmd.CreateParameter
            cmd.Parameters.Add(nameFld)
            cmd.Parameters.Add(descriptionFld)
            cmd.Parameters.Add(sequenceFld)
            cmd.Parameters.Add(uniqueSequenceIDFld)
            cmd.Parameters.Add(percentCoverageFld)

            ' Begin a SQL Transaction
            Dim SQLTransaction = sqlConnection.BeginTransaction()

            Dim proteinsProcessed = 0
            Dim inputFileLinesRead = 0

            Do
                Dim inputProteinFound = proteinFileReader.ReadNextProteinEntry()

                If Not inputProteinFound Then
                    Exit Do
                End If

                proteinsProcessed += 1
                inputFileLinesRead = proteinFileReader.LinesRead

                Dim name = proteinFileReader.ProteinName
                Dim description = proteinFileReader.ProteinDescription
                Dim sequence As String

                If RemoveSymbolCharacters Then
                    sequence = reReplaceSymbols.Replace(proteinFileReader.ProteinSequence, String.Empty)
                Else
                    sequence = proteinFileReader.ProteinSequence
                End If

                If ChangeProteinSequencesToLowercase Then
                    If IgnoreILDifferences Then
                        ' Replace all L characters with I
                        sequence = sequence.ToLower().Replace("l"c, "i"c)
                    Else
                        sequence = sequence.ToLower()
                    End If
                ElseIf ChangeProteinSequencesToUppercase Then
                    If IgnoreILDifferences Then
                        ' Replace all L characters with I
                        sequence = sequence.ToUpper.Replace("L"c, "I"c)
                    Else
                        sequence = sequence.ToUpper
                    End If
                Else
                    If IgnoreILDifferences Then
                        ' Replace all L characters with I
                        sequence = sequence.Replace("L"c, "I"c).Replace("l"c, "i"c)
                    End If
                End If

                ' Store this protein in the Sql Lite DB
                nameFld.Value = name
                descriptionFld.Value = description
                sequenceFld.Value = sequence

                ' Use mProteinCount to assign UniqueSequenceID values
                uniqueSequenceIDFld.Value = mProteinCount

                percentCoverageFld.Value = 0

                cmd.ExecuteNonQuery()

                mProteinCount += 1

                RaiseEvent ProteinCached(mProteinCount)

                If mProteinCount Mod 100 = 0 Then
                    RaiseEvent ProteinCachedWithProgress(mProteinCount, proteinFileReader.PercentFileProcessed)
                End If

                success = True
            Loop

            ' Finalize the SQL Transaction
            SQLTransaction.Commit()

            ' Set Synchronous mode to 1   (this may not be truly necessary)
            cmd.CommandText = "PRAGMA synchronous=1"
            cmd.ExecuteNonQuery()

            ' Close the Sql Lite DB
            cmd.Dispose()
            sqlConnection.Close()

            ' Close the protein file
            proteinFileReader.CloseFile()

            RaiseEvent ProteinCachingComplete()

            If success Then
                OnStatusEvent("Done: Processed " & proteinsProcessed.ToString("###,##0") & " proteins (" & inputFileLinesRead.ToString("###,###,##0") & " lines)")
            Else
                OnErrorEvent(mStatusMessage)
            End If

        Catch ex As Exception
            ReportError("Error reading protein input file (" & proteinInputFilePath & "): " & ex.Message, ex)
            success = False
        End Try

        Return success

    End Function

    Private Sub ReportError(errorMessage As String, Optional ex As Exception = Nothing)
        OnErrorEvent(errorMessage, ex)
        mStatusMessage = errorMessage
    End Sub

    ' Options class
    Public Class FastaFileOptionsClass

        Public Sub New()
            mProteinLineStartChar = ">"c
            mProteinLineAccessionEndChar = " "c

        End Sub

#Region "Classwide Variables"

        Private mProteinLineStartChar As Char
        Private mProteinLineAccessionEndChar As Char

#End Region

#Region "Processing Options Interface Functions"

        Public Property ProteinLineStartChar As Char
            Get
                Return mProteinLineStartChar
            End Get
            Set
                If Not Value = Nothing Then
                    mProteinLineStartChar = Value
                End If
            End Set
        End Property

        Public Property ProteinLineAccessionEndChar As Char
            Get
                Return mProteinLineAccessionEndChar
            End Get
            Set
                If Not Value = Nothing Then
                    mProteinLineAccessionEndChar = Value
                End If
            End Set
        End Property

#End Region

    End Class

End Class
