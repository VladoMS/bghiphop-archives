#!/usr/bin/env dotnet-script
#r "nuget: YoutubeExplode, 6.3.14"
#r "nuget: Slugify.Core, 5.0.0-prerelease.4"
#r "nuget: YamlDotNet, 15.1.1"

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Globalization;
using System.Threading.Tasks;
using Slugify;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var sourceDir = "_source";
var contentDir = Path.Combine("content", "video");

var slugHelper = new SlugHelper();
var slugCount = new Dictionary<string, int>();
var allSlugs = new HashSet<string>();
var http = new HttpClient();

async Task<string?> DownloadYouTubeThumbnailAsync(string ytid, string targetPath)
{
    var urls = new[]
    {
        $"https://i.ytimg.com/vi/{ytid}/maxresdefault.jpg",
        $"https://i.ytimg.com/vi/{ytid}/hqdefault.jpg"
    };

    foreach (var url in urls)
    {
        try
        {
            var response = await http.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(targetPath, bytes);
                return "cover.jpg";
            }
        }
        catch
        {
            // fallback
        }
    }

    return null;
}

foreach (var file in Directory.GetFiles(sourceDir, "data_*.yml"))
{
    var content = File.ReadAllText(file);
    var deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    var items = deserializer.Deserialize<List<Dictionary<string, object>>>(content);

    foreach (var item in items)
    {
        var title = item.ContainsKey("title") ? item["title"]?.ToString()?.Trim() ?? "" : "";
        if (string.IsNullOrWhiteSpace(title)) continue;

        var artistsList = item.ContainsKey("artists") && item["artists"] is IEnumerable<object> rawList
            ? rawList.Select(x => x?.ToString()?.Trim() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
            : new List<string>();

        var artistsString = string.Join(", ", artistsList);
        var fullTitle = !string.IsNullOrWhiteSpace(artistsString) ? $"{artistsString} - {title}" : title;

        var ytid = item.ContainsKey("ytid") ? item["ytid"]?.ToString()?.Trim() : null;
        var publishedRaw = item.ContainsKey("published_on") ? item["published_on"]?.ToString()?.Trim() : null;

        // Slugify
        var baseSlug = slugHelper.GenerateSlug($"{string.Join(" ", artistsList)} {title}");
        var slug = baseSlug;
        if (slugCount.ContainsKey(baseSlug))
        {
            slugCount[baseSlug]++;
            slug = $"{baseSlug}-{slugCount[baseSlug]}";
        }
        else
        {
            slugCount[baseSlug] = 0;
        }

        allSlugs.Add(slug);

        // Hugo Date
        string hugoDate = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        if (!string.IsNullOrEmpty(publishedRaw))
        {
            try
            {
                var parsed = DateTime.ParseExact(publishedRaw!, "MMMM d, yyyy", CultureInfo.InvariantCulture);
                var offset = new DateTimeOffset(parsed, TimeSpan.FromHours(3));
                hugoDate = offset.ToString("yyyy-MM-ddTHH:mm:sszzz");
            }
            catch
            {
                Console.WriteLine($"⚠️ Failed to parse date '{publishedRaw}' in entry: {title}");
            }
        }

        // Output paths
        var entryDir = Path.Combine(contentDir, slug!);
        var indexPath = Path.Combine(entryDir, "index.md");
        var coverPath = Path.Combine(entryDir, "cover.jpg");
        Directory.CreateDirectory(entryDir);

        // Download thumbnail
        string imageField = "";
        if (!string.IsNullOrEmpty(ytid))
        {
            var imageResult = await DownloadYouTubeThumbnailAsync(ytid!, coverPath);
            imageField = imageResult ?? "";
        }

        // Generate front matter
        var mdContent = $@"---
title: ""{fullTitle.Replace("\"", "\\\"")}""
slug: ""{slug}""
description: 
date: {hugoDate}
artists:
";

        foreach (var a in artistsList)
            mdContent += $"  - \"{a.Replace("\"", "\\\"")}\"\n";

        mdContent += "tags:\n";
        foreach (var a in artistsList)
            mdContent += $"  - \"{a.Replace("\"", "\\\"")}\"\n";

        mdContent += $"image: {imageField}\n";
        mdContent += @"math: 
license: 
hidden: false
comments: true
draft: false
---
";

        if (!string.IsNullOrEmpty(ytid))
            mdContent += $"\n{{{{< youtube {ytid} >}}}}\n";

        await File.WriteAllTextAsync(indexPath, mdContent);
    }
}

// Cleanup old entries
if (Directory.Exists(contentDir))
{
    var existingSlugs = Directory.GetDirectories(contentDir)
        .Select(Path.GetFileName)
        .Where(slug => !string.IsNullOrWhiteSpace(slug))
        .ToHashSet();

    var toRemove = existingSlugs.Except(allSlugs);
    foreach (var slug in toRemove)
    {
        var path = Path.Combine(contentDir, slug!);
        Directory.Delete(path, true);
        Console.WriteLine($"🗑️ Deleted orphaned folder: {slug}");
    }
}
