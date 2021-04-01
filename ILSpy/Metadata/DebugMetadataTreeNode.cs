﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.ILSpy.Options;
using ICSharpCode.ILSpy.TreeNodes;
using ICSharpCode.ILSpy.ViewModels;

namespace ICSharpCode.ILSpy.Metadata
{
	class DebugMetadataTreeNode : ILSpyTreeNode
	{
		private PEFile module;
		private MetadataReader provider;
		private AssemblyTreeNode assemblyTreeNode;
		private bool isEmbedded;

		public DebugMetadataTreeNode(PEFile module, bool isEmbedded, MetadataReader provider, AssemblyTreeNode assemblyTreeNode)
		{
			this.module = module;
			this.provider = provider;
			this.assemblyTreeNode = assemblyTreeNode;
			this.isEmbedded = isEmbedded;
			this.Text = "Debug Metadata (" + (isEmbedded ? "Embedded" : "From portable PDB") + ")";
			this.LazyLoading = true;
		}

		public override object Text { get; }

		public override object Icon => Images.Library;

		public override bool View(TabPageModel tabPage)
		{
			tabPage.Title = Text.ToString();
			tabPage.SupportsLanguageSwitching = false;

			return false;
		}

		public override void Decompile(Language language, ITextOutput output, DecompilationOptions options)
		{
			language.WriteCommentLine(output, "Debug Metadata");
		}

		protected override void LoadChildren()
		{
			this.Children.Add(new DebugMetadataTablesTreeNode(module, this.isEmbedded, this.provider));
			this.Children.Add(new StringHeapTreeNode(module, this.provider));
			this.Children.Add(new UserStringHeapTreeNode(module, this.provider));
			this.Children.Add(new GuidHeapTreeNode(module, this.provider));
			this.Children.Add(new BlobHeapTreeNode(module, this.provider));
		}

		public MetadataTableTreeNode FindNodeByHandleKind(HandleKind kind)
		{
			return this.Children.OfType<MetadataTableTreeNode>().SingleOrDefault(x => x.Kind == kind);
		}
	}
}
