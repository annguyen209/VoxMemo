#!/bin/bash
set -e

echo "=== VoxMemo Cross-Platform Build ==="

PUBLISH_DIR="publish"
VERSION="1.5.0"
ISCC="C:/Program Files (x86)/Inno Setup 6/ISCC.exe"

# Clean
rm -rf "$PUBLISH_DIR"

# Fixed platform order
RIDS=("win-x64" "linux-x64" "osx-x64" "osx-arm64")

for RID in "${RIDS[@]}"; do
    NAME="VoxMemo-v${VERSION}-${RID}"
    echo ""
    echo "--- Building $RID ---"

    dotnet publish -c Release -r "$RID" --self-contained \
        -p:PublishSingleFile=true \
        -o "$PUBLISH_DIR/$RID"

    echo "--- Packaging $NAME ---"

    if [[ "$RID" == win-* ]]; then
        pwsh -NoProfile -Command \
            "Compress-Archive -Path '${PUBLISH_DIR}/${RID}/*' -DestinationPath '${PUBLISH_DIR}/${NAME}.zip' -Force"
    else
        tar czf "${PUBLISH_DIR}/${NAME}.tar.gz" -C "${PUBLISH_DIR}/${RID}" .
    fi

    echo "--- $RID done ---"
done

# Windows installer via Inno Setup
if [[ -f "$ISCC" ]]; then
    echo ""
    echo "--- Building Windows installer ---"
    "$ISCC" setup.iss
else
    echo ""
    echo "--- Inno Setup not found, skipping installer ---"
fi

echo ""
echo "=== Build complete ==="
ls -lh "$PUBLISH_DIR"/*.{zip,tar.gz,exe} 2>/dev/null || true
