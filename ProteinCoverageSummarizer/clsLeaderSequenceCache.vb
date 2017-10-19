Option Strict On

Imports System.IO
Imports System.Text.RegularExpressions

' This class tracks the first n letters of each peptide sent to it, while also
' tracking the peptides and the location of those peptides in the leader sequence hash table
'
' -------------------------------------------------------------------------------
' Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
' Class started August 24, 2007
'
' E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov
' Website: https://panomics.pnl.gov/ or https://omics.pnl.gov
' -------------------------------------------------------------------------------
'
' Licensed under the Apache License, Version 2.0; you may not use this file except
' in compliance with the License.  You may obtain a copy of the License at
' http://www.apache.org/licenses/LICENSE-2.0
'

Public Class clsLeaderSequenceCache

    Public Sub New()
        InitializeVariables()
    End Sub

#Region "Constants and Enums"
    Public Const DEFAULT_LEADER_SEQUENCE_LENGTH As Integer = 5
    Public Const MINIMUM_LEADER_SEQUENCE_LENGTH As Integer = 5

    Private Const INITIAL_LEADER_SEQUENCE_COUNT_TO_RESERVE As Integer = 10000
    Public Const MAX_LEADER_SEQUENCE_COUNT As Integer = 500000
#End Region

#Region "Structures"
    Public Structure udtPeptideSequenceInfoType
        ''' <summary>
        ''' Protein name (optional)
        ''' </summary>
        Public ProteinName As String

        ''' <summary>
        ''' Peptide amino acids (stored as uppercase letters)
        ''' </summary>
        Public PeptideSequence As String

        ''' <summary>
        ''' Prefix residue
        ''' </summary>
        Public Prefix As Char

        ''' <summary>
        ''' Suffix residue
        ''' </summary>
        Public Suffix As Char

        ''' <summary>
        ''' Peptide sequence where leucines have been changed to isoleucine
        ''' </summary>
        ''' <remarks>Only used if mIgnoreILDifferences is True</remarks>
        Public PeptideSequenceLtoI As String

        ''' <summary>
        ''' Prefix residue; if leucine, changed to isoleucine
        ''' </summary>
        ''' <remarks>Only used if mIgnoreILDifferences is True</remarks>
        Public PrefixLtoI As Char

        ''' <summary>
        ''' Suffix residue; if leucine, changed to isoleucine
        ''' </summary>
        ''' <remarks>Only used if mIgnoreILDifferences is True</remarks>
        Public SuffixLtoI As Char

        ''' <summary>
        ''' Show the peptide sequence, including prefix and suffix
        ''' </summary>
        ''' <returns></returns>
        Public Overrides Function ToString() As String
            If String.IsNullOrWhiteSpace(Prefix) Then
                Return PeptideSequence
            End If

            Return Prefix & "." & PeptideSequence & "." & Suffix
        End Function
    End Structure

#End Region

#Region "Classwide variables"
    Private mLeaderSequenceMinimumLength As Integer
    Private mLeaderSequences As Dictionary(Of String, Integer)

    Public mCachedPeptideCount As Integer
    Public mCachedPeptideSeqInfo() As udtPeptideSequenceInfoType
    Private mCachedPeptideToHashIndexPointer() As Integer               ' Parallel to mCachedPeptideSeqInfo
    Private mIndicesSorted As Boolean

    Private mErrorMessage As String
    Private mAbortProcessing As Boolean

    Private mIgnoreILDifferences As Boolean

    Public Event ProgressReset()
    Public Event ProgressChanged(taskDescription As String, percentComplete As Single)     ' PercentComplete ranges from 0 to 100, but can contain decimal percentage values
    Public Event ProgressComplete()

    Protected mProgressStepDescription As String
    Protected mProgressPercentComplete As Single        ' Ranges from 0 to 100, but can contain decimal percentage values

#End Region

#Region "Properties"
    Public ReadOnly Property CachedPeptideCount As Integer
        Get
            Return mCachedPeptideCount
        End Get
    End Property

    Public ReadOnly Property ErrorMessage As String
        Get
            Return mErrorMessage
        End Get
    End Property

    Public Property IgnoreILDifferences As Boolean
        Get
            Return mIgnoreILDifferences
        End Get
        Set
            mIgnoreILDifferences = Value
        End Set
    End Property

    Public Property LeaderSequenceMinimumLength As Integer
        Get
            Return mLeaderSequenceMinimumLength
        End Get
        Set
            mLeaderSequenceMinimumLength = Value
        End Set
    End Property

    Public ReadOnly Property ProgressStepDescription As String
        Get
            Return mProgressStepDescription
        End Get
    End Property

    ' ProgressPercentComplete ranges from 0 to 100, but can contain decimal percentage values
    Public ReadOnly Property ProgressPercentComplete As Single
        Get
            Return CType(Math.Round(mProgressPercentComplete, 2), Single)
        End Get
    End Property

#End Region

    Public Sub AbortProcessingNow()
        mAbortProcessing = True
    End Sub

    Public Function CachePeptide(strPeptideSequence As String, chPrefixResidue As Char, chSuffixResidue As Char) As Boolean
        Return CachePeptide(strPeptideSequence, Nothing, chPrefixResidue, chSuffixResidue)
    End Function

    ''' <summary>
    ''' Caches the peptide and updates mLeaderSequences
    ''' </summary>
    ''' <param name="strPeptideSequence"></param>
    ''' <param name="strProteinName"></param>
    ''' <param name="chPrefixResidue"></param>
    ''' <param name="chSuffixResidue"></param>
    ''' <returns></returns>
    Public Function CachePeptide(strPeptideSequence As String, strProteinName As String, chPrefixResidue As Char, chSuffixResidue As Char) As Boolean

        Try
            If strPeptideSequence Is Nothing OrElse strPeptideSequence.Length < mLeaderSequenceMinimumLength Then
                ' Peptide is too short; cannot process it
                mErrorMessage = "Peptide length is shorter than " & mLeaderSequenceMinimumLength.ToString & "; unable to cache the peptide"
                Return False
            Else
                mErrorMessage = String.Empty
            End If

            ' Make sure the residues are capitalized
            strPeptideSequence = strPeptideSequence.ToUpper
            If Char.IsLetter(chPrefixResidue) Then chPrefixResidue = Char.ToUpper(chPrefixResidue)
            If Char.IsLetter(chSuffixResidue) Then chSuffixResidue = Char.ToUpper(chSuffixResidue)

            Dim strLeaderSequence = strPeptideSequence.Substring(0, mLeaderSequenceMinimumLength)
            Dim chPrefixResidueLtoI = chPrefixResidue
            Dim chSuffixResidueLtoI = chSuffixResidue

            If mIgnoreILDifferences Then
                ' Replace all L characters with I
                strLeaderSequence = strLeaderSequence.Replace("L"c, "I"c)

                If chPrefixResidueLtoI = "L"c Then chPrefixResidueLtoI = "I"c
                If chSuffixResidueLtoI = "L"c Then chSuffixResidueLtoI = "I"c
            End If

            Dim hashIndexPointer As Integer

            ' Look for strLeaderSequence in mLeaderSequences
            If Not mLeaderSequences.TryGetValue(strLeaderSequence, hashIndexPointer) Then
                ' strLeaderSequence was not found; add it and initialize intHashIndexPointer
                hashIndexPointer = mLeaderSequences.Count
                mLeaderSequences.Add(strLeaderSequence, hashIndexPointer)
            End If

            ' Expand mCachedPeptideSeqInfo if needed
            If mCachedPeptideCount >= mCachedPeptideSeqInfo.Length AndAlso mCachedPeptideCount < MAX_LEADER_SEQUENCE_COUNT Then
                ReDim Preserve mCachedPeptideSeqInfo(mCachedPeptideSeqInfo.Length * 2 - 1)
                ReDim Preserve mCachedPeptideToHashIndexPointer(mCachedPeptideSeqInfo.Length - 1)
            End If

            ' Add strPeptideSequence to mCachedPeptideSeqInfo
            With mCachedPeptideSeqInfo(mCachedPeptideCount)
                .ProteinName = String.Copy(strProteinName)
                .PeptideSequence = String.Copy(strPeptideSequence)
                .Prefix = chPrefixResidue
                .Suffix = chSuffixResidue
                .PrefixLtoI = chPrefixResidueLtoI
                .SuffixLtoI = chSuffixResidueLtoI
                If mIgnoreILDifferences Then
                    .PeptideSequenceLtoI = strPeptideSequence.Replace("L"c, "I"c)
                End If
            End With

            ' Update the peptide to Hash Index pointer array
            mCachedPeptideToHashIndexPointer(mCachedPeptideCount) = hashIndexPointer
            mCachedPeptideCount += 1
            mIndicesSorted = False

            Return True

        Catch ex As Exception
            Throw New Exception("Error in CachePeptide", ex)
        End Try

    End Function

    Public Function DetermineShortestPeptideLengthInFile(strInputFilePath As String, intTerminatorSize As Integer,
                            blnPeptideFileSkipFirstLine As Boolean, chPeptideInputFileDelimiter As Char,
                            intColumnNumWithPeptideSequence As Integer) As Boolean

        ' Parses strInputFilePath examining column intColumnNumWithPeptideSequence to determine the minimum peptide sequence length present
        ' Updates mLeaderSequenceMinimumLength if successful, though the minimum length is not allowed to be less than MINIMUM_LEADER_SEQUENCE_LENGTH

        ' intColumnNumWithPeptideSequence should be 1 if the peptide sequence is in the first column, 2 if in the second, etc.

        ' Define a RegEx to replace all of the non-letter characters
        Dim reReplaceSymbols = New Regex("[^A-Za-z]", RegexOptions.Compiled)

        Try
            Dim intValidPeptideCount = 0
            Dim intLeaderSequenceMinimumLength = 0

            ' Open the file and read in the lines
            Using srInFile = New StreamReader(New FileStream(strInputFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))


                Dim intCurrentLine = 1
                Dim bytesRead As Long = 0

                Do While Not srInFile.EndOfStream
                    If mAbortProcessing Then Exit Do

                    Dim strLineIn = srInFile.ReadLine
                    If strLineIn Is Nothing Then Continue Do

                    bytesRead += strLineIn.Length + intTerminatorSize

                    strLineIn = strLineIn.Trim

                    If intCurrentLine Mod 100 = 1 Then
                        UpdateProgress("Scanning input file to determine minimum peptide length: " & intCurrentLine.ToString, CSng((bytesRead / srInFile.BaseStream.Length) * 100))
                    End If

                    If intCurrentLine = 1 AndAlso blnPeptideFileSkipFirstLine Then
                        ' Do nothing, skip the first line
                    ElseIf strLineIn.Length > 0 Then

                        Dim blnValidLine As Boolean
                        Dim strPeptideSequence = ""

                        Try
                            Dim strSplitLine = strLineIn.Split(chPeptideInputFileDelimiter)

                            If intColumnNumWithPeptideSequence >= 1 And intColumnNumWithPeptideSequence < strSplitLine.Length - 1 Then
                                strPeptideSequence = strSplitLine(intColumnNumWithPeptideSequence - 1)
                            Else
                                strPeptideSequence = strSplitLine(0)
                            End If
                            blnValidLine = True
                        Catch ex As Exception
                            blnValidLine = False
                        End Try

                        If blnValidLine Then
                            If strPeptideSequence.Length >= 4 Then
                                ' Check for, and remove any prefix or suffix residues
                                If strPeptideSequence.Chars(1) = "."c AndAlso strPeptideSequence.Chars(strPeptideSequence.Length - 2) = "."c Then
                                    strPeptideSequence = strPeptideSequence.Substring(2, strPeptideSequence.Length - 4)
                                End If
                            End If

                            ' Remove any non-letter characters
                            strPeptideSequence = reReplaceSymbols.Replace(strPeptideSequence, String.Empty)

                            If strPeptideSequence.Length >= MINIMUM_LEADER_SEQUENCE_LENGTH Then
                                If intValidPeptideCount = 0 Then
                                    intLeaderSequenceMinimumLength = strPeptideSequence.Length
                                Else
                                    If strPeptideSequence.Length < intLeaderSequenceMinimumLength Then
                                        intLeaderSequenceMinimumLength = strPeptideSequence.Length
                                    End If
                                End If
                                intValidPeptideCount += 1
                            End If
                        End If

                    End If
                    intCurrentLine += 1
                Loop

            End Using

            Dim blnSuccess As Boolean

            If intValidPeptideCount = 0 Then
                ' No valid peptides were found; either no peptides are in the file or they're all shorter than MINIMUM_LEADER_SEQUENCE_LENGTH
                mLeaderSequenceMinimumLength = MINIMUM_LEADER_SEQUENCE_LENGTH
                blnSuccess = False
            Else
                mLeaderSequenceMinimumLength = intLeaderSequenceMinimumLength
                blnSuccess = True
            End If

            OperationComplete()
            Return blnSuccess

        Catch ex As Exception
            Throw New Exception("Error in DetermineShortestPeptideLengthInFile", ex)
        End Try

    End Function

    Public Function GetFirstPeptideIndexForLeaderSequence(strLeaderSequenceToFind As String) As Integer
        ' Looks up the first index value in mCachedPeptideSeqInfo that matches strLeaderSequenceToFind
        ' Returns the index value if found, or -1 if not found
        ' Calls SortIndices if mIndicesSorted = False

        Dim targetHashIndex As Integer

        If Not mLeaderSequences.TryGetValue(strLeaderSequenceToFind, targetHashIndex) Then
            Return -1
        End If

        ' Item found in mLeaderSequences
        ' Return the first peptide index value mapped to objzItem

        If Not mIndicesSorted Then
            SortIndices()
        End If

        Dim intCachedPeptideMatchIndex = Array.BinarySearch(mCachedPeptideToHashIndexPointer, 0, mCachedPeptideCount, targetHashIndex)

        Do While intCachedPeptideMatchIndex > 0 AndAlso mCachedPeptideToHashIndexPointer(intCachedPeptideMatchIndex - 1) = targetHashIndex
            intCachedPeptideMatchIndex -= 1
        Loop

        Return intCachedPeptideMatchIndex

    End Function

    Public Function GetNextPeptideWithLeaderSequence(intCachedPeptideMatchIndexCurrent As Integer) As Integer
        If intCachedPeptideMatchIndexCurrent < mCachedPeptideCount - 1 Then
            If mCachedPeptideToHashIndexPointer(intCachedPeptideMatchIndexCurrent + 1) = mCachedPeptideToHashIndexPointer(intCachedPeptideMatchIndexCurrent) Then
                Return intCachedPeptideMatchIndexCurrent + 1
            Else
                Return -1
            End If
        Else
            Return -1
        End If
    End Function


    Public Sub InitializeCachedPeptides()
        mCachedPeptideCount = 0
        ReDim mCachedPeptideSeqInfo(INITIAL_LEADER_SEQUENCE_COUNT_TO_RESERVE - 1)
        ReDim mCachedPeptideToHashIndexPointer(mCachedPeptideSeqInfo.Length - 1)

        mIndicesSorted = False

        If mLeaderSequences Is Nothing Then
            mLeaderSequences = New Dictionary(Of String, Integer)
        Else
            mLeaderSequences.Clear()
        End If
    End Sub

    Public Sub InitializeVariables()

        mLeaderSequenceMinimumLength = DEFAULT_LEADER_SEQUENCE_LENGTH
        mErrorMessage = String.Empty
        mAbortProcessing = False

        mIgnoreILDifferences = False

        InitializeCachedPeptides()
    End Sub

    Private Sub SortIndices()
        Array.Sort(mCachedPeptideToHashIndexPointer, mCachedPeptideSeqInfo, 0, mCachedPeptideCount)
        mIndicesSorted = True
    End Sub

    Protected Sub ResetProgress()
        RaiseEvent ProgressReset()
    End Sub

    Protected Sub ResetProgress(strProgressStepDescription As String)
        UpdateProgress(strProgressStepDescription, 0)
        RaiseEvent ProgressReset()
    End Sub

    Protected Sub UpdateProgress(strProgressStepDescription As String)
        UpdateProgress(strProgressStepDescription, mProgressPercentComplete)
    End Sub

    Protected Sub UpdateProgress(sngPercentComplete As Single)
        UpdateProgress(Me.ProgressStepDescription, sngPercentComplete)
    End Sub

    Protected Sub UpdateProgress(strProgressStepDescription As String, sngPercentComplete As Single)
        mProgressStepDescription = String.Copy(strProgressStepDescription)
        If sngPercentComplete < 0 Then
            sngPercentComplete = 0
        ElseIf sngPercentComplete > 100 Then
            sngPercentComplete = 100
        End If
        mProgressPercentComplete = sngPercentComplete

        RaiseEvent ProgressChanged(Me.ProgressStepDescription, Me.ProgressPercentComplete)
    End Sub

    Protected Sub OperationComplete()
        RaiseEvent ProgressComplete()
    End Sub

End Class
