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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Solution;
using ICSharpCode.Decompiler.Util;
using ICSharpCode.ILSpy.TextView;

namespace ICSharpCode.ILSpy
{
	/// <summary>
	/// An utility class that creates a Visual Studio solution containing projects for the
	/// decompiled assemblies.
	/// </summary>
	internal class SolutionWriter
	{
		/// <summary>
		/// Creates a Visual Studio solution that contains projects with decompiled code
		/// of the specified <paramref name="assemblies"/>. The solution file will be saved
		/// to the <paramref name="solutionFilePath"/>. The directory of this file must either
		/// be empty or not exist.
		/// </summary>
		/// <param name="textView">A reference to the <see cref="DecompilerTextView"/> instance.</param>
		/// <param name="solutionFilePath">The target file path of the solution file.</param>
		/// <param name="assemblies">The assembly nodes to decompile.</param>
		/// 
		/// <exception cref="ArgumentException">Thrown when <paramref name="solutionFilePath"/> is null,
		/// an empty or a whitespace string.</exception>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="textView"/>> or
		/// <paramref name="assemblies"/> is null.</exception>
		public static void CreateSolution(DecompilerTextView textView, string solutionFilePath, Language language, IEnumerable<LoadedAssembly> assemblies)
		{
			if (textView == null) {
				throw new ArgumentNullException(nameof(textView));
			}

			if (string.IsNullOrWhiteSpace(solutionFilePath)) {
				throw new ArgumentException("The solution file path cannot be null or empty.", nameof(solutionFilePath));
			}

			if (assemblies == null) {
				throw new ArgumentNullException(nameof(assemblies));
			}

			var writer = new SolutionWriter(solutionFilePath);

			textView
				.RunWithCancellation(ct => writer.CreateSolution(assemblies, language, ct))
				.Then(output => textView.ShowText(output))
				.HandleExceptions();
		}

		readonly string solutionFilePath;
		readonly string solutionDirectory;
		readonly ConcurrentBag<ProjectItem> projects;
		readonly ConcurrentBag<string> statusOutput;

		SolutionWriter(string solutionFilePath)
		{
			this.solutionFilePath = solutionFilePath;
			solutionDirectory = Path.GetDirectoryName(solutionFilePath);
			statusOutput = new ConcurrentBag<string>();
			projects = new ConcurrentBag<ProjectItem>();
		}

		async Task<AvalonEditTextOutput> CreateSolution(IEnumerable<LoadedAssembly> assemblies, Language language, CancellationToken ct)
		{
			var result = new AvalonEditTextOutput();

			var duplicates = new HashSet<string>();
			if (assemblies.Any(asm => !duplicates.Add(asm.ShortName))) {
				result.WriteLine("Duplicate assembly names selected, cannot generate a solution.");
				return result;
			}

			Stopwatch stopwatch = Stopwatch.StartNew();

			try {
				await Task.Run(() => Parallel.ForEach(assemblies, n => WriteProject(n, language, solutionDirectory, ct)))
					.ConfigureAwait(false);

				await Task.Run(() => SolutionCreator.WriteSolutionFile(solutionFilePath, projects))
					.ConfigureAwait(false);
			} catch (AggregateException ae) {
				if (ae.Flatten().InnerExceptions.All(e => e is OperationCanceledException)) {
					result.WriteLine();
					result.WriteLine("Generation was cancelled.");
					return result;
				}

				result.WriteLine();
				result.WriteLine("Failed to generate the Visual Studio Solution. Errors:");
				ae.Handle(e => {
					result.WriteLine(e.Message);
					return true;
				});

				return result;
			}

			foreach (var item in statusOutput) {
				result.WriteLine(item);
			}

			if (statusOutput.Count == 0) {
				result.WriteLine("Successfully decompiled the following assemblies into Visual Studio projects:");
				foreach (var item in assemblies.Select(n => n.Text.ToString())) {
					result.WriteLine(item);
				}

				result.WriteLine();

				if (assemblies.Count() == projects.Count) {
					result.WriteLine("Created the Visual Studio Solution file.");
				}

				result.WriteLine();
				result.WriteLine("Elapsed time: " + stopwatch.Elapsed.TotalSeconds.ToString("F1") + " seconds.");
				result.WriteLine();
				result.AddButton(null, "Open Explorer", delegate { Process.Start("explorer", "/select,\"" + solutionFilePath + "\""); });
			}

			return result;
		}

		void WriteProject(LoadedAssembly loadedAssembly, Language language, string targetDirectory, CancellationToken ct)
		{
			targetDirectory = Path.Combine(targetDirectory, loadedAssembly.ShortName);
			string projectFileName = Path.Combine(targetDirectory, loadedAssembly.ShortName + language.ProjectFileExtension);

			if (!Directory.Exists(targetDirectory)) {
				try {
					Directory.CreateDirectory(targetDirectory);
				} catch (Exception e) {
					statusOutput.Add($"Failed to create a directory '{targetDirectory}':{Environment.NewLine}{e}");
					return;
				}
			}

			try {
				using (var projectFileWriter = new StreamWriter(projectFileName)) {
					var projectFileOutput = new PlainTextOutput(projectFileWriter);
					var options = new DecompilationOptions() {
						FullDecompilation = true,
						CancellationToken = ct,
						SaveAsProjectDirectory = targetDirectory
					};

					var projectInfo = language.DecompileAssembly(loadedAssembly, projectFileOutput, options);
					if (projectInfo != null) {
						projects.Add(new ProjectItem(projectFileName, projectInfo.PlatformName, projectInfo.Guid, projectInfo.TypeGuid));
					}
				}
			} catch (Exception e) when (!(e is OperationCanceledException)) {
				statusOutput.Add($"Failed to decompile the assembly '{loadedAssembly.FileName}':{Environment.NewLine}{e}");
			}
		}
	}
}
