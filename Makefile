.PHONY: deps clean-videos sync build serve help

help: ## Show this help message
	@echo 'Available targets:'
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-15s\033[0m %s\n", $$1, $$2}'

deps: ## Install dotnet-script and restore tools
	@echo "📦 Installing dependencies..."
	@which dotnet > /dev/null || (echo "❌ .NET 8+ is required" && exit 1)
	@cd scripts && dotnet tool restore
	@echo "✅ Dependencies installed"

clean-videos: ## Remove all generated video posts
	@echo "🗑️ Removing all video posts..."
	@rm -rf content/videos
	@echo "✅ Video posts cleaned"

sync: ## Run the YouTube playlist sync script
	@echo "🔄 Syncing videos from YouTube playlist..."
	@cd scripts && dotnet script sync_videos.csx -- --apply
	@echo "✅ Sync completed"

sync-dry: ## Run sync in dry-run mode (preview changes)
	@echo "👁️ Preview sync changes (dry run)..."
	@cd scripts && dotnet script sync_videos.csx

build: ## Build Hugo site
	@echo "🏗️ Building site..."
	@hugo --minify
	@echo "✅ Build completed"

serve: ## Start Hugo development server
	@echo "🚀 Starting development server..."
	@hugo server -D

clean: ## Clean build artifacts
	@echo "🧹 Cleaning build artifacts..."
	@rm -rf public resources data/sync-index.json
	@echo "✅ Clean completed"