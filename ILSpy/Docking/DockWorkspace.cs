﻿// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.ILSpy.TextView;
using ICSharpCode.ILSpy.ViewModels;
using Xceed.Wpf.AvalonDock.Layout;
using Xceed.Wpf.AvalonDock.Layout.Serialization;

namespace ICSharpCode.ILSpy.Docking
{
	public class DockWorkspace : INotifyPropertyChanged
	{
		private SessionSettings sessionSettings;

		public event PropertyChangedEventHandler PropertyChanged;

		public static DockWorkspace Instance { get; } = new DockWorkspace();

		private DockWorkspace()
		{
		}

		public PaneCollection<DocumentModel> Documents { get; } = new PaneCollection<DocumentModel>();

		private ToolPaneModel[] toolPanes;
		public IEnumerable<ToolPaneModel> ToolPanes {
			get {
				if (toolPanes == null) {
					toolPanes = new ToolPaneModel[] {
						AssemblyListPaneModel.Instance,
						SearchPaneModel.Instance,
						AnalyzerPaneModel.Instance,
#if DEBUG
						DebugStepsPaneModel.Instance,
#endif
					};
				}
				return toolPanes;
			}
		}

		public void Remove(PaneModel model)
		{
			if (model is DocumentModel document)
				Documents.Remove(document);
			if (model is ToolPaneModel tool)
				tool.IsVisible = false;
		}

		private DocumentModel _activeDocument = null;
		public DocumentModel ActiveDocument {
			get {
				return _activeDocument;
			}
			set {
				if (_activeDocument != value) {
					_activeDocument = value;
					if (value is DecompiledDocumentModel ddm) {
						this.sessionSettings.FilterSettings.Language = ddm.Language;
						this.sessionSettings.FilterSettings.LanguageVersion = ddm.LanguageVersion;
					}
					RaisePropertyChanged(nameof(ActiveDocument));
				}
			}
		}

		public void InitializeLayout(Xceed.Wpf.AvalonDock.DockingManager manager)
		{
			XmlLayoutSerializer serializer = new XmlLayoutSerializer(manager);
			serializer.LayoutSerializationCallback += LayoutSerializationCallback;
			try {
				sessionSettings.DockLayout.Deserialize(serializer);
			} finally {
				serializer.LayoutSerializationCallback -= LayoutSerializationCallback;
			}
		}

		void LayoutSerializationCallback(object sender, LayoutSerializationCallbackEventArgs e)
		{
			switch (e.Model) {
				case LayoutAnchorable la:
					switch (la.ContentId) {
						case AssemblyListPaneModel.PaneContentId:
							e.Content = AssemblyListPaneModel.Instance;
							break;
						case SearchPaneModel.PaneContentId:
							e.Content = SearchPaneModel.Instance;
							break;
						case AnalyzerPaneModel.PaneContentId:
							e.Content = AnalyzerPaneModel.Instance;
							break;
#if DEBUG
						case DebugStepsPaneModel.PaneContentId:
							e.Content = DebugStepsPaneModel.Instance;
							break;
#endif
						default:
							e.Cancel = true;
							break;
					}
					if (!e.Cancel) {
						e.Cancel = ((ToolPaneModel)e.Content).IsVisible;
						((ToolPaneModel)e.Content).IsVisible = true;
					}
					break;
				default:
					e.Cancel = true;
					break;
			}
		}

		protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		public void ShowText(AvalonEditTextOutput textOutput)
		{
			GetTextView().ShowText(textOutput);
		}

		public DecompilerTextView GetTextView()
		{
			return ((DecompiledDocumentModel)ActiveDocument).TextView;
		}

		public DecompilerTextViewState GetState()
		{
			return GetTextView().GetState();
		}

		public Task<T> RunWithCancellation<T>(Func<CancellationToken, Task<T>> taskCreation)
		{
			return GetTextView().RunWithCancellation(taskCreation);
		}

		internal void ShowNodes(AvalonEditTextOutput output, TreeNodes.ILSpyTreeNode[] nodes, IHighlightingDefinition highlighting)
		{
			GetTextView().ShowNodes(output, nodes, highlighting);
		}

		internal void LoadSettings(SessionSettings sessionSettings)
		{
			this.sessionSettings = sessionSettings;
			sessionSettings.FilterSettings.PropertyChanged += FilterSettings_PropertyChanged;
		}

		private void FilterSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (ActiveDocument is DecompiledDocumentModel ddm) {
				if (e.PropertyName == "Language" || e.PropertyName == "LanguageVersion") {
					ddm.Language = sessionSettings.FilterSettings.Language;
					ddm.LanguageVersion = sessionSettings.FilterSettings.LanguageVersion;
				}
			}
		}

		internal void CloseAllDocuments()
		{
			foreach (var doc in Documents.ToArray()) {
				if (doc.IsCloseable)
					Documents.Remove(doc);
			}
		}

		internal void ResetLayout()
		{
			foreach (var pane in ToolPanes) {
				pane.IsVisible = false;
			}
			sessionSettings.DockLayout.Reset();
			InitializeLayout(MainWindow.Instance.DockManager);
		}
	}
}
