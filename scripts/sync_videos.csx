#!/usr/bin/env dotnet-script
#r "nuget: YoutubeExplode, 6.3.16"
#r "nuget: YamlDotNet, 15.1.1"
#r "nuget: System.Text.Json, 8.0.4"

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Globalization;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Playlists;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// Configuration
const string PLAYLIST_ID = "PLSEcZQoiVhpp3NtKasuxfcGXlnXIL-RbT";
const string CONTENT_DIR = "../content/videos";
const string DATA_DIR = "../data";
const string INDEX_FILE = "sync-index.json";
const int MAX_TAGS = 10;
readonly string[] FALLBACK_TAGS = { "bulgarian hip hop", "archive" };
readonly TimeZoneInfo SOFIA_TZ = TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia");

// Check for --apply flag
bool applyChanges = Args.Contains("--apply");

// Classes for data structures
public class SyncIndex
{
    public Dictionary<string, SyncIndexEntry> Videos { get; set; } = new();
}

public class SyncIndexEntry
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime LastModified { get; set; }
    public string CoverHash { get; set; } = "";
}

public class VideoMetadata
{
    public string VideoId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public DateTime PublishDate { get; set; }
    public string Cover { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public string CoverHash { get; set; } = "";
}

// Global instances
var youtube = new YoutubeClient();
var http = new HttpClient();
var syncStats = new { Created = 0, Updated = 0, Deleted = 0 };

// Utility functions
string Slugify(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return "";
    
    // Bulgarian transliteration map
    var bgMap = new Dictionary<char, string>
    {
        {'а', "a"}, {'б', "b"}, {'в', "v"}, {'г', "g"}, {'д', "d"},
        {'е', "e"}, {'ж', "zh"}, {'з', "z"}, {'и', "i"}, {'й', "y"},
        {'к', "k"}, {'л', "l"}, {'м', "m"}, {'н', "n"}, {'о', "o"},
        {'п', "p"}, {'р', "r"}, {'с', "s"}, {'т', "t"}, {'у', "u"},
        {'ф', "f"}, {'х', "h"}, {'ц', "ts"}, {'ч', "ch"}, {'ш', "sh"},
        {'щ', "sht"}, {'ъ', "a"}, {'ь', ""}, {'ю', "yu"}, {'я', "ya"},
        {'А', "A"}, {'Б', "B"}, {'В', "V"}, {'Г', "G"}, {'Д', "D"},
        {'Е', "E"}, {'Ж', "Zh"}, {'З', "Z"}, {'И', "I"}, {'Й', "Y"},
        {'К', "K"}, {'Л', "L"}, {'М', "M"}, {'Н', "N"}, {'О', "O"},
        {'П', "P"}, {'Р', "R"}, {'С', "S"}, {'Т', "T"}, {'У', "U"},
        {'Ф', "F"}, {'Х', "H"}, {'Ц', "Ts"}, {'Ч', "Ch"}, {'Ш', "Sh"},
        {'Щ', "Sht"}, {'Ъ', "A"}, {'Ь', ""}, {'Ю', "Yu"}, {'Я', "Ya"}
    };
    
    // Transliterate Bulgarian characters
    var transliterated = string.Concat(input.Select(c => bgMap.ContainsKey(c) ? bgMap[c] : c.ToString()));
    
    // Convert to lowercase and replace non-ASCII characters
    transliterated = transliterated.ToLowerInvariant();
    
    // Remove diacritics
    var normalized = transliterated.Normalize(NormalizationForm.FormD);
    var ascii = Encoding.ASCII.GetString(
        Encoding.Convert(
            Encoding.Unicode,
            Encoding.ASCII,
            Encoding.Unicode.GetBytes(normalized)
        )
    ).Replace("?", "");
    
    // Replace spaces and invalid characters with hyphens
    var slug = Regex.Replace(ascii, @"[^a-z0-9\-]", "-");
    slug = Regex.Replace(slug, @"-+", "-").Trim('-');
    
    return string.IsNullOrEmpty(slug) ? "untitled" : slug;
}

string NormalizeTag(string tag)
{
    if (string.IsNullOrWhiteSpace(tag)) return "";
    
    // Remove emojis and special characters, convert to lowercase
    var normalized = Regex.Replace(tag.ToLowerInvariant(), @"[^\p{L}\p{N}\s-]", "");
    normalized = Regex.Replace(normalized, @"\s+", "-");
    normalized = Regex.Replace(normalized, @"-+", "-").Trim('-');
    
    return string.IsNullOrEmpty(normalized) ? "" : normalized;
}

string ComputeHash(byte[] data)
{
    using var sha256 = SHA256.Create();
    var hash = sha256.ComputeHash(data);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

async Task<(byte[]? data, string hash)> DownloadThumbnailAsync(string videoId)
{
    var urls = new[]
    {
        $"https://i.ytimg.com/vi/{videoId}/maxresdefault.jpg",
        $"https://i.ytimg.com/vi/{videoId}/sddefault.jpg",
        $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg"
    };
    
    foreach (var url in urls)
    {
        try
        {
            var response = await http.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadAsByteArrayAsync();
                var hash = ComputeHash(data);
                return (data, hash);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to download thumbnail from {url}: {ex.Message}");
        }
    }
    
    return (null, "");
}

SyncIndex LoadSyncIndex()
{
    var indexPath = Path.Combine(DATA_DIR, INDEX_FILE);
    if (!File.Exists(indexPath))
    {
        Console.WriteLine("ℹ️ No sync index found, creating new one");
        return new SyncIndex();
    }
    
    try
    {
        var json = File.ReadAllText(indexPath);
        return JsonSerializer.Deserialize<SyncIndex>(json) ?? new SyncIndex();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Failed to load sync index: {ex.Message}");
        return new SyncIndex();
    }
}

async Task SaveSyncIndexAsync(SyncIndex index)
{
    if (!applyChanges) return;
    
    var indexPath = Path.Combine(DATA_DIR, INDEX_FILE);
    Directory.CreateDirectory(DATA_DIR);
    
    var options = new JsonSerializerOptions { WriteIndented = true };
    var json = JsonSerializer.Serialize(index, options);
    await File.WriteAllTextAsync(indexPath, json);
}

HashSet<string> GetExistingVideoIds()
{
    var videoIds = new HashSet<string>();
    
    if (!Directory.Exists(CONTENT_DIR)) return videoIds;
    
    foreach (var dir in Directory.GetDirectories(CONTENT_DIR))
    {
        var indexPath = Path.Combine(dir, "index.md");
        if (!File.Exists(indexPath)) continue;
        
        try
        {
            var content = File.ReadAllText(indexPath);
            var match = Regex.Match(content, @"youtube_id:\s*""?([^""^\n\r]+)""?");
            if (match.Success)
            {
                videoIds.Add(match.Groups[1].Value.Trim());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to read {indexPath}: {ex.Message}");
        }
    }
    
    return videoIds;
}

async Task<VideoMetadata?> FetchVideoMetadataAsync(string videoId)
{
    try
    {
        var video = await youtube.Videos.GetAsync(videoId);
        
        var title = video.Title ?? "";
        var slug = Slugify(title);
        var publishDate = video.UploadDate.DateTime;
        
        // Convert to Sofia timezone
        var sofiaTime = TimeZoneInfo.ConvertTimeFromUtc(publishDate, SOFIA_TZ);
        
        // Extract hashtags from video description
        var tags = new List<string>();
        if (!string.IsNullOrEmpty(video.Description))
        {
            var hashtagPattern = @"#(\w+)";
            var matches = Regex.Matches(video.Description, hashtagPattern, RegexOptions.IgnoreCase);
            
            tags = matches
                .Cast<Match>()
                .Select(m => NormalizeTag(m.Groups[1].Value))
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct()
                .Take(MAX_TAGS)
                .ToList();
        }
        
        if (!tags.Any())
        {
            tags = FALLBACK_TAGS.ToList();
        }
        
        return new VideoMetadata
        {
            VideoId = videoId,
            Title = title,
            Slug = slug,
            PublishDate = sofiaTime,
            Tags = tags
        };
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Failed to fetch metadata for {videoId}: {ex.Message}");
        return null;
    }
}

async Task<bool> WriteVideoPostAsync(VideoMetadata metadata, SyncIndex index)
{
    if (!applyChanges)
    {
        Console.WriteLine($"[DRY RUN] Would create/update: {metadata.Slug}");
        return true;
    }
    
    var postDir = Path.Combine(CONTENT_DIR, metadata.Slug);
    var indexPath = Path.Combine(postDir, "index.md");
    var coverPath = Path.Combine(postDir, "cover.jpg");
    
    Directory.CreateDirectory(postDir);
    
    // Download thumbnail
    var (thumbnailData, thumbnailHash) = await DownloadThumbnailAsync(metadata.VideoId);
    if (thumbnailData != null)
    {
        await File.WriteAllBytesAsync(coverPath, thumbnailData);
        metadata.Cover = "cover.jpg";
        metadata.CoverHash = thumbnailHash;
    }
    
    // Handle slug changes with aliases
    var aliases = new List<string>();
    if (index.Videos.TryGetValue(metadata.VideoId, out var existing) && existing.Slug != metadata.Slug)
    {
        aliases.Add($"{existing.Slug}/");
        
        // Remove old directory
        var oldDir = Path.Combine(CONTENT_DIR, existing.Slug);
        if (Directory.Exists(oldDir))
        {
            Directory.Delete(oldDir, true);
        }
    }
    
    // Generate front matter
    var serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();
    
    var frontMatter = new Dictionary<string, object>
    {
        ["title"] = metadata.Title,
        ["date"] = metadata.PublishDate.ToString("yyyy-MM-ddTHH:mm:sszzz"),
        ["slug"] = metadata.Slug,
        ["youtube_id"] = metadata.VideoId,
        ["cover"] = metadata.Cover,
        ["tags"] = metadata.Tags,
        ["draft"] = false
    };
    
    if (aliases.Any())
    {
        frontMatter["aliases"] = aliases;
    }
    
    var yaml = serializer.Serialize(frontMatter);
    var body = $"{{{{< youtube {metadata.VideoId} >}}}}";
    
    var content = $"---\n{yaml}---\n\n{body}\n";
    await File.WriteAllTextAsync(indexPath, content);
    
    // Update index
    index.Videos[metadata.VideoId] = new SyncIndexEntry
    {
        Slug = metadata.Slug,
        Title = metadata.Title,
        LastModified = DateTime.UtcNow,
        CoverHash = metadata.CoverHash
    };
    
    return true;
}

async Task<int> SyncPlaylistAsync()
{
    Console.WriteLine($"🔄 Fetching playlist: {PLAYLIST_ID}");
    
    var syncIndex = LoadSyncIndex();
    var existingVideoIds = GetExistingVideoIds();
    var playlistVideoIds = new HashSet<string>();
    
    int processedCount = 0;
    int createdCount = 0;
    int updatedCount = 0;
    
    try
    {
        var playlist = await youtube.Playlists.GetAsync(PLAYLIST_ID);
        Console.WriteLine($"📂 Playlist: {playlist.Title} ({playlist.Description})");
        
        await foreach (var video in youtube.Playlists.GetVideosAsync(playlist.Id))
        {
            playlistVideoIds.Add(video.Id);
            processedCount++;
            
            try
            {
                var metadata = await FetchVideoMetadataAsync(video.Id);
                if (metadata == null) continue;
                
                bool isNew = !existingVideoIds.Contains(video.Id);
                bool needsUpdate = false;
                
                if (!isNew && syncIndex.Videos.TryGetValue(video.Id, out var existing))
                {
                    // Check if update needed
                    needsUpdate = existing.Title != metadata.Title;
                    
                    // Check cover hash if we have both
                    if (!string.IsNullOrEmpty(existing.CoverHash))
                    {
                        var (_, newHash) = await DownloadThumbnailAsync(metadata.VideoId);
                        needsUpdate |= existing.CoverHash != newHash;
                    }
                }
                
                if (isNew || needsUpdate)
                {
                    var success = await WriteVideoPostAsync(metadata, syncIndex);
                    if (success)
                    {
                        if (isNew)
                        {
                            createdCount++;
                            Console.WriteLine($"✅ Created: {metadata.Title}");
                        }
                        else
                        {
                            updatedCount++;
                            Console.WriteLine($"🔄 Updated: {metadata.Title}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"⏭️ Unchanged: {metadata.Title}");
                }
                
                // Rate limiting
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error processing video {video.Id}: {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Failed to fetch playlist: {ex.Message}");
        return 1;
    }
    
    // Delete removed videos
    int deletedCount = 0;
    var videosToDelete = existingVideoIds.Except(playlistVideoIds).ToList();
    
    foreach (var videoId in videosToDelete)
    {
        if (syncIndex.Videos.TryGetValue(videoId, out var entry))
        {
            var postDir = Path.Combine(CONTENT_DIR, entry.Slug);
            if (Directory.Exists(postDir))
            {
                if (applyChanges)
                {
                    Directory.Delete(postDir, true);
                    syncIndex.Videos.Remove(videoId);
                    Console.WriteLine($"🗑️ Deleted: {entry.Title}");
                }
                else
                {
                    Console.WriteLine($"[DRY RUN] Would delete: {entry.Title}");
                }
                deletedCount++;
            }
        }
    }
    
    await SaveSyncIndexAsync(syncIndex);
    
    // Print summary
    Console.WriteLine();
    Console.WriteLine("📊 SYNC SUMMARY");
    Console.WriteLine($"  📹 Processed: {processedCount} videos");
    Console.WriteLine($"  ✅ Created:   {createdCount} posts");
    Console.WriteLine($"  🔄 Updated:   {updatedCount} posts");
    Console.WriteLine($"  🗑️ Deleted:   {deletedCount} posts");
    
    if (!applyChanges)
    {
        Console.WriteLine();
        Console.WriteLine("ℹ️ This was a dry run. Use --apply to make actual changes.");
    }
    
    return 0;
}

// Main execution
try
{
    Console.WriteLine("🎬 BG Hip Hop Archive - YouTube Sync");
    Console.WriteLine($"Mode: {(applyChanges ? "APPLY CHANGES" : "DRY RUN")}");
    Console.WriteLine();
    
    var exitCode = await SyncPlaylistAsync();
    Environment.Exit(exitCode);
}
catch (Exception ex)
{
    Console.WriteLine($"💥 Fatal error: {ex.Message}");
    Environment.Exit(1);
}