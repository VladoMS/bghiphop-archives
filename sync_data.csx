#!/usr/bin/env dotnet-script
#r "nuget: YoutubeExplode, 6.3.14"
#r "nuget: Slugify.Core, 5.0.0-prerelease.4"
#r "nuget: YamlDotNet, 15.1.1"

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Slugify;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Globalization;

// Set paths
var sourceDir = "_source";
var contentDir = Path.Combine("content", "video");

// Prepare slugifier
var slugHelper = new SlugHelper();

// Collect all entries from YAML
var allEntries = new List<(string slug, string title, string ytid)>();

foreach (var file in Directory.GetFiles(sourceDir, "data_*.yml"))
{
    var content = File.ReadAllText(file);
    var deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    var items = deserializer.Deserialize<List<Dictionary<string, object>>>(content);

    foreach (var item in items)
    {
        var title = item["title"]?.ToString()?.Trim() ?? "";
        var ytid = item.ContainsKey("ytid") ? item["ytid"]?.ToString()?.Trim() : null;
        var publishedRaw = item.ContainsKey("published_on") ? item["published_on"]?.ToString()?.Trim() : null;

        if (string.IsNullOrWhiteSpace(title)) continue;

        var slug = slugHelper.GenerateSlug(title);
        allEntries.Add((slug, title, ytid));

        var entryDir = Path.Combine(contentDir, slug);
        var indexPath = Path.Combine(entryDir, "index.md");

        Directory.CreateDirectory(entryDir);

        string hugoDate = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"); // fallback

        if (!string.IsNullOrEmpty(publishedRaw))
        {
            try
            {
                var parsed = DateTime.ParseExact(publishedRaw, "MMMM d, yyyy", CultureInfo.InvariantCulture);
                var offset = new DateTimeOffset(parsed, TimeSpan.FromHours(3)); // +03:00 timezone
                hugoDate = offset.ToString("yyyy-MM-ddTHH:mm:sszzz");
            }
            catch
            {
                Console.WriteLine($"⚠️ Failed to parse date '{publishedRaw}' in entry: {title}");
            }
        }

        var mdContent = $@"---
title: ""{title.Replace("\"", "\\\"")}""
slug: ""{slug}""
description: 
date: {hugoDate}
image: 
math: 
license: 
hidden: false
comments: true
draft: true
---
";

        if (!string.IsNullOrEmpty(ytid))
            mdContent += $"\n{{{{< youtube {ytid} >}}}}\n";

        File.WriteAllText(indexPath, mdContent);
    }
}

// Clean up orphaned entries
if (Directory.Exists(contentDir))
{
    var existingSlugs = Directory.GetDirectories(contentDir)
        .Select(Path.GetFileName)
        .Where(slug => slug != null)
        .ToHashSet();

    var currentSlugs = allEntries.Select(e => e.slug).ToHashSet();

    var toRemove = existingSlugs.Except(currentSlugs);
    foreach (var slug in toRemove)
    {
        var path = Path.Combine(contentDir, slug);
        Directory.Delete(path, true);
        Console.WriteLine($"🗑️ Deleted orphaned folder: {slug}");
    }
}
