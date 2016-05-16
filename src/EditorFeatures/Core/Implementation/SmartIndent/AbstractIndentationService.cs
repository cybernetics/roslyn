// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Formatting.Rules;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.SmartIndent
{
    internal abstract partial class AbstractIndentationService : IIndentationService
    {
        protected abstract IFormattingRule GetSpecializedIndentationFormattingRule();

        private IEnumerable<IFormattingRule> GetFormattingRules(Document document, int position)
        {
            var workspace = document.Project.Solution.Workspace;
            var formattingRuleFactory = workspace.Services.GetService<IHostDependentFormattingRuleFactoryService>();
            var baseIndentationRule = formattingRuleFactory.CreateRule(document, position);

            var formattingRules = new[] { baseIndentationRule, this.GetSpecializedIndentationFormattingRule() }.Concat(Formatter.GetDefaultFormattingRules(document));
            return formattingRules;
        }

        public async Task<IndentationResult?> GetDesiredIndentationAsync(Document document, int lineNumber, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

            var textSnapshot = sourceText.FindCorrespondingEditorTextSnapshot();
            if (textSnapshot == null)
            {
                // text snapshot doesn't exit. return null
                return null;
            }

            var lineToBeIndented = textSnapshot.GetLineFromLineNumber(lineNumber);

            var formattingRules = GetFormattingRules(document, lineToBeIndented.Start);

            // enter on a token case.
            if (ShouldUseSmartTokenFormatterInsteadOfIndenter(formattingRules, root, lineToBeIndented, document.Options, cancellationToken))
            {
                return null;
            }

            var indenter = await GetIndenterAsync(document, lineToBeIndented, formattingRules, document.Options, cancellationToken).ConfigureAwait(false);
            return indenter.GetDesiredIndentation();
        }

        protected abstract Task<AbstractIndenter> GetIndenterAsync(Document document, ITextSnapshotLine lineToBeIndented, IEnumerable<IFormattingRule> formattingRules, OptionSet optionSet, CancellationToken cancellationToken);

        protected abstract bool ShouldUseSmartTokenFormatterInsteadOfIndenter(
            IEnumerable<IFormattingRule> formattingRules, SyntaxNode root, ITextSnapshotLine line, OptionSet optionSet, CancellationToken cancellationToken);
    }
}
