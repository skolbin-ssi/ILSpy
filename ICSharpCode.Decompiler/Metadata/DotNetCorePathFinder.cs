﻿// Copyright (c) 2018 Siegfried Pammer
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
using System.Runtime.InteropServices;
using System.Text;
using ICSharpCode.Decompiler.Util;
using LightJson.Serialization;

namespace ICSharpCode.Decompiler.Metadata
{
	public class DotNetCorePathFinder
	{
		class DotNetCorePackageInfo
		{
			public readonly string Name;
			public readonly string Version;
			public readonly string Type;
			public readonly string Path;
			public readonly string[] RuntimeComponents;

			public DotNetCorePackageInfo(string fullName, string type, string path, string[] runtimeComponents)
			{
				var parts = fullName.Split('/');
				this.Name = parts[0];
				this.Version = parts[1];
				this.Type = type;
				this.Path = path;
				this.RuntimeComponents = runtimeComponents ?? Empty<string>.Array;
			}
		}

		static readonly string[] LookupPaths = new string[] {
			 Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
		};

		static readonly string[] RuntimePacks = new[] {
			"Microsoft.NETCore.App",
			"Microsoft.WindowsDesktop.App",
			"Microsoft.AspNetCore.App",
			"Microsoft.AspNetCore.All"
		};

		readonly DotNetCorePackageInfo[] packages;
		ISet<string> packageBasePaths = new HashSet<string>(StringComparer.Ordinal);
		readonly Version version;
		readonly string dotnetBasePath = FindDotNetExeDirectory();

		public DotNetCorePathFinder(Version version)
		{
			this.version = version;
		}

		public DotNetCorePathFinder(string parentAssemblyFileName, string targetFrameworkIdString, TargetFrameworkIdentifier targetFramework, Version version, ReferenceLoadInfo loadInfo = null)
		{
			string assemblyName = Path.GetFileNameWithoutExtension(parentAssemblyFileName);
			string basePath = Path.GetDirectoryName(parentAssemblyFileName);
			this.version = version;

			if (targetFramework == TargetFrameworkIdentifier.NETStandard) {
				// .NET Standard 2.1 is implemented by .NET Core 3.0 or higher
				if (version.Major == 2 && version.Minor == 1) {
					this.version = new Version(3, 0, 0);
				}
			}

			var depsJsonFileName = Path.Combine(basePath, $"{assemblyName}.deps.json");
			if (!File.Exists(depsJsonFileName)) {
				loadInfo?.AddMessage(assemblyName, MessageKind.Warning, $"{assemblyName}.deps.json could not be found!");
				return;
			}

			packages = LoadPackageInfos(depsJsonFileName, targetFrameworkIdString).ToArray();

			foreach (var path in LookupPaths) {
				foreach (var p in packages) {
					foreach (var item in p.RuntimeComponents) {
						var itemPath = Path.GetDirectoryName(item);
						var fullPath = Path.Combine(path, p.Name, p.Version, itemPath).ToLowerInvariant();
						if (Directory.Exists(fullPath))
							packageBasePaths.Add(fullPath);
					}
				}
			}
		}

		public string TryResolveDotNetCore(IAssemblyReference name)
		{
			foreach (var basePath in packageBasePaths) {
				if (File.Exists(Path.Combine(basePath, name.Name + ".dll"))) {
					return Path.Combine(basePath, name.Name + ".dll");
				} else if (File.Exists(Path.Combine(basePath, name.Name + ".exe"))) {
					return Path.Combine(basePath, name.Name + ".exe");
				}
			}

			return FallbackToDotNetSharedDirectory(name, version);
		}

		internal string GetReferenceAssemblyPath(string targetFramework)
		{
			var (tfi, version) = UniversalAssemblyResolver.ParseTargetFramework(targetFramework);
			string identifier, identifierExt;
			switch (tfi) {
				case TargetFrameworkIdentifier.NETCoreApp:
					identifier = "Microsoft.NETCore.App";
					identifierExt = "netcoreapp" + version.Major + "." + version.Minor;
					break;
				case TargetFrameworkIdentifier.NETStandard:
					identifier = "NETStandard.Library";
					identifierExt = "netstandard" + version.Major + "." + version.Minor;
					break;
				default:
					throw new NotSupportedException();
			}
			return Path.Combine(dotnetBasePath, "packs", identifier + ".Ref", version.ToString(), "ref", identifierExt);
		}

		static IEnumerable<DotNetCorePackageInfo> LoadPackageInfos(string depsJsonFileName, string targetFramework)
		{
			var dependencies = JsonReader.Parse(File.ReadAllText(depsJsonFileName));
			var runtimeInfos = dependencies["targets"][targetFramework].AsJsonObject;
			var libraries = dependencies["libraries"].AsJsonObject;
			if (runtimeInfos == null || libraries == null)
				yield break;
			foreach (var library in libraries) {
				var type = library.Value["type"].AsString;
				var path = library.Value["path"].AsString;
				var runtimeInfo = runtimeInfos[library.Key].AsJsonObject?["runtime"].AsJsonObject;
				string[] components = new string[runtimeInfo?.Count ?? 0];
				if (runtimeInfo != null) {
					int i = 0;
					foreach (var component in runtimeInfo) {
						components[i] = component.Key;
						i++;
					}
				}
				yield return new DotNetCorePackageInfo(library.Key, type, path, components);
			}
		}

		string FallbackToDotNetSharedDirectory(IAssemblyReference name, Version version)
		{
			if (dotnetBasePath == null)
				return null;
			var basePaths = RuntimePacks.Select(pack => Path.Combine(dotnetBasePath, "shared", pack));
			foreach (var basePath in basePaths) {
				if (!Directory.Exists(basePath))
					continue;
				var closestVersion = GetClosestVersionFolder(basePath, version);
				if (File.Exists(Path.Combine(basePath, closestVersion, name.Name + ".dll"))) {
					return Path.Combine(basePath, closestVersion, name.Name + ".dll");
				} else if (File.Exists(Path.Combine(basePath, closestVersion, name.Name + ".exe"))) {
					return Path.Combine(basePath, closestVersion, name.Name + ".exe");
				}
			}
			return null;
		}

		static string GetClosestVersionFolder(string basePath, Version version)
		{
			string result = null;
			foreach (var folder in new DirectoryInfo(basePath).GetDirectories().Select(d => ConvertToVersion(d.Name)).Where(v => v.Item1 != null).OrderByDescending(v => v.Item1)) {
				if (folder.Item1 >= version)
					result = folder.Item2;
			}
			return result ?? version.ToString();
		}

		internal static (Version, string) ConvertToVersion(string name)
		{
			string RemoveTrailingVersionInfo()
			{
				string shortName = name;
				int dashIndex = shortName.IndexOf('-');
				if (dashIndex > 0) {
					shortName = shortName.Remove(dashIndex);
				}
				return shortName;
			}

			try {
				return (new Version(RemoveTrailingVersionInfo()), name);
			} catch (Exception ex) {
				Trace.TraceWarning(ex.ToString());
				return (null, null);
			}
		}

		static string FindDotNetExeDirectory()
		{
			string dotnetExeName = (Environment.OSVersion.Platform == PlatformID.Unix) ? "dotnet" : "dotnet.exe";
			foreach (var item in Environment.GetEnvironmentVariable("PATH").Split(Path.PathSeparator)) {
				try {
					string fileName = Path.Combine(item, dotnetExeName);
					if (!File.Exists(fileName))
						continue;
					if (Environment.OSVersion.Platform == PlatformID.Unix) {
						if ((new FileInfo(fileName).Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) {
							var sb = new StringBuilder();
							realpath(fileName, sb);
							fileName = sb.ToString();
							if (!File.Exists(fileName))
								continue;
						}
					}
					return Path.GetDirectoryName(fileName);
				} catch (ArgumentException) { }
			}
			return null;
		}

		[DllImport("libc")]
		static extern void realpath(string path, StringBuilder resolvedPath);
	}
}
