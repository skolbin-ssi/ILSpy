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

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.ILSpy.TreeNodes;

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
			this.scrollTarget = module.Metadata.GetRowNumber((EntityHandle)handle);
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

		protected static string GenerateTooltip(ref string tooltip, PEFile module, EntityHandle handle)
		{
			if (tooltip == null)
			{
				if (handle.IsNil)
				{
					return null;
				}
				ITextOutput output = new PlainTextOutput();
				var context = new MetadataGenericContext(default(TypeDefinitionHandle), module);
				var metadata = module.Metadata;
				switch (handle.Kind)
				{
					case HandleKind.ModuleDefinition:
						output.Write(metadata.GetString(metadata.GetModuleDefinition().Name));
						output.Write(" (this module)");
						break;
					case HandleKind.ModuleReference:
						ModuleReference moduleReference = metadata.GetModuleReference((ModuleReferenceHandle)handle);
						output.Write(metadata.GetString(moduleReference.Name));
						break;
					case HandleKind.AssemblyReference:
						var asmRef = new Decompiler.Metadata.AssemblyReference(module, (AssemblyReferenceHandle)handle);
						output.Write(asmRef.ToString());
						break;
					case HandleKind.Parameter:
						var param = metadata.GetParameter((ParameterHandle)handle);
						output.Write(param.SequenceNumber + " - " + metadata.GetString(param.Name));
						break;
					case HandleKind.EventDefinition:
						var @event = metadata.GetEventDefinition((EventDefinitionHandle)handle);
						output.Write(metadata.GetString(@event.Name));
						break;
					case HandleKind.PropertyDefinition:
						var prop = metadata.GetPropertyDefinition((PropertyDefinitionHandle)handle);
						output.Write(metadata.GetString(prop.Name));
						break;
					case HandleKind.AssemblyDefinition:
						var ad = metadata.GetAssemblyDefinition();
						output.Write(metadata.GetString(ad.Name));
						output.Write(" (this assembly)");
						break;
					case HandleKind.AssemblyFile:
						var af = metadata.GetAssemblyFile((AssemblyFileHandle)handle);
						output.Write(metadata.GetString(af.Name));
						break;
					case HandleKind.GenericParameter:
						var gp = metadata.GetGenericParameter((GenericParameterHandle)handle);
						output.Write(metadata.GetString(gp.Name));
						break;
					case HandleKind.ManifestResource:
						var mfr = metadata.GetManifestResource((ManifestResourceHandle)handle);
						output.Write(metadata.GetString(mfr.Name));
						break;
					case HandleKind.Document:
						var doc = metadata.GetDocument((DocumentHandle)handle);
						output.Write(metadata.GetString(doc.Name));
						break;
					default:
						handle.WriteTo(module, output, context);
						break;
				}
				tooltip = "(" + handle.Kind + ") " + output.ToString();
			}
			return tooltip;
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