Option Strict On

Imports ProteinCoverageSummarizer
Imports ProteinFileReader
' This class uses ProteinCoverageSummarizer.dll to read in a protein fasta file or delimited protein info file along with
' an accompanying file with peptide sequences to then compute the percent coverage of each of the proteins
'
' -------------------------------------------------------------------------------
' Written by Matthew Monroe and Nik�a Blonder for the Department of Energy (PNNL, Richland, WA)
' Program started June 14, 2005
'
' E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov
' Website: http://omics.pnl.gov/ or http://www.sysbio.org/resources/staff/
' -------------------------------------------------------------------------------
'
' Licensed under the Apache License, Version 2.0; you may not use this file except
' in compliance with the License.  You may obtain a copy of the License at
' http://www.apache.org/licenses/LICENSE-2.0
'

Public Class clsProteinCoverageSummarizerRunner
    Inherits PRISM.FileProcessor.ProcessFilesBase

    Public Sub New()
        InitializeVariables()
    End Sub

#Region "Constants and Enums"
    Public Enum eProteinCoverageErrorCodes
        NoError = 0
        UnspecifiedError = -1
    End Enum
#End Region

#Region "Structures"

#End Region

#Region "Classwide variables"
    Private mProteinCoverageSummarizer As clsProteinCoverageSummarizer

    Private mCallingAppHandlesEvents As Boolean

    Private mStatusMessage As String

#End Region

#Region "Properties"

    Public Property CallingAppHandlesEvents() As Boolean
        Get
            Return mCallingAppHandlesEvents
        End Get
        Set(value As Boolean)
            mCallingAppHandlesEvents = value
        End Set
    End Property

    Public Property IgnoreILDifferences() As Boolean
        Get
            Return mProteinCoverageSummarizer.IgnoreILDifferences
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.IgnoreILDifferences = Value
        End Set
    End Property

    Public Property MatchPeptidePrefixAndSuffixToProtein() As Boolean
        Get
            Return mProteinCoverageSummarizer.MatchPeptidePrefixAndSuffixToProtein
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.MatchPeptidePrefixAndSuffixToProtein = Value
        End Set
    End Property

    Public Property OutputProteinSequence() As Boolean
        Get
            Return mProteinCoverageSummarizer.OutputProteinSequence
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.OutputProteinSequence = Value
        End Set
    End Property

    Public Property PeptideFileFormatCode() As clsProteinCoverageSummarizer.ePeptideFileColumnOrderingCode
        Get
            Return mProteinCoverageSummarizer.PeptideFileFormatCode
        End Get
        Set(Value As clsProteinCoverageSummarizer.ePeptideFileColumnOrderingCode)
            mProteinCoverageSummarizer.PeptideFileFormatCode = Value
        End Set
    End Property

    Public Property PeptideFileSkipFirstLine() As Boolean
        Get
            Return mProteinCoverageSummarizer.PeptideFileSkipFirstLine
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.PeptideFileSkipFirstLine = Value
        End Set
    End Property

    Public Property PeptideInputFileDelimiter() As Char
        Get
            Return mProteinCoverageSummarizer.PeptideInputFileDelimiter
        End Get
        Set(Value As Char)
            mProteinCoverageSummarizer.PeptideInputFileDelimiter = Value
        End Set
    End Property

    Public Property ProteinDataDelimitedFileDelimiter() As Char
        Get
            Return mProteinCoverageSummarizer.mProteinDataCache.DelimitedFileDelimiter
        End Get
        Set(value As Char)
            mProteinCoverageSummarizer.mProteinDataCache.DelimitedFileDelimiter = value
        End Set
    End Property
    Public Property ProteinDataDelimitedFileFormatCode() As DelimitedFileReader.eDelimitedFileFormatCode
        Get
            Return mProteinCoverageSummarizer.mProteinDataCache.DelimitedFileFormatCode
        End Get
        Set(value As DelimitedFileReader.eDelimitedFileFormatCode)
            mProteinCoverageSummarizer.mProteinDataCache.DelimitedFileFormatCode = value
        End Set
    End Property
    Public Property ProteinDataDelimitedFileSkipFirstLine() As Boolean
        Get
            Return mProteinCoverageSummarizer.mProteinDataCache.DelimitedFileSkipFirstLine
        End Get
        Set(value As Boolean)
            mProteinCoverageSummarizer.mProteinDataCache.DelimitedFileSkipFirstLine = value
        End Set
    End Property
    Public Property ProteinDataRemoveSymbolCharacters() As Boolean
        Get
            Return mProteinCoverageSummarizer.mProteinDataCache.RemoveSymbolCharacters
        End Get
        Set(value As Boolean)
            mProteinCoverageSummarizer.mProteinDataCache.RemoveSymbolCharacters = value
        End Set
    End Property
    Public Property ProteinDataIgnoreILDifferences() As Boolean
        Get
            Return mProteinCoverageSummarizer.mProteinDataCache.IgnoreILDifferences
        End Get
        Set(value As Boolean)
            mProteinCoverageSummarizer.mProteinDataCache.IgnoreILDifferences = value
        End Set
    End Property

    Public Property ProteinInputFilePath() As String
        Get
            Return mProteinCoverageSummarizer.ProteinInputFilePath
        End Get
        Set(Value As String)
            mProteinCoverageSummarizer.ProteinInputFilePath = Value
        End Set
    End Property

    Public ReadOnly Property ProteinToPeptideMappingFilePath() As String
        Get
            Return mProteinCoverageSummarizer.ProteinToPeptideMappingFilePath
        End Get
    End Property

    Public Property RemoveSymbolCharacters() As Boolean
        Get
            Return mProteinCoverageSummarizer.RemoveSymbolCharacters
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.RemoveSymbolCharacters = Value
        End Set
    End Property

    Public ReadOnly Property ResultsFilePath() As String
        Get
            Return mProteinCoverageSummarizer.ResultsFilePath
        End Get
    End Property

    Public Property SaveProteinToPeptideMappingFile() As Boolean
        Get
            Return mProteinCoverageSummarizer.SaveProteinToPeptideMappingFile
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.SaveProteinToPeptideMappingFile = Value
        End Set
    End Property

    Public Property SearchAllProteinsForPeptideSequence() As Boolean
        Get
            Return mProteinCoverageSummarizer.SearchAllProteinsForPeptideSequence
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.SearchAllProteinsForPeptideSequence = Value
        End Set
    End Property

    Public Property UseLeaderSequenceHashTable() As Boolean
        Get
            Return mProteinCoverageSummarizer.UseLeaderSequenceHashTable
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.UseLeaderSequenceHashTable = Value
        End Set
    End Property

    Public Property SearchAllProteinsSkipCoverageComputationSteps() As Boolean
        Get
            Return mProteinCoverageSummarizer.SearchAllProteinsSkipCoverageComputationSteps
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.SearchAllProteinsSkipCoverageComputationSteps = Value
        End Set
    End Property

    Public ReadOnly Property StatusMessage() As String
        Get
            Return mStatusMessage
        End Get
    End Property

    Public Property TrackPeptideCounts() As Boolean
        Get
            Return mProteinCoverageSummarizer.TrackPeptideCounts
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.TrackPeptideCounts = Value
        End Set
    End Property

#End Region

    Public Overrides Sub AbortProcessingNow()
        MyBase.AbortProcessingNow()
        If Not mProteinCoverageSummarizer Is Nothing Then
            mProteinCoverageSummarizer.AbortProcessingNow()
        End If
    End Sub

    Public Overrides Function GetErrorMessage() As String
        Return MyBase.GetBaseClassErrorMessage
    End Function

    Private Sub InitializeVariables()
        Me.mCallingAppHandlesEvents = False

        AbortProcessing = False
        mStatusMessage = String.Empty

        mProteinCoverageSummarizer = New clsProteinCoverageSummarizer()
        RegisterEvents(mProteinCoverageSummarizer)

        AddHandler mProteinCoverageSummarizer.ProgressChanged, AddressOf mProteinCoverageSummarizer_ProgressChanged

        AddHandler mProteinCoverageSummarizer.ProgressReset, AddressOf mProteinCoverageSummarizer_ProgressReset

    End Sub

    Public Function LoadParameterFileSettings(strParameterFilePath As String) As Boolean
        Return mProteinCoverageSummarizer.LoadParameterFileSettings(strParameterFilePath)
    End Function

    Public Overloads Overrides Function ProcessFile(strInputFilePath As String, strOutputFolderPath As String, strParameterFilePath As String, blnResetErrorCode As Boolean) As Boolean

        Dim blnSuccess As Boolean

        If blnResetErrorCode Then
            MyBase.SetBaseClassErrorCode(eProcessFilesErrorCodes.NoError)
        End If

        Try
            ' Show the progress form
            If Not mCallingAppHandlesEvents Then
                Console.WriteLine(MyBase.ProgressStepDescription)
            End If

            ' Call mProteinCoverageSummarizer.ProcessFile to perform the work
            blnSuccess = mProteinCoverageSummarizer.ProcessFile(strInputFilePath, strOutputFolderPath, strParameterFilePath, True)

            mProteinCoverageSummarizer.mProteinDataCache.DeleteSQLiteDBFile()

        Catch ex As Exception
            mStatusMessage = "Error in ProcessFile:" & ControlChars.NewLine & ex.Message
            OnErrorEvent(mStatusMessage, ex)
            blnSuccess = False
        End Try

        Return blnSuccess

    End Function

    Private Sub mProteinCoverageSummarizer_ProgressChanged(taskDescription As String, percentComplete As Single)
        UpdateProgress(taskDescription, percentComplete)

        ''If mUseProgressForm AndAlso Not mProgressForm Is Nothing Then
        ''    mProgressForm.UpdateCurrentTask(taskDescription)
        ''    mProgressForm.UpdateProgressBar(percentComplete)
        ''    Windows.Forms.Application.DoEvents()
        ''End If
    End Sub

    Private Sub mProteinCoverageSummarizer_ProgressReset()
        ResetProgress(mProteinCoverageSummarizer.ProgressStepDescription)

        ''If mUseProgressForm AndAlso Not mProgressForm Is Nothing Then
        ''    mProgressForm.UpdateProgressBar(0, True)
        ''    mProgressForm.UpdateCurrentTask(mProteinCoverageSummarizer.ProgressStepDescription)
        ''End If

    End Sub

End Class
