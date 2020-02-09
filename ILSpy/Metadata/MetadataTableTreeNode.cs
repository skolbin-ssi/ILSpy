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

using System;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.ILSpy.TextView;
using ICSharpCode.ILSpy.TreeNodes;
using ICSharpCode.ILSpy.ViewModels;
using ICSharpCode.TreeView;

namespace ICSharpCode.ILSpy.Metadata
{
	internal abstract class MetadataTableTreeNode : ILSpyTreeNode
	{
		protected PEFile module;
		protected int scrollTarget;

		public HandleKind Kind { get; }

		public MetadataTableTreeNode(HandleKind kind, PEFile module)
		{
			this.module = module;
			this.Kind = kind;
		}

		internal void ScrollTo(Handle handle)
		{
			this.scrollTarget = MetadataTokens.GetRowNumber((EntityHandle)handle);
		}

		protected void ScrollItemIntoView(DataGrid view, object item)
		{
			view.Loaded += View_Loaded;
			view.Dispatcher.BeginInvoke((Action)(() => view.SelectItem(item)), DispatcherPriority.Background);
		}

		private void View_Loaded(object sender, System.Windows.RoutedEventArgs e)
		{
			DataGrid view = (DataGrid)sender;
			var sv = view.FindVisualChild<ScrollViewer>();
			sv.ScrollToVerticalOffset(scrollTarget - 1);
			view.Loaded -= View_Loaded;
			this.scrollTarget = default;
		}
	}

	internal abstract class DebugMetadataTableTreeNode : MetadataTableTreeNode
	{
		protected MetadataReader metadata;

		public DebugMetadataTableTreeNode(HandleKind kind, PEFile module, MetadataReader metadata)
			: base(kind, module)
		{
			this.metadata = metadata;
		}
	}
}