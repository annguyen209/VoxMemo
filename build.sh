#!/bin/bash
set -e

echo "=== VoxMemo Cross-Platform Build ==="

PUBLISH_DIR="publish"
VERSION="1.4.0"

# Clean
rm -rf "$PUBLISH_DIR"

# Build targets
declare -A TARGETS=(
    ["win-x64"]="VoxMemo-v${VERSION}-win-x64"
    ["linux-x64"]="VoxMemo-v${VERSION}-linux-x64"
    ["osx-x64"]="VoxMemo-v${VERSION}-osx-x64"
    ["osx-arm64"]="VoxMemo-v${VERSION}-osx-arm64"
)

for RID in "${!TARGETS[@]}"; do
    NAME="${TARGETS[$RID]}"
    echo ""
    echo "--- Building $RID ---"

    dotnet publish -c Release -r "$RID" --self-contained \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "$PUBLISH_DIR/$RID"

    echo "--- Packaging $NAME ---"
    cd "$PUBLISH_DIR"

    if [[ "$RID" == win-* ]]; then
        # Zip for Windows
        if command -v zip &> /dev/null; then
            cd "$RID" && zip -r "../${NAME}.zip" . && cd ..
        elif command -v powershell &> /dev/null; then
            powershell -Command "Compress-Archive -Path '${RID}/*' -DestinationPath '${NAME}.zip' -Force"
        fi
    else
        # Tar.gz for Linux/macOS
        tar czf "${NAME}.tar.gz" -C "$RID" .
    fi

    cd ..
    echo "--- $RID done ---"
done

echo ""
echo "=== Build complete ==="
ls -lh "$PUBLISH_DIR"/*.{zip,tar.gz} 2>/dev/null || true
