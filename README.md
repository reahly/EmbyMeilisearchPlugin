# EmbyMeilisearchPlugin

A plugin for [Emby Media Server](https://emby.media) that replaces the built-in search with [Meilisearch](https://www.meilisearch.com/) — a fast, typo-tolerant, full-text search engine.

## Why?

Emby's native search uses basic SQL `LIKE` queries, which means:
- No typo tolerance — searching "star wras" returns nothing
- No relevance ranking — results aren't sorted by how well they match
- Slow on large libraries
- No fuzzy matching for Live TV channel names

This plugin intercepts Emby's search queries via [Harmony](https://github.com/pardeike/Harmony) patches and routes them through Meilisearch, giving you instant, typo-tolerant results across your entire library — including Live TV channels and programs.

## Features

- **Typo-tolerant search** — find "Star Wars" even when you type "star wras"
- **Instant results** — Meilisearch responds in single-digit milliseconds
- **Auto-sync** — new items are indexed automatically when added to your library
- **Scheduled tasks** — rebuild or sync the index from Emby's dashboard
- **Configurable** — customize searchable fields, filterable attributes, and more

## Requirements

- **Emby Server**
- **Meilisearch**

## Quick Start

### 1. Set up Meilisearch

The easiest way is with Docker:

```bash
docker run -d \
  --name meilisearch \
  -p 7700:7700 \
  -v meilisearch_data:/meili_data \
  -e MEILI_MASTER_KEY=your-secret-key \
  getmeili/meilisearch:latest
```

Or install directly — see the [Meilisearch docs](https://www.meilisearch.com/docs/learn/getting_started/installation).

### 2. Install the Plugin

1. Download the latest release from [Releases](../../releases)
2. Extract the ZIP into your Emby plugins directory:
   - **Linux**: `/var/lib/emby/plugins/MeilisearchSearch/`
   - **Windows**: `%AppData%\Emby-Server\plugins\MeilisearchSearch\`
   - **Docker**: `/config/plugins/MeilisearchSearch/`
3. Make sure both `EmbyMeilisearchPlugin.dll` and `0Harmony.dll` are in the plugin folder
4. Restart Emby Server

### 3. Configure

1. Go to **Emby Dashboard → Plugins → Meilisearch Search**
2. Set:
   - **Meilisearch URL**: `http://localhost:7700` (or your Meilisearch address)
   - **API Key**: your Meilisearch master key
   - **Index Name**: `emby_media` (default)
3. Save and restart Emby

### 4. Build the Index

1. Go to **Emby Dashboard → Scheduled Tasks**
2. Run **Meilisearch: Rebuild Index**
3. Wait for it to complete — this indexes your entire library

That's it! Search should now use Meilisearch.

## Configuration Options

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `true` | Enable/disable the plugin |
| `MeilisearchUrl` | `http://localhost:7700` | Meilisearch server URL |
| `MeilisearchApiKey` | *(empty)* | Meilisearch API key |
| `IndexName` | `emby_media` | Meilisearch index name |
| `AutoSync` | `true` | Auto-index items when added/updated/removed |
| `MaxSearchResults` | `100` | Maximum results returned per search |
| `MinSearchTermLength` | `1` | Minimum characters before Meilisearch activates |
| `IncludeItemTypes` | `Movie,Series,...` | Item types to index (comma-separated) |

## Building from Source

### Prerequisites

- .NET 8.0 SDK
- Emby Server assemblies (for reference)

### Build

```bash
git clone https://github.com/reahly/EmbyMeilisearchPlugin.git
cd EmbyMeilisearchPlugin
dotnet build -c Release
```

The output DLL will be in `bin/Release/net8.0/`.

## How It Works

The plugin uses [Harmony](https://github.com/pardeike/Harmony) to patch Emby's internal `GetItemsResult` and `QueryItems` methods at runtime. When a search query arrives:

1. **Meilisearch query** — the search term is sent to Meilisearch, which returns ranked item IDs split into regular items (movies, series, episodes) and Live TV items (channels, programs)
2. **Query routing** — based on the query characteristics, the plugin decides how to handle it:
   - *Best Results* → builds the result directly from Meilisearch rankings via `GetItemById`
   - *Tab counting* → returns 1 item per type so Emby creates the correct category tabs
   - *Tab content* → passes the correct IDs to Emby's native SQL for type-filtered display

## Scheduled Tasks

| Task | Description |
|---|---|
| **Meilisearch: Rebuild Index** | Drops and recreates the entire index from scratch |
| **Meilisearch: Sync Index** | Incremental sync — adds new items, removes deleted ones |