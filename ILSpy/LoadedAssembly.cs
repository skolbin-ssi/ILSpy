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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.PdbProvider;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.TypeSystem.Implementation;
using ICSharpCode.Decompiler.Util;
using ICSharpCode.ILSpy.Options;

namespace ICSharpCode.ILSpy
{
	/// <summary>
	/// Represents a file loaded into ILSpy.
	/// 
	/// Note: this class is misnamed.
	/// The file is not necessarily an assembly, nor is it necessarily loaded.
	/// 
	/// A LoadedAssembly can refer to:
	///   * a .NET module (single-file) loaded into ILSpy
	///   * a non-existant file
	///   * a file of unknown format that could not be loaded
	///   * a .nupkg file or .NET core bundle
	///   * a file that is still being loaded in the background
	/// </summary>
	[DebuggerDisplay("[LoadedAssembly {shortName}]")]
	public sealed class LoadedAssembly
	{
		/// <summary>
		/// Maps from PEFile (successfully loaded .NET module) back to the LoadedAssembly instance
		/// that was used to load the module.
		/// </summary>
		internal static readonly ConditionalWeakTable<PEFile, LoadedAssembly> loadedAssemblies = new ConditionalWeakTable<PEFile, LoadedAssembly>();

		public sealed class LoadResult
		{
			public PEFile PEFile { get; }
			public PEFileNotSupportedException PEFileLoadException { get; }
			public LoadedPackage Package { get; }

			public LoadResult(PEFile peFile)
			{
				this.PEFile = peFile ?? throw new ArgumentNullException(nameof(peFile));
			}
			public LoadResult(PEFileNotSupportedException peFileLoadException, LoadedPackage package)
			{
				this.PEFileLoadException = peFileLoadException ?? throw new ArgumentNullException(nameof(peFileLoadException));
				this.Package = package ?? throw new ArgumentNullException(nameof(package));
			}
		}

		readonly Task<LoadResult> loadingTask;
		readonly AssemblyList assemblyList;
		readonly string fileName;
		readonly string shortName;
		readonly IAssemblyResolver providedAssemblyResolver;

		public LoadedAssembly ParentBundle { get; }

		public LoadedAssembly(AssemblyList assemblyList, string fileName,
			Task<Stream> stream = null, IAssemblyResolver assemblyResolver = null, string pdbFileName = null)
		{
			this.assemblyList = assemblyList ?? throw new ArgumentNullException(nameof(assemblyList));
			this.fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
			this.PdbFileName = pdbFileName;
			this.providedAssemblyResolver = assemblyResolver;

			this.loadingTask = Task.Run(() => LoadAsync(stream)); // requires that this.fileName is set
			this.shortName = Path.GetFileNameWithoutExtension(fileName);
			this.resolver = new MyAssemblyResolver(this);
		}

		public LoadedAssembly(LoadedAssembly bundle, string fileName, Task<Stream> stream, IAssemblyResolver assemblyResolver = null)
			: this(bundle.assemblyList, fileName, stream, assemblyResolver)
		{
			this.ParentBundle = bundle;
		}

		/// <summary>
		/// Returns a target framework identifier in the form '&lt;framework&gt;Version=v&lt;version&gt;'.
		/// Returns an empty string if no TargetFrameworkAttribute was found or the file doesn't contain an assembly header, i.e., is only a module.
		/// 
		/// Throws an exception if the file does not contain any .NET metadata (e.g. file of unknown format).
		/// </summary>
		public async Task<string> GetTargetFrameworkIdAsync()
		{
			var assembly = await GetPEFileAsync().ConfigureAwait(false);
			return assembly.DetectTargetFrameworkId() ?? string.Empty;
		}

		public ReferenceLoadInfo LoadedAssemblyReferencesInfo { get; } = new ReferenceLoadInfo();

		IDebugInfoProvider debugInfoProvider;

		/// <summary>
		/// Gets the <see cref="LoadResult"/>.
		/// </summary>
		public Task<LoadResult> GetLoadResultAsync()
		{
			return loadingTask;
		}

		/// <summary>
		/// Gets the <see cref="PEFile"/>.
		/// </summary>
		public async Task<PEFile> GetPEFileAsync()
		{
			var loadResult = await loadingTask.ConfigureAwait(false);
			if (loadResult.PEFile != null)
				return loadResult.PEFile;
			else
				throw loadResult.PEFileLoadException;
		}

		/// <summary>
		/// Gets the <see cref="PEFile"/>.
		/// Returns null in case of load errors.
		/// </summary>
		public PEFile GetPEFileOrNull()
		{
			try
			{
				var loadResult = loadingTask.GetAwaiter().GetResult();
				return loadResult.PEFile;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Trace.TraceError(ex.ToString());
				return null;
			}
		}

		ICompilation typeSystem;

		/// <summary>
		/// Gets a type system containing all types from this assembly + primitive types from mscorlib.
		/// Returns null in case of load errors.
		/// </summary>
		/// <remarks>
		/// This is an uncached type system.
		/// </remarks>
		public ICompilation GetTypeSystemOrNull()
		{
			return LazyInitializer.EnsureInitialized(ref this.typeSystem, () => {
				var module = GetPEFileOrNull();
				if (module == null)
					return null;
				return new SimpleCompilation(
					module.WithOptions(TypeSystemOptions.Default | TypeSystemOptions.Uncached | TypeSystemOptions.KeepModifiers),
					MinimalCorlib.Instance);
			});
		}

		readonly object typeSystemWithOptionsLockObj = new object();
		ICompilation typeSystemWithOptions;
		TypeSystemOptions currentTypeSystemOptions;

		public ICompilation GetTypeSystemOrNull(TypeSystemOptions options)
		{
			lock (typeSystemWithOptionsLockObj)
			{
				if (typeSystemWithOptions != null && options == currentTypeSystemOptions)
					return typeSystemWithOptions;
				var module = GetPEFileOrNull();
				if (module == null)
					return null;
				currentTypeSystemOptions = options;
				return typeSystemWithOptions = new SimpleCompilation(
					module.WithOptions(options | TypeSystemOptions.Uncached | TypeSystemOptions.KeepModifiers),
					MinimalCorlib.Instance);
			}
		}

		public AssemblyList AssemblyList => assemblyList;

		public string FileName => fileName;

		public string ShortName => shortName;

		public string Text {
			get {
				if (IsLoaded && !HasLoadError)
				{
					PEFile module = GetPEFileOrNull();
					var metadata = module?.Metadata;
					string versionOrInfo = null;
					if (metadata != null)
					{
						if (metadata.IsAssembly)
						{
							versionOrInfo = metadata.GetAssemblyDefinition().Version?.ToString();
							var tfId = GetTargetFrameworkIdAsync().Result;
							if (!string.IsNullOrEmpty(tfId))
								versionOrInfo += ", " + tfId.Replace("Version=", " ");
						}
						else
						{
							versionOrInfo = ".netmodule";
						}
					}
					if (versionOrInfo == null)
						return ShortName;
					return string.Format("{0} ({1})", ShortName, versionOrInfo);
				}
				else
				{
					return ShortName;
				}
			}
		}

		/// <summary>
		/// Gets whether loading finished for this file (either successfully or unsuccessfully).
		/// </summary>
		public bool IsLoaded => loadingTask.IsCompleted;

		/// <summary>
		/// Gets whether this file was loaded successfully as an assembly (not as a bundle).
		/// </summary>
		public bool IsLoadedAsValidAssembly {
			get {
				return loadingTask.Status == TaskStatus.RanToCompletion && loadingTask.Result.PEFile != null;
			}
		}

		/// <summary>
		/// Gets whether loading failed (file does not exist, unknown file format).
		/// Returns false for valid assemblies and valid bundles.
		/// </summary>
		public bool HasLoadError => loadingTask.IsFaulted;

		public bool IsAutoLoaded { get; set; }

		/// <summary>
		/// Gets the PDB file name or null, if no PDB was found or it's embedded.
		/// </summary>
		public string PdbFileName { get; private set; }

		async Task<LoadResult> LoadAsync(Task<Stream> streamTask)
		{
			// runs on background thread
			if (streamTask != null)
			{
				var stream = await streamTask;
				// Read the module from a precrafted stream
				if (!stream.CanSeek)
				{
					var memoryStream = new MemoryStream();
					stream.CopyTo(memoryStream);
					stream.Close();
					memoryStream.Position = 0;
					stream = memoryStream;
				}
				var streamOptions = stream is MemoryStream ? PEStreamOptions.PrefetchEntireImage : PEStreamOptions.Default;
				return LoadAssembly(stream, streamOptions);
			}
			// Read the module from disk
			PEFileNotSupportedException loadAssemblyException;
			try
			{
				using (var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read))
				{
					return LoadAssembly(fileStream, PEStreamOptions.PrefetchEntireImage);
				}
			}
			catch (PEFileNotSupportedException ex)
			{
				loadAssemblyException = ex;
			}
			// If it's not a .NET module, maybe it's a single-file bundle
			var bundle = LoadedPackage.FromBundle(fileName);
			if (bundle != null)
			{
				bundle.LoadedAssembly = this;
				return new LoadResult(loadAssemblyException, bundle);
			}
			// If it's not a .NET module, maybe it's a zip archive (e.g. .nupkg)
			try
			{
				var zip = LoadedPackage.FromZipFile(fileName);
				zip.LoadedAssembly = this;
				return new LoadResult(loadAssemblyException, zip);
			}
			catch (InvalidDataException)
			{
				throw loadAssemblyException;
			}
		}

		LoadResult LoadAssembly(Stream stream, PEStreamOptions streamOptions)
		{
			MetadataReaderOptions options;
			if (DecompilerSettingsPanel.CurrentDecompilerSettings.ApplyWindowsRuntimeProjections)
			{
				options = MetadataReaderOptions.ApplyWindowsRuntimeProjections;
			}
			else
			{
				options = MetadataReaderOptions.None;
			}

			PEFile module = new PEFile(fileName, stream, streamOptions, metadataOptions: options);

			debugInfoProvider = LoadDebugInfo(module);
			lock (loadedAssemblies)
			{
				loadedAssemblies.Add(module, this);
			}
			return new LoadResult(module);
		}

		IDebugInfoProvider LoadDebugInfo(PEFile module)
		{
			if (DecompilerSettingsPanel.CurrentDecompilerSettings.UseDebugSymbols)
			{
				try
				{
					return DebugInfoUtils.FromFile(module, PdbFileName)
						?? DebugInfoUtils.LoadSymbols(module);
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
				catch (InvalidOperationException)
				{
					// ignore any errors during symbol loading
				}
			}
			return null;
		}

		public async Task<IDebugInfoProvider> LoadDebugInfo(string fileName)
		{
			this.PdbFileName = fileName;
			var assembly = await GetPEFileAsync().ConfigureAwait(false);
			debugInfoProvider = await Task.Run(() => LoadDebugInfo(assembly));
			return debugInfoProvider;
		}

		[ThreadStatic]
		static int assemblyLoadDisableCount;

		public static IDisposable DisableAssemblyLoad(AssemblyList assemblyList)
		{
			assemblyLoadDisableCount++;
			return new DecrementAssemblyLoadDisableCount(assemblyList);
		}

		public static IDisposable DisableAssemblyLoad()
		{
			assemblyLoadDisableCount++;
			return new DecrementAssemblyLoadDisableCount(MainWindow.Instance.CurrentAssemblyList);
		}

		sealed class DecrementAssemblyLoadDisableCount : IDisposable
		{
			bool disposed;
			AssemblyList assemblyList;

			public DecrementAssemblyLoadDisableCount(AssemblyList assemblyList)
			{
				this.assemblyList = assemblyList;
			}

			public void Dispose()
			{
				if (!disposed)
				{
					disposed = true;
					assemblyLoadDisableCount--;
					// clear the lookup cache since we might have stored the lookups failed due to DisableAssemblyLoad()
					assemblyList.ClearCache();
				}
			}
		}

		sealed class MyAssemblyResolver : IAssemblyResolver
		{
			readonly LoadedAssembly parent;

			public MyAssemblyResolver(LoadedAssembly parent)
			{
				this.parent = parent;
			}

			public PEFile Resolve(IAssemblyReference reference)
			{
				var module = parent.providedAssemblyResolver?.Resolve(reference);
				if (module != null)
					return module;
				return parent.LookupReferencedAssembly(reference)?.GetPEFileOrNull();
			}

			public PEFile ResolveModule(PEFile mainModule, string moduleName)
			{
				var module = parent.providedAssemblyResolver?.ResolveModule(mainModule, moduleName);
				if (module != null)
					return module;
				return parent.LookupReferencedModule(mainModule, moduleName)?.GetPEFileOrNull();
			}
		}

		readonly MyAssemblyResolver resolver;

		public IAssemblyResolver GetAssemblyResolver()
		{
			return resolver;
		}

		public AssemblyReferenceClassifier GetAssemblyReferenceClassifier()
		{
			return universalResolver;
		}

		/// <summary>
		/// Returns the debug info for this assembly. Returns null in case of load errors or no debug info is available.
		/// </summary>
		public IDebugInfoProvider GetDebugInfoOrNull()
		{
			if (GetPEFileOrNull() == null)
				return null;
			return debugInfoProvider;
		}

		public LoadedAssembly LookupReferencedAssembly(IAssemblyReference reference)
		{
			if (reference == null)
				throw new ArgumentNullException(nameof(reference));
			var tfm = GetTargetFrameworkIdAsync().Result;
			if (reference.IsWindowsRuntime)
			{
				return assemblyList.assemblyLookupCache.GetOrAdd((reference.Name, true, tfm), key => LookupReferencedAssemblyInternal(reference, true, tfm));
			}
			else
			{
				return assemblyList.assemblyLookupCache.GetOrAdd((reference.FullName, false, tfm), key => LookupReferencedAssemblyInternal(reference, false, tfm));
			}
		}

		public LoadedAssembly LookupReferencedModule(PEFile mainModule, string moduleName)
		{
			if (mainModule == null)
				throw new ArgumentNullException(nameof(mainModule));
			if (moduleName == null)
				throw new ArgumentNullException(nameof(moduleName));
			return assemblyList.moduleLookupCache.GetOrAdd(mainModule.FileName + ";" + moduleName, _ => LookupReferencedModuleInternal(mainModule, moduleName));
		}

		class MyUniversalResolver : UniversalAssemblyResolver
		{
			public MyUniversalResolver(LoadedAssembly assembly)
				: base(assembly.FileName, false, assembly.GetTargetFrameworkIdAsync().Result, PEStreamOptions.PrefetchEntireImage, DecompilerSettingsPanel.CurrentDecompilerSettings.ApplyWindowsRuntimeProjections ? MetadataReaderOptions.ApplyWindowsRuntimeProjections : MetadataReaderOptions.None)
			{
			}
		}

		static readonly Dictionary<string, LoadedAssembly> loadingAssemblies = new Dictionary<string, LoadedAssembly>();
		MyUniversalResolver universalResolver;

		/// <summary>
		/// 0) if we're inside a package, look for filename.dll in parent directories
		///    (this step already happens in MyAssemblyResolver; not in LookupReferencedAssembly)
		/// 1) try to find exact match by tfm + full asm name in loaded assemblies
		/// 2) try to find match in search paths
		/// 3) if a.deps.json is found: search %USERPROFILE%/.nuget/packages/* as well
		/// 4) look in /dotnet/shared/{runtime-pack}/{closest-version}
		/// 5) if the version is retargetable or all zeros or ones, search C:\Windows\Microsoft.NET\Framework64\v4.0.30319
		/// 6) For "mscorlib.dll" we use the exact same assembly with which ILSpy runs
		/// 7) Search the GAC
		/// 8) search C:\Windows\Microsoft.NET\Framework64\v4.0.30319
		/// 9) try to find match by asm name (no tfm/version) in loaded assemblies
		/// </summary>
		LoadedAssembly LookupReferencedAssemblyInternal(IAssemblyReference fullName, bool isWinRT, string tfm)
		{
			string key = tfm + ";" + (isWinRT ? fullName.Name : fullName.FullName);

			string file;
			LoadedAssembly asm;
			lock (loadingAssemblies)
			{
				foreach (LoadedAssembly loaded in assemblyList.GetAssemblies())
				{
					try
					{
						var module = loaded.GetPEFileOrNull();
						var reader = module?.Metadata;
						if (reader == null || !reader.IsAssembly)
							continue;
						var asmDef = reader.GetAssemblyDefinition();
						var asmDefName = loaded.GetTargetFrameworkIdAsync().Result + ";"
							+ (isWinRT ? reader.GetString(asmDef.Name) : reader.GetFullAssemblyName());
						if (key.Equals(asmDefName, StringComparison.OrdinalIgnoreCase))
						{
							LoadedAssemblyReferencesInfo.AddMessageOnce(fullName.FullName, MessageKind.Info, "Success - Found in Assembly List");
							return loaded;
						}
					}
					catch (BadImageFormatException)
					{
						continue;
					}
				}

				if (universalResolver == null)
				{
					universalResolver = new MyUniversalResolver(this);
				}

				file = universalResolver.FindAssemblyFile(fullName);

				foreach (LoadedAssembly loaded in assemblyList.GetAssemblies())
				{
					if (loaded.FileName.Equals(file, StringComparison.OrdinalIgnoreCase))
					{
						return loaded;
					}
				}

				if (file != null && loadingAssemblies.TryGetValue(file, out asm))
					return asm;

				if (assemblyLoadDisableCount > 0)
					return null;

				if (file != null)
				{
					LoadedAssemblyReferencesInfo.AddMessage(fullName.ToString(), MessageKind.Info, "Success - Loading from: " + file);
					asm = new LoadedAssembly(assemblyList, file) { IsAutoLoaded = true };
				}
				else
				{
					var candidates = new List<(LoadedAssembly assembly, Version version)>();

					foreach (LoadedAssembly loaded in assemblyList.GetAssemblies())
					{
						var module = loaded.GetPEFileOrNull();
						var reader = module?.Metadata;
						if (reader == null || !reader.IsAssembly)
							continue;
						var asmDef = reader.GetAssemblyDefinition();
						var asmDefName = reader.GetString(asmDef.Name);
						if (fullName.Name.Equals(asmDefName, StringComparison.OrdinalIgnoreCase))
						{
							candidates.Add((loaded, asmDef.Version));
						}
					}

					if (candidates.Count == 0)
					{
						LoadedAssemblyReferencesInfo.AddMessageOnce(fullName.ToString(), MessageKind.Error, "Could not find reference: " + fullName);
						return null;
					}

					candidates.SortBy(c => c.version);

					var bestCandidate = candidates.FirstOrDefault(c => c.version >= fullName.Version).assembly ?? candidates.Last().assembly;
					LoadedAssemblyReferencesInfo.AddMessageOnce(fullName.ToString(), MessageKind.Info, "Success - Found in Assembly List with different TFM or version: " + bestCandidate.fileName);
					return bestCandidate;
				}
				loadingAssemblies.Add(file, asm);
			}
			App.Current.Dispatcher.BeginInvoke((Action)delegate () {
				lock (assemblyList.assemblies)
				{
					assemblyList.assemblies.Add(asm);
				}
				lock (loadingAssemblies)
				{
					loadingAssemblies.Remove(file);
				}
			}, DispatcherPriority.Normal);
			return asm;
		}

		LoadedAssembly LookupReferencedModuleInternal(PEFile mainModule, string moduleName)
		{
			string file;
			LoadedAssembly asm;
			lock (loadingAssemblies)
			{
				foreach (LoadedAssembly loaded in assemblyList.GetAssemblies())
				{
					var reader = loaded.GetPEFileOrNull()?.Metadata;
					if (reader == null || reader.IsAssembly)
						continue;
					var moduleDef = reader.GetModuleDefinition();
					if (moduleName.Equals(reader.GetString(moduleDef.Name), StringComparison.OrdinalIgnoreCase))
					{
						LoadedAssemblyReferencesInfo.AddMessageOnce(moduleName, MessageKind.Info, "Success - Found in Assembly List");
						return loaded;
					}
				}

				file = Path.Combine(Path.GetDirectoryName(mainModule.FileName), moduleName);
				if (!File.Exists(file))
					return null;

				foreach (LoadedAssembly loaded in assemblyList.GetAssemblies())
				{
					if (loaded.FileName.Equals(file, StringComparison.OrdinalIgnoreCase))
					{
						return loaded;
					}
				}

				if (file != null && loadingAssemblies.TryGetValue(file, out asm))
					return asm;

				if (assemblyLoadDisableCount > 0)
					return null;

				if (file != null)
				{
					LoadedAssemblyReferencesInfo.AddMessage(moduleName, MessageKind.Info, "Success - Loading from: " + file);
					asm = new LoadedAssembly(assemblyList, file) { IsAutoLoaded = true };
				}
				else
				{
					LoadedAssemblyReferencesInfo.AddMessageOnce(moduleName, MessageKind.Error, "Could not find reference: " + moduleName);
					return null;
				}
				loadingAssemblies.Add(file, asm);
			}
			App.Current.Dispatcher.BeginInvoke((Action)delegate () {
				lock (assemblyList.assemblies)
				{
					assemblyList.assemblies.Add(asm);
				}
				lock (loadingAssemblies)
				{
					loadingAssemblies.Remove(file);
				}
			});
			return asm;
		}

		[Obsolete("Use GetPEFileAsync() or GetLoadResultAsync() instead")]
		public Task ContinueWhenLoaded(Action<Task<PEFile>> onAssemblyLoaded, TaskScheduler taskScheduler)
		{
			return this.GetPEFileAsync().ContinueWith(onAssemblyLoaded, default(CancellationToken), TaskContinuationOptions.RunContinuationsAsynchronously, taskScheduler);
		}

		/// <summary>
		/// Wait until the assembly is loaded.
		/// Throws an AggregateException when loading the assembly fails.
		/// </summary>
		public void WaitUntilLoaded()
		{
			loadingTask.Wait();
		}
	}
}
