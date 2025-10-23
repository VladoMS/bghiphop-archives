# BG Hip Hop Archive

Българският хип-хоп архив - модерна Hugo система за архивиране на български хип-хоп видеа от YouTube.

## ✨ Features

- 🎬 **Автоматично синхронизиране** от YouTube плейлист
- 🔄 **CRUD операции** - създаване, актуализиране и изтриване на постове
- 🖼️ **Автоматично теглене** на най-високо качествени миниатюри
- 🏷️ **Автоматично извличане** и нормализиране на тагове
- ⏰ **Timezone конвертиране** към Europe/Sofia
- 🎨 **Модерна тема** с urban/graffiti естетика
- 📱 **Responsive дизайн** с masonry grid
- 🔍 **SEO оптимизирана** с OpenGraph и structured data
- 🌐 **RSS feeds** за всички секции
- 💾 **Dry-run mode** за преглед на промените

## 🛠️ Prerequisites

- [.NET 8+](https://dotnet.microsoft.com/download)
- [Hugo Extended](https://gohugo.io/installation/) (v0.100.0+)
- [dotnet-script](https://github.com/filipw/dotnet-script)

## 🚀 Quick Start

### One-shot Setup

```bash
# Install dependencies
make deps

# Clean old content
make clean-videos

# Sync videos from YouTube
make sync

# Start development server
make serve
```

Сайтът ще бъде достъпен на `http://localhost:1313`

### Individual Commands

```bash
# Install dotnet-script and restore tools
make deps

# Preview sync changes (dry run)
make sync-dry

# Apply sync changes
make sync

# Build for production
make build

# Clean everything
make clean
```

## 📁 Project Structure

```
bghiphop-archives/
├── config/_default/          # Hugo configuration
├── content/
│   ├── videos/              # Auto-generated video posts
│   └── _index.md           # Homepage content
├── data/
│   └── sync-index.json     # Sync state tracking
├── scripts/
│   ├── sync_videos.csx     # Main sync script
│   └── dotnet-tools.json   # Tool dependencies
├── themes/urban-archive/    # Custom theme
├── static/                 # Static assets
├── Makefile               # Automation
└── nuget.config           # Package sources
```

## ⚙️ Configuration

### Playlist Configuration

Edit `scripts/sync_videos.csx` to change the YouTube playlist:

```csharp
const string PLAYLIST_ID = "PLSEcZQoiVhpp3NtKasuxfcGXlnXIL-RbT";
```

### Hugo Configuration

Key settings in `config/_default/config.toml`:

```toml
theme = "urban-archive"
pagerSize = 24
enableRobotsTXT = true

[params]
  description = "Българският хип-хоп архив - колекция от български хип-хоп видеа"
  author = "BG Hip Hop Archive"
```

## 🎬 How It Works

### Sync Process

1. **Fetch Playlist**: Използва YoutubeExplode за получаване на видеата
2. **Extract Metadata**: Извлича заглавие, дата, тагове и миниатюри
3. **Normalize Data**: 
   - Конвертира дати към Europe/Sofia timezone
   - Създава slugs с Bulgarian transliteration
   - Нормализира тагове (lowercase, без emojis)
4. **CRUD Operations**:
   - **Create**: Нови видеа от плейлиста
   - **Update**: Променени заглавия или миниатюри
   - **Delete**: Премахнати видеа от плейлиста
5. **Generate Posts**: Hugo markdown файлове с YAML front matter

### Content Model

Всеки пост има следната структура:

```yaml
---
title: "Artist - Song Title"
date: 2025-10-23T02:15:00+03:00
slug: "artist-song-title"
youtube_id: "hAl8s23BLMU"
cover: "cover.jpg"
tags: ["bulgarian hip hop", "artist-name", "genre"]
draft: false
aliases: ["old-slug/"]  # if slug changed
---

{{< youtube hAl8s23BLMU >}}
```

### Slug Generation

- Bulgarian Cyrillic → Latin transliteration
- Lowercase transformation
- Special characters → hyphens
- Duplicate handling with suffixes

### Tag Processing

- Извличане от YouTube video keywords
- Lowercase нормализация
- Премахване на emojis и специални символи
- Ограничаване до 10 тага
- Fallback: `["bulgarian hip hop", "archive"]`

## 🎨 Theme Customization

### Color Palette

CSS променливи в `themes/urban-archive/assets/css/main.css`:

```css
:root {
  --bg: #0b0b0e;          /* Background */
  --fg: #e8e8ea;          /* Foreground */
  --muted: #a3a3ad;       /* Muted text */
  --accent1: #ff3b81;     /* Primary accent */
  --accent2: #00c2ff;     /* Secondary accent */
  --accent3: #ffd166;     /* Tertiary accent */
  --accent4: #7c3aed;     /* Quaternary accent */
  --surface: #1a1a1f;     /* Cards/surfaces */
  --border: #333338;      /* Borders */
}
```

### Typography

- **Body**: Inter (Google Fonts fallback to system)
- **Headers**: Oswald (condensed, bold)
- **Logo**: System stack with letter-spacing

### Layout Features

- Responsive CSS Grid (min 300px columns)
- Hover effects with transform and shadows
- Loading states for images
- Masonry-style video grid
- Sticky navigation header

## 🔧 Troubleshooting

### Common Issues

**1. .NET Script не се стартира**
```bash
# Install dotnet-script globally
dotnet tool install -g dotnet-script

# Or restore locally
cd scripts && dotnet tool restore
```

**2. Hugo не намира темата**
```bash
# Check theme is configured
grep "theme" config/_default/config.toml

# Should output: theme = "urban-archive"
```

**3. YouTube API rate limiting**
```bash
# Script включва rate limiting (100ms delay)
# За по-бавна работа, увеличете в sync_videos.csx:
await Task.Delay(500); // 500ms instead of 100ms
```

**4. Thumbnail download fails**
- Проверете интернет връзката
- Script-ът автоматично пробва различни резолюции
- Fallback на default placeholder

**5. Date parsing errors**
```bash
# Check timezone configuration
timedatectl status

# Should show: Time zone: Europe/Sofia
```

### Debug Mode

За debugging на sync script-а:

```csharp
// Add at top of sync_videos.csx
Console.WriteLine($"Debug: Processing video {videoId}");
```

### Performance

- **Concurrent operations**: Script използва async/await
- **Memory efficient**: Streaming approach за файлове  
- **Rate limiting**: 100ms delay между YouTube calls
- **Caching**: Sync index предотвратява излишни операции

## 📊 Statistics

Sync summary показва:
- 📹 **Processed**: Общо обработени видеа
- ✅ **Created**: Създадени нови постове  
- 🔄 **Updated**: Актуализирани постове
- 🗑️ **Deleted**: Изтрити постове

## 🤝 Contributing

1. Fork repository-то
2. Създайте feature branch (`git checkout -b feature/amazing-feature`)
3. Commit промените (`git commit -m 'Add amazing feature'`)
4. Push към branch-а (`git push origin feature/amazing-feature`)
5. Отворете Pull Request

## 📄 License

Този проект е лицензиран под MIT License - вижте [LICENSE](LICENSE) файла за детайли.

## 🙏 Acknowledgments

- [Hugo](https://gohugo.io/) - Static site generator
- [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode) - YouTube API library
- [dotnet-script](https://github.com/filipw/dotnet-script) - C# scripting
- Urban Archive Theme - Custom theme с graffiti естетика

---

**BG Hip Hop Archive** - Запазваме българската хип-хоп култура за бъдещите поколения 🎤🇧🇬