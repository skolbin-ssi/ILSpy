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
using System.Linq;
using System.Threading.Tasks;
using ICSharpCode.ILSpy.TextView;

namespace ICSharpCode.ILSpy.ViewModels
{
	public class TabPageModel : PaneModel
	{
		private readonly Dictionary<Language, LanguageVersion> languageVersionHistory = new Dictionary<Language, LanguageVersion>();

		private Language language;
		public Language Language {
			get => language;
			set {
				if (language != value) {
					if (language != null && language.HasLanguageVersions) {
						languageVersionHistory[language] = languageVersion;
					}
					language = value;
					RaisePropertyChanged(nameof(Language));
					if (language.HasLanguageVersions) {
						if (languageVersionHistory.TryGetValue(value, out var version)) {
							LanguageVersion = version;
						} else {
							LanguageVersion = Language.LanguageVersions.Last();
						}
					} else {
						LanguageVersion = default;
					}
				}
			}
		}

		private LanguageVersion languageVersion;
		public LanguageVersion LanguageVersion {
			get => languageVersion;
			set {
				if (languageVersion != value) {
					languageVersion = value;
					if (language.HasLanguageVersions) {
						languageVersionHistory[language] = languageVersion;
					}
					RaisePropertyChanged(nameof(LanguageVersion));
				}
			}
		}

		private bool supportsLanguageSwitching = true;
		public bool SupportsLanguageSwitching {
			get => supportsLanguageSwitching;
			set {
				if (supportsLanguageSwitching != value) {
					supportsLanguageSwitching = value;
					RaisePropertyChanged(nameof(SupportsLanguageSwitching));
				}
			}
		}

		private object content;
		public object Content {
			get => content;
			set {
				if (content != value) {
					content = value;
					RaisePropertyChanged(nameof(Content));
				}
			}
		}

		public ViewState GetState()
		{
			return (Content as IHaveState)?.GetState();
		}
	}

	public static class TabPageModelExtensions
	{
		public static Task<T> ShowTextViewAsync<T>(this TabPageModel tabPage, Func<DecompilerTextView, Task<T>> action)
		{
			if (!(tabPage.Content is DecompilerTextView textView)) {
				textView = new DecompilerTextView();
				tabPage.Content = textView;
			}
			return action(textView);
		}

		public static Task ShowTextViewAsync(this TabPageModel tabPage, Func<DecompilerTextView, Task> action)
		{
			if (!(tabPage.Content is DecompilerTextView textView)) {
				textView = new DecompilerTextView();
				tabPage.Content = textView;
			}
			return action(textView);
		}

		public static void ShowTextView(this TabPageModel tabPage, Action<DecompilerTextView> action)
		{
			if (!(tabPage.Content is DecompilerTextView textView)) {
				textView = new DecompilerTextView();
				tabPage.Content = textView;
			}
			action(textView);
		}
	}

	public interface IHaveState
	{
		ViewState GetState();
	}
}