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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Xml.Linq;

using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.Decompiler;
using ICSharpCode.ILSpy.Properties;
using ICSharpCode.ILSpy.TextView;

using OSVersionHelper;

namespace ICSharpCode.ILSpy
{
	[ExportMainMenuCommand(Menu = nameof(Resources._Help), Header = nameof(Resources._About), MenuOrder = 99999)]
	sealed class AboutPage : SimpleCommand
	{
		public override void Execute(object parameter)
		{
			MainWindow.Instance.NavigateTo(new RequestNavigateEventArgs(new Uri("resource://aboutpage"), null));
		}
		
		static readonly Uri UpdateUrl = new Uri("https://ilspy.net/updates.xml");
		const string band = "stable";
		
		static AvailableVersionInfo latestAvailableVersion;
		
		public static void Display(DecompilerTextView textView)
		{
			AvalonEditTextOutput output = new AvalonEditTextOutput() { Title = Resources.About, EnableHyperlinks = true };
			output.WriteLine(Resources.ILSpyVersion + RevisionClass.FullVersion);
			if(WindowsVersionHelper.HasPackageIdentity) {
				output.WriteLine($"Package Name: {WindowsVersionHelper.GetPackageFamilyName()}");
			} else {// if we're running in an MSIX, updates work differently
				output.AddUIElement(
				delegate {
					StackPanel stackPanel = new StackPanel();
					stackPanel.HorizontalAlignment = HorizontalAlignment.Center;
					stackPanel.Orientation = Orientation.Horizontal;
					if (latestAvailableVersion == null) {
						AddUpdateCheckButton(stackPanel, textView);
					} else {
						// we already retrieved the latest version sometime earlier
						ShowAvailableVersion(latestAvailableVersion, stackPanel);
					}
					CheckBox checkBox = new CheckBox();
					checkBox.Margin = new Thickness(4);
					checkBox.Content = Resources.AutomaticallyCheckUpdatesEveryWeek;
					UpdateSettings settings = new UpdateSettings(ILSpySettings.Load());
					checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding("AutomaticUpdateCheckEnabled") { Source = settings });
					return new StackPanel {
						Margin = new Thickness(0, 4, 0, 0),
						Cursor = Cursors.Arrow,
						Children = { stackPanel, checkBox }
					};
				});
				output.WriteLine();
			}
			
			foreach (var plugin in App.ExportProvider.GetExportedValues<IAboutPageAddition>())
				plugin.Write(output);
			output.WriteLine();
			output.Address = new Uri("resource://AboutPage");
			using (Stream s = typeof(AboutPage).Assembly.GetManifestResourceStream(typeof(AboutPage), "ILSpyAboutPage.txt")) {
				using (StreamReader r = new StreamReader(s)) {
					string line;
					while ((line = r.ReadLine()) != null) {
						output.WriteLine(line);
					}
				}
			}
			output.AddVisualLineElementGenerator(new MyLinkElementGenerator("MIT License", "resource:license.txt"));
			output.AddVisualLineElementGenerator(new MyLinkElementGenerator("third-party notices", "resource:third-party-notices.txt"));
			textView.ShowText(output);
		}
		
		sealed class MyLinkElementGenerator : LinkElementGenerator
		{
			readonly Uri uri;
			
			public MyLinkElementGenerator(string matchText, string url) : base(new Regex(Regex.Escape(matchText)))
			{
				this.uri = new Uri(url);
				this.RequireControlModifierForClick = false;
			}
			
			protected override Uri GetUriFromMatch(Match match)
			{
				return uri;
			}
		}
		
		static void AddUpdateCheckButton(StackPanel stackPanel, DecompilerTextView textView)
		{
			Button button = new Button();
			button.Content = Resources.CheckUpdates;
			button.Cursor = Cursors.Arrow;
			stackPanel.Children.Add(button);
			
			button.Click += async delegate {
				button.Content = Resources.Checking;
				button.IsEnabled = false;
				
				try {
					AvailableVersionInfo vInfo = await GetLatestVersionAsync();
					stackPanel.Children.Clear();
					ShowAvailableVersion(vInfo, stackPanel);
				} catch (Exception ex) {
					AvalonEditTextOutput exceptionOutput = new AvalonEditTextOutput();
					exceptionOutput.WriteLine(ex.ToString());
					textView.ShowText(exceptionOutput);
				}
			};
		}
		
		static readonly Version currentVersion = new Version(RevisionClass.Major + "." + RevisionClass.Minor + "." + RevisionClass.Build + "." + RevisionClass.Revision);
		
		static void ShowAvailableVersion(AvailableVersionInfo availableVersion, StackPanel stackPanel)
		{
			if (currentVersion == availableVersion.Version) {
				stackPanel.Children.Add(
					new Image {
						Width = 16, Height = 16,
						Source = Images.OK,
						Margin = new Thickness(4,0,4,0)
					});
				stackPanel.Children.Add(
					new TextBlock {
						Text = Resources.UsingLatestRelease,
						VerticalAlignment = VerticalAlignment.Bottom
					});
			} else if (currentVersion < availableVersion.Version) {
				stackPanel.Children.Add(
					new TextBlock {
						Text = string.Format(Resources.VersionAvailable, availableVersion.Version ),
						Margin = new Thickness(0,0,8,0),
						VerticalAlignment = VerticalAlignment.Bottom
					});
				if (availableVersion.DownloadUrl != null) {
					Button button = new Button();
					button.Content = Resources.Download;
					button.Cursor = Cursors.Arrow;
					button.Click += delegate {
						MainWindow.OpenLink(availableVersion.DownloadUrl);
					};
					stackPanel.Children.Add(button);
				}
			} else {
				stackPanel.Children.Add(new TextBlock { Text = Resources.UsingNightlyBuildNewerThanLatestRelease });
			}
		}
		
		static async Task<AvailableVersionInfo> GetLatestVersionAsync()
		{
			WebClient wc = new WebClient();
			IWebProxy systemWebProxy = WebRequest.GetSystemWebProxy();
			systemWebProxy.Credentials = CredentialCache.DefaultCredentials;
			wc.Proxy = systemWebProxy;

			string data = await wc.DownloadStringTaskAsync(UpdateUrl);

			XDocument doc = XDocument.Load(new StringReader(data));
			var bands = doc.Root.Elements("band");
			var currentBand = bands.FirstOrDefault(b => (string)b.Attribute("id") == band) ?? bands.First();
			Version version = new Version((string)currentBand.Element("latestVersion"));
			string url = (string)currentBand.Element("downloadUrl");
			if (!(url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal)))
				url = null; // don't accept non-urls

			latestAvailableVersion = new AvailableVersionInfo { Version = version, DownloadUrl = url };
			return latestAvailableVersion;
		}
		
		sealed class AvailableVersionInfo
		{
			public Version Version;
			public string DownloadUrl;
		}
		
		sealed class UpdateSettings : INotifyPropertyChanged
		{
			public UpdateSettings(ILSpySettings spySettings)
			{
				XElement s = spySettings["UpdateSettings"];
				this.automaticUpdateCheckEnabled = (bool?)s.Element("AutomaticUpdateCheckEnabled") ?? true;
				try {
					this.lastSuccessfulUpdateCheck = (DateTime?)s.Element("LastSuccessfulUpdateCheck");
				} catch (FormatException) {
					// avoid crashing on settings files invalid due to
					// https://github.com/icsharpcode/ILSpy/issues/closed/#issue/2
				}
			}
			
			bool automaticUpdateCheckEnabled;
			
			public bool AutomaticUpdateCheckEnabled {
				get { return automaticUpdateCheckEnabled; }
				set {
					if (automaticUpdateCheckEnabled != value) {
						automaticUpdateCheckEnabled = value;
						Save();
						OnPropertyChanged(nameof(AutomaticUpdateCheckEnabled));
					}
				}
			}
			
			DateTime? lastSuccessfulUpdateCheck;
			
			public DateTime? LastSuccessfulUpdateCheck {
				get { return lastSuccessfulUpdateCheck; }
				set {
					if (lastSuccessfulUpdateCheck != value) {
						lastSuccessfulUpdateCheck = value;
						Save();
						OnPropertyChanged(nameof(LastSuccessfulUpdateCheck));
					}
				}
			}
			
			public void Save()
			{
				XElement updateSettings = new XElement("UpdateSettings");
				updateSettings.Add(new XElement("AutomaticUpdateCheckEnabled", automaticUpdateCheckEnabled));
				if (lastSuccessfulUpdateCheck != null)
					updateSettings.Add(new XElement("LastSuccessfulUpdateCheck", lastSuccessfulUpdateCheck));
				ILSpySettings.SaveSettings(updateSettings);
			}
			
			public event PropertyChangedEventHandler PropertyChanged;
			
			void OnPropertyChanged(string propertyName)
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
			}
		}
		
		/// <summary>
		/// If automatic update checking is enabled, checks if there are any updates available.
		/// Returns the download URL if an update is available.
		/// Returns null if no update is available, or if no check was performed.
		/// </summary>
		public static async Task<string> CheckForUpdatesIfEnabledAsync(ILSpySettings spySettings)
		{
			UpdateSettings s = new UpdateSettings(spySettings);

			// If we're in an MSIX package, updates work differently
			if (s.AutomaticUpdateCheckEnabled && !WindowsVersionHelper.HasPackageIdentity) {
				// perform update check if we never did one before;
				// or if the last check wasn't in the past 7 days
				if (s.LastSuccessfulUpdateCheck == null
				    || s.LastSuccessfulUpdateCheck < DateTime.UtcNow.AddDays(-7)
				    || s.LastSuccessfulUpdateCheck > DateTime.UtcNow)
				{
					return await CheckForUpdateInternal(s);
				} else {
					return null;
				}
			} else {
				return null;
			}
		}

		public static Task<string> CheckForUpdatesAsync(ILSpySettings spySettings)
		{
			UpdateSettings s = new UpdateSettings(spySettings);
			return CheckForUpdateInternal(s);
		}

		static async Task<string> CheckForUpdateInternal(UpdateSettings s)
		{
			try {
				var v = await GetLatestVersionAsync();
				s.LastSuccessfulUpdateCheck = DateTime.UtcNow;
				if (v.Version > currentVersion)
					return v.DownloadUrl;
				else
					return null;
			} catch (Exception) {
				// ignore errors getting the version info
				return null;
			}
		}
	}
	
	/// <summary>
	/// Interface that allows plugins to extend the about page.
	/// </summary>
	public interface IAboutPageAddition
	{
		void Write(ISmartTextOutput textOutput);
	}
}
