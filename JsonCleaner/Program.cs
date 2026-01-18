using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class Program
{
	static void Main(string[] args)
	{
		if (args.Length < 2)
		{
			Console.WriteLine("Usage: JsonCleanerTool <input.json> <output.json>");
			return;
		}

		string inputPath = args[0];
		string outputPath = args[1];

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
		var seen = new HashSet<string>();
		var duplicatePaths = new HashSet<string>();
		var duplicates = new List<JToken>();

		foreach (var file in filesToken)
		{
			var path = file?["_path"]?.ToString();
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
			File.WriteAllText(outputPath, originalContent);
			Console.WriteLine($"Cleaned JSON written to {outputPath}");
			return;
		}

		// Remove duplicates while preserving comments and formatting
		string cleanedContent = RemoveDuplicateEntries(originalContent, duplicatePaths);

		File.WriteAllText(outputPath, cleanedContent);
		Console.WriteLine($"Cleaned JSON written to {outputPath}");
	}

	static string RemoveDuplicateEntries(string jsonContent, HashSet<string> duplicatePaths)
	{
		var result = new System.Text.StringBuilder();
		var lines = jsonContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

		bool inFilesArray = false;
		bool inObject = false;
		int braceDepth = 0;
		int currentObjectStart = -1;
		string currentPath = null;
		var pathsToRemove = new HashSet<string>(duplicatePaths);
		var seenPaths = new HashSet<string>();
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
}
