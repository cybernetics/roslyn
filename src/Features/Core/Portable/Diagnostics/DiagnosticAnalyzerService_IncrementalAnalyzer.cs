﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Shared.Options;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.SolutionCrawler;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    [ExportIncrementalAnalyzerProvider(
        highPriorityForActiveFile: true, name: WellKnownSolutionCrawlerAnalyzers.Diagnostic, 
        workspaceKinds: new string[] { WorkspaceKind.Host, WorkspaceKind.Interactive, WorkspaceKind.AnyCodeRoslynWorkspace })]
    internal partial class DiagnosticAnalyzerService : IIncrementalAnalyzerProvider
    {
        private readonly ConditionalWeakTable<Workspace, BaseDiagnosticIncrementalAnalyzer> _map;
        private readonly ConditionalWeakTable<Workspace, BaseDiagnosticIncrementalAnalyzer>.CreateValueCallback _createIncrementalAnalyzer;

        private DiagnosticAnalyzerService()
        {
            _map = new ConditionalWeakTable<Workspace, BaseDiagnosticIncrementalAnalyzer>();
            _createIncrementalAnalyzer = CreateIncrementalAnalyzerCallback;
        }

        public IIncrementalAnalyzer CreateIncrementalAnalyzer(Workspace workspace)
        {
            if (!workspace.Options.GetOption(ServiceComponentOnOffOptions.DiagnosticProvider))
            {
                return null;
            }

            return GetOrCreateIncrementalAnalyzer(workspace);
        }

        private BaseDiagnosticIncrementalAnalyzer GetOrCreateIncrementalAnalyzer(Workspace workspace)
        {
            return _map.GetValue(workspace, _createIncrementalAnalyzer);
        }

        public void ShutdownAnalyzerFrom(Workspace workspace)
        {
            // this should be only called once analyzer associated with the workspace is done.
            BaseDiagnosticIncrementalAnalyzer analyzer;
            if (_map.TryGetValue(workspace, out analyzer))
            {
                analyzer.Shutdown();
            }
        }

        private BaseDiagnosticIncrementalAnalyzer CreateIncrementalAnalyzerCallback(Workspace workspace)
        {
            // subscribe to active context changed event for new workspace
            workspace.DocumentActiveContextChanged += OnDocumentActiveContextChanged;
            return new IncrementalAnalyzerDelegatee(this, workspace, _hostAnalyzerManager, _hostDiagnosticUpdateSource);
        }

        private void OnDocumentActiveContextChanged(object sender, DocumentActiveContextChangedEventArgs e)
        {
            Reanalyze(e.Solution.Workspace, documentIds: SpecializedCollections.SingletonEnumerable(e.NewActiveContextDocumentId), highPriority: true);
        }

        // internal for testing
        internal class IncrementalAnalyzerDelegatee : BaseDiagnosticIncrementalAnalyzer
        {
            // v2 diagnostic engine - for now v1
            private readonly EngineV2.DiagnosticIncrementalAnalyzer _engineV2;

            public IncrementalAnalyzerDelegatee(DiagnosticAnalyzerService owner, Workspace workspace, HostAnalyzerManager hostAnalyzerManager, AbstractHostDiagnosticUpdateSource hostDiagnosticUpdateSource)
                : base(owner, workspace, hostAnalyzerManager, hostDiagnosticUpdateSource)
            {
                var v2CorrelationId = LogAggregator.GetNextId();
                _engineV2 = new EngineV2.DiagnosticIncrementalAnalyzer(owner, v2CorrelationId, workspace, hostAnalyzerManager, hostDiagnosticUpdateSource);
            }

            #region IIncrementalAnalyzer
            public override Task AnalyzeSyntaxAsync(Document document, InvocationReasons reasons, CancellationToken cancellationToken)
            {
                return Analyzer.AnalyzeSyntaxAsync(document, reasons, cancellationToken);
            }

            public override Task AnalyzeDocumentAsync(Document document, SyntaxNode bodyOpt, InvocationReasons reasons, CancellationToken cancellationToken)
            {
                return Analyzer.AnalyzeDocumentAsync(document, bodyOpt, reasons, cancellationToken);
            }

            public override Task AnalyzeProjectAsync(Project project, bool semanticsChanged, InvocationReasons reasons, CancellationToken cancellationToken)
            {
                return Analyzer.AnalyzeProjectAsync(project, semanticsChanged, reasons, cancellationToken);
            }

            public override Task DocumentOpenAsync(Document document, CancellationToken cancellationToken)
            {
                return Analyzer.DocumentOpenAsync(document, cancellationToken);
            }

            public override Task DocumentCloseAsync(Document document, CancellationToken cancellationToken)
            {
                return Analyzer.DocumentCloseAsync(document, cancellationToken);
            }

            public override Task DocumentResetAsync(Document document, CancellationToken cancellationToken)
            {
                return Analyzer.DocumentResetAsync(document, cancellationToken);
            }

            public override Task NewSolutionSnapshotAsync(Solution solution, CancellationToken cancellationToken)
            {
                return Analyzer.NewSolutionSnapshotAsync(solution, cancellationToken);
            }

            public override void RemoveDocument(DocumentId documentId)
            {
                Analyzer.RemoveDocument(documentId);
            }

            public override void RemoveProject(ProjectId projectId)
            {
                Analyzer.RemoveProject(projectId);
            }

            public override bool NeedsReanalysisOnOptionChanged(object sender, OptionChangedEventArgs e)
            {
                return e.Option.Feature == nameof(SimplificationOptions) ||
                       e.Option == ServiceFeatureOnOffOptions.ClosedFileDiagnostic ||
                       e.Option == RuntimeOptions.FullSolutionAnalysis ||
                       e.Option == InternalDiagnosticsOptions.UseDiagnosticEngineV2 ||
                       Analyzer.NeedsReanalysisOnOptionChanged(sender, e);
            }
            #endregion

            #region delegating methods from diagnostic analyzer service to each implementation of the engine
            public override Task<ImmutableArray<DiagnosticData>> GetCachedDiagnosticsAsync(Solution solution, ProjectId projectId = null, DocumentId documentId = null, bool includeSuppressedDiagnostics = false, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Analyzer.GetCachedDiagnosticsAsync(solution, projectId, documentId, includeSuppressedDiagnostics, cancellationToken);
            }

            public override Task<ImmutableArray<DiagnosticData>> GetSpecificCachedDiagnosticsAsync(Solution solution, object id, bool includeSuppressedDiagnostics = false, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Analyzer.GetSpecificCachedDiagnosticsAsync(solution, id, includeSuppressedDiagnostics, cancellationToken);
            }

            public override Task<ImmutableArray<DiagnosticData>> GetDiagnosticsAsync(Solution solution, ProjectId projectId = null, DocumentId documentId = null, bool includeSuppressedDiagnostics = false, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Analyzer.GetDiagnosticsAsync(solution, projectId, documentId, includeSuppressedDiagnostics, cancellationToken);
            }

            public override Task<ImmutableArray<DiagnosticData>> GetSpecificDiagnosticsAsync(Solution solution, object id, bool includeSuppressedDiagnostics = false, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Analyzer.GetSpecificDiagnosticsAsync(solution, id, includeSuppressedDiagnostics, cancellationToken);
            }

            public override Task<ImmutableArray<DiagnosticData>> GetDiagnosticsForIdsAsync(Solution solution, ProjectId projectId = null, DocumentId documentId = null, ImmutableHashSet<string> diagnosticIds = null, bool includeSuppressedDiagnostics = false, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Analyzer.GetDiagnosticsForIdsAsync(solution, projectId, documentId, diagnosticIds, includeSuppressedDiagnostics, cancellationToken);
            }

            public override Task<ImmutableArray<DiagnosticData>> GetProjectDiagnosticsForIdsAsync(Solution solution, ProjectId projectId = null, ImmutableHashSet<string> diagnosticIds = null, bool includeSuppressedDiagnostics = false, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Analyzer.GetProjectDiagnosticsForIdsAsync(solution, projectId, diagnosticIds, includeSuppressedDiagnostics, cancellationToken);
            }

            public override Task<bool> TryAppendDiagnosticsForSpanAsync(Document document, TextSpan range, List<DiagnosticData> diagnostics, bool includeSuppressedDiagnostics = false, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Analyzer.TryAppendDiagnosticsForSpanAsync(document, range, diagnostics, includeSuppressedDiagnostics, cancellationToken);
            }

            public override Task<IEnumerable<DiagnosticData>> GetDiagnosticsForSpanAsync(Document document, TextSpan range, bool includeSuppressedDiagnostics = false, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Analyzer.GetDiagnosticsForSpanAsync(document, range, includeSuppressedDiagnostics, cancellationToken);
            }

            public override bool ContainsDiagnostics(Workspace workspace, ProjectId projectId)
            {
                return Analyzer.ContainsDiagnostics(workspace, projectId);
            }
            #endregion

            #region build synchronization
            public override Task SynchronizeWithBuildAsync(Workspace workspace, ImmutableDictionary<ProjectId, ImmutableArray<DiagnosticData>> diagnostics)
            {
                return Analyzer.SynchronizeWithBuildAsync(workspace, diagnostics);
            }
            #endregion

            public override void Shutdown()
            {
                Analyzer.Shutdown();
            }

            public override void LogAnalyzerCountSummary()
            {
                Analyzer.LogAnalyzerCountSummary();
            }

            public void TurnOff(bool useV2)
            {
                // Uncomment the below when we add a v3 engine.

                //var turnedOffAnalyzer = GetAnalyzer(!useV2);

                //foreach (var project in Workspace.CurrentSolution.Projects)
                //{
                //    foreach (var document in project.Documents)
                //    {
                //        turnedOffAnalyzer.RemoveDocument(document.Id);
                //    }

                //    turnedOffAnalyzer.RemoveProject(project.Id);
                //}
            }

            // internal for testing
            internal BaseDiagnosticIncrementalAnalyzer Analyzer
            {
                get
                {
                    var option = Workspace.Options.GetOption(InternalDiagnosticsOptions.UseDiagnosticEngineV2);
                    return GetAnalyzer(option);
                }
            }

            private BaseDiagnosticIncrementalAnalyzer GetAnalyzer(bool useV2)
            {
                // v1 engine has been removed, always use v2 engine (until v3 engine is added).
                //return useV2 ? (BaseDiagnosticIncrementalAnalyzer)_engineV2 : _engineV1;

                return (BaseDiagnosticIncrementalAnalyzer)_engineV2;
            }
        }
    }
}
