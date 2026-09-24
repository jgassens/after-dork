#!/bin/zsh
# After Dork release pipeline: Developer ID sign -> notarize -> staple ->
# GitHub release -> Sparkle appcast. Run via `make release`.
set -e
cd "$(dirname "$0")/.."

IDENTITY="Developer ID Application: JEREMIAH JOSEPH GASSENSMITH (C2N7W5247T)"
PROFILE="afterdork-notary"
SPARKLE_BIN="${SPARKLE_BIN:-$HOME/programming/chemdraw/apps/desktop/src-tauri/sparkle-bin}"
REPO="jgassens/after-dork"
APP="build/AfterDork.app"
VERSION=$(plutil -extract CFBundleShortVersionString raw ControlPanel/Info.plist)
ZIP="dist/AfterDork-$VERSION.zip"

echo "== Releasing After Dork $VERSION =="
make app >/dev/null

echo "== Signing (Developer ID, hardened runtime) =="
for s in "$APP"/Contents/Resources/Savers/*.saver; do
  codesign --force --options runtime --timestamp --sign "$IDENTITY" "$s"
done
FW="$APP/Contents/Frameworks/Sparkle.framework"
codesign --force --options runtime --timestamp --preserve-metadata=entitlements \
  --sign "$IDENTITY" "$FW/Versions/B/XPCServices/Installer.xpc"
codesign --force --options runtime --timestamp --preserve-metadata=entitlements \
  --sign "$IDENTITY" "$FW/Versions/B/XPCServices/Downloader.xpc"
codesign --force --options runtime --timestamp --sign "$IDENTITY" "$FW/Versions/B/Autoupdate"
codesign --force --options runtime --timestamp --sign "$IDENTITY" "$FW/Versions/B/Updater.app"
codesign --force --options runtime --timestamp --sign "$IDENTITY" "$FW"
codesign --force --options runtime --timestamp --sign "$IDENTITY" "$APP"
codesign --verify --deep --strict "$APP"

echo "== Notarizing =="
mkdir -p dist
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"
xcrun notarytool submit "$ZIP" --keychain-profile "$PROFILE" --wait
xcrun stapler staple "$APP"
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"
spctl -a -vv "$APP"

echo "== GitHub release =="
if gh release view "v$VERSION" --repo "$REPO" >/dev/null 2>&1; then
  gh release upload "v$VERSION" "$ZIP" --clobber --repo "$REPO"
else
  gh release create "v$VERSION" "$ZIP" --repo "$REPO" \
    --title "After Dork $VERSION for Mac" \
    --notes "## 🍎 After Dork $VERSION for **Mac**

**This is the macOS version** (\`AfterDork-$VERSION.zip\` contains *After Dork.app*).

> 🪟 **On Windows?** This download won't run on your PC. Get **[After Dork for Windows](https://github.com/$REPO/releases?q=windows&expanded=true)** instead.

Chemistry screen savers like it's 1996. Download, unzip, and open After Dork.app."
fi

echo "== Appcast =="
"$SPARKLE_BIN/generate_appcast" \
  --download-url-prefix "https://github.com/$REPO/releases/download/v$VERSION/" \
  -o appcast.xml dist
git add appcast.xml
git commit -m "Appcast for $VERSION" || true
git push origin main

echo "== Done: After Dork $VERSION is live =="
