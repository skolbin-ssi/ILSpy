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

using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Metadata;

namespace ICSharpCode.ILSpy.Metadata
{
	class FileTableTreeNode : MetadataTableTreeNode
	{
		public FileTableTreeNode(PEFile module)
			: base(HandleKind.AssemblyFile, module)
		{
		}

		public override object Text => $"26 File ({module.Metadata.GetTableRowCount(TableIndex.File)})";

		public override object Icon => Images.Literal;

		public override bool View(ViewModels.TabPageModel tabPage)
		{
			tabPage.Title = Text.ToString();
			tabPage.SupportsLanguageSwitching = false;

			var view = Helpers.PrepareDataGrid(tabPage, this);
			var metadata = module.Metadata;

			var list = new List<FileEntry>();
			FileEntry scrollTargetEntry = default;

			foreach (var row in metadata.AssemblyFiles) {
				FileEntry entry = new FileEntry(module, row);
				if (entry.RID == this.scrollTarget) {
					scrollTargetEntry = entry;
				}
				list.Add(entry);
			}

			view.ItemsSource = list;

			tabPage.Content = view;

			if (scrollTargetEntry.RID > 0) {
				ScrollItemIntoView(view, scrollTargetEntry);
			}

			return true;
		}

		struct FileEntry
		{
			readonly int metadataOffset;
			readonly PEFile module;
			readonly MetadataReader metadata;
			readonly AssemblyFileHandle handle;
			readonly AssemblyFile assemblyFile;

			public int RID => MetadataTokens.GetRowNumber(handle);

			public int Token => MetadataTokens.GetToken(handle);

			public int Offset => metadataOffset
				+ metadata.GetTableMetadataOffset(TableIndex.File)
				+ metadata.GetTableRowSize(TableIndex.File) * (RID - 1);

			[StringFormat("X8")]
			public int Attributes => assemblyFile.ContainsMetadata ? 1 : 0;

			public string AttributesTooltip => assemblyFile.ContainsMetadata ? "ContainsMetaData" : "ContainsNoMetaData";

			public string Name => metadata.GetString(assemblyFile.Name);

			public string NameTooltip => $"{MetadataTokens.GetHeapOffset(assemblyFile.Name):X} \"{Name}\"";

			[StringFormat("X")]
			public int HashValue => MetadataTokens.GetHeapOffset(assemblyFile.HashValue);

			public string HashValueTooltip {
				get {
					if (assemblyFile.HashValue.IsNil)
						return null;
					System.Collections.Immutable.ImmutableArray<byte> token = metadata.GetBlobContent(assemblyFile.HashValue);
					return token.ToHexString(token.Length);
				}
			}

			public FileEntry(PEFile module, AssemblyFileHandle handle)
			{
				this.metadataOffset = module.Reader.PEHeaders.MetadataStartOffset;
				this.module = module;
				this.metadata = module.Metadata;
				this.handle = handle;
				this.assemblyFile = metadata.GetAssemblyFile(handle);
			}
		}

		public override void Decompile(Language language, ITextOutput output, DecompilationOptions options)
		{
			language.WriteCommentLine(output, "Files");
		}
	}
}
