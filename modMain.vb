Option Strict On

' -------------------------------------------------------------------------------
' Written by Matthew Monroe and Nik�a Blonder for the Department of Energy (PNNL, Richland, WA)
' Program started June 14, 2005
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

Imports System.Runtime.InteropServices
Imports PRISM

''' <summary>
''' This program uses clsProteinCoverageSummarizer to read in a file with protein sequences along with
''' an accompanying file with peptide sequences and compute the percent coverage of each of the proteins
'''
''' Example command Line
''' /I:PeptideInputFilePath /R:ProteinInputFilePath /O:OutputFolderPath /P:ParameterFilePath
''' </summary>
Public Module modMain

    Public Const PROGRAM_DATE As String = "September 14, 2018"

    Private mPeptideInputFilePath As String
    Private mProteinInputFilePath As String
    Private mOutputFolderPath As String
    Private mParameterFilePath As String

    Private mIgnoreILDifferences As Boolean
    Private mOutputProteinSequence As Boolean
    Private mSaveProteinToPeptideMappingFile As Boolean
    Private mSkipCoverageComputationSteps As Boolean
    Private mDebugMode As Boolean
    Private mKeepDB As Boolean

    Private mProteinCoverageSummarizer As clsProteinCoverageSummarizerRunner
    Private mLastProgressReportTime As DateTime
    Private mLastProgressReportValue As Integer

    <DllImport("kernel32.dll")>
    Private Function GetConsoleWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    Const SW_HIDE As Integer = 0
    Const SW_SHOW As Integer = 5

    Public Function Main() As Integer
        ' Returns 0 if no error, error code if an error
        Dim intReturnCode As Integer
        Dim objParseCommandLine As New PRISM.clsParseCommandLine
        Dim blnProceed As Boolean

        intReturnCode = 0
        mPeptideInputFilePath = String.Empty
        mProteinInputFilePath = String.Empty
        mParameterFilePath = String.Empty

        mIgnoreILDifferences = False
        mOutputProteinSequence = True
        mSaveProteinToPeptideMappingFile = False
        mSkipCoverageComputationSteps = False
        mDebugMode = False
        mKeepDB = False

        Try
            blnProceed = False
            If objParseCommandLine.ParseCommandLine Then
                If SetOptionsUsingCommandLineParameters(objParseCommandLine) Then blnProceed = True
            End If

            Dim blnShowGUI = Not objParseCommandLine.NeedToShowHelp And (
                                 String.IsNullOrEmpty(mProteinInputFilePath))

            If blnShowGUI Then
                ShowGUI()
            ElseIf Not blnProceed OrElse objParseCommandLine.NeedToShowHelp OrElse objParseCommandLine.ParameterCount = 0 OrElse mPeptideInputFilePath.Length = 0 Then
                ShowProgramHelp()
                intReturnCode = -1
            Else
                Try
                    mProteinCoverageSummarizer = New clsProteinCoverageSummarizerRunner() With {
                        .ProteinInputFilePath = mProteinInputFilePath,
                        .CallingAppHandlesEvents = False,
                        .IgnoreILDifferences = mIgnoreILDifferences,
                        .OutputProteinSequence = mOutputProteinSequence,
                        .SaveProteinToPeptideMappingFile = mSaveProteinToPeptideMappingFile,
                        .SearchAllProteinsSkipCoverageComputationSteps = mSkipCoverageComputationSteps,
                        .KeepDB = mKeepDB
                    }

                    AddHandler mProteinCoverageSummarizer.StatusEvent, AddressOf mProteinCoverageSummarizer_StatusEvent
                    AddHandler mProteinCoverageSummarizer.ErrorEvent, AddressOf mProteinCoverageSummarizer_ErrorEvent
                    AddHandler mProteinCoverageSummarizer.WarningEvent, AddressOf mProteinCoverageSummarizer_WarningEvent

                    AddHandler mProteinCoverageSummarizer.ProgressUpdate, AddressOf mProteinCoverageSummarizer_ProgressChanged
                    AddHandler mProteinCoverageSummarizer.ProgressReset, AddressOf mProteinCoverageSummarizer_ProgressReset

                    mProteinCoverageSummarizer.ProcessFilesWildcard(mPeptideInputFilePath, mOutputFolderPath, mParameterFilePath)

                Catch ex As Exception
                    ShowErrorMessage("Error initializing Protein File Parser General Options " & ex.Message)
                End Try

            End If

        Catch ex As Exception
            ShowErrorMessage("Error occurred in modMain->Main: " & Environment.NewLine & ex.Message)
            intReturnCode = -1
        End Try

        Return intReturnCode

    End Function

    Private Sub DisplayProgressPercent(intPercentComplete As Integer, blnAddCarriageReturn As Boolean)
        If blnAddCarriageReturn Then
            Console.WriteLine()
        End If
        If intPercentComplete > 100 Then intPercentComplete = 100
        Console.Write("Processing: " & intPercentComplete.ToString() & "% ")
        If blnAddCarriageReturn Then
            Console.WriteLine()
        End If
    End Sub

    Private Function GetAppVersion() As String
        Return PRISM.FileProcessor.ProcessFilesBase.GetAppVersion(PROGRAM_DATE)
    End Function

    Private Function SetOptionsUsingCommandLineParameters(objParseCommandLine As PRISM.clsParseCommandLine) As Boolean
        ' Returns True if no problems; otherwise, returns false
        ' /I:PeptideInputFilePath /R: ProteinInputFilePath /O:OutputFolderPath /P:ParameterFilePath

        Dim strValue As String = String.Empty
        Dim validParameters = New List(Of String) From {"I", "O", "R", "P", "G", "H", "M", "K", "Debug", "KeepDB"}

        Try
            ' Make sure no invalid parameters are present
            If objParseCommandLine.InvalidParametersPresent(validParameters) Then
                ShowErrorMessage("Invalid command line parameters",
                  (From item In objParseCommandLine.InvalidParameters(validParameters) Select "/" + item).ToList())
                Return False
            Else
                With objParseCommandLine
                    ' Query objParseCommandLine to see if various parameters are present
                    If .RetrieveValueForParameter("I", strValue) Then
                        mPeptideInputFilePath = strValue
                    ElseIf .NonSwitchParameterCount > 0 Then
                        mPeptideInputFilePath = .RetrieveNonSwitchParameter(0)
                    End If

                    If .RetrieveValueForParameter("O", strValue) Then mOutputFolderPath = strValue
                    If .RetrieveValueForParameter("R", strValue) Then mProteinInputFilePath = strValue
                    If .RetrieveValueForParameter("P", strValue) Then mParameterFilePath = strValue
                    If .RetrieveValueForParameter("H", strValue) Then mOutputProteinSequence = False

                    mIgnoreILDifferences = .IsParameterPresent("G")
                    mSaveProteinToPeptideMappingFile = .IsParameterPresent("M")
                    mSkipCoverageComputationSteps = .IsParameterPresent("K")
                    mDebugMode = .IsParameterPresent("Debug")
                    mKeepDB = .IsParameterPresent("KeepDB")
                End With

                Return True
            End If

        Catch ex As Exception
            ShowErrorMessage("Error parsing the command line parameters: " & Environment.NewLine & ex.Message)
        End Try

        Return False

    End Function

    Private Sub ShowErrorMessage(message As String)
        PRISM.ConsoleMsgUtils.ShowError(message)
    End Sub

    Private Sub ShowErrorMessage(title As String, errorMessages As List(Of String))
        PRISM.ConsoleMsgUtils.ShowErrors(title, errorMessages)
    End Sub

    Private Sub ShowGUI()
        Dim objFormMain As GUI

        Application.EnableVisualStyles()
        Application.DoEvents()

        Try
            Dim handle = GetConsoleWindow()

            If Not mDebugMode Then
                ' Hide the console
                ShowWindow(handle, SW_HIDE)
            End If

            objFormMain = New GUI()
            objFormMain.KeepDB = mKeepDB

            objFormMain.ShowDialog()

            If Not mDebugMode Then
                ' Show the console
                ShowWindow(handle, SW_SHOW)
            End If
        Catch ex As Exception
            ConsoleMsgUtils.ShowWarning("Error in ShowGUI: " + ex.Message)
            ConsoleMsgUtils.ShowWarning(clsStackTraceFormatter.GetExceptionStackTraceMultiLine(ex))

            MsgBox("Error in ShowGUI: " & ex.Message, MsgBoxStyle.Exclamation Or MsgBoxStyle.OkOnly, "Error")
        End Try

    End Sub

    Private Sub ShowProgramHelp()


        Try
            Console.WriteLine(ConsoleMsgUtils.WrapParagraph(
                "This program reads in a .fasta or .txt file containing protein names and sequences (and optionally descriptions). " &
                "The program also reads in a .txt file containing peptide sequences and protein names (though protein name is optional) " &
                "then uses this information to compute the sequence coverage percent for each protein."))
            Console.WriteLine()
            Console.WriteLine("Program syntax:" & Environment.NewLine & Path.GetFileName(PRISM.FileProcessor.ProcessFilesBase.GetAppPath()))
            Console.WriteLine("  /I:PeptideInputFilePath /R:ProteinInputFilePath [/O:OutputFolderName]")
            Console.WriteLine("  [/P:ParameterFilePath] [/G] [/H] [/M] [/K] [/Debug] [/KeepDB]")
            Console.WriteLine()
            Console.WriteLine(ConsoleMsgUtils.WrapParagraph(
                "The input file path can contain the wildcard character *. If a wildcard is present, the same protein input file path " &
                "will be used for each of the peptide input files matched."))
            Console.WriteLine()
            Console.WriteLine(ConsoleMsgUtils.WrapParagraph(
                "The output folder name is optional. If omitted, the output files will be created in the same folder as the input file. " &
                "If included, a subfolder is created with the name OutputFolderName."))
            Console.WriteLine()
            Console.WriteLine(ConsoleMsgUtils.WrapParagraph(
                "The parameter file path is optional. If included, it should point to a valid XML parameter file."))
            Console.WriteLine()
            Console.WriteLine(ConsoleMsgUtils.WrapParagraph(
                "Use /G to ignore I/L differences when finding peptides in proteins or computing coverage."))
            Console.WriteLine("Use /H to suppress (hide) the protein sequence in the _coverage.txt file.")
            Console.WriteLine("Use /M to enable the creation of a protein to peptide mapping file.")
            Console.WriteLine("Use /K to skip protein coverage computation steps")
            Console.WriteLine()
            Console.WriteLine("Use /Debug to keep the console open to see additional debug messages")
            Console.WriteLine("Use /KeepDB to keep the SQLite database after processing (by default it is deleted)")
            Console.WriteLine()

            Console.WriteLine("Program written by Matthew Monroe and Nik�a Blonder for the Department of Energy (PNNL, Richland, WA) in 2005")
            Console.WriteLine("Version: " & GetAppVersion())
            Console.WriteLine()

            Console.WriteLine("E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov")
            Console.WriteLine("Website: https://omics.pnl.gov or https://panomics.pnl.gov/")
            Console.WriteLine()

        Catch ex As Exception
            ShowErrorMessage("Error displaying the program syntax: " & ex.Message)
        End Try

    End Sub

    Private Sub mProteinCoverageSummarizer_StatusEvent(message As String)
        Console.WriteLine(message)
    End Sub

    Private Sub mProteinCoverageSummarizer_WarningEvent(message As String)
        PRISM.ConsoleMsgUtils.ShowWarning(message)
    End Sub

    Private Sub mProteinCoverageSummarizer_ErrorEvent(message As String, ex As Exception)
        ShowErrorMessage(message)
    End Sub

    Private Sub mProteinCoverageSummarizer_ProgressChanged(taskDescription As String, percentComplete As Single)
        Const PERCENT_REPORT_INTERVAL = 25
        Const PROGRESS_DOT_INTERVAL_MSEC = 250

        If percentComplete >= mLastProgressReportValue Then
            If mLastProgressReportValue > 0 Then
                Console.WriteLine()
            End If
            DisplayProgressPercent(mLastProgressReportValue, False)
            mLastProgressReportValue += PERCENT_REPORT_INTERVAL
            mLastProgressReportTime = DateTime.UtcNow
        Else
            If DateTime.UtcNow.Subtract(mLastProgressReportTime).TotalMilliseconds > PROGRESS_DOT_INTERVAL_MSEC Then
                mLastProgressReportTime = DateTime.UtcNow
                Console.Write(".")
            End If
        End If
    End Sub

    Private Sub mProteinCoverageSummarizer_ProgressReset()
        mLastProgressReportTime = DateTime.UtcNow
        mLastProgressReportValue = 0
    End Sub
End Module
