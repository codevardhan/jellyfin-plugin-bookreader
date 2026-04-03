PLUGIN_NAME   := JellyfinBookReader
PLUGIN_DIR    := /var/lib/jellyfin/plugins/BookReader
PUBLISH_DIR   := ./bin/publish
DIST_DIR      := ./dist
PACKAGE_NAME  := jellyfin-book-reader.zip
TEST_PROJECT  := $(PLUGIN_NAME).Tests/$(PLUGIN_NAME).Tests.csproj
SOLUTION      := $(PLUGIN_NAME).sln

# Ship only the plugin DLL and its third-party dependencies.
# Jellyfin framework assemblies (Jellyfin.*, MediaBrowser.*, Microsoft.Extensions.*,
# Microsoft.Data.Sqlite) are already in Jellyfin's runtime — bundling duplicates
# causes type identity conflicts. Be explicit: list only what isn't provided by Jellyfin.
ARTIFACTS := $(PLUGIN_NAME).dll SharpCompress.dll

# Extract version from build.yaml if present, otherwise default
VERSION := $(shell grep -m1 '^version:' build.yaml 2>/dev/null | awk '{print $$2}' | tr -d '"' || echo "1.0.0.0")

.PHONY: build publish deploy clean test test-verbose test-coverage \
        package package-verify checksum smoke lint restore help

help: ## Show this help
	@echo "JellyfinBookReader Plugin — Make Targets"
	@echo "─────────────────────────────────────────"
	@grep -E '^[a-zA-Z_-]+:.*?## ' $(MAKEFILE_LIST) | \
		awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'

restore: ## Restore NuGet packages
	dotnet restore $(SOLUTION)

build: restore ## Build plugin (Release)
	dotnet build $(SOLUTION) --configuration Release --no-restore

publish: ## Publish plugin DLLs
	dotnet publish $(PLUGIN_NAME).csproj --configuration Release --output $(PUBLISH_DIR)

test: restore ## Run all tests
	dotnet test $(TEST_PROJECT) --configuration Release --no-restore --verbosity normal
	@echo ""
	@echo "✓ All tests passed"

test-verbose: restore ## Run tests with detailed output
	dotnet test $(TEST_PROJECT) --configuration Release --no-restore --verbosity detailed --logger "console;verbosity=detailed"

test-coverage: restore ## Run tests with code coverage report
	dotnet test $(TEST_PROJECT) --configuration Release --no-restore \
		--collect:"XPlat Code Coverage" \
		--results-directory ./TestResults
	@echo ""
	@echo "Coverage report written to ./TestResults/"

test-filter: restore ## Run specific tests (usage: make test-filter FILTER="ClassName")
ifndef FILTER
	$(error Set FILTER, e.g.: make test-filter FILTER="ProgressRepository")
endif
	dotnet test $(TEST_PROJECT) --configuration Release --no-restore \
		--filter "FullyQualifiedName~$(FILTER)" --verbosity normal

package: test publish ## Run tests, then build distributable zip
	@echo "Packaging v$(VERSION)..."
	rm -rf $(DIST_DIR)
	mkdir -p $(DIST_DIR)
	for f in $(ARTIFACTS); do cp $(PUBLISH_DIR)/$$f $(DIST_DIR)/; done
	cp meta.json $(DIST_DIR)/ 2>/dev/null || true
	cd $(DIST_DIR) && zip -r ../$(PACKAGE_NAME) . && cd ..
	@echo ""
	@$(MAKE) --no-print-directory checksum
	@$(MAKE) --no-print-directory package-verify
	@echo ""
	@echo "✓ Package ready: $(PACKAGE_NAME) (v$(VERSION))"

package-verify: ## Verify the zip contains all required artifacts
	@echo "Verifying package contents..."
	@if ! unzip -l $(PACKAGE_NAME) | grep -q "$(PLUGIN_NAME).dll"; then \
		echo "  ✗ MISSING: $(PLUGIN_NAME).dll"; exit 1; \
	else \
		echo "  ✓ $(PLUGIN_NAME).dll"; \
	fi
	@if unzip -l $(PACKAGE_NAME) | grep -q "meta.json"; then \
		echo "  ✓ meta.json"; \
	fi

checksum: ## Print MD5 checksum of the package (for manifest.json)
	@if [ -f $(PACKAGE_NAME) ]; then \
		MD5=$$(md5sum $(PACKAGE_NAME) | awk '{print $$1}'); \
		SIZE=$$(du -h $(PACKAGE_NAME) | awk '{print $$1}'); \
		echo "Package : $(PACKAGE_NAME) ($$SIZE)"; \
		echo "MD5     : $$MD5"; \
		echo ""; \
		echo "Paste into manifest.json → versions[0].checksum:"; \
		echo "  \"checksum\": \"$$MD5\""; \
	else \
		echo "No package found. Run: make package"; exit 1; \
	fi

deploy: package ## Build, test, package, and deploy to local Jellyfin
	@echo "Deploying to $(PLUGIN_DIR)..."
	sudo mkdir -p $(PLUGIN_DIR)
	sudo find $(PLUGIN_DIR) -name "*.dll" -delete
	for f in $(ARTIFACTS); do sudo cp $(DIST_DIR)/$$f $(PLUGIN_DIR)/; done
	sudo cp $(DIST_DIR)/meta.json $(PLUGIN_DIR)/ 2>/dev/null || true
	sudo chown -R jellyfin:jellyfin $(PLUGIN_DIR)/
	sudo systemctl restart jellyfin
	@echo "Deployed v$(VERSION). Waiting for Jellyfin to start..."
	@sleep 10
	@echo "✓ Done. Run: make smoke"

smoke: ## Hit live API endpoints to verify the plugin is loaded
	@if [ -z "$$JF_TOKEN" ]; then \
		echo "Set JF_TOKEN first:"; \
		echo "  export JF_TOKEN=\$$(curl -s -X POST http://localhost:8096/Users/AuthenticateByName \\"; \
		echo "    -H 'Content-Type: application/json' \\"; \
		echo "    -H 'X-Emby-Authorization: MediaBrowser Client=\"BookReader\", Device=\"make\", DeviceId=\"dev\", Version=\"1.0\"' \\"; \
		echo "    -d '{\"Username\": \"<user>\", \"Pw\": \"<pass>\"}' | jq -r '.AccessToken')"; \
		exit 1; \
	fi
	@JF="http://localhost:8096"; \
	echo "── Stats ──"; \
	curl -sf "$$JF/api/BookReader/stats" -H "X-Emby-Token: $$JF_TOKEN" | jq '{totalBooks, totalAuthors, formatBreakdown}' || echo "FAIL: /stats"; \
	echo ""; \
	echo "── Books (first 3) ──"; \
	curl -sf "$$JF/api/BookReader/books?limit=3" -H "X-Emby-Token: $$JF_TOKEN" | jq '.items[] | {title, authors, format}' || echo "FAIL: /books"; \
	echo ""; \
	echo "── Authors ──"; \
	curl -sf "$$JF/api/BookReader/authors" -H "X-Emby-Token: $$JF_TOKEN" | jq '.items[:5]' || echo "FAIL: /authors"; \
	echo ""; \
	echo "── Sessions Stats ──"; \
	curl -sf "$$JF/api/BookReader/sessions/stats" -H "X-Emby-Token: $$JF_TOKEN" | jq '{totalSessions, totalReadingTimeSeconds, currentStreak}' || echo "FAIL: /sessions/stats"; \
	echo ""; \
	echo "✓ Smoke tests complete"

lint: ## Run dotnet format to check code style
	dotnet format $(SOLUTION) --verify-no-changes --verbosity normal || \
		(echo ""; echo "Run 'dotnet format $(SOLUTION)' to fix."; exit 1)

format: ## Auto-fix code style issues
	dotnet format $(SOLUTION)

clean: ## Remove all build artifacts
	rm -rf bin obj $(PUBLISH_DIR) $(DIST_DIR) $(PACKAGE_NAME) TestResults
	@echo "✓ Clean"