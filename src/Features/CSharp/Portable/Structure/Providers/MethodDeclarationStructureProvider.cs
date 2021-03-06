// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Structure;

namespace Microsoft.CodeAnalysis.CSharp.Structure
{
    internal class MethodDeclarationStructureProvider : AbstractSyntaxNodeStructureProvider<MethodDeclarationSyntax>
    {
        protected override void CollectBlockSpans(
            MethodDeclarationSyntax methodDeclaration,
            ImmutableArray<BlockSpan>.Builder spans,
            CancellationToken cancellationToken)
        {
            CSharpStructureHelpers.CollectCommentRegions(methodDeclaration, spans);

            // fault tolerance
            if (methodDeclaration.Body == null ||
                methodDeclaration.Body.OpenBraceToken.IsMissing ||
                methodDeclaration.Body.CloseBraceToken.IsMissing)
            {
                return;
            }

            spans.Add(CSharpStructureHelpers.CreateRegion(
                methodDeclaration,
                methodDeclaration.ParameterList.GetLastToken(includeZeroWidth: true),
                autoCollapse: true));
        }
    }
}