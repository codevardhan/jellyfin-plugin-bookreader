<h1 align="center">Jellyfin Book Reader Plugin</h1>

<p align="center">
A Jellyfin plugin that exposes a REST API for book reading apps - browse the library, download books, extract covers, and sync reading progress across devices.
</p>

<p align="center">
<img alt="Jellyfin 10.11+" src="https://img.shields.io/badge/Jellyfin-10.11%2B-00a4dc?style=flat-square&logo=jellyfin&logoColor=white"/>
<img alt=".NET 9" src="https://img.shields.io/badge/.NET-9.0-512bd4?style=flat-square&logo=dotnet&logoColor=white"/>
<img alt="License: GPLv3" src="https://img.shields.io/badge/License-GPLv3-blue?style=flat-square"/>
</p>

---

## Features

**Library Browsing** - List, search, filter, and sort your book collection. Filter by author, genre, format, reading status, or library. Paginated results with configurable page sizes.

**Book Downloads** - Stream or download full book files (EPUB, PDF, MOBI, AZW3, CBZ, CBR, FB2, TXT, DJVU) with proper MIME types and range request support for resumable downloads.

**Cover Extraction** - Automatic cover image serving with a fallback chain: Jellyfin image cache → EPUB internal extraction (supports all three OPF cover strategies). Responses include `Cache-Control` headers.

**Reading Progress Sync** - Three-tier progress model that works across all book formats:
- **Tier 1 (universal):** percentage, position (opaque CFI/offset token), isFinished
- **Tier 2 (page-based):** currentPage, totalPages
- **Tier 3 (chapter-based, EPUB/FB2):** chapterIndex, chapterTitle, pageInChapter, totalPagesInChapter

Includes offline sync support with conflict detection - if a client sends a stale `lastReadAt`, the server returns a `409 Conflict` with the current server state so the client can merge.

**Batch Progress Sync** - Bulk-update up to 100 books in a single request (`PUT /progress/batch`), ideal for offline-first clients reconnecting after being away.

**Reading Sessions** - Track active reading sessions with start/heartbeat/end lifecycle. Stale sessions are auto-closed by a scheduled task (configurable timeout, default 30 min).

**Reading Statistics** - Per-user stats including total reading time, session counts, books finished, daily averages, current/longest reading streaks, last-30-day breakdown, and per-book time tracking.

**Collection Stats** - Library-wide statistics: total books, total authors, format breakdown, total file size, and recently added books.

---

## Installation

### From Plugin Repository (recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**
2. Click the **+** button and add:
   - **Name:** `Book Reader`
   - **URL:** `https://raw.githubusercontent.com/<your-username>/jellyfin-plugin-bookreader/main/manifest.json`
3. Go to **Catalog**, find **Book Reader** under the General category, and click **Install**
4. **Restart Jellyfin**

### Manual Installation

1. Download the latest release `.zip` from the [Releases](https://github.com/<your-username>/jellyfin-plugin-bookreader/releases) page
2. Extract into your Jellyfin plugins directory:
   - **Linux:** `/var/lib/jellyfin/plugins/BookReader/`
   - **Docker:** Mount or copy into the container's plugin path
   - **Windows:** `%ProgramData%\Jellyfin\Server\plugins\BookReader\`
3. **Restart Jellyfin**

### Build from Source

```bash
git clone https://github.com/<your-username>/jellyfin-plugin-bookreader.git
cd jellyfin-plugin-bookreader
make package
# Output: jellyfin-book-reader.zip
```

Then follow the manual installation steps with the resulting zip.

---

## API Reference

All endpoints require authentication via Jellyfin's standard auth (`X-Emby-Token` header or API key).

Base path: `/api/BookReader`

### Library

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/books` | List books (supports `?search=`, `?author=`, `?genre=`, `?format=`, `?status=`, `?libraryId=`, `?sort=`, `?sortOrder=`, `?limit=`, `?offset=`) |
| `GET` | `/books/{id}` | Single book details with progress |
| `GET` | `/authors` | All authors with book counts |
| `GET` | `/stats` | Collection-level statistics |

### File Serving

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/books/{id}/file` | Download the book file (supports range requests) |
| `GET` | `/books/{id}/cover` | Cover image (Jellyfin cache → EPUB extraction) |

### Reading Progress

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/books/{id}/progress` | Get progress for a book |
| `PUT` | `/books/{id}/progress` | Create/update progress (conflict detection via `lastReadAt`) |
| `DELETE` | `/books/{id}/progress` | Clear progress (mark unread) |
| `PUT` | `/progress/batch` | Bulk sync up to 100 books |

### Reading Sessions

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/sessions/start` | Begin a session (auto-closes previous for same book) |
| `POST` | `/sessions/heartbeat` | Keep-alive ping |
| `POST` | `/sessions/end` | End session with optional pages/percentage stats |
| `GET` | `/sessions/stats` | Reading statistics (admin can query `?userId=`) |

---

## Configuration

After installation, configuration is available at **Dashboard → Plugins → Book Reader**:

| Setting | Default | Description |
|---------|---------|-------------|
| Stale Session Timeout | 30 min | Minutes before an idle session is auto-closed |
| Default Page Size | 20 | Books returned per page when `limit` is not specified |
| Max Page Size | 100 | Maximum allowed value for the `limit` parameter |

---

## Supported Formats

| Format | Extension | MIME Type |
|--------|-----------|-----------|
| EPUB | `.epub` | `application/epub+zip` |
| PDF | `.pdf` | `application/pdf` |
| MOBI | `.mobi` | `application/x-mobipocket-ebook` |
| AZW3 | `.azw3` | `application/x-mobi8-ebook` |
| AZW | `.azw` | `application/x-mobipocket-ebook` |
| CBZ | `.cbz` | `application/x-cbz` |
| CBR | `.cbr` | `application/x-cbr` |
| FB2 | `.fb2` | `application/x-fictionbook+xml` |
| TXT | `.txt` | `text/plain` |
| DJVU | `.djvu` | `image/vnd.djvu` |

---

## Library Structure

This plugin uses Jellyfin's standard book library. The recommended folder structure is:

```
Books/
├── Author Name/
│   ├── Book Title/
│   │   └── book.epub
│   └── Another Book/
│       └── book.pdf
└── Another Author/
    └── Their Book/
        └── book.mobi
```

The plugin extracts author names from the parent folder (Jellyfin's recommended convention), falling back to the Studios metadata field.

---

## Development

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- A running Jellyfin 10.11+ server (for integration testing)

### Build & Test

```bash
# Build
make build

# Run unit tests
dotnet test JellyfinBookReader.Tests/

# Build + deploy to local Jellyfin (requires sudo)
make deploy

# Quick smoke test against running server
make smoke

# Package for distribution
make package
```

### Project Structure

```
├── Api/                    # REST controller
├── Configuration/          # Plugin settings
├── Data/                   # SQLite repositories (WAL mode)
├── Dto/                    # Request/response models
├── Services/               # Business logic
├── Tasks/                  # Scheduled tasks
├── Utils/                  # Helpers
├── JellyfinBookReader.Tests/  # xUnit test suite
├── build.yaml              # JPRM metadata
├── manifest.json           # Plugin repository manifest
└── Makefile                # Build automation
```

---

## License

This plugin is licensed under the [GNU General Public License v3.0](LICENSE), as required by the Jellyfin plugin ecosystem.