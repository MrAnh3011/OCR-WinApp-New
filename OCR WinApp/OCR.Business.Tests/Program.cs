using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OCR.Business.Ai;
using OCR.Business.Auth;
using OCR.Business.Configuration;
using OCR.Business.Models;
using OCR.Business.NewGcn;
using OCR.Business.Notifications;
using OCR.Business.Pdf;
using OCR.Business.Split;
using OCR_WinApp.Services;
using OCR_WinApp.ViewModels;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Windows.Graphics.Imaging;

internal static partial class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Contains("--gemini-files-only", StringComparer.OrdinalIgnoreCase))
        {
            await GeminiFilesApiUploadsPollsReusesAndDeletes();
            await GeminiFilesApiKeepsUploadedUriWhenPollingFails();
            await AiModelClientUsesGeminiFileUriPart();
            Console.WriteLine("All Gemini Files API tests passed.");
            return;
        }

        if (args.Contains("--final-fix-only", StringComparer.OrdinalIgnoreCase))
        {
            await UploadWorkQueueSettlesEachItemExactlyOnce();
            await UploadWorkQueueReregisterIsNoop();
            await UploadWorkQueueConsumerFailureDoesNotLeakIntoFailures();
            await UploadWorkQueueSettleRemainingClosesPendingItemsWithoutRecordingFailures();
            GeminiUploadOptionsParsesValuesAndFallsBackToDefaults();
            await UploadPipelineRetriesThenSucceeds();
            await UploadPipelineMarksPermanentFailureAndKeepsItOutOfQueue();
            await UploadPipelineSkipsSourcesThatHitJsonCache();
            await UploadPipelinePassesEverythingThroughWhenFilesApiDisabled();
            await UploadPipelineClaimsEachSourceExactlyOnceAndKeepsArtifactOrder();
            await UploadPipelineClosesQueueWhenPrepareThrowsUnexpectedly();
            await UploadPipelineReleasesSemaphoreBeforeSettleEvenWhenSubscriberThrows();
            await UploadPipelineOnItemSettledCatchesEveryEventEvenWhenProducerSettlesImmediately();
            await UploadPipelineCompletionCompletesAfterProducerFinishesAllWork();
            await UploadPipelineSettleRemainingClosesItemsAbandonedByCancelledConsumer();
            await UploadPipelineInvokesOnItemUploadingExactlyOnceForSourcesThatActuallyUpload();
            await UploadPipelineDoesNotInvokeOnItemUploadingWhenFilesApiDisabled();
            await UploadPipelineDoesNotRecordCancelledSourcesAsFailures();
            NewGcnExcelExporterReturnsActualInsertedRowCount();
            NewGcnEnvelopeDeserializesNumericBarcode();
            NewGcnFlexibleStringConverterIsPublicForObfuscation();
            NewGcnExcelExporterWritesBarcodeColumnD();
            NewGcnExcelExporterWritesOwnerIssueDateAndPlace();
            NewGcnExcelExporterWritesFallbackRowWhenParcelRowsAreEmpty();
            NewGcnExcelExporterWritesDuplicateSerialRowsWithWarning();
            NewGcnExcelExporterDoesNotFlagMultiParcelGcnAsDuplicate();
            NewGcnExcelExporterNumbersSttSequentiallyPerGcn();
            NewGcnRenamePlanSuffixesDuplicateSerialAcrossFolders();
            NewGcnMultiParcelGcnIsNeverTreatedAsDuplicateSource();
            NewGcnExcelExporterDoesNotHardcodeEthnicity();
        NewGcnExcelExporterSeparatesNotesFromChangeHistory();
            NewGcnExcelExporterSeparatesNotesFromChangeHistory();
            RunVietBdGcnExporterTests();
            await RunBlankPageTests();
            AiScreensNotifyTelegramAfterRunCompletionAndNotOnExport();
            ErrorLogServiceWritesExceptionUnderLocalAppData();
            ScreensLogExceptionsToPerScreenFiles();
            AppHooksGlobalUnhandledExceptionsToErrorLog();
            NewGcnCacheUsesSelectedFolderWorkspaceAndRelativeJsonPaths();
            GcnNewPromptsForExistingCacheAtStart();
            TachGcnPromptsOnlyForCurrentFileCacheAtStart();
            GcnNewStoresEnvelopeBeforeQueuedUiAndRefreshesExportCommand();
            MainWindowSchedulesForceExitAfterGracefulShutdown();
            await NewGcnHydratesMissingSerialFromSourceFileName();
            await NewGcnHydratesMissingSerialFromFreshCache();
            await NewGcnIgnoresJsonCacheOlderThanSourceFile();
            await SplitServiceIgnoresJsonCacheOlderThanSourceFile();
            await SplitStandardUsesHyphenOutputNames();
            await SplitNewReportsFoldersWithoutAllThreeFiles();
            await SplitNoGcnCreatesOnlyGtAndGtk();
            await NewGcnUsesPreUploadedFileRefsWithoutCallingUpload();
            await RunMultiGcnAndCommuneTests();
            Console.WriteLine("All final-fix regression tests passed.");
            return;
        }

        if (args.Contains("--multi-gcn-only", StringComparer.OrdinalIgnoreCase))
        {
            await RunMultiGcnAndCommuneTests();
            Console.WriteLine("All multi-GCN detection + commune column tests passed.");
            return;
        }

        if (args.Contains("--ilis-ub-only", StringComparer.OrdinalIgnoreCase))
        {
            RunGcnIlisUbTests();
            Console.WriteLine("All OCR GCN iLis-UB tests passed.");
            return;
        }

        if (args.Contains("--blank-page-only", StringComparer.OrdinalIgnoreCase))
        {
            await RunBlankPageTests();
            Console.WriteLine("All blank-page removal tests passed.");
            return;
        }

        if (args.Contains("--serial-rename-only", StringComparer.OrdinalIgnoreCase))
        {
            await RunSerialRenameTests();
            Console.WriteLine("All serial-rename tests passed.");
            return;
        }

        if (args.Contains("--viewmodel-only", StringComparer.OrdinalIgnoreCase))
        {
            await RunViewModelTests();
            Console.WriteLine("All ViewModel full-flow tests passed.");
            return;
        }

        await UploadWorkQueueSettlesEachItemExactlyOnce();
        await UploadWorkQueueReregisterIsNoop();
        await UploadWorkQueueConsumerFailureDoesNotLeakIntoFailures();
        await UploadWorkQueueSettleRemainingClosesPendingItemsWithoutRecordingFailures();
        GeminiUploadOptionsParsesValuesAndFallsBackToDefaults();
        await UploadPipelineRetriesThenSucceeds();
        await UploadPipelineMarksPermanentFailureAndKeepsItOutOfQueue();
        await UploadPipelineSkipsSourcesThatHitJsonCache();
        await UploadPipelinePassesEverythingThroughWhenFilesApiDisabled();
        await UploadPipelineClaimsEachSourceExactlyOnceAndKeepsArtifactOrder();
        await UploadPipelineClosesQueueWhenPrepareThrowsUnexpectedly();
        await UploadPipelineReleasesSemaphoreBeforeSettleEvenWhenSubscriberThrows();
        await UploadPipelineOnItemSettledCatchesEveryEventEvenWhenProducerSettlesImmediately();
        await UploadPipelineCompletionCompletesAfterProducerFinishesAllWork();
        await UploadPipelineSettleRemainingClosesItemsAbandonedByCancelledConsumer();
        await UploadPipelineInvokesOnItemUploadingExactlyOnceForSourcesThatActuallyUpload();
        await UploadPipelineDoesNotInvokeOnItemUploadingWhenFilesApiDisabled();
        await UploadPipelineDoesNotRecordCancelledSourcesAsFailures();
        NewGcnExcelExporterReturnsActualInsertedRowCount();
        NewGcnEnvelopeDeserializesNumericBarcode();
        NewGcnFlexibleStringConverterIsPublicForObfuscation();
        NewGcnExcelExporterWritesBarcodeColumnD();
        NewGcnExcelExporterWritesOwnerIssueDateAndPlace();
        NewGcnExcelExporterWritesFallbackRowWhenParcelRowsAreEmpty();
        NewGcnExcelExporterWritesDuplicateSerialRowsWithWarning();
        NewGcnExcelExporterDoesNotFlagMultiParcelGcnAsDuplicate();
        NewGcnExcelExporterNumbersSttSequentiallyPerGcn();
        NewGcnRenamePlanSuffixesDuplicateSerialAcrossFolders();
        NewGcnMultiParcelGcnIsNeverTreatedAsDuplicateSource();
        NewGcnExcelExporterDoesNotHardcodeEthnicity();
        NewGcnExcelExporterSeparatesNotesFromChangeHistory();
        RunVietBdGcnExporterTests();
        RunGcnIlisUbTests();
        await RunBlankPageTests();
        await RunSerialRenameTests();
        AiScreensNotifyTelegramAfterRunCompletionAndNotOnExport();
        ErrorLogServiceWritesExceptionUnderLocalAppData();
        ScreensLogExceptionsToPerScreenFiles();
        AppHooksGlobalUnhandledExceptionsToErrorLog();
        NewGcnCacheUsesSelectedFolderWorkspaceAndRelativeJsonPaths();
        GcnNewPromptsForExistingCacheAtStart();
        TachGcnPromptsOnlyForCurrentFileCacheAtStart();
        GcnNewStoresEnvelopeBeforeQueuedUiAndRefreshesExportCommand();
        MainWindowSchedulesForceExitAfterGracefulShutdown();
        AiModelClientDisablesDefaultHttpClientTimeout();
        await ExportReportContainsUserDurationAndItems();
        await ExportReportSplitsLongMessages();
        await ExportReportSkipsEmptyPartAtBoundaryNewline();
        await ExportReportPreservesSurrogatePairAtChunkBoundary();
        await ExportReportWritesNotifierErrorLogForExceptions();
        await ExportReportWritesNotifierErrorLogForHttpFailure();
        ExportReportUsesObfuscationSafeJsonPayload();
        await ExportReportSwallowsHttpFailure();
        await ExportReportSkipsMissingConfiguration();
        if (args.Contains("--export-only", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("All export notifier tests passed.");
            return;
        }
        LabelAllocatorUsesHyphenForParcelAndCollisionSuffixes();
        await SplitStandardUsesHyphenOutputNames();
        FolderFileEnumeratorFindsPdfRecursively();
        SplitRunStateEnablesExportAfterMixedResultsComplete();
        SplitRunStateKeepsExportDisabledForAllErrorsOrCanceledRun();
        await SplitRunCacheSeparatesScreensAndSameNamedSources();
        NewGcnCacheStoresResponseUnderLocalRootAndSeparatesSameNamedSources();
        SplitPromptsContainOneValidJsonExample();
        await SplitServiceReusesValidJsonAndRefreshesInvalidJson();
        await SplitVariantsUseMaximumTokensMediumReasoningAndZeroTemperature();
        await NewGcnPdfAndImageUseMaximumTokensDefaultReasoningAndZeroTemperature();
        await NewGcnRetriesWhenDeclaredParcelCountExceedsRows();
        await NewGcnThrowsClearErrorWhenResponseIsMultiGcnArray();
        await NewGcnRetriesMismatchedCachedParcelCount();
        await NewGcnRefreshesCachedEnvelopeWithoutParcelCount();
        await NewGcnHydratesMissingSerialFromSourceFileName();
        await NewGcnHydratesMissingSerialFromFreshCache();
        await NewGcnIgnoresJsonCacheOlderThanSourceFile();
        await SplitServiceIgnoresJsonCacheOlderThanSourceFile();
        await SplitNewReportsFoldersWithoutAllThreeFiles();
        await SplitNoGcnCreatesOnlyGtAndGtk();
        await SplitNoGcnFallbackNormalizesLegacyUnderscoreName();
        AiJsonTextTrimsExtraClosingBraces();
        AiJsonTextQuotesInvalidLeadingZeroNumbers();
        AiJsonTextMergesDigitStringsSplitByStrayQuote();
        await SplitServiceRecoversFromExtraClosingBraceAndWritesCleanCache();
        await AllSplitVariantsSendResponseSchema();
        await AiModelClientBalancesExtraBraceAndRetriesInvalidJson();
        await AiModelClientRetriesWithoutSchemaWhenProviderRejectsIt();
        await AiModelClientConcatenatesGeminiTextPartsSkippingThought();
        await AiModelClientReportsFinishReasonWhenNoText();
        await NewGcnUsesPreUploadedFileRefsWithoutCallingUpload();
        await RunMultiGcnAndCommuneTests();
        await RunViewModelTests();
        Console.WriteLine("All OCR.Business tests passed.");
    }

    private static void AiScreensNotifyTelegramAfterRunCompletionAndNotOnExport()
    {
        AssertRunCompletionNotificationMovedFromExport(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "DatUyBanViewModel.cs"),
            "DatUyBan");
        AssertRunCompletionNotificationMovedFromExport(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "TachGcnViewModel.cs"),
            "TachGcn");
        AssertRunCompletionNotificationMovedFromExport(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "GcnNewViewModel.cs"),
            "GcnNew");
        AssertRunCompletionNotificationMovedFromExport(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "GcnVietBdViewModel.cs"),
            "GcnVietBd");
        AssertRunCompletionNotificationMovedFromExport(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "GcnIlisUbViewModel.cs"),
            "GcnIlisUb");
    }


    private static void ErrorLogServiceWritesExceptionUnderLocalAppData()
    {
        var sourcePath = FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR WinApp", "Services", "ErrorLogService.cs"));
        var source = File.ReadAllText(sourcePath);

        AssertTrue(source.Contains("Environment.SpecialFolder.LocalApplicationData", StringComparison.Ordinal),
            "ErrorLogService must use LocalAppData.");
        AssertTrue(source.Contains("\"OCR WinApp\"", StringComparison.Ordinal), "ErrorLogService must write under OCR WinApp.");
        AssertTrue(source.Contains("\"error-log\"", StringComparison.Ordinal), "ErrorLogService must write under error-log.");
        AssertTrue(source.Contains("\"error-\"", StringComparison.Ordinal), "ErrorLogService must use error-<screen>.log naming.");
        AssertTrue(source.Contains("File.AppendAllText", StringComparison.Ordinal)
                   || source.Contains("File.AppendAllTextAsync", StringComparison.Ordinal),
            "ErrorLogService must append exception records.");
        AssertTrue(source.Contains("exception.ToString()", StringComparison.Ordinal),
            "ErrorLogService must include exception type, message, and stack trace.");
    }

    private static void ScreensLogExceptionsToPerScreenFiles()
    {
        AssertScreenLogsExceptions(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "LoginViewModel.cs"),
            "login");
        AssertScreenLogsExceptions(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "DatUyBanViewModel.cs"),
            "dat-uy-ban");
        AssertScreenLogsExceptions(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "TachGcnViewModel.cs"),
            "_screenKey");
        AssertScreenLogsExceptions(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "GcnNewViewModel.cs"),
            "gcn-new");
        AssertScreenLogsExceptions(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "GcnVietBdViewModel.cs"),
            "gcn-vietbd");
        AssertScreenLogsExceptions(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "GcnIlisUbViewModel.cs"),
            "gcn-ilis-ub");
    }

    private static void AppHooksGlobalUnhandledExceptionsToErrorLog()
    {
        var servicePath = FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR WinApp", "Services", "AppExceptionHandlerService.cs"));
        var serviceSource = File.ReadAllText(servicePath);

        AssertTrue(serviceSource.Contains("Application.UnhandledException", StringComparison.Ordinal),
            "Global handler must hook WinUI Application.UnhandledException.");
        AssertTrue(serviceSource.Contains("AppDomain.CurrentDomain.UnhandledException", StringComparison.Ordinal),
            "Global handler must hook AppDomain.CurrentDomain.UnhandledException.");
        AssertTrue(serviceSource.Contains("TaskScheduler.UnobservedTaskException", StringComparison.Ordinal),
            "Global handler must hook TaskScheduler.UnobservedTaskException.");
        AssertTrue(serviceSource.Contains("LogException(\"app\"", StringComparison.Ordinal),
            "Global handler must write unhandled exceptions to error-app.log.");

        var appPath = FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR WinApp", "App.xaml.cs"));
        var appSource = File.ReadAllText(appPath);
        AssertTrue(appSource.Contains("IAppExceptionHandlerService", StringComparison.Ordinal),
            "App must register the shared exception handler service.");
        AssertTrue(appSource.Contains(".Initialize(this)", StringComparison.Ordinal),
            "App must initialize global exception hooks during startup.");
    }

    private static void NewGcnCacheUsesSelectedFolderWorkspaceAndRelativeJsonPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        var sourceFolder = Path.Combine(root, "Ho so GCN");
        var firstSub = Path.Combine(sourceFolder, "sub-a");
        var secondSub = Path.Combine(sourceFolder, "sub-b");
        Directory.CreateDirectory(firstSub);
        Directory.CreateDirectory(secondSub);

        try
        {
            var firstPdf = Path.Combine(firstSub, "AA 000001.pdf");
            var secondPdf = Path.Combine(secondSub, "AA 000002.pdf");
            File.WriteAllBytes(firstPdf, [1, 2, 3]);
            File.WriteAllBytes(secondPdf, [4, 5, 6]);

            var cacheRoot = Path.Combine(root, "newgcn-temp");
            var service = new NewGcnRunCacheService(cacheRoot);
            var workspace = service.GetWorkspace("gcn-new", sourceFolder);
            var sameWorkspace = service.GetWorkspace("gcn-new", sourceFolder + Path.DirectorySeparatorChar);
            var firstCache = service.GetJsonPath(workspace, firstPdf);
            var secondCache = service.GetJsonPath(workspace, secondPdf);

            AssertEqual(workspace.CacheDir, sameWorkspace.CacheDir, "Same selected OCR GCN New folder must reuse one workspace.");
            AssertTrue(firstCache.StartsWith(workspace.JsonDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "OCR GCN New JSON must stay under the selected-folder workspace.");
            AssertTrue(secondCache.StartsWith(workspace.JsonDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "All child folders must share the selected-folder workspace.");
            AssertEqual(
                Path.Combine(workspace.CacheDir, "sub-a", "AA 000001.json"),
                firstCache,
                "OCR GCN New cache must preserve relative path under selected folder.");
            AssertEqual(
                Path.Combine(workspace.CacheDir, "sub-b", "AA 000002.json"),
                secondCache,
                "OCR GCN New cache must preserve relative path under selected folder.");
            AssertEqual(workspace.CacheDir, workspace.JsonDir, "OCR GCN New JSON cache must not use an extra json folder.");

            AssertFalse(service.HasJsonCache(workspace), "New OCR GCN New workspace starts without JSON cache.");
            Directory.CreateDirectory(Path.GetDirectoryName(firstCache)!);
            File.WriteAllText(firstCache, "{}");
            AssertTrue(service.HasJsonCache(workspace), "Existing OCR GCN New JSON cache must be detected before Start.");

            var sourceTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var cacheTime = sourceTime.AddMinutes(1);
            File.SetLastWriteTimeUtc(firstPdf, sourceTime);
            File.SetLastWriteTimeUtc(firstCache, cacheTime);
            AssertTrue(service.HasJsonCache(workspace, [firstPdf, secondPdf]),
                "Current OCR GCN New file with matching JSON should trigger cache prompt.");
            AssertFalse(service.HasJsonCache(workspace, [secondPdf]),
                "Old OCR GCN New JSON for files no longer selected must not trigger cache prompt.");
            File.SetLastWriteTimeUtc(firstPdf, cacheTime.AddMinutes(1));
            AssertTrue(service.HasJsonCache(workspace, [firstPdf]),
                "OCR GCN New matching JSON should trigger cache prompt even when service later decides whether it is stale.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void GcnNewPromptsForExistingCacheAtStart()
    {
        var sourcePath = FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "GcnNewViewModel.cs"));
        var source = File.ReadAllText(sourcePath);
        var startStart = source.IndexOf("private async Task StartAsync()", StringComparison.Ordinal);
        var exportStart = source.IndexOf("private async Task ExportAsync()", StringComparison.Ordinal);
        AssertTrue(startStart >= 0 && exportStart > startStart, "Expected GcnNew StartAsync before ExportAsync.");
        var startBody = source[startStart..exportStart];

        AssertTrue(source.Contains("ISplitCachePromptService", StringComparison.Ordinal),
            "OCR GCN New ViewModel must use the existing cache prompt dialog service.");
        AssertTrue(source.Contains("NewGcnRunCacheService", StringComparison.Ordinal),
            "OCR GCN New ViewModel must own a selected-folder cache workspace service.");
        AssertTrue(startBody.Contains("HasJsonCache", StringComparison.Ordinal),
            "OCR GCN New StartAsync must detect existing JSON cache.");
        AssertTrue(startBody.Contains("HasJsonCache(_workspace, SelectedFiles.Select", StringComparison.Ordinal),
            "OCR GCN New StartAsync must only prompt when JSON cache matches current selected files.");
        AssertTrue(startBody.Contains("else if (_cache.HasJsonCache(_workspace))", StringComparison.Ordinal),
            "OCR GCN New StartAsync must auto-clear old unmatched workspace cache.");
        AssertTrue(startBody.Contains("_cachePrompt.AskAsync()", StringComparison.Ordinal),
            "OCR GCN New StartAsync must ask whether to reuse cached JSON.");
        AssertTrue(startBody.Contains("ResetWorkspaceAsync", StringComparison.Ordinal),
            "OCR GCN New StartAsync must support rescan by clearing old workspace.");
        AssertTrue(startBody.Contains("GetJsonPath", StringComparison.Ordinal),
            "OCR GCN New StartAsync must pass selected-folder JSON path per file.");
        AssertTrue(startBody.Contains("useCachedJson", StringComparison.Ordinal),
            "OCR GCN New StartAsync must pass the user's cache choice into extraction.");
    }

    private static void TachGcnPromptsOnlyForCurrentFileCacheAtStart()
    {
        var sourcePath = FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "TachGcnViewModel.cs"));
        var source = File.ReadAllText(sourcePath);
        var startStart = source.IndexOf("private async Task StartAsync()", StringComparison.Ordinal);
        var exportStart = source.IndexOf("private async Task ExportAsync()", StringComparison.Ordinal);
        AssertTrue(startStart >= 0 && exportStart > startStart, "Expected TachGcn StartAsync before ExportAsync.");
        var startBody = source[startStart..exportStart];

        AssertTrue(startBody.Contains("HasJsonCache(_workspace, SelectedFiles.Select", StringComparison.Ordinal),
            "Split screens must only prompt when JSON cache matches current selected files.");
        AssertTrue(startBody.Contains("else if (_cache.HasJsonCache(_workspace))", StringComparison.Ordinal),
            "Split screens must auto-clear old unmatched workspace cache.");
        AssertTrue(startBody.Contains("_cachePrompt.AskAsync()", StringComparison.Ordinal),
            "Split screens must still ask before using matching JSON cache.");
    }

    private static void AssertScreenLogsExceptions(string relativePath, string screenKeyExpression)
    {
        var sourcePath = FindRepositoryFile(relativePath);
        var source = File.ReadAllText(sourcePath);

        AssertTrue(source.Contains("IErrorLogService", StringComparison.Ordinal),
            $"{Path.GetFileName(relativePath)} must receive IErrorLogService through DI.");
        AssertTrue(source.Contains("_errorLog", StringComparison.Ordinal),
            $"{Path.GetFileName(relativePath)} must keep an error log service field.");
        AssertTrue(source.Contains("LogException(", StringComparison.Ordinal),
            $"{Path.GetFileName(relativePath)} must log caught exceptions.");
        AssertTrue(source.Contains(screenKeyExpression, StringComparison.Ordinal),
            $"{Path.GetFileName(relativePath)} must log with screen key {screenKeyExpression}.");
    }

    private static void AssertRunCompletionNotificationMovedFromExport(string relativePath, string name)
    {
        var sourcePath = FindRepositoryFile(relativePath);
        var source = File.ReadAllText(sourcePath);
        var startStart = source.IndexOf("private async Task StartAsync()", StringComparison.Ordinal);
        var exportStart = source.IndexOf("private async Task ExportAsync()", StringComparison.Ordinal);
        AssertTrue(startStart >= 0 && exportStart > startStart, $"Expected {name} StartAsync before ExportAsync.");

        var startBody = source[startStart..exportStart];
        var completion = startBody.LastIndexOf("_lastRunCompletedAt = DateTime.Now;", StringComparison.Ordinal);
        var notifyInStart = startBody.IndexOf("_notifier.NotifyExport(", StringComparison.Ordinal);
        AssertTrue(completion >= 0, $"Expected {name} to capture run completion time.");
        AssertTrue(notifyInStart > completion,
            $"{name} must notify Telegram after StartAsync run completion, not during Export.");

        var exportBody = source[exportStart..];
        AssertFalse(exportBody.Contains("_notifier.NotifyExport(", StringComparison.Ordinal),
            $"{name} ExportAsync must not send Telegram to avoid duplicate reports.");
    }

    private static void GcnNewStoresEnvelopeBeforeQueuedUiAndRefreshesExportCommand()
    {
        var sourcePath = FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR WinApp", "ViewModels", "GcnNewViewModel.cs"));
        var source = File.ReadAllText(sourcePath);

        var processCall = source.IndexOf("var envelope = await _extract.ProcessFileAsync", StringComparison.Ordinal);
        var firstQueuedUiAfterProcess = source.IndexOf("Ui(() =>", processCall, StringComparison.Ordinal);
        var envelopeStore = source.IndexOf("_envelopes.Add(envelope);", processCall, StringComparison.Ordinal);

        AssertTrue(processCall >= 0 && firstQueuedUiAfterProcess > processCall,
            "Expected GcnNew success branch with queued UI update.");
        AssertTrue(envelopeStore > processCall && envelopeStore < firstQueuedUiAfterProcess,
            "OCR GCN New must store successful envelopes before queued UI updates so Export can enable after Task.WhenAll.");

        var completionStart = source.IndexOf("        _lastRunCompletedAt = DateTime.Now;", StringComparison.Ordinal);
        var isRunningFalse = source.IndexOf("IsRunning = false;", completionStart, StringComparison.Ordinal);
        var exportNotify = source.IndexOf("ExportCommand.NotifyCanExecuteChanged();", isRunningFalse, StringComparison.Ordinal);
        var disposeCts = source.IndexOf("_cts?.Dispose();", completionStart, StringComparison.Ordinal);

        AssertTrue(completionStart >= 0 && isRunningFalse > completionStart && disposeCts > isRunningFalse,
            "Expected GcnNew run completion cleanup block.");
        AssertTrue(exportNotify > isRunningFalse && exportNotify < disposeCts,
            "OCR GCN New must refresh ExportCommand after the run is no longer running.");
    }

    private static void MainWindowSchedulesForceExitAfterGracefulShutdown()
    {
        var sourcePath = FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR WinApp", "MainWindow.xaml.cs"));
        var source = File.ReadAllText(sourcePath);

        var closedStart = source.IndexOf("private void OnClosed", StringComparison.Ordinal);
        var disposeBackdrop = source.IndexOf("_backdrop.Dispose();", closedStart, StringComparison.Ordinal);
        var gracefulExit = source.IndexOf("Application.Current.Exit();", closedStart, StringComparison.Ordinal);
        var forceExitCall = source.IndexOf("ScheduleForceExitFallback();", closedStart, StringComparison.Ordinal);

        AssertTrue(closedStart >= 0 && disposeBackdrop > closedStart && gracefulExit > disposeBackdrop,
            "Expected MainWindow.OnClosed to cleanup backdrop before graceful app exit.");
        AssertTrue(forceExitCall > gracefulExit,
            "MainWindow must schedule force-exit fallback after graceful Application.Current.Exit().");

        var fallbackStart = source.IndexOf("private static void ScheduleForceExitFallback()", StringComparison.Ordinal);
        var delay = source.IndexOf("Task.Delay(TimeSpan.FromMilliseconds", fallbackStart, StringComparison.Ordinal);
        var exit = source.IndexOf("Environment.Exit(0);", fallbackStart, StringComparison.Ordinal);

        AssertTrue(fallbackStart >= 0 && delay > fallbackStart && exit > delay,
            "Force-exit fallback must wait briefly before calling Environment.Exit(0).");
    }

    private static void AiModelClientDisablesDefaultHttpClientTimeout()
    {
        var client = new AiModelClient(new AiProviderOptions
        {
            Provider = "google-ai-studio",
            Url = "https://example.invalid",
            ApiKey = "test-key",
            Model = "test-model"
        });

        var field = typeof(AiModelClient).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Expected AiModelClient._http field.");
        var http = (HttpClient)(field.GetValue(client)
            ?? throw new InvalidOperationException("Expected AiModelClient HttpClient instance."));

        AssertEqual(Timeout.InfiniteTimeSpan, http.Timeout,
            "AI client must not use HttpClient default 100-second timeout");
    }

    private static string FindRepositoryFile(string relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(Path.GetFullPath(start));
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
                current = current.Parent;
            }
        }

        throw new FileNotFoundException($"Không tìm thấy source test: {relativePath}");
    }

    private static void NewGcnExcelExporterReturnsActualInsertedRowCount()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var templatePath = Path.Combine(root, "template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            using (var template = new XLWorkbook())
            {
                template.AddWorksheet("Data");
                template.SaveAs(templatePath);
            }

            var duplicate = CreateNewGcnEnvelope(
                "duplicate.pdf",
                "ca_nhan",
                [new NewGcnOwner { ho_ten = "Nguyễn Văn A" }],
                "10");
            var coOwned = CreateNewGcnEnvelope(
                "co-owned.pdf",
                "dong_su_dung",
                [
                    new NewGcnOwner { ho_ten = "Trần Văn B" },
                    new NewGcnOwner { ho_ten = "Lê Thị C" }
                ],
                "20");

            var exporter = new NewGcnExcelExporter();
            int rowCount = exporter.Write([duplicate, duplicate, coOwned], outputPath, templatePath);

            AssertEqual(4, rowCount, "Actual Excel row count after duplicate serial warning and co-owner expansion");
            using var output = new XLWorkbook(outputPath);
            var data = output.Worksheet("Data");
            var writtenRows = data.RowsUsed()
                .Count(row => row.RowNumber() >= 5 && !row.Cell("GA").IsEmpty());
            AssertEqual(4, writtenRows, "Rows physically written to Excel");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void NewGcnEnvelopeDeserializesNumericBarcode()
    {
        const string json = """
        {
          "ten_file": "barcode.pdf",
          "thong_tin_gcn": {
            "so_serial": "BD 123456",
            "loai_quan_he": "ca_nhan",
            "ma_vach": 1234567890123,
            "chu_su_dung_chi_tiet": []
          },
          "danh_sach_dong": []
        }
        """;

        var envelope = JsonSerializer.Deserialize<NewGcnEnvelope>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        AssertEqual("1234567890123", envelope?.thong_tin_gcn.ma_vach, "New GCN barcode from numeric JSON");
    }

    private static void NewGcnFlexibleStringConverterIsPublicForObfuscation()
    {
        AssertTrue(typeof(FlexibleStringJsonConverter).IsPublic,
            "FlexibleStringJsonConverter must stay public so Obfuscar KeepPublicApi preserves JsonConverterAttribute type metadata.");
    }

    private static void NewGcnExcelExporterWritesBarcodeColumnD()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var templatePath = Path.Combine(root, "template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            using (var template = new XLWorkbook())
            {
                template.AddWorksheet("Data");
                template.SaveAs(templatePath);
            }

            var envelope = CreateNewGcnEnvelope(
                "barcode.pdf",
                "ca_nhan",
                [new NewGcnOwner { ho_ten = "Nguyen Van A" }],
                "40",
                "1234567890123");

            var exporter = new NewGcnExcelExporter();
            exporter.Write([envelope], outputPath, templatePath);

            using var output = new XLWorkbook(outputPath);
            var data = output.Worksheet("Data");
            AssertEqual("1234567890123", data.Cell(5, "D").Value.ToString(), "Barcode column D");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void NewGcnExcelExporterWritesOwnerIssueDateAndPlace()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var templatePath = Path.Combine(root, "template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            using (var template = new XLWorkbook())
            {
                template.AddWorksheet("Data");
                template.SaveAs(templatePath);
            }

            var envelope = CreateNewGcnEnvelope(
                "spouse.pdf",
                "vo_chong",
                [
                    new NewGcnOwner
                    {
                        ho_ten = "Nguyen Van A",
                        so_giay_to = "001",
                        ngay_cap = "01/02/2020",
                        noi_cap = "CA Ha Noi"
                    },
                    new NewGcnOwner
                    {
                        ho_ten = "Tran Thi B",
                        so_giay_to = "002",
                        ngay_cap = "03/04/2021",
                        noi_cap = "CA TP HCM"
                    }
                ],
                "30");

            var exporter = new NewGcnExcelExporter();
            exporter.Write([envelope], outputPath, templatePath);

            using var output = new XLWorkbook(outputPath);
            var data = output.Worksheet("Data");
            AssertEqual("01/02/2020", data.Cell(5, "O").Value.ToString(), "First owner issue date column O");
            AssertEqual("CA Ha Noi", data.Cell(5, "P").Value.ToString(), "First owner issue place column P");
            AssertEqual("03/04/2021", data.Cell(5, "AF").Value.ToString(), "Spouse issue date column AF");
            AssertEqual("CA TP HCM", data.Cell(5, "AG").Value.ToString(), "Spouse issue place column AG");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void NewGcnExcelExporterWritesFallbackRowWhenParcelRowsAreEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var templatePath = Path.Combine(root, "template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            using (var template = new XLWorkbook())
            {
                template.AddWorksheet("Data");
                template.SaveAs(templatePath);
            }

            var envelope = new NewGcnEnvelope
            {
                ten_file = "33-104_DH 444331.pdf",
                thong_tin_gcn = new NewGcnInfo
                {
                    so_serial = "DH 444331",
                    loai_quan_he = "ca_nhan",
                    ma_vach = "0739023057278",
                    so_luong_thua_dat_doc_duoc = 0,
                    chu_su_dung_chi_tiet =
                    [
                        new NewGcnOwner
                        {
                            ho_ten = "Le Thi Quyen",
                            nam_sinh = "1966",
                            loai_giay_to = "CCCD",
                            so_giay_to = "022166006889",
                            to_dan_pho = "Thon Dong Lam",
                            xa_phuong = "xa Binh Duong",
                            xa_huyen_tinh = "thi xa Dong Trieu, tinh Quang Ninh",
                            dia_chi_day_du = "Thon Dong Lam, xa Binh Duong, thi xa Dong Trieu, tinh Quang Ninh"
                        }
                    ],
                    do_tin_cay = "trung_binh",
                    canh_bao =
                    [
                        "danh_sach_dong (trang 1): Khong tim thay thong tin thua dat trong tai lieu cung cap - doc duoc: 0"
                    ]
                },
                danh_sach_dong = []
            };

            var exporter = new NewGcnExcelExporter();
            int rowCount = exporter.Write([envelope], outputPath, templatePath);

            AssertEqual(1, rowCount, "Fallback Excel row count for empty parcel rows");
            using var output = new XLWorkbook(outputPath);
            var data = output.Worksheet("Data");
            AssertEqual("DH 444331", data.Cell(5, "B").Value.ToString(), "Fallback serial column B");
            AssertEqual("DH444331", data.Cell(5, "CQ").Value.ToString(), "Fallback serial no-space column CQ");
            AssertEqual("0739023057278", data.Cell(5, "D").Value.ToString(), "Fallback barcode column D");
            AssertEqual("057278", data.Cell(5, "C").Value.ToString(), "Fallback last-6 barcode column C");
            AssertEqual("Le Thi Quyen", data.Cell(5, "I").Value.ToString(), "Fallback owner column I");
            AssertEqual("", data.Cell(5, "AQ").Value.ToString(), "Fallback parcel number stays empty");
            AssertEqual("", data.Cell(5, "AZ").Value.ToString(), "Fallback map area column AZ stays empty");
            // GA lấy trực tiếp từ so_serial (đổi ở 7308bd0), không còn là tên file nguồn.
            AssertEqual("DH 444331", data.Cell(5, "GA").Value.ToString(), "Fallback serial column GA");
            AssertTrue(data.Cell(5, "GL").Value.ToString().Contains("Khong tim thay thong tin thua dat", StringComparison.Ordinal),
                "Fallback warning column GL");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void NewGcnExcelExporterWritesDuplicateSerialRowsWithWarning()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var templatePath = Path.Combine(root, "template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            using (var template = new XLWorkbook())
            {
                template.AddWorksheet("Data");
                template.SaveAs(templatePath);
            }

            // Nghiệp vụ: chỉ báo trùng khi khớp CẢ BA số serial + số tờ + số thửa.
            var first = CreateNewGcnEnvelope(
                "first.pdf",
                "ca_nhan",
                [new NewGcnOwner { ho_ten = "Nguyen Van A" }],
                "01");
            first.thong_tin_gcn.so_serial = "DUP 001";

            // Cùng serial, KHÁC số thửa, nhưng là FILE KHÁC -> vẫn là trùng (ca trước đây lọt lưới:
            // chỉ so nội dung sheet thì nó trông y hệt một giấy nhiều thửa).
            var sameSerialOtherParcel = CreateNewGcnEnvelope(
                "other-parcel.pdf",
                "ca_nhan",
                [new NewGcnOwner { ho_ten = "Nguyen Van B" }],
                "99");
            sameSerialOtherParcel.thong_tin_gcn.so_serial = "DUP 001";

            // Khớp cả serial + tờ + thửa -> đúng là trùng.
            var realDuplicate = CreateNewGcnEnvelope(
                "real-duplicate.pdf",
                "ca_nhan",
                [new NewGcnOwner { ho_ten = "Nguyen Van C" }],
                "01");
            realDuplicate.thong_tin_gcn.so_serial = "DUP 001";

            // Cùng tờ + thửa nhưng KHÁC serial -> không trùng.
            var sameParcelOtherSerial = CreateNewGcnEnvelope(
                "same-key.pdf",
                "ca_nhan",
                [new NewGcnOwner { ho_ten = "Nguyen Van A" }],
                "01");
            sameParcelOtherSerial.thong_tin_gcn.so_serial = "DUP 002";

            var exporter = new NewGcnExcelExporter();
            int rowCount = exporter.Write(
                [first, sameSerialOtherParcel, realDuplicate, sameParcelOtherSerial], outputPath, templatePath);

            AssertEqual(4, rowCount, "Duplicate serial Excel row count");
            using var output = new XLWorkbook(outputPath);
            var data = output.Worksheet("Data");
            AssertEqual("DUP 001", data.Cell(5, "B").Value.ToString(), "First serial column B");
            AssertEqual("DUP 001", data.Cell(6, "B").Value.ToString(), "Same serial other parcel column B");
            AssertEqual("DUP 001", data.Cell(7, "B").Value.ToString(), "Real duplicate serial column B");
            AssertEqual("DUP 002", data.Cell(8, "B").Value.ToString(), "Same parcel other serial column B");

            // Dòng 5 là file NGUỒN ĐẦU TIÊN mang serial DUP 001 -> nó là "chủ", không cảnh báo.
            AssertEqual("", data.Cell(5, "GL").Value.ToString(), "File nguồn đầu tiên của một serial không bị cảnh báo");
            AssertFalse(data.Row(5).Style.Fill.BackgroundColor.Equals(XLColor.Red),
                "File nguồn đầu tiên của một serial không bị tô đỏ.");

            // Dòng 6: FILE KHÁC nhưng cùng serial, khác số thửa. Phép kiểm cũ (serial+tờ+thửa) không
            // thấy gì; phép kiểm theo FILE NGUỒN phải bắt được.
            var otherFileWarning = data.Cell(6, "GL").Value.ToString();
            AssertTrue(otherFileWarning.Contains("HAI FILE NGUỒN KHÁC NHAU", StringComparison.Ordinal),
                "Hai file nguồn khác nhau cùng serial phải được cảnh báo dù khác số thửa.");
            AssertTrue(otherFileWarning.Contains("other-parcel.pdf", StringComparison.Ordinal),
                "Cảnh báo phải nêu tên file nguồn của chính dòng này.");
            AssertTrue(otherFileWarning.Contains("first.pdf", StringComparison.Ordinal),
                "Cảnh báo phải nêu tên file nguồn đã chiếm serial trước đó.");
            AssertTrue(otherFileWarning.Contains("dòng 5", StringComparison.Ordinal),
                "Cảnh báo phải chỉ về đúng dòng của file nguồn đầu tiên.");
            AssertFalse(otherFileWarning.Contains("thửa 99", StringComparison.Ordinal),
                "Cảnh báo theo file nguồn KHÔNG nhắc số thửa — nó không dựa vào tờ/thửa.");
            AssertTrue(data.Row(6).Style.Fill.BackgroundColor.Equals(XLColor.Red),
                "Dòng trùng serial theo file nguồn phải được tô đỏ.");

            // Dòng 7: khớp CẢ BA serial+tờ+thửa VÀ cũng là file khác -> nhận CẢ HAI cảnh báo.
            var duplicateWarning = data.Cell(7, "GL").Value.ToString();
            AssertTrue(duplicateWarning.Contains("serial DUP 001", StringComparison.Ordinal),
                "Duplicate warning must include duplicate serial.");
            AssertTrue(duplicateWarning.Contains("thửa 01", StringComparison.Ordinal),
                "Duplicate warning must name the duplicated parcel.");
            AssertTrue(duplicateWarning.Contains("dòng 5", StringComparison.Ordinal),
                "Duplicate warning must include existing row number.");
            AssertFalse(duplicateWarning.Contains("dòng 5, 6", StringComparison.Ordinal),
                "Duplicate warning must not point at the other parcel row.");
            AssertTrue(duplicateWarning.Contains("HAI FILE NGUỒN KHÁC NHAU", StringComparison.Ordinal),
                "Dòng trùng cả tờ/thửa lẫn file nguồn phải mang CẢ HAI cảnh báo, không nuốt mất cái nào.");
            AssertTrue(data.Row(7).Style.Fill.BackgroundColor.Equals(XLColor.Red),
                "Row matching serial + sheet + parcel must be red.");

            // Dòng 8: serial khác hẳn -> không cảnh báo gì, dù trùng tờ/thửa với dòng 5.
            AssertEqual("", data.Cell(8, "GL").Value.ToString(), "Serial khác thì không cảnh báo trùng");
            AssertFalse(data.Row(8).Style.Fill.BackgroundColor.Equals(XLColor.Red),
                "Same parcel with different serial must not be red.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 1 GCN có nhiều thửa là nghiệp vụ bình thường (mỗi thửa 1 dòng, chung số serial).
    /// Không dòng nào được tô đỏ / gắn cảnh báo trùng.
    /// </summary>
    private static void NewGcnExcelExporterDoesNotFlagMultiParcelGcnAsDuplicate()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var templatePath = Path.Combine(root, "template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            using (var template = new XLWorkbook())
            {
                template.AddWorksheet("Data");
                template.SaveAs(templatePath);
            }

            var envelope = CreateNewGcnEnvelope(
                "multi-parcel.pdf",
                "ca_nhan",
                [new NewGcnOwner { ho_ten = "Nguyen Van A" }],
                "01");
            envelope.thong_tin_gcn.so_serial = "MULTI 001";
            envelope.danh_sach_dong =
            [
                new NewGcnRow { td_so_thua = "01", td_so_to = "1", td_tong_dien_tich = "100" },
                new NewGcnRow { td_so_thua = "02", td_so_to = "1", td_tong_dien_tich = "200" },
                new NewGcnRow { td_so_thua = "03", td_so_to = "2", td_tong_dien_tich = "300" }
            ];

            var exporter = new NewGcnExcelExporter();
            int rowCount = exporter.Write([envelope], outputPath, templatePath);

            AssertEqual(3, rowCount, "Multi-parcel GCN row count");
            using var output = new XLWorkbook(outputPath);
            var data = output.Worksheet("Data");

            for (int row = 5; row <= 7; row++)
            {
                AssertEqual("MULTI 001", data.Cell(row, "B").Value.ToString(), $"Multi-parcel serial row {row}");
                AssertFalse(data.Row(row).Style.Fill.BackgroundColor.Equals(XLColor.Red),
                    $"Parcel row {row} of one GCN must not be flagged as duplicate.");
                AssertFalse(data.Cell(row, "GL").Value.ToString().Contains("Trùng", StringComparison.Ordinal),
                    $"Parcel row {row} of one GCN must not carry a duplicate warning.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Hai FILE NGUỒN khác nhau đọc ra cùng số serial thì file đầu (theo đường dẫn A→Z) giữ tên trơn
    /// <c>{serial}-GCN.pdf</c>, các file sau lần lượt <c>_1</c>, <c>_2</c>… Chống trùng tính trên TOÀN LÔ,
    /// kể cả hai file nằm ở hai thư mục khác nhau.
    /// </summary>
    private static void NewGcnRenamePlanSuffixesDuplicateSerialAcrossFolders()
    {
        static GcnNewViewModel.SuccessfulGcnSource Source(string path, string serial)
        {
            var envelope = CreateNewGcnEnvelope(Path.GetFileName(path), "ca_nhan", [], "01");
            envelope.thong_tin_gcn.so_serial = serial;
            return new GcnNewViewModel.SuccessfulGcnSource(path, envelope);
        }

        // Cố ý truyền vào theo thứ tự LỘN XỘN (mô phỏng thứ tự OCR xong của các worker song song).
        var plan = GcnNewViewModel.BuildRenamePlan(
        [
            Source(@"E:\HoSo\B\quet-lai.pdf", "AA 123456"),
            Source(@"E:\HoSo\C\ban-sao.pdf", "AA 123456"),
            Source(@"E:\HoSo\A\goc.pdf", "AA 123456"),
            Source(@"E:\HoSo\A\khac.pdf", "BB 999999"),
            Source(@"E:\HoSo\A\thieu-serial.pdf", "")
        ]);

        AssertEqual("AA 123456-GCN.pdf", plan[@"E:\HoSo\A\goc.pdf"],
            "File có đường dẫn nhỏ nhất giữ tên trơn, không phụ thuộc thứ tự OCR xong");
        AssertEqual("AA 123456_1-GCN.pdf", plan[@"E:\HoSo\B\quet-lai.pdf"], "File trùng thứ hai nhận hậu tố _1");
        AssertEqual("AA 123456_2-GCN.pdf", plan[@"E:\HoSo\C\ban-sao.pdf"], "File trùng thứ ba nhận hậu tố _2");
        AssertEqual("BB 999999-GCN.pdf", plan[@"E:\HoSo\A\khac.pdf"], "Serial không trùng thì không có hậu tố");
        AssertFalse(plan.ContainsKey(@"E:\HoSo\A\thieu-serial.pdf"),
            "Thiếu serial thì không nằm trong kế hoạch đổi tên (nhánh gọi ghi log rồi bỏ qua).");

        // Chạy lại đúng lô đó với thứ tự đầu vào khác phải cho kết quả Y HỆT.
        var again = GcnNewViewModel.BuildRenamePlan(
        [
            Source(@"E:\HoSo\A\goc.pdf", "AA 123456"),
            Source(@"E:\HoSo\C\ban-sao.pdf", "AA 123456"),
            Source(@"E:\HoSo\B\quet-lai.pdf", "AA 123456")
        ]);
        AssertEqual(plan[@"E:\HoSo\A\goc.pdf"], again[@"E:\HoSo\A\goc.pdf"], "Đổi tên phải tất định (file 1)");
        AssertEqual(plan[@"E:\HoSo\B\quet-lai.pdf"], again[@"E:\HoSo\B\quet-lai.pdf"], "Đổi tên phải tất định (file 2)");
        AssertEqual(plan[@"E:\HoSo\C\ban-sao.pdf"], again[@"E:\HoSo\C\ban-sao.pdf"], "Đổi tên phải tất định (file 3)");
    }

    /// <summary>
    /// Ranh giới quan trọng nhất: MỘT giấy chứng nhận có nhiều thửa chỉ là MỘT file nguồn, nên
    /// KHÔNG được đánh hậu tố và KHÔNG được cảnh báo trùng — dù nó sinh ra nhiều dòng Excel.
    /// </summary>
    private static void NewGcnMultiParcelGcnIsNeverTreatedAsDuplicateSource()
    {
        var envelope = CreateNewGcnEnvelope("mot-giay-nhieu-thua.pdf", "ca_nhan",
            [new NewGcnOwner { ho_ten = "Nguyen Van A" }], "01");
        envelope.thong_tin_gcn.so_serial = "CC 555555";
        envelope.danh_sach_dong =
        [
            new NewGcnRow { td_so_thua = "01", td_so_to = "1", td_tong_dien_tich = "100" },
            new NewGcnRow { td_so_thua = "02", td_so_to = "1", td_tong_dien_tich = "200" },
            new NewGcnRow { td_so_thua = "03", td_so_to = "2", td_tong_dien_tich = "300" }
        ];

        var plan = GcnNewViewModel.BuildRenamePlan(
            [new GcnNewViewModel.SuccessfulGcnSource(@"E:\HoSo\A\mot-giay-nhieu-thua.pdf", envelope)]);
        AssertEqual("CC 555555-GCN.pdf", plan[@"E:\HoSo\A\mot-giay-nhieu-thua.pdf"],
            "1 giấy nhiều thửa vẫn là MỘT file nguồn nên giữ tên trơn, không có hậu tố _1.");

        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = Path.Combine(root, "template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            using (var template = new XLWorkbook())
            {
                template.AddWorksheet("Data");
                template.SaveAs(templatePath);
            }

            new NewGcnExcelExporter().Write([envelope], outputPath, templatePath);

            using var output = new XLWorkbook(outputPath);
            var data = output.Worksheet("Data");
            for (int row = 5; row <= 7; row++)
            {
                AssertEqual("", data.Cell(row, "GL").Value.ToString(),
                    $"Thửa ở dòng {row} của cùng một giấy không được mang cảnh báo trùng file nguồn");
                AssertFalse(data.Row(row).Style.Fill.BackgroundColor.Equals(XLColor.Red),
                    $"Thửa ở dòng {row} của cùng một giấy không được tô đỏ");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// STT (cột A) đánh 1, 2, 3… theo đúng thứ tự giấy được ghi vào Excel: MỖI GIẤY một số, mọi dòng
    /// thửa / đồng sở hữu của cùng giấy dùng chung ô STT đã merge.
    /// Trước đây STT tính bằng "số serial phân biệt đã ghi + 1" nên sinh 2 lỗi: giấy thiếu serial không
    /// làm tăng bộ đếm (giấy sau lấy lại đúng số cũ → STT trùng), và 2 file khác nhau trùng serial thì
    /// file sau tái dùng STT của file trước (→ STT lùi số).
    /// </summary>
    private static void NewGcnExcelExporterNumbersSttSequentiallyPerGcn()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var templatePath = Path.Combine(root, "template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            using (var template = new XLWorkbook())
            {
                template.AddWorksheet("Data");
                template.SaveAs(templatePath);
            }

            // Giấy 1: THIẾU serial (model không đọc được, phục hồi từ tên file cũng thất bại).
            var missingSerial = CreateNewGcnEnvelope(
                "missing-serial.pdf", "ca_nhan", [new NewGcnOwner { ho_ten = "Nguyen Van A" }], "01");
            missingSerial.thong_tin_gcn.so_serial = "";

            // Giấy 2: 1 giấy 2 thửa -> 2 dòng nhưng dùng chung 1 STT.
            var twoParcels = CreateNewGcnEnvelope(
                "two-parcels.pdf", "ca_nhan", [new NewGcnOwner { ho_ten = "Nguyen Van B" }], "01");
            twoParcels.thong_tin_gcn.so_serial = "AA 111111";
            twoParcels.danh_sach_dong =
            [
                new NewGcnRow { td_so_thua = "01", td_so_to = "1", td_tong_dien_tich = "100" },
                new NewGcnRow { td_so_thua = "02", td_so_to = "1", td_tong_dien_tich = "200" }
            ];

            // Giấy 3: FILE KHÁC nhưng đọc ra CÙNG serial với giấy 2 -> vẫn phải là STT riêng.
            var duplicateSerial = CreateNewGcnEnvelope(
                "duplicate-serial.pdf", "ca_nhan", [new NewGcnOwner { ho_ten = "Nguyen Van C" }], "77");
            duplicateSerial.thong_tin_gcn.so_serial = "AA 111111";

            // Giấy 4: đồng sử dụng 2 chủ -> 2 dòng dùng chung 1 STT.
            var coOwned = CreateNewGcnEnvelope(
                "co-owned.pdf",
                "dong_su_dung",
                [new NewGcnOwner { ho_ten = "Tran Van D" }, new NewGcnOwner { ho_ten = "Le Thi E" }],
                "05");
            coOwned.thong_tin_gcn.so_serial = "BB 222222";

            int rowCount = new NewGcnExcelExporter().Write(
                [missingSerial, twoParcels, duplicateSerial, coOwned], outputPath, templatePath);

            AssertEqual(6, rowCount, "STT: tổng số dòng ghi ra (1 + 2 thửa + 1 + 2 chủ)");

            using var output = new XLWorkbook(outputPath);
            var data = output.Worksheet("Data");

            AssertEqual("1", SttAt(data, 5), "Giấy thiếu serial vẫn nhận STT 1");
            AssertEqual("2", SttAt(data, 6), "Giấy 2 nhận STT 2, KHÔNG lấy lại số của giấy thiếu serial");
            AssertEqual("2", SttAt(data, 7), "Thửa thứ hai của cùng giấy dùng chung STT 2");
            AssertTrue(data.Cell(6, "A").IsMerged(),
                "Nhiều thửa của cùng một giấy phải dùng chung một ô STT đã merge.");
            AssertEqual("3", SttAt(data, 8), "File khác trùng serial phải có STT riêng, không tái dùng STT 2");
            AssertEqual("4", SttAt(data, 9), "Giấy 4 nhận STT 4");
            AssertEqual("4", SttAt(data, 10), "Dòng đồng sở hữu dùng chung STT của giấy");
            AssertTrue(data.Cell(9, "A").IsMerged(),
                "Dòng đồng sở hữu phải nằm trong ô STT đã merge của giấy.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        // Ô STT đã merge chỉ giữ giá trị ở ô đầu dải; dòng tiếp theo đọc trực tiếp sẽ ra rỗng.
        static string SttAt(IXLWorksheet ws, int row)
        {
            var cell = ws.Cell(row, "A");
            return cell.IsMerged()
                ? cell.MergedRange().FirstCell().Value.ToString()
                : cell.Value.ToString();
        }
    }

    /// <summary>
    /// GCN không in dân tộc nên điền sẵn "Kinh" chỉ là suy đoán — sai với chủ sử dụng là người dân tộc
    /// thiểu số. Cột dân tộc (X của chủ, AO của vợ/chồng) phải để trống; quốc tịch vẫn giữ mặc định.
    /// </summary>
    private static void NewGcnExcelExporterDoesNotHardcodeEthnicity()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var templatePath = Path.Combine(root, "template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            using (var template = new XLWorkbook())
            {
                template.AddWorksheet("Data");
                template.SaveAs(templatePath);
            }

            var spouse = CreateNewGcnEnvelope(
                "spouse.pdf",
                "vo_chong",
                [
                    new NewGcnOwner { ho_ten = "Nguyen Van A" },
                    new NewGcnOwner { ho_ten = "Tran Thi B" }
                ],
                "10");

            new NewGcnExcelExporter().Write([spouse], outputPath, templatePath);

            using var output = new XLWorkbook(outputPath);
            var data = output.Worksheet("Data");
            AssertEqual("", data.Cell(5, "X").Value.ToString(), "Dân tộc chủ sử dụng KHÔNG được điền sẵn");
            AssertEqual("", data.Cell(5, "AO").Value.ToString(), "Dân tộc vợ/chồng KHÔNG được điền sẵn");
            AssertEqual("Việt Nam", data.Cell(5, "Y").Value.ToString(), "Quốc tịch chủ vẫn giữ mặc định");
            AssertEqual("Việt Nam", data.Cell(5, "AP").Value.ToString(), "Quốc tịch vợ/chồng vẫn giữ mặc định");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ba mục khác nhau trên giấy phải vào ba cột khác nhau: ghi chú mặt 1 → `DE`, mục "Ghi chú" ở mặt 2
    /// → `DF`, mục "Những thay đổi sau khi cấp GCN" (mặt 3 + mặt 4 mẫu cũ / mục 6 mẫu QR) → `GD`.
    /// Trước đây "Những thay đổi" bị ghi nhầm vào cột "Ghi chú trang 1" (`DE`).
    /// </summary>
    private static void NewGcnExcelExporterSeparatesNotesFromChangeHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var templatePath = Path.Combine(root, "template.xlsx");
            var outputPath = Path.Combine(root, "output.xlsx");
            using (var template = new XLWorkbook())
            {
                template.AddWorksheet("Data");
                template.SaveAs(templatePath);
            }

            var envelope = CreateNewGcnEnvelope(
                "notes.pdf",
                "ca_nhan",
                [new NewGcnOwner { ho_ten = "Nguyen Van A" }],
                "79");
            envelope.thong_tin_gcn.ghi_chu_trang_1 = ["Ghi chu nam tren mat 1"];
            envelope.thong_tin_gcn.ghi_chu = ["So hieu va dien tich thua dat duoc xac dinh theo ban do dia chinh"];
            envelope.thong_tin_gcn.thong_tin_thay_doi =
            [
                "Chuyen muc dich su dung tu BHK thanh ONT theo Quyet dinh so 3471/QD-UBND ngay 10/9/2025",
                "Thoi han su dung: Lau dai theo ho so so 111.CM.001.2025"
            ];

            new NewGcnExcelExporter().Write([envelope], outputPath, templatePath);

            using var output = new XLWorkbook(outputPath);
            var data = output.Worksheet("Data");

            AssertEqual("1) Ghi chu nam tren mat 1", data.Cell(5, "DE").Value.ToString(),
                "Cột DE 'Ghi chú trang 1' chỉ nhận ghi chú của mặt 1");
            AssertTrue(data.Cell(5, "DF").Value.ToString().Contains("ban do dia chinh", StringComparison.Ordinal),
                "Cột DF 'Ghi chú trang 2' nhận mục Ghi chú của mặt 2");

            var changes = data.Cell(5, "GD").Value.ToString();
            AssertTrue(changes.Contains("Chuyen muc dich su dung", StringComparison.Ordinal),
                "Cột GD nhận nội dung 'Những thay đổi sau khi cấp GCN'");
            AssertTrue(changes.Contains("111.CM.001.2025", StringComparison.Ordinal),
                "Cột GD phải gom đủ mọi dòng thay đổi (mặt 3 + mặt 4)");

            AssertFalse(data.Cell(5, "DE").Value.ToString().Contains("Chuyen muc dich", StringComparison.Ordinal),
                "Nội dung 'Những thay đổi' KHÔNG được lọt vào cột Ghi chú trang 1");
            AssertFalse(data.Cell(5, "DF").Value.ToString().Contains("Chuyen muc dich", StringComparison.Ordinal),
                "Nội dung 'Những thay đổi' KHÔNG được lọt vào cột Ghi chú trang 2");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task UploadWorkQueueSettlesEachItemExactlyOnce()
    {
        var queue = new UploadWorkQueue();
        var settled = new List<(string Path, bool Success)>();
        queue.ItemSettled += (path, ok) => { lock (settled) settled.Add((path, ok)); };

        queue.Register("a.pdf");
        queue.Register("b.pdf");

        var itemA = new UploadWorkItem("a.pdf", Array.Empty<GeminiFileReference>(), true);
        queue.Publish(itemA);
        queue.CompleteWriter();

        var claimed = await queue.ClaimNextAsync(CancellationToken.None);
        AssertEqual("a.pdf", claimed?.SourcePath, "Item lay ra tu hang doi");

        AssertTrue(queue.Settle(itemA, true), "Lan chot dau tien phai tra true.");
        AssertFalse(queue.Settle(itemA, true), "Chot lan hai phai tra false (idempotent).");
        AssertFalse(queue.Settle(itemA, false), "Chot lai voi ket qua khac cung phai tra false.");

        // b.pdf chua bao gio duoc publish -> producer chot loi.
        AssertTrue(queue.Settle("b.pdf", false, "hong that"), "Chot loi cho file chua publish.");

        AssertEqual((UploadWorkItem?)null, await queue.ClaimNextAsync(CancellationToken.None),
            "Hang doi da dong thi ClaimNextAsync phai tra null.");

        AssertEqual(2, settled.Count, "Moi file bao ItemSettled dung mot lan");
        AssertEqual(1, queue.Failures.Count, "So file loi");
        AssertEqual("b.pdf", queue.Failures[0].SourcePath, "File loi");
    }

    private static async Task UploadWorkQueueReregisterIsNoop()
    {
        var queue = new UploadWorkQueue();
        var settled = new List<(string Path, bool Success)>();
        queue.ItemSettled += (path, ok) => { lock (settled) settled.Add((path, ok)); };

        queue.Register("c.pdf");
        var itemC = new UploadWorkItem("c.pdf", Array.Empty<GeminiFileReference>(), false);
        queue.Publish(itemC);
        queue.CompleteWriter();

        var claimed = await queue.ClaimNextAsync(CancellationToken.None);
        AssertEqual("c.pdf", claimed?.SourcePath, "Item lay ra tu hang doi");

        AssertTrue(queue.Settle(itemC, true), "Lan chot dau tien phai tra true.");

        // Ghi danh lại cùng file sau khi đã chốt.
        queue.Register("c.pdf");

        // Chốt lần nữa cũng phải trả false (bất biến "mỗi nguồn chốt đúng một lần").
        AssertFalse(queue.Settle("c.pdf", true, null), "Chot lan hai (sau re-register) phai tra false.");

        AssertEqual(1, settled.Count, "Moi file bao ItemSettled dung mot lan (khong phai hai lan)");
        AssertEqual("c.pdf", settled[0].Path, "ItemSettled phai chuyen duong dan dung");
    }

    private static async Task UploadWorkQueueConsumerFailureDoesNotLeakIntoFailures()
    {
        // Failures chi danh cho loi TRUOC khi toi consumer (upload/chuan bi). Loi inference (consumer
        // tu chot that bai qua Settle(UploadWorkItem, bool)) khong duoc lot vao day, neu khong se ghi
        // de thong tin loi that + log trung (bug tung phat hien o review Task 5).
        var queue = new UploadWorkQueue();
        var settled = new List<(string Path, bool Success)>();
        queue.ItemSettled += (path, ok) => { lock (settled) settled.Add((path, ok)); };

        queue.Register("infer-loi.pdf");
        var item = new UploadWorkItem("infer-loi.pdf", Array.Empty<GeminiFileReference>(), false);
        queue.Publish(item);
        queue.CompleteWriter();

        var claimed = await queue.ClaimNextAsync(CancellationToken.None);
        AssertEqual("infer-loi.pdf", claimed?.SourcePath, "Item lay ra tu hang doi");

        // Consumer chot that bai (vi du model tra JSON hong) qua duong item-based public Settle.
        AssertTrue(queue.Settle(item, false), "Consumer chot lan dau phai tra true.");

        AssertEqual(1, settled.Count, "ItemSettled van phai ban dung mot lan");
        AssertFalse(settled[0].Success, "Ket qua chot phai la that bai");
        AssertEqual(0, queue.Failures.Count, "Loi tang consumer KHONG duoc lot vao Failures");
    }

    private static async Task UploadWorkQueueSettleRemainingClosesPendingItemsWithoutRecordingFailures()
    {
        // Mo phong nguoi dung bam Dung: mot nguon da Register + Publish nhung chua consumer nao kip
        // claim (ClaimNextAsync bi huy truoc). SettleRemaining phai chot no, ban ItemSettled dung
        // mot lan, KHONG ghi vao Failures (khong phai loi upload that su), va TRA VE dung danh sach
        // nguon vua chot — day la nguon su that de ViewModel biet nguon nao "khong ai xu ly", KHONG
        // duoc suy doan qua chuoi TrangThai hien thi (bug tung xay ra: nham file thanh cong thanh treo).
        // Ba nguon: 1 da chot boi consumer (thanh cong), 1 da chot boi producer (loi upload that su),
        // 1 con treo thuc su — SettleRemaining chi duoc tra ve dung nguon thu ba.
        var queue = new UploadWorkQueue();
        var settled = new List<(string Path, bool Success)>();
        queue.ItemSettled += (path, ok) => { lock (settled) settled.Add((path, ok)); };

        queue.Register("chua-claim.pdf");
        queue.Register("da-xong.pdf");
        queue.Register("loi-producer.pdf");
        var doneItem = new UploadWorkItem("da-xong.pdf", Array.Empty<GeminiFileReference>(), false);
        queue.Publish(doneItem);
        queue.CompleteWriter();

        var claimed = await queue.ClaimNextAsync(CancellationToken.None);
        AssertEqual("da-xong.pdf", claimed?.SourcePath, "Item lay ra tu hang doi");
        AssertTrue(queue.Settle(doneItem, true), "Nguon da xu ly xong phai chot thanh cong truoc (duong consumer)");
        AssertTrue(queue.Settle("loi-producer.pdf", false, "upload that bai"),
            "Nguon loi upload phai chot truoc (duong producer)");

        // "chua-claim.pdf" chua bao gio duoc Publish/claim -> con StatePending.
        var abandoned = queue.SettleRemaining(false, "Da dung");

        AssertEqual(1, abandoned.Count, "Chi dung 1 nguon con treo duoc tra ve");
        AssertEqual("chua-claim.pdf", abandoned[0],
            "Nguon tra ve phai la nguon con treo, khong phai nguon da chot boi consumer hay producer");
        AssertEqual(3, settled.Count, "Ca ba nguon deu phai bao ItemSettled");
        AssertTrue(settled.Any(s => s.Path == "chua-claim.pdf" && !s.Success), "Nguon con treo phai chot that bai");
        AssertEqual(1, queue.Failures.Count, "Chi nguon loi producer nam trong Failures");
        AssertEqual("loi-producer.pdf", queue.Failures[0].SourcePath, "Dung nguon loi producer trong Failures");

        // Goi lai lan hai phai tra ve rong (khong chot lai, khong ban them event) nho CAS bao ve.
        var abandonedAgain = queue.SettleRemaining(false, "Da dung");
        AssertEqual(0, abandonedAgain.Count, "Goi lai SettleRemaining phai tra ve danh sach rong");
        AssertEqual(3, settled.Count, "SettleRemaining goi lai phai la no-op");
    }

    private static void GeminiUploadOptionsParsesValuesAndFallsBackToDefaults()
    {
        var withValues = JsonDocument.Parse(
            """{ "GeminiUpload": { "Workers": 6, "MaxRetries": 5, "RetryBaseDelayMs": 750 } }""");
        var parsed = AppSettingsLoader.ParseGeminiUpload(withValues.RootElement);
        AssertEqual(6, parsed.Workers, "GeminiUpload Workers");
        AssertEqual(5, parsed.MaxRetries, "GeminiUpload MaxRetries");
        AssertEqual(750, parsed.RetryBaseDelayMs, "GeminiUpload RetryBaseDelayMs");

        var empty = JsonDocument.Parse("""{ }""");
        var defaults = AppSettingsLoader.ParseGeminiUpload(empty.RootElement);
        AssertEqual(3, defaults.Workers, "GeminiUpload Workers mặc định");
        AssertEqual(3, defaults.MaxRetries, "GeminiUpload MaxRetries mặc định");
        AssertEqual(2000, defaults.RetryBaseDelayMs, "GeminiUpload RetryBaseDelayMs mặc định");
    }

    private static GeminiUploadRequest BuildUploadRequest(
        IReadOnlyList<string> paths,
        Func<string, bool>? willHitCache = null,
        int artifactsPerSource = 1,
        Action<string>? onItemUploading = null)
        => new()
        {
            SourcePaths = paths,
            WillHitJsonCache = willHitCache,
            PrepareArtifactsAsync = (path, _) => Task.FromResult<IReadOnlyList<UploadArtifact>>(
                Enumerable.Range(1, artifactsPerSource)
                    .Select(i => new UploadArtifact(
                        $"page-{i:D4}", Path.GetFileName(path), "application/pdf", new byte[] { 1, 2, 3 }))
                    .ToList()),
            OnItemUploading = onItemUploading
        };

    private static GeminiUploadOptions FastUploadOptions(int workers = 3, int maxRetries = 3)
        => new() { Workers = workers, MaxRetries = maxRetries, RetryBaseDelayMs = 0 };

    private static async Task<List<UploadWorkItem>> DrainAsync(IUploadWorkQueue queue, int consumers = 5)
    {
        var result = new List<UploadWorkItem>();
        var tasks = Enumerable.Range(0, consumers).Select(_ => Task.Run(async () =>
        {
            while (true)
            {
                var item = await queue.ClaimNextAsync(CancellationToken.None);
                if (item is null) break;
                lock (result) result.Add(item);
                queue.Settle(item, true);
            }
        })).ToList();
        await Task.WhenAll(tasks);
        return result;
    }

    private static async Task UploadPipelineRetriesThenSucceeds()
    {
        var files = new FakeGeminiFileApiService(
            remainingFailures: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["b.pdf"] = 2 });
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions());

        var queue = pipeline.Start(BuildUploadRequest(new[] { "a.pdf", "b.pdf" }), CancellationToken.None);
        var items = await DrainAsync(queue);

        AssertEqual(2, items.Count, "So item toi duoc consumer sau khi retry");
        AssertEqual(0, queue.Failures.Count, "Khong con loi sau khi retry thanh cong");
        AssertEqual(3, files.UploadCalls.Count(c => c.StartsWith("b.pdf|", StringComparison.Ordinal)),
            "b.pdf phai duoc goi du 3 luot");
    }

    private static async Task UploadPipelineMarksPermanentFailureAndKeepsItOutOfQueue()
    {
        var files = new FakeGeminiFileApiService(
            remainingFailures: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["bad.pdf"] = 99 });
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions());

        var settled = new List<(string Path, bool Success)>();
        var queue = pipeline.Start(BuildUploadRequest(new[] { "ok.pdf", "bad.pdf" }), CancellationToken.None);
        queue.ItemSettled += (path, ok) => { lock (settled) settled.Add((path, ok)); };

        var items = await DrainAsync(queue);

        AssertEqual(1, items.Count, "Chi file tot toi duoc consumer");
        AssertEqual("ok.pdf", items[0].SourcePath, "File toi consumer");
        AssertEqual(1, queue.Failures.Count, "So file loi");
        AssertEqual("bad.pdf", queue.Failures[0].SourcePath, "File loi");
        AssertEqual(2, settled.Count, "Moi file chot dung mot lan");
        AssertEqual(1, settled.Count(s => !s.Success), "Dung mot file bao that bai");
    }

    private static async Task UploadPipelineSkipsSourcesThatHitJsonCache()
    {
        var files = new FakeGeminiFileApiService();
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions());

        var request = BuildUploadRequest(
            new[] { "cached.pdf", "fresh.pdf" },
            willHitCache: path => path == "cached.pdf");
        var items = await DrainAsync(pipeline.Start(request, CancellationToken.None));

        AssertEqual(2, items.Count, "Ca hai deu phai toi consumer");
        AssertTrue(items.Single(i => i.SourcePath == "cached.pdf").SkippedUpload,
            "File trung cache phai co SkippedUpload = true.");
        AssertEqual(1, files.UploadCalls.Count, "Chi upload dung file khong trung cache");
        AssertTrue(files.UploadCalls[0].StartsWith("fresh.pdf|", StringComparison.Ordinal),
            "File duoc upload phai la fresh.pdf.");
    }

    private static async Task UploadPipelinePassesEverythingThroughWhenFilesApiDisabled()
    {
        var files = new FakeGeminiFileApiService(isEnabled: false);
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions());

        var items = await DrainAsync(
            pipeline.Start(BuildUploadRequest(new[] { "a.pdf", "b.pdf", "c.pdf" }), CancellationToken.None));

        AssertEqual(3, items.Count, "OpenRouter: moi item phai qua thang");
        AssertEqual(0, files.UploadCalls.Count, "OpenRouter: khong duoc upload lan nao");
        AssertTrue(items.All(i => i.Files.Count == 0), "OpenRouter: danh sach file phai rong.");
    }

    private static async Task UploadPipelineClaimsEachSourceExactlyOnceAndKeepsArtifactOrder()
    {
        var files = new FakeGeminiFileApiService();
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions());
        var paths = Enumerable.Range(1, 200).Select(i => $"f{i:D3}.pdf").ToList();

        var items = await DrainAsync(
            pipeline.Start(BuildUploadRequest(paths, artifactsPerSource: 3), CancellationToken.None), consumers: 5);

        AssertEqual(200, items.Count, "Tong so item nhan duoc");
        AssertEqual(200, items.Select(i => i.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "Moi nguon chi duoc claim dung mot lan");

        var sample = items.First(i => i.SourcePath == "f001.pdf");
        AssertEqual(3, sample.Files.Count, "So artifact moi nguon");
        AssertEqual("files/page-0001", sample.Files[0].Name, "Artifact thu tu 1");
        AssertEqual("files/page-0002", sample.Files[1].Name, "Artifact thu tu 2");
        AssertEqual("files/page-0003", sample.Files[2].Name, "Artifact thu tu 3");
    }

    private static async Task UploadPipelineClosesQueueWhenPrepareThrowsUnexpectedly()
    {
        var files = new FakeGeminiFileApiService();
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions());
        var request = new GeminiUploadRequest
        {
            SourcePaths = new[] { "boom.pdf" },
            PrepareArtifactsAsync = (_, _) => throw new InvalidOperationException("no tung khi chuan bi")
        };

        var queue = pipeline.Start(request, CancellationToken.None);
        var drain = DrainAsync(queue);
        var finished = await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(10)));

        AssertTrue(ReferenceEquals(finished, drain),
            "Producer ném exception thì hàng đợi vẫn phải đóng, consumer không được treo.");
        AssertEqual(0, (await drain).Count, "Khong item nao toi consumer");
        AssertEqual(1, queue.Failures.Count, "File hong phai nam trong Failures");
    }

    private static async Task UploadPipelineReleasesSemaphoreBeforeSettleEvenWhenSubscriberThrows()
    {
        // Chi 1 worker: neu suat semaphore bi ro ri (khong duoc Release) khi nguon dau tien
        // that bai, hai nguon con lai se cho slots.WaitAsync mai mai va producer treo vinh vien.
        var files = new FakeGeminiFileApiService();
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions(workers: 1));
        var request = new GeminiUploadRequest
        {
            SourcePaths = new[] { "cancel1.pdf", "cancel2.pdf", "cancel3.pdf" },
            // OperationCanceledException bi catch rieng, khong goi Settle o do -> Settle trong
            // finally la lan chot DAU TIEN va DUY NHAT cho nguon nay (dung duong bi review neu ra).
            PrepareArtifactsAsync = (_, _) => throw new OperationCanceledException("gia lap huy giua chung")
        };

        var queue = pipeline.Start(request, CancellationToken.None);
        // Subscriber gia lap nem loi (giong handler UI that su co the nem khi dung dispatcher).
        queue.ItemSettled += (_, _) => throw new InvalidOperationException("subscriber gia lap nem loi khi Settle");

        var drain = DrainAsync(queue);
        var finished = await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(10)));

        AssertTrue(ReferenceEquals(finished, drain),
            "Settle bi subscriber nem loi khong duoc lam ro ri suat semaphore roi treo producer.");
        AssertEqual(0, (await drain).Count, "Khong nguon nao duoc publish vi ca 3 deu bi huy");
        AssertEqual(3, queue.Failures.Count, "Ca 3 nguon deu phai nam trong Failures du subscriber nem loi");
    }

    private static async Task UploadPipelineOnItemSettledCatchesEveryEventEvenWhenProducerSettlesImmediately()
    {
        // OnItemSettled phai duoc dang ky ben trong Start (truoc khi phong producer), khong phai
        // qua "+=" sau khi Start tra ve — neu khong, nguon chot qua nhanh se lam mat su kien va
        // ViewModel dem "done" khong bao gio du tong so file (khoa nut Export vinh vien).
        // De doi hoi kha thi nhat cho race (neu co) loi ra: PrepareArtifactsAsync nem NGAY LAP TUC,
        // khong await gi ca, nen finally co the chot ngay khi task cua Task.Run vua duoc lap lich.
        var files = new FakeGeminiFileApiService();
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions(workers: 10));
        var paths = Enumerable.Range(1, 50).Select(i => $"tuc-thi-{i:D3}.pdf").ToList();

        var settledPaths = new List<string>();
        var request = new GeminiUploadRequest
        {
            SourcePaths = paths,
            PrepareArtifactsAsync = (_, _) => throw new InvalidOperationException("loi ngay lap tuc, khong await gi"),
            OnItemSettled = (path, _) => { lock (settledPaths) settledPaths.Add(path); }
        };

        // Co y KHONG tu "+=" vao queue.ItemSettled sau Start — muc dich cua fix la khong con can
        // lam vay nua; moi thu phai di qua OnItemSettled truyen trong request.
        var queue = pipeline.Start(request, CancellationToken.None);
        var items = await DrainAsync(queue);

        AssertEqual(0, items.Count, "Khong nguon nao chuan bi duoc du lieu nen khong toi consumer");
        AssertEqual(paths.Count, settledPaths.Count,
            "OnItemSettled phai nhan DU su kien cho moi nguon, ke ca nguon chot ngay khi producer vua chay.");
        AssertEqual(paths.Count, settledPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "Moi nguon chi duoc bao qua OnItemSettled dung mot lan.");
        AssertEqual(paths.Count, queue.Failures.Count, "Moi nguon phai nam trong Failures");
    }

    private static async Task UploadPipelineCompletionCompletesAfterProducerFinishesAllWork()
    {
        // Completion phai la task cua chinh producer (RunProducerAsync) — hoan tat NGHIA LA producer
        // da chay het, moi nguon da duoc publish/chot xong. Neu quen AttachProducer, Completion se la
        // Task.CompletedTask ngay tu dau va ViewModel se tong ket phien truoc khi producer kip lam gi.
        var files = new FakeGeminiFileApiService();
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions());
        var paths = Enumerable.Range(1, 30).Select(i => $"comp-{i:D3}.pdf").ToList();

        var queue = pipeline.Start(BuildUploadRequest(paths), CancellationToken.None);

        var finished = await Task.WhenAny(queue.Completion, Task.Delay(TimeSpan.FromSeconds(10)));
        AssertTrue(ReferenceEquals(finished, queue.Completion),
            "Completion phai hoan tat trong thoi gian hop ly, khong duoc treo.");
        await queue.Completion; // khong duoc nem loi ra ngoai

        // Sau khi Completion da xong, channel writer da dong (producer chay het finally) nen drain
        // toan bo con lai phai lay du het so file, khong nguon nao bi "quen" o phia sau.
        var items = await DrainAsync(queue);
        AssertEqual(paths.Count, items.Count, "Sau Completion, drain phai lay du moi nguon da publish");
    }

    private static async Task UploadPipelineSettleRemainingClosesItemsAbandonedByCancelledConsumer()
    {
        // Dung tinh huong review Task 5 chi ra: nguon da duoc producer publish xong nhung khong
        // consumer nao claim (nguoi dung bam Dung dung luc ClaimNextAsync dang cho) -> phai dua vao
        // SettleRemaining moi chot duoc, khong thi treo mai o trang thai chua ket thuc.
        var files = new FakeGeminiFileApiService();
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions());
        var queue = pipeline.Start(BuildUploadRequest(new[] { "bo-do.pdf" }), CancellationToken.None);

        // Doi producer publish xong ma KHONG claim gi ca — mo phong nguoi dung bam Dung ngay sau do.
        await queue.Completion;

        AssertEqual(0, queue.Failures.Count, "Chua goi SettleRemaining thi chua co gi trong Failures");

        var abandoned = queue.SettleRemaining(false, "Da dung");

        AssertEqual(1, abandoned.Count, "Dung 1 nguon con treo duoc tra ve");
        AssertEqual("bo-do.pdf", abandoned[0], "Nguon tra ve phai la nguon chua ai claim");
        AssertEqual(0, queue.Failures.Count, "SettleRemaining khong duoc ghi vao Failures");
        AssertFalse(queue.Settle(new UploadWorkItem("bo-do.pdf", Array.Empty<GeminiFileReference>(), false), true),
            "Nguon da duoc SettleRemaining chot thi khong the chot lai duoc nua (bat bien CAS).");

        // Goi lai lan hai (mo phong tong ket phien goi SettleRemaining) phai tra ve rong.
        var abandonedAgain = queue.SettleRemaining(false, "Da dung");
        AssertEqual(0, abandonedAgain.Count, "Goi lai SettleRemaining sau khi da chot het phai tra ve rong");
    }

    private static async Task UploadPipelineInvokesOnItemUploadingExactlyOnceForSourcesThatActuallyUpload()
    {
        // OnItemUploading la moc "Dang tai len" tren dong luoi (Task 10). Nguon trung cache JSON
        // return som truoc khi goi PrepareArtifactsAsync/upload nen KHONG duoc bao "Dang tai len" —
        // nguon do khong tai gi len ca. Chi hai nguon that su di qua nhanh upload moi duoc bao,
        // moi nguon dung mot lan.
        var files = new FakeGeminiFileApiService();
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions());

        var uploading = new List<string>();
        var request = BuildUploadRequest(
            new[] { "cached.pdf", "fresh1.pdf", "fresh2.pdf" },
            willHitCache: path => path == "cached.pdf",
            onItemUploading: path => { lock (uploading) uploading.Add(path); });

        var items = await DrainAsync(pipeline.Start(request, CancellationToken.None));

        AssertEqual(3, items.Count, "Ca ba nguon deu phai toi consumer");
        AssertEqual(2, uploading.Count, "Chi nguon THAT SU upload moi duoc bao Dang tai len");
        AssertTrue(uploading.Contains("fresh1.pdf") && uploading.Contains("fresh2.pdf"),
            "Ca hai nguon khong trung cache phai duoc bao Dang tai len");
        AssertFalse(uploading.Contains("cached.pdf"),
            "Nguon trung cache JSON khong tai gi len nen khong duoc bao Dang tai len");
        AssertEqual(2, uploading.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "Moi nguon chi duoc bao Dang tai len dung mot lan");
    }

    private static async Task UploadPipelineDoesNotInvokeOnItemUploadingWhenFilesApiDisabled()
    {
        // Provider khong dung Files API (OpenRouter, IsEnabled = false) -> khong nguon nao tai gi
        // len ca, nen OnItemUploading khong duoc goi cho bat ky nguon nao.
        var files = new FakeGeminiFileApiService(isEnabled: false);
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions());

        var uploading = new List<string>();
        var request = BuildUploadRequest(
            new[] { "a.pdf", "b.pdf", "c.pdf" },
            onItemUploading: path => { lock (uploading) uploading.Add(path); });

        var items = await DrainAsync(pipeline.Start(request, CancellationToken.None));

        AssertEqual(3, items.Count, "Ca ba nguon deu phai toi consumer qua thang");
        AssertEqual(0, uploading.Count, "IsEnabled = false thi khong nguon nao duoc bao Dang tai len");
    }

    private static async Task UploadPipelineDoesNotRecordCancelledSourcesAsFailures()
    {
        // Bam Dung giua chung (finding review cuoi cung): nguon con dang cho slots.WaitAsync(ct) (chua
        // kip vao PrepareArtifactsAsync/upload) bi huy ngay lap tuc khi nguoi dung huy phien. Truoc fix,
        // finally cua producer van ghi VO DIEU KIEN nhung nguon nay vao Failures voi ly do "Khong chuan
        // bi duoc du lieu de gui" du khong he co loi upload nao that su — voi lo 1400 file la ~1390
        // entry "loi" ma trong mot danh sach ma XML doc khai la "nguon hong TRUOC KHI toi duoc luong
        // inference". Sau fix: nguon van phai duoc CHOT du (so sach du - moi nguon bao dung mot lan
        // OnItemSettled, tong dung bang so nguon) nhung KHONG duoc liet vao Failures vi day la huy theo
        // yeu cau nguoi dung, khong phai loi upload/chuan bi that su.
        var files = new FakeGeminiFileApiService();
        var pipeline = new GeminiUploadPipeline(files, FastUploadOptions());
        var paths = Enumerable.Range(1, 20).Select(i => $"huy-{i:D3}.pdf").ToList();

        var settled = new List<(string Path, bool Success)>();
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Mo phong nguoi dung bam Dung TRUOC khi bat ky nguon nao kip chay.

        var request = new GeminiUploadRequest
        {
            SourcePaths = paths,
            PrepareArtifactsAsync = (path, _) => Task.FromResult<IReadOnlyList<UploadArtifact>>(
                new[] { new UploadArtifact("source-pdf", path, "application/pdf", new byte[] { 1 }) }),
            OnItemSettled = (path, ok) => { lock (settled) settled.Add((path, ok)); }
        };

        var queue = pipeline.Start(request, cts.Token);
        await queue.Completion;

        AssertEqual(paths.Count, settled.Count,
            "Moi nguon phai duoc chot dung mot lan (so sach du) du bi huy truoc khi bat dau.");
        AssertTrue(settled.All(s => !s.Success), "Nguon bi huy truoc khi bat dau phai chot that bai.");
        AssertEqual(0, queue.Failures.Count,
            "Nguon huy theo yeu cau nguoi dung KHONG duoc liet vao Failures (tranh phinh 'file loi' ma).");

        var claimed = await queue.ClaimNextAsync(CancellationToken.None);
        AssertEqual((UploadWorkItem?)null, claimed,
            "Khong nguon nao duoc publish vi tat ca deu bi huy truoc khi kip chuan bi du lieu.");
    }

    private static NewGcnEnvelope CreateNewGcnEnvelope(
        string fileName,
        string relationship,
        List<NewGcnOwner> owners,
        string parcelNumber,
        string? barcode = null)
        => new()
        {
            ten_file = fileName,
            thong_tin_gcn = new NewGcnInfo
            {
                so_serial = fileName,
                loai_quan_he = relationship,
                ma_vach = barcode,
                chu_su_dung_chi_tiet = owners
            },
            danh_sach_dong =
            [
                new NewGcnRow
                {
                    td_so_thua = parcelNumber,
                    td_so_to = "1",
                    td_tong_dien_tich = "100"
                }
            ]
        };

    private static async Task ExportReportContainsUserDurationAndItems()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK);
        var notifier = CreateExportNotifier(handler);
        var started = new DateTime(2026, 7, 16, 8, 0, 0);
        await notifier.SendExportReportAsync(new ExportRunReport
        {
            ScreenName = "OCR GCN (New)",
            FeatureName = "Export Excel OCR GCN (New)",
            RunStartedAt = started,
            RunCompletedAt = started.AddMinutes(2).AddSeconds(3),
            ExportedAt = started.AddMinutes(5),
            TotalOutput = 12,
            OutputPath = @"D:\ket-qua",
            Items = [new ExportReportItem { Name = "Số dòng dữ liệu", Value = "12" }]
        });

        AssertEqual(1, handler.RequestBodies.Count, "Export request count");
        using var json = JsonDocument.Parse(handler.RequestBodies[0]);
        var text = json.RootElement.GetProperty("text").GetString() ?? "";
        AssertTrue(text.Contains("Nguyễn Văn A"), "Expected display name.");
        AssertTrue(text.Contains("00:02:03"), "Expected duration.");
        AssertTrue(text.Contains("Số dòng dữ liệu: 12"), "Expected report item.");
        AssertEqual("test-recipient", json.RootElement.GetProperty("chat_id").GetString(), "Recipient");
    }

    private static async Task ExportReportSplitsLongMessages()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK);
        var notifier = CreateExportNotifier(handler);
        await notifier.SendExportReportAsync(new ExportRunReport
        {
            ScreenName = "OCR Tách GCN New",
            RunStartedAt = DateTime.Now.AddMinutes(-1),
            RunCompletedAt = DateTime.Now,
            ExportedAt = DateTime.Now,
            Items = [new ExportReportItem { Name = "Chi tiết", Value = new string('x', 9_000) }]
        });

        AssertTrue(handler.RequestBodies.Count >= 3, "Long report must be split.");
        foreach (var body in handler.RequestBodies)
        {
            using var json = JsonDocument.Parse(body);
            AssertTrue((json.RootElement.GetProperty("text").GetString() ?? "").Length <= 4096,
                "Each part must fit API limit.");
        }
    }

    private static async Task ExportReportSkipsEmptyPartAtBoundaryNewline()
    {
        const string itemPrefix = "Boundary: ";
        const string tail = "TAIL-AFTER-BOUNDARY";
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK);
        await CreateExportNotifier(handler).SendExportReportAsync(new ExportRunReport
        {
            ScreenName = "OCR Tách GCN",
            RunStartedAt = DateTime.Now,
            RunCompletedAt = DateTime.Now,
            ExportedAt = DateTime.Now,
            Items =
            [
                new ExportReportItem
                {
                    Name = "Boundary",
                    Value = new string('a', 4096 - itemPrefix.Length) + "\n" +
                            tail + new string('b', 5_000)
                }
            ]
        });

        var parts = ReadRequestTexts(handler);
        AssertTrue(parts.Count >= 3, "Boundary report must be split.");
        AssertTrue(parts.All(part => part.Length > 0), "Boundary newline must not create an empty part.");
        AssertTrue(parts.All(part => part.Length <= 4096), "Boundary parts must fit API limit.");
        AssertTrue(string.Concat(parts).Contains(tail, StringComparison.Ordinal),
            "Content after boundary newline must be preserved.");
    }

    private static async Task ExportReportPreservesSurrogatePairAtChunkBoundary()
    {
        const string itemPrefix = "Unicode: ";
        const string unicodeTail = "😀TAIL-UNICODE";
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK);
        await CreateExportNotifier(handler).SendExportReportAsync(new ExportRunReport
        {
            ScreenName = "OCR GCN (New)",
            RunStartedAt = DateTime.Now,
            RunCompletedAt = DateTime.Now,
            ExportedAt = DateTime.Now,
            Items =
            [
                new ExportReportItem
                {
                    Name = "Unicode",
                    Value = new string('x', 4095 - itemPrefix.Length) + unicodeTail +
                            new string('y', 5_000)
                }
            ]
        });

        var parts = ReadRequestTexts(handler);
        AssertTrue(parts.All(part => part.Length > 0), "Unicode report must not contain empty parts.");
        AssertTrue(parts.All(part => part.Length <= 4096), "Unicode parts must fit API limit.");
        AssertTrue(string.Concat(parts).Contains(unicodeTail, StringComparison.Ordinal),
            "Surrogate pair and Unicode tail must remain intact.");
    }

    private static async Task ExportReportWritesNotifierErrorLogForExceptions()
    {
        const string token = "987654321:secret-test-token-must-not-leak";
        const string recipient = "sensitive-test-recipient";
        var logDir = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        var handler = new ThrowingHttpMessageHandler(recipient);
        try
        {
            await CreateExportNotifier(
                handler,
                accessToken: token,
                recipientId: recipient,
                notifierLog: new NotifierErrorLogService(logDir)).SendExportReportAsync(new ExportRunReport
                {
                    ScreenName = "OCR Tách GCN",
                    RunStartedAt = DateTime.Now,
                    RunCompletedAt = DateTime.Now,
                    ExportedAt = DateTime.Now
                });

            var logPath = Path.Combine(logDir, "error-notifier.log");
            AssertTrue(File.Exists(logPath), "Notifier failures must be written to error-notifier.log.");
            var logs = await File.ReadAllTextAsync(logPath);
            AssertTrue(logs.Contains("error notifier", StringComparison.Ordinal), "Notifier error log must identify notifier errors.");
            AssertTrue(logs.Contains("Request failed", StringComparison.Ordinal), "Notifier error log must include exception.Message.");
            AssertFalse(logs.Contains(nameof(HttpRequestException), StringComparison.Ordinal), "Notifier error log must not include exception type.");
            AssertFalse(logs.Contains("   at ", StringComparison.Ordinal), "Notifier error log must not include stack trace.");
            AssertFalse(logs.Contains(token, StringComparison.Ordinal), "Notifier error log must not contain access token.");
            AssertFalse(logs.Contains(recipient, StringComparison.Ordinal), "Notifier error log must not contain recipient.");
        }
        finally
        {
            if (Directory.Exists(logDir))
                Directory.Delete(logDir, recursive: true);
        }
    }

    private static async Task ExportReportWritesNotifierErrorLogForHttpFailure()
    {
        var logDir = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.InternalServerError);
        try
        {
            await CreateExportNotifier(handler, notifierLog: new NotifierErrorLogService(logDir))
                .SendExportReportAsync(new ExportRunReport
                {
                    ScreenName = "OCR Tách GCN",
                    RunStartedAt = DateTime.Now,
                    RunCompletedAt = DateTime.Now,
                    ExportedAt = DateTime.Now
                });

            var logPath = Path.Combine(logDir, "error-notifier.log");
            AssertTrue(File.Exists(logPath), "HTTP notifier failure must be written to error-notifier.log.");
            var logs = await File.ReadAllTextAsync(logPath);
            AssertTrue(
                Regex.IsMatch(logs, @"error notifier \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} HTTP 500"),
                "Notifier error log must use: error notifier + date time + exception.message.");
        }
        finally
        {
            if (Directory.Exists(logDir))
                Directory.Delete(logDir, recursive: true);
        }
    }

    private static void ExportReportUsesObfuscationSafeJsonPayload()
    {
        var sourcePath = FindRepositoryFile(
            Path.Combine("OCR WinApp", "OCR.Business", "Notifications", "ExportResultNotifier.cs"));
        var source = File.ReadAllText(sourcePath);

        AssertFalse(source.Contains("JsonContent.Create(new", StringComparison.Ordinal),
            "Notifier must not serialize anonymous objects; Obfuscar can remove constructor parameter names.");
        AssertTrue(source.Contains("StringContent", StringComparison.Ordinal),
            "Notifier must use explicit JSON StringContent for Telegram request body.");
        AssertTrue(source.Contains("JsonSerializer.Serialize", StringComparison.Ordinal),
            "Notifier must serialize an obfuscation-safe payload explicitly.");
    }

    private static async Task ExportReportSwallowsHttpFailure()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.InternalServerError);
        await CreateExportNotifier(handler).SendExportReportAsync(new ExportRunReport
        {
            ScreenName = "OCR Tách GCN",
            RunStartedAt = DateTime.Now,
            RunCompletedAt = DateTime.Now,
            ExportedAt = DateTime.Now
        });
        AssertEqual(1, handler.RequestBodies.Count, "Failed HTTP request count");
    }

    private static async Task ExportReportSkipsMissingConfiguration()
    {
        var logDir = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", "notifier-missing-config");
        if (Directory.Exists(logDir))
            Directory.Delete(logDir, recursive: true);

        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK);
        var notifier = new ExportResultNotifier(
            new ExportNotificationOptions(),
            new FakeAuthService(),
            new NotifierErrorLogService(logDir),
            new HttpClient(handler));
        await notifier.SendExportReportAsync(new ExportRunReport());
        AssertEqual(0, handler.RequestBodies.Count, "Missing configuration request count");
        AssertFalse(File.Exists(Path.Combine(logDir, "error-notifier.log")),
            "Missing notifier configuration must not create notifier error log.");
    }

    private static IReadOnlyList<string> ReadRequestTexts(RecordingHttpMessageHandler handler)
        => handler.RequestBodies.Select(body =>
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("text").GetString() ?? "";
        }).ToList();

    private static ExportResultNotifier CreateExportNotifier(
        HttpMessageHandler handler,
        string accessToken = "123:test-token",
        string recipientId = "test-recipient",
        INotifierErrorLogService? notifierLog = null)
        => new(
            new ExportNotificationOptions
            {
                Enabled = true,
                BaseUrl = "https://example.invalid",
                AccessToken = accessToken,
                RecipientId = recipientId,
                TimeoutSeconds = 5
            },
            new FakeAuthService(),
            notifierLog ?? new NotifierErrorLogService(Path.Combine(
                Path.GetTempPath(),
                "ocr-winapp-tests",
                "notifier-default")),
            new HttpClient(handler));

    private static void LabelAllocatorUsesHyphenForParcelAndCollisionSuffixes()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var allocator = new LabelAllocator(root);
            var labels = allocator.AllocateGroup("CS123", 2);

            AssertEqual("CS123-1", labels[0].Label, "First parcel label");
            AssertEqual("CS123-2", labels[1].Label, "Second parcel label");

            var collision = allocator.Allocate("CS123");
            AssertEqual("CS123-3", collision.Label, "Collision suffix label");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void SplitPromptsContainOneValidJsonExample()
    {
        var assembly = typeof(SplitGcnService).Assembly;
        AssertPromptSchema(assembly, "prompt_split_gcn.md", mustHaveParcelCount: true, mustHaveGcn: true);
        AssertPromptSchema(assembly, "prompt_split_gcn_new.md", mustHaveParcelCount: false, mustHaveGcn: true);
        AssertPromptSchema(assembly, "prompt_split_gcn_no_gcn.md", mustHaveParcelCount: false, mustHaveGcn: false);
    }

    private static void AssertPromptSchema(Assembly assembly, string suffix, bool mustHaveParcelCount, bool mustHaveGcn)
    {
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var prompt = reader.ReadToEnd();
        var blocks = Regex.Matches(prompt, "```json\\s*(.*?)\\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        AssertEqual(1, blocks.Count, $"{suffix} fenced JSON block count");

        using var json = JsonDocument.Parse(blocks[0].Groups[1].Value);
        var root = json.RootElement;
        AssertTrue(root.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array, $"{suffix} pages");
        AssertTrue(root.TryGetProperty("documents", out var documents) && documents.ValueKind == JsonValueKind.Array, $"{suffix} documents");
        var item = documents.EnumerateArray().First();
        AssertEqual(mustHaveParcelCount, item.TryGetProperty("parcel_count", out _), $"{suffix} parcel_count");
        AssertEqual(mustHaveGcn, item.TryGetProperty("serial_GCN.pdf", out _), $"{suffix} serial_GCN.pdf");
    }

    private static async Task SplitServiceReusesValidJsonAndRefreshesInvalidJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pdf = Path.Combine(root, "source.pdf");
            var json = Path.Combine(root, "json", "source.json");
            CreatePdf(pdf, 1);
            Directory.CreateDirectory(Path.GetDirectoryName(json)!);
            await File.WriteAllTextAsync(json, """{"pages":[],"documents":[]}""");
            var ai = new FakeAiModelClient("""{"pages":[],"documents":[]}""");
            var service = new SplitGcnService(ai, new FakePdfRotationNormalizer(), new SplitGcnOptions { MaxRetries = 0 });

            await service.SplitAsync(pdf, new LabelAllocator(Path.Combine(root, "out1")), jsonPath: json, useCachedJson: true);
            AssertEqual(0, ai.Calls.Count, "Valid JSON cache must skip AI");

            await File.WriteAllTextAsync(json, "invalid");
            await service.SplitAsync(pdf, new LabelAllocator(Path.Combine(root, "out2")), jsonPath: json, useCachedJson: true);
            AssertEqual(1, ai.Calls.Count, "Invalid JSON cache must call AI");
            AssertTrue((await File.ReadAllTextAsync(json)).Contains("documents"), "Invalid cache must be replaced.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SplitRunCacheSeparatesScreensAndSameNamedSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        var sourceA = Path.Combine(root, "a", "Ho so");
        var sourceB = Path.Combine(root, "b", "Ho so");
        Directory.CreateDirectory(Path.Combine(sourceA, "Thon A"));
        Directory.CreateDirectory(Path.Combine(sourceB, "Thon A"));

        try
        {
            var service = new SplitRunCacheService(root);
            var first = service.GetWorkspace("tach-gcn", sourceA);
            var second = service.GetWorkspace("tach-gcn", sourceB);
            var otherScreen = service.GetWorkspace("tach-gcn-new", sourceA);

            AssertFalse(string.Equals(first.CacheDir, second.CacheDir, StringComparison.OrdinalIgnoreCase), "Same-named sources need different cache keys.");
            AssertFalse(string.Equals(first.CacheDir, otherScreen.CacheDir, StringComparison.OrdinalIgnoreCase), "Screens need separate cache roots.");

            var jsonA = service.GetJsonPath(first, Path.Combine(sourceA, "Thon A", "source.pdf"));
            var jsonB = service.GetJsonPath(first, Path.Combine(sourceA, "Thon B", "source.pdf"));
            AssertTrue(jsonA.EndsWith(Path.Combine("Thon A", "source.json"), StringComparison.OrdinalIgnoreCase), "JSON must preserve relative path.");
            AssertEqual(Path.Combine(first.CacheDir, "Thon A", "source.json"), jsonA, "Split JSON cache must not use an extra json folder.");
            AssertEqual(first.CacheDir, first.JsonDir, "Split JSON cache root must be the workspace cache directory.");
            AssertFalse(string.Equals(jsonA, jsonB, StringComparison.OrdinalIgnoreCase), "Relative paths must prevent JSON collisions.");

            Directory.CreateDirectory(first.JsonDir);
            Directory.CreateDirectory(second.JsonDir);
            File.WriteAllText(Path.Combine(first.JsonDir, "old.json"), "{}");
            File.WriteAllText(Path.Combine(second.JsonDir, "keep.json"), "{}");
            await service.ResetWorkspaceAsync(first);
            AssertFalse(File.Exists(Path.Combine(first.JsonDir, "old.json")), "Reset must clear current workspace.");
            AssertTrue(File.Exists(Path.Combine(second.JsonDir, "keep.json")), "Reset must not clear another workspace.");

            var currentPdf = Path.Combine(sourceA, "Thon A", "source.pdf");
            var removedPdf = Path.Combine(sourceA, "Thon B", "source.pdf");
            File.WriteAllBytes(currentPdf, [1, 2, 3]);
            Directory.CreateDirectory(Path.GetDirectoryName(removedPdf)!);
            File.WriteAllBytes(removedPdf, [4, 5, 6]);
            var currentJson = service.GetJsonPath(first, currentPdf);
            var removedJson = service.GetJsonPath(first, removedPdf);
            Directory.CreateDirectory(Path.GetDirectoryName(currentJson)!);
            Directory.CreateDirectory(Path.GetDirectoryName(removedJson)!);
            var sourceTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var cacheTime = sourceTime.AddMinutes(1);
            File.SetLastWriteTimeUtc(currentPdf, sourceTime);
            File.WriteAllText(currentJson, "{}");
            File.SetLastWriteTimeUtc(currentJson, cacheTime);
            File.WriteAllText(removedJson, "{}");
            File.SetLastWriteTimeUtc(removedJson, cacheTime);

            AssertTrue(service.HasJsonCache(first, [currentPdf]),
                "Current split file with matching JSON should trigger cache prompt.");
            AssertFalse(service.HasJsonCache(first, [Path.Combine(sourceA, "new-file.pdf")]),
                "Old split JSON for files no longer selected must not trigger cache prompt.");
            File.SetLastWriteTimeUtc(currentPdf, cacheTime.AddMinutes(1));
            AssertTrue(service.HasJsonCache(first, [currentPdf]),
                "Split matching JSON should trigger cache prompt even when service later decides whether it is stale.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SplitVariantsUseMaximumTokensMediumReasoningAndZeroTemperature()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        var tempDir = Path.Combine(root, "split-temp");
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "source.pdf");
            CreatePdf(sourcePdf, 1);
            var ai = new FakeAiModelClient("""{"pages":[],"documents":[]}""");
            var service = new SplitGcnService(
                ai,
                new FakePdfRotationNormalizer(),
                new SplitGcnOptions { TempDir = tempDir, MaxRetries = 0, ReasoningEffort = "medium" });

            foreach (var variant in new[] { SplitGcnVariant.Standard, SplitGcnVariant.New, SplitGcnVariant.NoGcn })
            {
                await service.SplitAsync(
                    sourcePdf,
                    new LabelAllocator(tempDir),
                    normalizePageRotation: false,
                    variant: variant);
            }

            AssertEqual(3, ai.Calls.Count, "AI call count");
            foreach (var call in ai.Calls)
            {
                AssertEqual(true, call.ReasoningEnabled, "Reasoning enabled");
                AssertEqual(65536, call.MaxTokens, "Maximum output tokens");
                AssertEqual("medium", call.ReasoningEffort, "Reasoning effort");
                AssertEqual(0d, call.Temperature, "Temperature");
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task NewGcnPdfAndImageUseMaximumTokensDefaultReasoningAndZeroTemperature()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "source.pdf");
            var sourceImage = Path.Combine(root, "source-image.jpg");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);
            await File.WriteAllBytesAsync(sourceImage, [4, 5, 6]);

            // Envelope hợp lệ CÓ chủ sử dụng: test này đo tham số gọi AI, không phải hậu kiểm — trả
            // "{}" sẽ kích hoạt lượt đọc lại chủ sử dụng và làm sai số lượt gọi.
            var ai = new FakeAiModelClient(CreateNewGcnJsonWithDeclaredParcelCount(1, ["10"]));
            var service = new NewGcnExtractService(
                new FakePdfRenderer(),
                ai,
                new NewGcnOptions
                {
                    OptimizeImages = false,
                    TempDir = Path.Combine(root, "newgcn-temp")
                });

            await service.ProcessFileAsync(sourcePdf, logCallback: null);
            await service.ProcessFileAsync(sourceImage, logCallback: null);

            var cache = new NewGcnRunCacheService(Path.Combine(root, "newgcn-temp"));
            AssertTrue(File.Exists(cache.GetResponseCachePath(sourcePdf)), "OCR GCN New PDF cache must be stored under TempDir.");
            AssertTrue(File.Exists(cache.GetResponseCachePath(sourceImage)), "OCR GCN New image cache must be stored under TempDir.");
            AssertFalse(Directory.Exists(Path.Combine(root, "response_new")), "OCR GCN New must not create response_new beside source files.");

            AssertEqual(2, ai.Calls.Count, "OCR GCN New AI call count");
            foreach (var call in ai.Calls)
            {
                AssertEqual(true, call.ReasoningEnabled, "OCR GCN New reasoning enabled");
                AssertEqual(65536, call.MaxTokens, "OCR GCN New maximum output tokens");
                AssertEqual("medium", call.ReasoningEffort, "OCR GCN New default reasoning effort");
                AssertEqual(0d, call.Temperature, "OCR GCN New temperature");
                // Yêu cầu nghiệp vụ: 3 lượt gọi/file (1 lượt đầu + 2 lượt thử lại), và các lượt thử lại
                // phải nâng reasoning lên "high" thay vì lặp y nguyên request đã thất bại. Trước đây là 5
                // lượt cùng effort — hạ được xuống 3 vì responseSchema đã chặn gốc kiểu JSON hỏng cú pháp.
                AssertEqual(3, call.MaxAttempts, "OCR GCN New must retry invalid JSON 3 times");
                AssertEqual("high", call.EscalatedReasoningEffort,
                    "OCR GCN New retries must escalate reasoning effort");
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task NewGcnRetriesWhenDeclaredParcelCountExceedsRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "multi-parcel.pdf");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            var firstJson = CreateNewGcnJsonWithDeclaredParcelCount(3, ["10", "11"]);
            var retryJson = CreateNewGcnJsonWithDeclaredParcelCount(3, ["10", "11", "12"]);
            var ai = new FakeAiModelClient(firstJson, retryJson);
            var service = new NewGcnExtractService(
                new FakePdfRenderer(),
                ai,
                new NewGcnOptions
                {
                    OptimizeImages = false,
                    TempDir = Path.Combine(root, "newgcn-temp")
                });

            var envelope = await service.ProcessFileAsync(sourcePdf, logCallback: null);

            AssertEqual(2, ai.Calls.Count, "OCR GCN New must retry when declared parcel count is greater than row count");
            AssertEqual(3, envelope?.thong_tin_gcn.so_luong_thua_dat_doc_duoc, "Declared parcel count");
            AssertEqual(3, envelope?.danh_sach_dong.Count, "Rows after parcel-count retry");
            AssertEqual("medium", ai.Calls[0].ReasoningEffort, "First OCR GCN New call keeps the cheaper default effort");
            AssertEqual("high", ai.Calls[1].ReasoningEffort, "Parcel-count retry escalates to the highest reasoning effort");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task NewGcnThrowsClearErrorWhenResponseIsMultiGcnArray()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "merged-two-gcn.pdf");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            // File PDF gộp 2 GCN riêng biệt (lỗi ghép khi scan) khiến model trả về JSON array
            // thay vì 1 object như schema yêu cầu.
            var arrayResponse = "[" +
                CreateNewGcnJsonWithDeclaredParcelCount(1, ["10"]) + "," +
                CreateNewGcnJsonWithDeclaredParcelCount(1, ["11"]) + "]";
            var ai = new FakeAiModelClient(arrayResponse);
            var service = new NewGcnExtractService(
                new FakePdfRenderer(),
                ai,
                new NewGcnOptions
                {
                    OptimizeImages = false,
                    TempDir = Path.Combine(root, "newgcn-temp")
                });

            Exception? caught = null;
            try
            {
                await service.ProcessFileAsync(sourcePdf, logCallback: null);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            AssertTrue(caught is not null, "OCR GCN New must throw when the model returns a multi-GCN JSON array");
            AssertTrue(
                caught!.Message.Contains("Tách GCN", StringComparison.OrdinalIgnoreCase),
                "Error message must guide the user to the Tách GCN (Split) feature: " + caught.Message);
            AssertTrue(
                caught.Message.Contains('2'),
                "Error message must mention how many GCN certificates were detected: " + caught.Message);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task NewGcnRetriesMismatchedCachedParcelCount()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "cached-multi-parcel.pdf");
            var tempDir = Path.Combine(root, "newgcn-temp");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            var cache = new NewGcnRunCacheService(tempDir);
            var cachePath = cache.GetResponseCachePath(sourcePdf);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(cachePath, CreateNewGcnJsonWithDeclaredParcelCount(3, ["10", "11"]));

            var ai = new FakeAiModelClient(CreateNewGcnJsonWithDeclaredParcelCount(3, ["10", "11", "12"]));
            var service = new NewGcnExtractService(
                new FakePdfRenderer(),
                ai,
                new NewGcnOptions
                {
                    OptimizeImages = false,
                    TempDir = tempDir
                });

            var envelope = await service.ProcessFileAsync(sourcePdf, logCallback: null);

            AssertEqual(1, ai.Calls.Count, "OCR GCN New must re-check mismatched cached parcel count");
            AssertEqual(3, envelope?.danh_sach_dong.Count, "Rows after cached parcel-count retry");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task NewGcnRefreshesCachedEnvelopeWithoutParcelCount()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "legacy-cache.pdf");
            var tempDir = Path.Combine(root, "newgcn-temp");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            var cache = new NewGcnRunCacheService(tempDir);
            var cachePath = cache.GetResponseCachePath(sourcePdf);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var legacyEnvelope = new NewGcnEnvelope
            {
                thong_tin_gcn = new NewGcnInfo { so_serial = "BD 000002", loai_quan_he = "ca_nhan" },
                danh_sach_dong = [new NewGcnRow { td_so_thua = "10" }]
            };
            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(legacyEnvelope));

            var ai = new FakeAiModelClient(CreateNewGcnJsonWithDeclaredParcelCount(2, ["10", "11"]));
            var service = new NewGcnExtractService(
                new FakePdfRenderer(),
                ai,
                new NewGcnOptions
                {
                    OptimizeImages = false,
                    TempDir = tempDir
                });

            var envelope = await service.ProcessFileAsync(sourcePdf, logCallback: null);

            AssertEqual(1, ai.Calls.Count, "OCR GCN New must refresh legacy cache without declared parcel count");
            AssertEqual(2, envelope?.danh_sach_dong.Count, "Rows after legacy cache refresh");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task NewGcnUsesPreUploadedFileRefsWithoutCallingUpload()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Nhánh PDF chỉ đọc byte, không parse PDF — nên byte giả là đủ (giống các test NewGcn hiện có).
            var pdfPath = Path.Combine(root, "AA 00101801.pdf");
            await File.WriteAllBytesAsync(pdfPath, [1, 2, 3]);

            var files = new FakeGeminiFileApiService();
            var ai = new FakeAiModelClient("""
                { "thong_tin_gcn": { "so_serial": "AA 00101801" }, "danh_sach_dong": [] }
                """);
            var opt = new NewGcnOptions { TempDir = root };
            var service = new NewGcnExtractService(
                new PdfRenderer(), ai, files, opt, new NewGcnRunCacheService(root));

            var preUploaded = new[] { new GeminiFileReference("files/x", "https://fake/x", "application/pdf") };
            var envelope = await service.ProcessFileAsync(
                pdfPath, null, CancellationToken.None, Path.Combine(root, "cache.json"), false, preUploaded);

            AssertTrue(envelope is not null, "Phai doc duoc envelope.");
            AssertEqual(0, files.UploadCalls.Count,
                "Da co san file_uri thi khong duoc goi GetOrUploadAsync lan nao");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task NewGcnHydratesMissingSerialFromSourceFileName()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "192-81_AA 06654954.pdf");
            var tempDir = Path.Combine(root, "newgcn-temp");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            var ai = new FakeAiModelClient(CreateNewGcnJsonWithNullSerial(1, ["81"]));
            var service = new NewGcnExtractService(
                new FakePdfRenderer(),
                ai,
                new NewGcnOptions
                {
                    OptimizeImages = false,
                    TempDir = tempDir
                });

            var envelope = await service.ProcessFileAsync(sourcePdf, logCallback: null);

            AssertEqual("AA 06654954", envelope?.thong_tin_gcn.so_serial, "OCR GCN New QR serial hydrated from source file name");
            AssertTrue((envelope?.thong_tin_gcn.canh_bao ?? []).Any(w => w.Contains("so_serial", StringComparison.Ordinal)
                && w.Contains("tên file", StringComparison.Ordinal)) == true,
                "Hydrated serial must add warning.");

            var cache = new NewGcnRunCacheService(tempDir);
            var cachedEnvelope = JsonSerializer.Deserialize<NewGcnEnvelope>(
                await File.ReadAllTextAsync(cache.GetResponseCachePath(sourcePdf)),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            AssertEqual("AA 06654954", cachedEnvelope?.thong_tin_gcn.so_serial, "Hydrated QR serial must be persisted to cache");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task NewGcnHydratesMissingSerialFromFreshCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "1-1_BX 037034.pdf");
            var tempDir = Path.Combine(root, "newgcn-temp");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            var cache = new NewGcnRunCacheService(tempDir);
            var cachePath = cache.GetResponseCachePath(sourcePdf);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(cachePath, CreateNewGcnJsonWithNullSerial(1, ["01"]));
            File.SetLastWriteTimeUtc(cachePath, File.GetLastWriteTimeUtc(sourcePdf).AddMinutes(1));

            var ai = new FakeAiModelClient(CreateNewGcnJsonWithDeclaredParcelCount(1, ["fresh"]));
            var service = new NewGcnExtractService(
                new FakePdfRenderer(),
                ai,
                new NewGcnOptions
                {
                    OptimizeImages = false,
                    TempDir = tempDir
                });

            var envelope = await service.ProcessFileAsync(sourcePdf, logCallback: null, useCachedJson: true);

            AssertEqual(0, ai.Calls.Count, "OCR GCN New can use fresh cache after hydrating missing serial");
            AssertEqual("BX 037034", envelope?.thong_tin_gcn.so_serial, "OCR GCN New cached serial hydrated from source file name");
            AssertTrue((envelope?.thong_tin_gcn.canh_bao ?? []).Any(w => w.Contains("so_serial", StringComparison.Ordinal)
                && w.Contains("cache", StringComparison.Ordinal)) == true,
                "Cached hydrated serial must add cache warning.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task NewGcnIgnoresJsonCacheOlderThanSourceFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "newer-source.pdf");
            var tempDir = Path.Combine(root, "newgcn-temp");
            await File.WriteAllBytesAsync(sourcePdf, [1, 2, 3]);

            var cache = new NewGcnRunCacheService(tempDir);
            var cachePath = cache.GetResponseCachePath(sourcePdf);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(cachePath, CreateNewGcnJsonWithDeclaredParcelCount(1, ["old"]));
            var cacheTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(cachePath, cacheTime);
            File.SetLastWriteTimeUtc(sourcePdf, cacheTime.AddMinutes(1));

            var ai = new FakeAiModelClient(CreateNewGcnJsonWithDeclaredParcelCount(1, ["fresh"]));
            var service = new NewGcnExtractService(
                new FakePdfRenderer(),
                ai,
                new NewGcnOptions
                {
                    OptimizeImages = false,
                    TempDir = tempDir
                });

            var envelope = await service.ProcessFileAsync(sourcePdf, logCallback: null, useCachedJson: true);

            AssertEqual(1, ai.Calls.Count, "OCR GCN New must ignore JSON cache older than source file.");
            AssertEqual("fresh", envelope?.danh_sach_dong.Single().td_so_thua, "Fresh AI result after stale OCR GCN New cache.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SplitServiceIgnoresJsonCacheOlderThanSourceFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "newer-source.pdf");
            CreatePdf(sourcePdf, 1);
            var jsonPath = Path.Combine(root, "cache", "newer-source.json");
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
            await File.WriteAllTextAsync(jsonPath, """{"pages":[],"documents":[]}""");
            var cacheTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(jsonPath, cacheTime);
            File.SetLastWriteTimeUtc(sourcePdf, cacheTime.AddMinutes(1));

            var ai = new FakeAiModelClient("""{"pages":[],"documents":[]}""");
            var service = new SplitGcnService(ai, new FakePdfRotationNormalizer(), new SplitGcnOptions { MaxRetries = 0 });

            await service.SplitAsync(
                sourcePdf,
                new LabelAllocator(Path.Combine(root, "out")),
                jsonPath: jsonPath,
                useCachedJson: true);

            AssertEqual(1, ai.Calls.Count, "Split service must ignore JSON cache older than source PDF.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateNewGcnJsonWithDeclaredParcelCount(int declaredCount, IReadOnlyList<string> parcelNumbers)
    {
        var envelope = new NewGcnEnvelope
        {
            thong_tin_gcn = new NewGcnInfo
            {
                so_serial = "BD 000001",
                loai_quan_he = "ca_nhan",
                so_luong_thua_dat_doc_duoc = declaredCount,
                chu_su_dung_chi_tiet = [new NewGcnOwner { ho_ten = "Nguyen Van A" }],
                canh_bao = []
            },
            danh_sach_dong = parcelNumbers
                .Select(parcelNumber => new NewGcnRow
                {
                    td_so_thua = parcelNumber,
                    td_so_to = "1",
                    td_tong_dien_tich = "100"
                })
                .ToList()
        };

        return JsonSerializer.Serialize(envelope);
    }

    private static string CreateNewGcnJsonWithNullSerial(int declaredCount, IReadOnlyList<string> parcelNumbers)
    {
        var envelope = JsonSerializer.Deserialize<NewGcnEnvelope>(
            CreateNewGcnJsonWithDeclaredParcelCount(declaredCount, parcelNumbers))!;
        envelope.thong_tin_gcn.so_serial = null;
        return JsonSerializer.Serialize(envelope);
    }

    private static void NewGcnCacheStoresResponseUnderLocalRootAndSeparatesSameNamedSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        var firstSource = Path.Combine(root, "A", "Ho so");
        var secondSource = Path.Combine(root, "B", "Ho so");
        Directory.CreateDirectory(firstSource);
        Directory.CreateDirectory(secondSource);

        try
        {
            var firstPdf = Path.Combine(firstSource, "BR 123456.pdf");
            var secondPdf = Path.Combine(secondSource, "BR 123456.pdf");
            var cacheRoot = Path.Combine(root, "newgcn-temp");
            var service = new NewGcnRunCacheService(cacheRoot);

            var firstCache = service.GetResponseCachePath(firstPdf);
            var secondCache = service.GetResponseCachePath(secondPdf);

            AssertTrue(firstCache.StartsWith(cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "OCR GCN New cache must stay under configured temp root.");
            AssertTrue(firstCache.EndsWith(Path.Combine("response_new", "BR 123456.json"), StringComparison.OrdinalIgnoreCase),
                "OCR GCN New cache must keep response_new JSON file names.");
            AssertFalse(string.Equals(firstCache, secondCache, StringComparison.OrdinalIgnoreCase),
                "Same-named source folders and files need different OCR GCN New cache paths.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SplitNewReportsFoldersWithoutAllThreeFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "source.pdf");
            var outputDir = Path.Combine(root, "output");
            CreatePdf(sourcePdf, 5);

            var ai = new FakeAiModelClient("""
                [
                  {
                    "name": "AA 111111",
                    "serial_GCN.pdf": { "from": 1, "to": 1 },
                    "serial_GT.pdf": { "from": 2, "to": 2 },
                    "serial_GTK.pdf": { "from": 3, "to": 3 }
                  },
                  {
                    "name": "BB 222222",
                    "serial_GCN.pdf": { "from": 4, "to": 4 },
                    "serial_GT.pdf": { "from": 5, "to": 5 },
                    "serial_GTK.pdf": null
                  }
                ]
                """);
            var service = new SplitGcnService(
                ai,
                new FakePdfRotationNormalizer(),
                new SplitGcnOptions { MaxRetries = 0, ReasoningEffort = "medium" });

            var result = await service.SplitAsync(
                sourcePdf,
                new LabelAllocator(outputDir),
                variant: SplitGcnVariant.New);

            AssertEqual(1, result.MissingFileFolders.Count, "Missing folder count");
            AssertEqual("BB 222222", result.MissingFileFolders[0], "Missing folder name");
            AssertEqual(1, result.CompleteSetCount, "New complete set count");

            var completeFolder = Path.Combine(outputDir, "AA 111111");
            AssertTrue(File.Exists(Path.Combine(completeFolder, "AA 111111-GCN.pdf")), "Expected complete GCN output.");
            AssertTrue(File.Exists(Path.Combine(completeFolder, "AA 111111-GT.pdf")), "Expected complete GT output.");
            AssertTrue(File.Exists(Path.Combine(completeFolder, "AA 111111-GTK.pdf")), "Expected complete GTK output.");

            var missingFolder = Path.Combine(outputDir, "BB 222222");
            AssertTrue(File.Exists(Path.Combine(missingFolder, "BB 222222-GCN.pdf")), "Expected incomplete GCN output.");
            AssertTrue(File.Exists(Path.Combine(missingFolder, "BB 222222-GT.pdf")), "Expected incomplete GT output.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SplitStandardUsesHyphenOutputNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "source.pdf");
            var outputDir = Path.Combine(root, "output");
            CreatePdf(sourcePdf, 3);

            var ai = new FakeAiModelClient("""
                {
                  "pages": [],
                  "documents": [
                    {
                      "name": "CS123",
                      "parcel_count": 2,
                      "serial_GCN.pdf": { "from": 1, "to": 1 },
                      "serial_GT.pdf": { "from": 2, "to": 2 },
                      "serial_GTK.pdf": { "from": 3, "to": 3 }
                    }
                  ]
                }
                """);
            var service = new SplitGcnService(
                ai,
                new FakePdfRotationNormalizer(),
                new SplitGcnOptions { MaxRetries = 0, ReasoningEffort = "medium" });

            var result = await service.SplitAsync(
                sourcePdf,
                new LabelAllocator(outputDir),
                variant: SplitGcnVariant.Standard);

            AssertEqual(2, result.CompleteSetCount, "Standard complete set count for one source");

            AssertTrue(
                File.Exists(Path.Combine(outputDir, "CS123-1", "CS123-1-GCN.pdf")),
                "Expected first parcel GCN output.");
            AssertTrue(
                File.Exists(Path.Combine(outputDir, "CS123-2", "CS123-2-GCN.pdf")),
                "Expected second parcel GCN output.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void SplitRunStateEnablesExportAfterMixedResultsComplete()
    {
        var state = new SplitRunState(3);

        state.MarkFileCompleted(hasOutput: true);
        AssertNear(100d / 3, state.ProgressValue, "Progress after first file");
        state.MarkFileCompleted(hasOutput: false);
        AssertNear(200d / 3, state.ProgressValue, "Progress after second file");
        state.MarkFileCompleted(hasOutput: false);
        AssertNear(100, state.ProgressValue, "Progress after all files");
        AssertFalse(state.CanExport, "Export must remain disabled until the run is finalized.");

        state.MarkRunCompleted(canceled: false);

        AssertTrue(state.CanExport, "Export must be enabled for a completed run with output.");
    }

    private static void SplitRunStateKeepsExportDisabledForAllErrorsOrCanceledRun()
    {
        var allErrors = new SplitRunState(2);
        allErrors.MarkFileCompleted(hasOutput: false);
        allErrors.MarkFileCompleted(hasOutput: false);
        allErrors.MarkRunCompleted(canceled: false);
        AssertFalse(allErrors.CanExport, "All-error run must not enable Export.");

        var canceled = new SplitRunState(3);
        canceled.MarkFileCompleted(hasOutput: true);
        canceled.MarkRunCompleted(canceled: true);
        AssertNear(100d / 3, canceled.ProgressValue, "Canceled run progress");
        AssertFalse(canceled.CanExport, "Canceled run must not enable Export.");
    }

    private static void FolderFileEnumeratorFindsPdfRecursively()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        var level1 = Path.Combine(root, "level-1");
        var level2 = Path.Combine(level1, "level-2");
        Directory.CreateDirectory(level2);

        try
        {
            var rootPdf = Path.Combine(root, "root.pdf");
            var nestedPdf = Path.Combine(level2, "nested.PDF");
            File.WriteAllText(rootPdf, "root");
            File.WriteAllText(nestedPdf, "nested");
            File.WriteAllText(Path.Combine(level1, "ignore.txt"), "ignore");

            var recursive = FolderFileEnumerator.Enumerate(root, new[] { ".pdf" }, recursive: true);
            var topOnly = FolderFileEnumerator.Enumerate(root, new[] { ".pdf" }, recursive: false);

            AssertEqual(2, recursive.Count, "Recursive PDF count");
            AssertTrue(recursive.Contains(rootPdf, StringComparer.OrdinalIgnoreCase), "Expected root PDF.");
            AssertTrue(recursive.Contains(nestedPdf, StringComparer.OrdinalIgnoreCase), "Expected nested uppercase PDF.");
            AssertEqual(recursive.Count, recursive.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "PDF paths must be unique");
            AssertEqual(1, topOnly.Count, "Top-level PDF count");
            AssertTrue(topOnly.Contains(rootPdf, StringComparer.OrdinalIgnoreCase), "Top-level scan must contain root PDF.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SplitNoGcnCreatesOnlyGtAndGtk()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        var tempDir = Path.Combine(root, "split-temp");
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "source.pdf");
            CreatePdf(sourcePdf, 4);

            var ai = new FakeAiModelClient("""
            {
              "pages": [
                { "page": 1, "type": "DON", "sheet_number": "12", "parcel_number": "34" },
                { "page": 2, "type": "BBXD", "sheet_number": "", "parcel_number": "" },
                { "page": 3, "type": "PLYK", "sheet_number": "", "parcel_number": "" },
                { "page": 4, "type": "KHAC", "sheet_number": "", "parcel_number": "" }
              ],
              "documents": [
                {
                  "sheet_number": "12",
                  "parcel_number": "34",
                  "serial_GT.pdf": { "from": 1, "to": 2 },
                  "serial_GTK.pdf": { "from": 3, "to": 4 }
                }
              ]
            }
            """);

            var service = new SplitGcnService(
                ai,
                new FakePdfRotationNormalizer(),
                new SplitGcnOptions { TempDir = tempDir, MaxRetries = 0 });

            var result = await service.SplitAsync(
                sourcePdf,
                new LabelAllocator(tempDir),
                normalizePageRotation: false,
                variant: SplitGcnVariant.NoGcn);

            AssertEqual(1, result.GcnCount, "NoGcn document count");
            AssertEqual(2, result.FilesCreated, "NoGcn file count");
            AssertEqual(1, result.CompleteSetCount, "NoGcn complete set count");

            var outputDir = Path.Combine(tempDir, "12-34");
            AssertTrue(Directory.Exists(outputDir), "Expected output folder 12-34.");
            AssertTrue(File.Exists(Path.Combine(outputDir, "12-34-GT.pdf")), "Expected GT output.");
            AssertTrue(File.Exists(Path.Combine(outputDir, "12-34-GTK.pdf")), "Expected GTK output.");
            AssertFalse(File.Exists(Path.Combine(outputDir, "12-34-GCN.pdf")), "NoGcn must not create GCN output.");

            AssertEqual(2, CountPages(Path.Combine(outputDir, "12-34-GT.pdf")), "GT page count");
            AssertEqual(2, CountPages(Path.Combine(outputDir, "12-34-GTK.pdf")), "GTK page count");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void AiJsonTextTrimsExtraClosingBraces()
    {
        AssertEqual("""{"a":1}""", AiJsonText.ExtractBalancedJson("""{"a":1}}"""), "Single extra closing brace trimmed");
        AssertEqual("""{"a":1}""", AiJsonText.ExtractBalancedJson("""{"a":1}}}"""), "Two extra closing braces trimmed");
        AssertEqual("""{"a":1}""", AiJsonText.ExtractBalancedJson("{\"a\":1}\n}\n"), "Extra brace on its own line trimmed");
        AssertEqual("""{"s":"}"}""", AiJsonText.ExtractBalancedJson("""{"s":"}"}}"""), "Brace inside string must not close object");
        AssertEqual("""{"a":1}""", AiJsonText.ExtractBalancedJson("﻿{\"a\":1}}"), "Leading BOM and extra brace trimmed");
        AssertEqual("""[{"a":1}]""", AiJsonText.ExtractBalancedJson("""[{"a":1}]]"""), "Top-level array extra bracket trimmed");
        AssertEqual("""{"a":1}""", AiJsonText.ExtractBalancedJson("```json\n{\"a\":1}\n```"), "Fenced JSON unwrapped");
        AssertFalse(AiJsonText.IsParsableJson("""{"a":"""), "Truncated JSON must be reported unparsable");
        AssertTrue(AiJsonText.IsParsableJson("""{"a":1}"""), "Valid JSON must be reported parsable");
    }

    private static void AiJsonTextQuotesInvalidLeadingZeroNumbers()
    {
        // Bug thực tế: model trả "ma_vach": 0719220000680 (số JSON không được phép có leading zero) ->
        // JSON hỏng cú pháp ngay từ bước parse dù ngoặc vẫn cân bằng.
        var malformed = """{"ma_vach":0719220000680,"so_thua":"49"}""";
        AssertFalse(AiJsonText.IsParsableJson(malformed), "Leading-zero number must be unparsable before fix");

        var fixed_ = AiJsonText.QuoteInvalidLeadingZeroNumbers(malformed);
        AssertTrue(AiJsonText.IsParsableJson(fixed_), "Leading-zero number must become parsable after quoting: " + fixed_);
        AssertEqual("""{"ma_vach":"0719220000680","so_thua":"49"}""", fixed_, "Leading-zero number quoted, value preserved");

        // Không đổi số hợp lệ (không leading zero) và không đụng nội dung nằm trong chuỗi.
        AssertEqual(
            """{"a":123,"b":"0719 ghi trong chuoi khong doi"}""",
            AiJsonText.QuoteInvalidLeadingZeroNumbers("""{"a":123,"b":"0719 ghi trong chuoi khong doi"}"""),
            "Valid number and in-string digits must stay untouched");

        // Số 0 đơn lẻ và số thập phân dạng 0.x vẫn hợp lệ, không bị đụng tới.
        AssertEqual(
            """{"a":0,"b":0.5}""",
            AiJsonText.QuoteInvalidLeadingZeroNumbers("""{"a":0,"b":0.5}"""),
            "Bare zero and 0.x decimals are valid JSON and must stay untouched");
    }

    private static void AiJsonTextMergesDigitStringsSplitByStrayQuote()
    {
        // Bug thực tế: model chèn nhầm 1 dấu '"' giữa 2 nửa dãy số dài (VD mã vạch), ra
        // "ma_vach": 71982"0000232" thay vì "719820000232" -> JSON hỏng cú pháp dù ngoặc cân bằng.
        var malformed = """{"ma_vach":71982"0000232","so_thua":"49"}""";
        AssertFalse(AiJsonText.IsParsableJson(malformed), "Digit string split by stray quote must be unparsable before fix");

        var merged = AiJsonText.MergeSplitDigitStrings(malformed);
        AssertEqual("""{"ma_vach":719820000232,"so_thua":"49"}""", merged, "Split digit halves merged back into one number token");
        AssertTrue(AiJsonText.IsParsableJson(merged), "Merged token starting with non-zero digit is already valid JSON on its own: " + merged);

        // Trường hợp thực tế đầy đủ: qua đúng pipeline SendAndExtractAsync dùng (merge rồi mới quote).
        var pipeline = AiJsonText.QuoteInvalidLeadingZeroNumbers(AiJsonText.MergeSplitDigitStrings(malformed));
        AssertTrue(AiJsonText.IsParsableJson(pipeline), "Full repair pipeline must produce parsable JSON: " + pipeline);
        AssertEqual("""{"ma_vach":719820000232,"so_thua":"49"}""", pipeline, "Merged non-leading-zero number must stay a bare number, untouched by quoting step");

        // Sau khi nối, nếu dãy số kết quả có leading zero thì vẫn phải được bọc ngoặc kép ở bước sau.
        var malformedLeadingZero = """{"ma_vach":0"0000232"}""";
        var pipelineLeadingZero = AiJsonText.QuoteInvalidLeadingZeroNumbers(AiJsonText.MergeSplitDigitStrings(malformedLeadingZero));
        AssertTrue(AiJsonText.IsParsableJson(pipelineLeadingZero), "Merged+leading-zero case must be parsable: " + pipelineLeadingZero);
        AssertEqual("""{"ma_vach":"00000232"}""", pipelineLeadingZero, "Merged digits with resulting leading zero get quoted");

        // Không đụng vào key/value hợp lệ liền kề nhau qua nhiều field số khác nhau.
        AssertEqual(
            """{"a":1,"b":2,"c":"x"}""",
            AiJsonText.MergeSplitDigitStrings("""{"a":1,"b":2,"c":"x"}"""),
            "Unrelated adjacent numeric fields must stay untouched");
    }

    private static async Task SplitServiceRecoversFromExtraClosingBraceAndWritesCleanCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pdf = Path.Combine(root, "source.pdf");
            var json = Path.Combine(root, "json", "source.json");
            CreatePdf(pdf, 1);

            // Model trả JSON hợp lệ NHƯNG dư một dấu '}' ở đuôi (đúng lỗi thực tế đã gặp).
            var malformed =
                "{\"pages\":[{\"page\":1,\"type\":\"GCN\",\"serial\":\"BR 097368\"}]," +
                "\"documents\":[{\"name\":\"BR 097368\",\"serial_GCN.pdf\":{\"from\":1,\"to\":1}," +
                "\"serial_GT.pdf\":null,\"serial_GTK.pdf\":null}]}\n}";
            var ai = new FakeAiModelClient(malformed);
            var service = new SplitGcnService(ai, new FakePdfRotationNormalizer(), new SplitGcnOptions { MaxRetries = 0 });

            var result = await service.SplitAsync(
                pdf, new LabelAllocator(Path.Combine(root, "out")),
                variant: SplitGcnVariant.New, jsonPath: json);

            AssertEqual(1, result.GcnCount, "Extra-brace response must still yield one document.");
            AssertTrue(result.FilesCreated >= 1, "Extra-brace response must still cut at least the GCN file.");
            AssertTrue(File.Exists(json), "Cache JSON must be written.");
            AssertTrue(AiJsonText.IsParsableJson(await File.ReadAllTextAsync(json)),
                "Written cache JSON must be valid (no leftover extra brace).");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AllSplitVariantsSendResponseSchema()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pdf = Path.Combine(root, "source.pdf");
            CreatePdf(pdf, 1);
            var ai = new FakeAiModelClient(
                """{"pages":[],"documents":[]}""",
                """{"pages":[],"documents":[]}""",
                """{"pages":[],"documents":[]}""");
            var service = new SplitGcnService(ai, new FakePdfRotationNormalizer(), new SplitGcnOptions { MaxRetries = 0 });

            var variants = new[] { SplitGcnVariant.Standard, SplitGcnVariant.New, SplitGcnVariant.NoGcn };
            foreach (var variant in variants)
                await service.SplitAsync(pdf, new LabelAllocator(Path.Combine(root, variant.ToString())), variant: variant);

            AssertEqual(3, ai.Calls.Count, "Three split calls expected.");
            for (int i = 0; i < variants.Length; i++)
                AssertTrue(ai.Calls[i].ResponseSchema is not null,
                    $"Split screen {variants[i]} must send a response schema (Layer 2).");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AiModelClientBalancesExtraBraceAndRetriesInvalidJson()
    {
        var content = new object[] { new Dictionary<string, object> { ["type"] = "text", ["text"] = "hi" } };

        // 1. Dư dấu '}' → cân bằng, không cần gọi lại.
        var balanceHandler = new QueuedHttpMessageHandler(
            (HttpStatusCode.OK, GeminiBody("""{"pages":[],"documents":[]}}""")));
        var balanced = await NewGoogleClient(balanceHandler).GenerateJsonAsync(content, maxAttempts: 2);
        AssertEqual("""{"pages":[],"documents":[]}""", balanced, "Extra brace must be balanced by the client.");
        AssertEqual(1, balanceHandler.RequestBodies.Count, "Balancing must not need a retry.");

        // 2. JSON cắt cụt ở lần đầu → gọi lại và trả bản hợp lệ.
        var retryHandler = new QueuedHttpMessageHandler(
            (HttpStatusCode.OK, GeminiBody("""{"a":""")),
            (HttpStatusCode.OK, GeminiBody("""{"a":1}""")));
        var retried = await NewGoogleClient(retryHandler).GenerateJsonAsync(content, maxAttempts: 2);
        AssertEqual("""{"a":1}""", retried, "Client must retry unparsable JSON and return the valid response.");
        AssertEqual(2, retryHandler.RequestBodies.Count, "Unparsable JSON must trigger exactly one retry.");
    }

    private static async Task AiModelClientRetriesWithoutSchemaWhenProviderRejectsIt()
    {
        var content = new object[] { new Dictionary<string, object> { ["type"] = "text", ["text"] = "hi" } };
        var handler = new QueuedHttpMessageHandler(
            (HttpStatusCode.BadRequest, "schema rejected"),
            (HttpStatusCode.OK, GeminiBody("""{"ok":1}""")));

        var result = await NewGoogleClient(handler).GenerateJsonAsync(
            content, maxAttempts: 2, responseSchema: new Dictionary<string, object> { ["type"] = "OBJECT" });

        AssertEqual("""{"ok":1}""", result, "Client must recover by retrying without the rejected schema.");
        AssertEqual(2, handler.RequestBodies.Count, "Schema rejection must trigger one no-schema retry.");
        AssertTrue(handler.RequestBodies[0].Contains("responseSchema", StringComparison.Ordinal),
            "First attempt must include the response schema.");
        AssertFalse(handler.RequestBodies[1].Contains("responseSchema", StringComparison.Ordinal),
            "Fallback attempt must drop the response schema.");
    }

    private static async Task AiModelClientConcatenatesGeminiTextPartsSkippingThought()
    {
        var content = new object[] { new Dictionary<string, object> { ["type"] = "text", ["text"] = "hi" } };
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["candidates"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["content"] = new Dictionary<string, object>
                    {
                        ["parts"] = new object[]
                        {
                            new Dictionary<string, object> { ["text"] = "suy nghi noi bo", ["thought"] = true },
                            new Dictionary<string, object> { ["text"] = "{\"a\":1," },
                            new Dictionary<string, object> { ["text"] = "\"b\":2}" }
                        }
                    }
                }
            }
        });
        var handler = new QueuedHttpMessageHandler((HttpStatusCode.OK, body));

        var result = await NewGoogleClient(handler).GenerateJsonAsync(content, maxAttempts: 2);

        AssertEqual("""{"a":1,"b":2}""", result, "Client must join all Gemini text parts and skip thought parts.");
        AssertEqual(1, handler.RequestBodies.Count, "Joined valid JSON must not need a retry.");
    }

    private static async Task AiModelClientReportsFinishReasonWhenNoText()
    {
        var content = new object[] { new Dictionary<string, object> { ["type"] = "text", ["text"] = "hi" } };
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["candidates"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["finishReason"] = "MAX_TOKENS",
                    ["content"] = new Dictionary<string, object> { ["parts"] = Array.Empty<object>() }
                }
            }
        });
        var handler = new QueuedHttpMessageHandler((HttpStatusCode.OK, body));

        string message = "";
        try
        {
            await NewGoogleClient(handler).GenerateJsonAsync(content, maxAttempts: 3);
        }
        catch (Exception ex)
        {
            message = ex.Message;
        }

        AssertTrue(message.Contains("MAX_TOKENS", StringComparison.Ordinal),
            "Empty Gemini content must surface finishReason (e.g. MAX_TOKENS) for diagnosis.");
        AssertEqual(1, handler.RequestBodies.Count, "Empty-content finishReason error must fail fast without retrying.");
    }

    private static AiModelClient NewGoogleClient(HttpMessageHandler handler)
        => new(
            new AiProviderOptions
            {
                Provider = "google-ai-studio",
                Url = "https://example.invalid",
                ApiKey = "test-key",
                Model = "gemini-2.5-pro"
            },
            handler);

    private static string GeminiBody(string modelText)
        => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["candidates"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["content"] = new Dictionary<string, object>
                    {
                        ["parts"] = new object[] { new Dictionary<string, object> { ["text"] = modelText } }
                    }
                }
            }
        });

    private static void CreatePdf(string path, int pages)
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < pages; i++)
            doc.AddPage();
        doc.Save(path);
    }

    private static int CountPages(string path)
    {
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }

    private static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
    }

    private static async Task SplitNoGcnFallbackNormalizesLegacyUnderscoreName()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-winapp-tests", Guid.NewGuid().ToString("N"));
        var outputDir = Path.Combine(root, "output");
        Directory.CreateDirectory(root);

        try
        {
            var sourcePdf = Path.Combine(root, "source.pdf");
            CreatePdf(sourcePdf, 1);

            var ai = new FakeAiModelClient("""
                {
                  "pages": [],
                  "documents": [
                    {
                      "name": "12_34",
                      "serial_GT.pdf": { "from": 1, "to": 1 },
                      "serial_GTK.pdf": null
                    }
                  ]
                }
                """);
            var service = new SplitGcnService(
                ai,
                new FakePdfRotationNormalizer(),
                new SplitGcnOptions { MaxRetries = 0 });

            await service.SplitAsync(
                sourcePdf,
                new LabelAllocator(outputDir),
                variant: SplitGcnVariant.NoGcn);

            var normalizedFolder = Path.Combine(outputDir, "12-34");
            AssertTrue(Directory.Exists(normalizedFolder), "Expected fallback output folder 12-34.");
            AssertTrue(File.Exists(Path.Combine(normalizedFolder, "12-34-GT.pdf")), "Expected fallback GT output.");
            AssertFalse(Directory.Exists(Path.Combine(outputDir, "12_34")), "Legacy underscore folder must not be created.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertNear(double expected, double actual, string name)
    {
        if (Math.Abs(expected - actual) > 0.01)
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
    }

    private static async Task GeminiFilesApiUploadsPollsReusesAndDeletes()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-gemini-files-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "sample.pdf");
        var cachePath = Path.Combine(root, "gemini-files.json");
        var bytes = System.Text.Encoding.UTF8.GetBytes("fake-pdf-content");
        await File.WriteAllBytesAsync(sourcePath, bytes);

        try
        {
            var handler = new GeminiFilesHttpHandler();
            var service = new GeminiFileApiService(
                new AiProviderOptions
                {
                    Provider = "google-ai-studio",
                    Url = "https://generativelanguage.googleapis.com/v1beta",
                    ApiKey = "test-key",
                    Model = "gemini-test"
                },
                handler,
                cachePath,
                TimeSpan.Zero);

            await service.PrepareExecutionAsync();
            var first = await service.GetOrUploadAsync(
                sourcePath, "source-pdf", "sample.pdf", "application/pdf", bytes);
            AssertEqual("files/test-file", first.Name, "Gemini file name");
            AssertEqual("https://generativelanguage.googleapis.com/v1beta/files/test-file", first.Uri, "Gemini file URI");
            AssertEqual(1, handler.UploadStartCount, "Upload start count");
            AssertEqual(1, handler.UploadContentCount, "Upload content count");
            AssertTrue(File.Exists(cachePath), "Gemini file cache must be written after upload.");

            await service.PrepareExecutionAsync();
            var second = await service.GetOrUploadAsync(
                sourcePath, "source-pdf", "sample.pdf", "application/pdf", bytes);
            AssertEqual(first.Uri, second.Uri, "Reused Gemini file URI");
            AssertEqual(1, handler.UploadStartCount, "Cached file must not upload again");

            await service.DeleteBySourcePathsAsync(new[] { sourcePath });
            AssertEqual(1, handler.DeleteCount, "Gemini delete count");
            var cacheJson = await File.ReadAllTextAsync(cachePath);
            using var cacheDocument = JsonDocument.Parse(cacheJson);
            AssertEqual(0, cacheDocument.RootElement.GetProperty("Files").GetArrayLength(), "Gemini cache count after delete");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AiModelClientUsesGeminiFileUriPart()
    {
        var handler = new QueuedHttpMessageHandler(
            (HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"{\"ok\":true}"}]}}]}"""));
        var client = new AiModelClient(
            new AiProviderOptions
            {
                Provider = "google-ai-studio",
                Url = "https://generativelanguage.googleapis.com/v1beta",
                ApiKey = "test-key",
                Model = "gemini-test"
            },
            handler);
        var content = new object[]
        {
            new Dictionary<string, object> { ["type"] = "text", ["text"] = "prompt" },
            new Dictionary<string, object>
            {
                ["type"] = "file_uri",
                ["file_uri"] = new Dictionary<string, string>
                {
                    ["mime_type"] = "application/pdf",
                    ["file_uri"] = "https://generativelanguage.googleapis.com/v1beta/files/test-file"
                }
            }
        };

        var result = await client.GenerateJsonAsync(content, maxAttempts: 1);
        AssertEqual("""{"ok":true}""", result, "Gemini JSON result");
        AssertTrue(handler.RequestBodies[0].Contains("\"file_data\"", StringComparison.Ordinal), "Gemini request must use file_data.");
        AssertTrue(handler.RequestBodies[0].Contains("\"file_uri\"", StringComparison.Ordinal), "Gemini request must include file_uri.");
        AssertFalse(handler.RequestBodies[0].Contains("\"inline_data\"", StringComparison.Ordinal), "Gemini request must not use inline_data.");
    }

    private static async Task GeminiFilesApiKeepsUploadedUriWhenPollingFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-gemini-files-error-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "sample.pdf");
        var cachePath = Path.Combine(root, "gemini-files.json");
        var bytes = System.Text.Encoding.UTF8.GetBytes("fake-pdf-content");
        await File.WriteAllBytesAsync(sourcePath, bytes);

        try
        {
            var handler = new GeminiFilesHttpHandler { FailGet = true };
            var service = new GeminiFileApiService(
                new AiProviderOptions
                {
                    Provider = "google-ai-studio",
                    Url = "https://generativelanguage.googleapis.com/v1beta",
                    ApiKey = "test-key",
                    Model = "gemini-test"
                },
                handler,
                cachePath,
                TimeSpan.Zero);

            await service.PrepareExecutionAsync();
            try
            {
                await service.GetOrUploadAsync(
                    sourcePath, "source-pdf", "sample.pdf", "application/pdf", bytes);
                throw new InvalidOperationException("Polling failure must propagate.");
            }
            catch (Exception ex) when (ex.Message.Contains("kiểm tra file lỗi", StringComparison.Ordinal))
            {
            }

            var cacheJson = await File.ReadAllTextAsync(cachePath);
            using var cacheDocument = JsonDocument.Parse(cacheJson);
            var files = cacheDocument.RootElement.GetProperty("Files");
            AssertEqual(1, files.GetArrayLength(), "Gemini cache count after polling failure");
            AssertEqual("files/test-file", files[0].GetProperty("Name").GetString(), "Cached Gemini name after polling failure");
            AssertEqual("PROCESSING", files[0].GetProperty("State").GetString(), "Cached Gemini state after polling failure");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition) throw new InvalidOperationException(message);
    }
}

sealed class RecordingHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
{
    public List<string> RequestBodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
        return new HttpResponseMessage(statusCode) { Content = new StringContent("{\"ok\":true}") };
    }
}

sealed class ThrowingHttpMessageHandler(string recipient) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new HttpRequestException($"Request failed at {request.RequestUri}; recipient={recipient}");
}

sealed class QueuedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses;

    public QueuedHttpMessageHandler(params (HttpStatusCode Status, string Body)[] responses)
        => _responses = new Queue<(HttpStatusCode, string)>(responses);

    public List<string> RequestBodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
        var (status, body) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.OK, "{}");
        return new HttpResponseMessage(status) { Content = new StringContent(body) };
    }
}

sealed class GeminiFilesHttpHandler : HttpMessageHandler
{
    public bool FailGet { get; init; }
    public int UploadStartCount { get; private set; }
    public int UploadContentCount { get; private set; }
    public int GetCount { get; private set; }
    public int DeleteCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri?.ToString() ?? "";
        if (request.Method == HttpMethod.Post && uri.EndsWith("/upload/v1beta/files", StringComparison.Ordinal))
        {
            UploadStartCount++;
            var response = JsonResponse("{}");
            response.Headers.TryAddWithoutValidation("X-Goog-Upload-URL", "https://upload.example/session");
            return Task.FromResult(response);
        }

        if (request.Method == HttpMethod.Post && uri == "https://upload.example/session")
        {
            UploadContentCount++;
            return Task.FromResult(JsonResponse(FileJson("PROCESSING")));
        }

        if (request.Method == HttpMethod.Get && uri.EndsWith("/v1beta/files/test-file", StringComparison.Ordinal))
        {
            GetCount++;
            if (FailGet)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"error\":\"temporary\"}")
                });
            }
            return Task.FromResult(JsonResponse(FileJson("ACTIVE")));
        }

        if (request.Method == HttpMethod.Delete && uri.EndsWith("/v1beta/files/test-file", StringComparison.Ordinal))
        {
            DeleteCount++;
            return Task.FromResult(JsonResponse("{}"));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{}")
        });
    }

    private static HttpResponseMessage JsonResponse(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static string FileJson(string state)
        => $$"""
             {
               "file": {
                 "name": "files/test-file",
                 "uri": "https://generativelanguage.googleapis.com/v1beta/files/test-file",
                 "mimeType": "application/pdf",
                 "state": "{{state}}"
               }
             }
             """;
}

sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(formatter(state, exception));
        if (exception is not null)
            Entries.Add(exception.ToString());
    }
}

sealed class FakeAuthService : IAuthService
{
    public bool IsAuthenticated => true;
    public string? CurrentUser => "fallback-user";
    public AuthSession? CurrentSession { get; } = new()
    {
        UserName = "nguyenvana",
        FullName = "Nguyễn Văn A"
    };

    /// <summary>Ghi lại từng lần gọi RecordOcrCreditAsync — dùng để kiểm chứng kịch bản quota
    /// (Dừng giữa chừng không được trừ, chạy xong trừ đúng 1 lần theo đúng soTrang).</summary>
    public List<(string TenFile, string DuongDanFile, int SoTrang)> RecordOcrCreditCalls { get; } = new();

    public Task<Result> LoginAsync(string username, string password, CancellationToken ct = default)
        => Task.FromResult(Result.Success());

    public Task<Result> RecordOcrCreditAsync(
        string tenFile, string duongDanFile, int soTrang, CancellationToken ct = default)
    {
        RecordOcrCreditCalls.Add((tenFile, duongDanFile, soTrang));
        return Task.FromResult(Result.Success());
    }

    public void Logout() { }
}

sealed class FakeGeminiFileApiService : IGeminiFileApiService
{
    private readonly Dictionary<string, int> _remainingFailures;
    private readonly object _gate = new();

    public FakeGeminiFileApiService(bool isEnabled = true, Dictionary<string, int>? remainingFailures = null)
    {
        IsEnabled = isEnabled;
        _remainingFailures = remainingFailures ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsEnabled { get; }
    public List<string> UploadCalls { get; } = new();

    public Task PrepareExecutionAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<GeminiFileReference> GetOrUploadAsync(
        string sourcePath, string artifactKey, string displayName, string mimeType, byte[] content,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            UploadCalls.Add(sourcePath + "|" + artifactKey);
            if (_remainingFailures.TryGetValue(sourcePath, out var left) && left > 0)
            {
                _remainingFailures[sourcePath] = left - 1;
                throw new IOException("upload hong gia lap: " + sourcePath);
            }
        }
        return Task.FromResult(new GeminiFileReference(
            "files/" + artifactKey, "https://fake/" + artifactKey, mimeType));
    }

    public Task DeleteBySourcePathsAsync(IEnumerable<string> sourcePaths, CancellationToken ct = default)
        => Task.CompletedTask;
}

sealed class FakeAiModelClient : IAiModelClient
{
    private readonly Queue<string> _responses;
    private string _lastResponse = "{}";

    public FakeAiModelClient(params string[] responses)
    {
        _responses = new Queue<string>(responses);
        if (responses.Length > 0)
            _lastResponse = responses[^1];
    }

    public List<AiCallOptions> Calls { get; } = new();

    public Task<string> GenerateJsonAsync(
        IReadOnlyList<object> content,
        int maxAttempts = 3,
        int? maxTokens = null,
        double? temperature = null,
        bool? reasoningEnabled = null,
        string? reasoningEffort = null,
        string? pdfParserEngine = null,
        bool enableRouterMetadata = false,
        int timeoutSeconds = 180,
        object? responseSchema = null,
        string? escalatedReasoningEffort = null,
        CancellationToken ct = default)
    {
        Calls.Add(new AiCallOptions(maxAttempts, maxTokens, temperature, reasoningEnabled, reasoningEffort,
            responseSchema, escalatedReasoningEffort));
        if (_responses.Count > 0)
            _lastResponse = _responses.Dequeue();
        return Task.FromResult(_lastResponse);
    }
}

sealed record AiCallOptions(int MaxAttempts, int? MaxTokens, double? Temperature, bool? ReasoningEnabled, string? ReasoningEffort, object? ResponseSchema = null, string? EscalatedReasoningEffort = null);

sealed class FakePdfRotationNormalizer : IPdfRotationNormalizer
{
    public Task<int> NormalizeAsync(string pdfPath, CancellationToken ct = default)
        => Task.FromResult(0);
}

sealed class FakePdfRenderer : IPdfRenderer
{
    public Task<IReadOnlyList<SoftwareBitmap>> RenderPagesAsync(string pdfPath, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SoftwareBitmap>>([]);

    public Task<byte[]> RenderPageJpegAsync(string pdfPath, int pageIndex = 0, CancellationToken ct = default)
        => Task.FromResult(Array.Empty<byte>());

    public Task<IReadOnlyList<byte[]>> RenderPagesJpegAsync(string pdfPath, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<byte[]>>([]);
}
