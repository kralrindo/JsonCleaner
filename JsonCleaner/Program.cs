using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class Program
{
	static void Main(string[] args)
	{
		if (args.Length < 1)
		{
			Console.WriteLine("Usage: JsonCleanerTool <input.json> [output.json] [--reorder]");
			Console.WriteLine("       JsonCleanerTool <json1.json> <json2.json> [output.json]");
			Console.WriteLine("  --reorder    Reorder assets by _type (stgs, stlt, dtbl, matl, shds, shdr, txan, txtr, arig, mdl_)");
			Console.WriteLine();
			Console.WriteLine("Single file mode: Removes duplicate assets within the file.");
			Console.WriteLine("Dual file mode:   Removes assets from json2 that exist in json1. Writes to output or overwrites json2.");
			return;
		}

		// Parse arguments to find JSON files and options
		var jsonFiles = new List<string>();
		var options = new List<string>();

		foreach (var arg in args)
		{
			if (arg.StartsWith("-"))
			{
				options.Add(arg);
			}
			else
			{
				jsonFiles.Add(arg);
			}
		}

		// Dual file mode: two or three JSON files provided
		if (jsonFiles.Count >= 2 && jsonFiles.Count <= 3 && !options.Contains("--reorder"))
		{
			string json1Path = jsonFiles[0];
			string json2Path = jsonFiles[1];
			string? dualOutputPath = jsonFiles.Count == 3 ? jsonFiles[2] : null;

			if (!File.Exists(json1Path))
			{
				Console.WriteLine($"File not found: {json1Path}");
				return;
			}
			if (!File.Exists(json2Path))
			{
				Console.WriteLine($"File not found: {json2Path}");
				return;
			}

			ProcessDualFileMode(json1Path, json2Path, dualOutputPath);
			return;
		}

		// Single file mode
		string inputPath = jsonFiles[0];
		string? outputPath = null;
		bool reorderTypes = false;

		if (jsonFiles.Count > 1)
		{
			outputPath = jsonFiles[1];
		}

		if (options.Contains("--reorder", StringComparer.OrdinalIgnoreCase))
		{
			reorderTypes = true;
		}

		if (outputPath == null)
		{
			outputPath = inputPath; // Overwrite input file if no output specified
		}

		if (!File.Exists(inputPath))
		{
			Console.WriteLine($"Input file not found: {inputPath}");
			return;
		}

		// Read the original file content
		var originalContent = File.ReadAllText(inputPath);

		// Parse JSON to find which paths are duplicates
		var settings = new JsonLoadSettings { CommentHandling = CommentHandling.Load, LineInfoHandling = LineInfoHandling.Load };
		JObject root;
		try
		{
			root = JObject.Parse(originalContent, settings);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to parse JSON: {ex.Message}");
			return;
		}

		var filesToken = root["files"];
		if (filesToken == null || filesToken.Type != JTokenType.Array)
		{
			Console.WriteLine("Invalid JSON format: missing or invalid 'files' array");
			return;
		}

		// Find duplicate paths
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var duplicatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var duplicates = new List<JToken>();

		foreach (var file in filesToken)
		{
			if (file == null || file.Type != JTokenType.Object)
			{
				continue;
			}
			var path = file["_path"]?.ToString();
			if (path == null)
			{
				continue;
			}
			if (!seen.Add(path))
			{
				duplicatePaths.Add(path);
				duplicates.Add(file);
			}
		}

		if (duplicates.Count > 0)
		{
			Console.WriteLine($"Found {duplicates.Count} duplicate assets. Removing:");
			foreach (var dup in duplicates)
			{
				Console.WriteLine(dup.ToString(Formatting.Indented));
			}
		}
		else
		{
			Console.WriteLine("No duplicate assets found.");
			if (reorderTypes)
			{
				string reorderedContent = ReorderByAssetType(originalContent);
				File.WriteAllText(outputPath, reorderedContent);
				Console.WriteLine($"Reordered JSON written to {outputPath}");
			}
			else
			{
				File.WriteAllText(outputPath, originalContent);
				Console.WriteLine($"Cleaned JSON written to {outputPath}");
			}
			return;
		}

		// Remove duplicates while preserving comments and formatting
		string cleanedContent = RemoveDuplicateEntries(originalContent, duplicatePaths);

		if (reorderTypes)
		{
			cleanedContent = ReorderByAssetType(cleanedContent);
			Console.WriteLine($"Cleaned and reordered JSON written to {outputPath}");
		}
		else
		{
			Console.WriteLine($"Cleaned JSON written to {outputPath}");
		}

		File.WriteAllText(outputPath, cleanedContent);
	}

	static void ProcessDualFileMode(string json1Path, string json2Path, string? outputPath)
	{
		string targetPath = outputPath ?? json2Path;
		Console.WriteLine($"Dual file mode: Checking {json2Path} against {json1Path}");

		// Read both files
		var json1Content = File.ReadAllText(json1Path);
		var json2Content = File.ReadAllText(json2Path);

		// Parse json1 to get all paths
		var settings = new JsonLoadSettings { CommentHandling = CommentHandling.Load, LineInfoHandling = LineInfoHandling.Load };
		JObject root1;
		try
		{
			root1 = JObject.Parse(json1Content, settings);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to parse {json1Path}: {ex.Message}");
			return;
		}

		var files1 = root1["files"];
		if (files1 == null || files1.Type != JTokenType.Array)
		{
			Console.WriteLine($"Invalid JSON format in {json1Path}: missing or invalid 'files' array");
			return;
		}

		// Collect all paths from json1
		var pathsInJson1 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var file in files1)
		{
			if (file != null && file.Type == JTokenType.Object)
			{
				var path = file["_path"]?.ToString();
				if (path != null)
				{
					pathsInJson1.Add(path);
				}
			}
		}

		Console.WriteLine($"Found {pathsInJson1.Count} assets in {json1Path}");

		// Parse json2 to find duplicates
		JObject root2;
		try
		{
			root2 = JObject.Parse(json2Content, settings);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to parse {json2Path}: {ex.Message}");
			return;
		}

		var files2 = root2["files"];
		if (files2 == null || files2.Type != JTokenType.Array)
		{
			Console.WriteLine($"Invalid JSON format in {json2Path}: missing or invalid 'files' array");
			return;
		}

		// Find paths in json2 that exist in json1
		var duplicatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var duplicates = new List<JToken>();

		foreach (var file in files2)
		{
			if (file != null && file.Type == JTokenType.Object)
			{
				var path = file["_path"]?.ToString();
				if (path != null && pathsInJson1.Contains(path))
				{
					duplicatePaths.Add(path);
					duplicates.Add(file);
				}
			}
		}

		if (duplicates.Count > 0)
		{
			Console.WriteLine($"Found {duplicates.Count} duplicate assets in {json2Path}. Removing:");
			foreach (var dup in duplicates)
			{
				Console.WriteLine(dup.ToString(Formatting.Indented));
			}

			// Remove duplicates from json2
			string cleanedContent = RemoveEntriesByPaths(json2Content, duplicatePaths);
			File.WriteAllText(targetPath, cleanedContent);
			Console.WriteLine($"Removed {duplicates.Count} duplicates from {json2Path} -> {targetPath}");
		}
		else
		{
			Console.WriteLine($"No duplicate assets found. {json2Path} remains unchanged.");
		}
	}

	static string RemoveDuplicateEntries(string jsonContent, HashSet<string> duplicatePaths)
	{
		var result = new System.Text.StringBuilder();
		var lines = jsonContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

		bool inFilesArray = false;
		bool inObject = false;
		int braceDepth = 0;
		int currentObjectStart = -1;
		string? currentPath = null;
		var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var linesToRemove = new HashSet<int>();

		for (int i = 0; i < lines.Length; i++)
		{
			var line = lines[i];

			// Track if we're in the files array
			if (line.Contains("\"files\""))
			{
				inFilesArray = true;
			}

			// Track brace depth to identify objects
			int openBraces = CountOccurrences(line, '{');
			int closeBraces = CountOccurrences(line, '}');

			if (inFilesArray && openBraces > 0 && !inObject)
			{
				inObject = true;
				currentObjectStart = i;
				braceDepth = openBraces - closeBraces;
			}
			else if (inObject)
			{
				braceDepth += openBraces - closeBraces;
			}

			// Extract path from current object
			if (inObject && line.Contains("\"_path\""))
			{
				var match = Regex.Match(line, "\"_path\"\\s*:\\s*\"([^\"]+)\"");
				if (match.Success)
				{
					currentPath = match.Groups[1].Value;
				}
			}

			// Object ended
			if (inObject && braceDepth == 0 && closeBraces > 0)
			{
				if (currentPath != null)
				{
					if (seenPaths.Contains(currentPath))
					{
						// Mark all lines of this object for removal (including comments before it)
						int startLine = currentObjectStart;
						// Include any blank lines or comments before this object
						while (startLine > 0 && IsBlankOrComment(lines[startLine - 1]))
						{
							startLine--;
						}
						for (int j = startLine; j <= i; j++)
						{
							linesToRemove.Add(j);
						}
					}
					else
					{
						seenPaths.Add(currentPath);
					}
				}
				inObject = false;
				currentPath = null;
				currentObjectStart = -1;
			}
		}

		// Build output, skipping removed lines
		for (int i = 0; i < lines.Length; i++)
		{
			if (!linesToRemove.Contains(i))
			{
				result.AppendLine(lines[i]);
			}
		}

		return result.ToString();
	}

	static string RemoveEntriesByPaths(string jsonContent, HashSet<string> pathsToRemove)
	{
		var result = new System.Text.StringBuilder();
		var lines = jsonContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

		bool inFilesArray = false;
		bool inObject = false;
		int braceDepth = 0;
		int currentObjectStart = -1;
		string? currentPath = null;
		var linesToRemove = new HashSet<int>();

		for (int i = 0; i < lines.Length; i++)
		{
			var line = lines[i];

			// Track if we're in the files array
			if (line.Contains("\"files\""))
			{
				inFilesArray = true;
			}

			// Track brace depth to identify objects
			int openBraces = CountOccurrences(line, '{');
			int closeBraces = CountOccurrences(line, '}');

			if (inFilesArray && openBraces > 0 && !inObject)
			{
				inObject = true;
				currentObjectStart = i;
				braceDepth = openBraces - closeBraces;
			}
			else if (inObject)
			{
				braceDepth += openBraces - closeBraces;
			}

			// Extract path from current object
			if (inObject && line.Contains("\"_path\""))
			{
				var match = Regex.Match(line, "\"_path\"\\s*:\\s*\"([^\"]+)\"");
				if (match.Success)
				{
					currentPath = match.Groups[1].Value;
				}
			}

			// Object ended
			if (inObject && braceDepth == 0 && closeBraces > 0)
			{
				if (currentPath != null && pathsToRemove.Contains(currentPath))
				{
					// Mark all lines of this object for removal (including comments before it)
					int startLine = currentObjectStart;
					// Include any blank lines or comments before this object
					while (startLine > 0 && IsBlankOrComment(lines[startLine - 1]))
					{
						startLine--;
					}
					for (int j = startLine; j <= i; j++)
					{
						linesToRemove.Add(j);
					}
				}
				inObject = false;
				currentPath = null;
				currentObjectStart = -1;
			}
		}

		// Build output, skipping removed lines
		for (int i = 0; i < lines.Length; i++)
		{
			if (!linesToRemove.Contains(i))
			{
				result.AppendLine(lines[i]);
			}
		}

		return result.ToString();
	}

	static int CountOccurrences(string str, char c)
	{
		int count = 0;
		foreach (char ch in str)
		{
			if (ch == c)
				count++;
		}
		return count;
	}

	static bool IsBlankOrComment(string line)
	{
		var trimmed = line.Trim();
		return string.IsNullOrEmpty(trimmed) ||
		       trimmed.StartsWith("//") ||
		       trimmed.StartsWith("/*") ||
		       trimmed.StartsWith("*");
	}

	// Asset type order for reordering
	static readonly string[] AssetTypeOrder = { "stgs", "stlt", "dtbl", "matl", "shds", "shdr", "txan", "txtr", "arig", "mdl_" };

	static string ReorderByAssetType(string jsonContent)
	{
		var lines = jsonContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
		var result = new System.Text.StringBuilder();

		// Find the files array start and end
		int filesStart = -1;
		int filesEnd = -1;
		int bracketDepth = 0;

		for (int i = 0; i < lines.Length; i++)
		{
			if (lines[i].Contains("\"files\""))
			{
				filesStart = i;
				bracketDepth = 0;
			}
			if (filesStart >= 0)
			{
				bracketDepth += CountOccurrences(lines[i], '[') - CountOccurrences(lines[i], ']');
				if (bracketDepth == 0 && lines[i].Contains("]"))
				{
					filesEnd = i;
					break;
				}
			}
		}

		if (filesStart < 0 || filesEnd < 0)
		{
			return jsonContent; // Can't find files array, return as-is
		}

		// Output everything before files array
		for (int i = 0; i <= filesStart; i++)
		{
			result.AppendLine(lines[i]);
		}

		// Parse all asset objects with their types
		var assetsByType = new Dictionary<string, List<string[]>>();
		foreach (var type in AssetTypeOrder)
		{
			assetsByType[type] = new List<string[]>();
		}
		assetsByType["other"] = new List<string[]>(); // For unknown types

		bool inObject = false;
		int objectBraceDepth = 0;
		int currentObjectStart = -1;
		string? currentType = null;

		for (int i = filesStart + 1; i < filesEnd; i++)
		{
			var line = lines[i];

			int openBraces = CountOccurrences(line, '{');
			int closeBraces = CountOccurrences(line, '}');

			if (openBraces > 0 && !inObject)
			{
				inObject = true;
				currentObjectStart = i;
				objectBraceDepth = openBraces - closeBraces;
			}
			else if (inObject)
			{
				objectBraceDepth += openBraces - closeBraces;
			}

			// Extract type from current object
			if (inObject && line.Contains("\"_type\""))
			{
				var match = Regex.Match(line, "\"_type\"\\s*:\\s*\"([^\"]+)\"");
				if (match.Success)
				{
					currentType = match.Groups[1].Value;
				}
			}

			// Object ended
			if (inObject && objectBraceDepth == 0 && closeBraces > 0)
			{
				// Collect all lines for this object
				var objectLines = new List<string>();
				int start = currentObjectStart;

				// Include any comments/blank lines before the object
				while (start > filesStart + 1 && IsBlankOrComment(lines[start - 1]))
				{
					start--;
				}

				for (int j = start; j <= i; j++)
				{
					objectLines.Add(lines[j]);
				}

				string typeKey = "other";
				if (currentType != null && assetsByType.ContainsKey(currentType))
				{
					typeKey = currentType;
				}
				else if (currentType != null)
				{
					// Add unknown type to the dictionary
					if (!assetsByType.ContainsKey(currentType))
					{
						assetsByType[currentType] = new List<string[]>();
					}
					typeKey = currentType;
				}

				assetsByType[typeKey].Add(objectLines.ToArray());

				inObject = false;
				currentType = null;
				currentObjectStart = -1;
			}
		}

		// Output assets in the specified order
		foreach (var type in AssetTypeOrder)
		{
			foreach (var assetLines in assetsByType[type])
			{
				foreach (var assetLine in assetLines)
				{
					result.AppendLine(assetLine);
				}
			}
		}

		// Output any unknown types (sorted alphabetically)
		var otherTypes = assetsByType.Keys.Where(k => !AssetTypeOrder.Contains(k) && k != "other").OrderBy(k => k);
		foreach (var type in otherTypes)
		{
			foreach (var assetLines in assetsByType[type])
			{
				foreach (var assetLine in assetLines)
				{
					result.AppendLine(assetLine);
				}
			}
		}

		// Output "other" types last (assets without _type)
		foreach (var assetLines in assetsByType["other"])
		{
			foreach (var assetLine in assetLines)
			{
				result.AppendLine(assetLine);
			}
		}

		// Output the closing bracket and everything after
		result.AppendLine("  ]");
		for (int i = filesEnd + 1; i < lines.Length; i++)
		{
			result.AppendLine(lines[i]);
		}

		return result.ToString();
	}
}
