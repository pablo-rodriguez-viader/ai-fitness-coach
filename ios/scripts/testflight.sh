#!/usr/bin/env bash
# Builds the Beta configuration, runs the unit tests, archives and uploads to TestFlight.
# Run on macOS with Xcode 16+ and XcodeGen. Signing is automatic through an App Store Connect
# API key, so no certificates or profiles have to be installed by hand.
#
# Required environment:
#   API_BASE_URL       HTTPS URL of the beta backend, e.g. https://api-beta.yourdomain.com
#   TEAM_ID            Apple Developer team id (10 characters)
#   APP_BUNDLE_ID      Real bundle id registered with HealthKit + Sign in with Apple
#   ASC_KEY_ID         App Store Connect API key id
#   ASC_ISSUER_ID      App Store Connect API issuer id
#   ASC_KEY_PATH       Path to the AuthKey_XXXX.p8 file
# Optional:
#   BUILD_NUMBER       Defaults to a UTC timestamp (always increasing)
#   MARKETING_VERSION  Defaults to the value in Config/Shared.xcconfig
#   SKIP_TESTS=1       Skip the unit tests (not recommended)
#   UPLOAD=0           Archive and export the .ipa without uploading
set -euo pipefail

cd "$(dirname "$0")/.."

fail() { echo "error: $*" >&2; exit 1; }

for name in API_BASE_URL TEAM_ID APP_BUNDLE_ID; do
  [[ -n "${!name:-}" ]] || fail "$name is required."
done
[[ "$API_BASE_URL" == https://* ]] || fail "API_BASE_URL must use HTTPS (TestFlight builds reject HTTP)."
[[ "$APP_BUNDLE_ID" != com.example.* ]] || fail "APP_BUNDLE_ID still uses the com.example placeholder."
UPLOAD="${UPLOAD:-1}"
if [[ "$UPLOAD" == "1" ]]; then
  for name in ASC_KEY_ID ASC_ISSUER_ID ASC_KEY_PATH; do
    [[ -n "${!name:-}" ]] || fail "$name is required to upload (or set UPLOAD=0)."
  done
  [[ -f "$ASC_KEY_PATH" ]] || fail "ASC_KEY_PATH does not point to a file."
fi

BUILD_NUMBER="${BUILD_NUMBER:-$(date -u +%Y%m%d%H%M)}"
BUILD_DIR="build/testflight"
ARCHIVE_PATH="$BUILD_DIR/AIFitnessCoach-Beta-$BUILD_NUMBER.xcarchive"
EXPORT_PATH="$BUILD_DIR/export-$BUILD_NUMBER"
mkdir -p "$BUILD_DIR"

# xcodebuild settings override Config/*.xcconfig.
SETTINGS=(
  "APP_BUNDLE_ID=$APP_BUNDLE_ID"
  "DEVELOPMENT_TEAM=$TEAM_ID"
  "CURRENT_PROJECT_VERSION=$BUILD_NUMBER"
  "API_BASE_URL=$API_BASE_URL"
)
[[ -n "${MARKETING_VERSION:-}" ]] && SETTINGS+=("MARKETING_VERSION=$MARKETING_VERSION")

AUTH=()
if [[ -n "${ASC_KEY_ID:-}" && -n "${ASC_ISSUER_ID:-}" && -n "${ASC_KEY_PATH:-}" ]]; then
  AUTH=(
    -allowProvisioningUpdates
    -authenticationKeyID "$ASC_KEY_ID"
    -authenticationKeyIssuerID "$ASC_ISSUER_ID"
    -authenticationKeyPath "$(cd "$(dirname "$ASC_KEY_PATH")" && pwd)/$(basename "$ASC_KEY_PATH")"
  )
fi

echo "==> Generating Xcode project"
xcodegen generate

# The privacy manifest must be bundled; App Store Connect warns (and will reject) without it.
grep -q "PrivacyInfo.xcprivacy" AIFitnessCoach.xcodeproj/project.pbxproj \
  || fail "PrivacyInfo.xcprivacy is not part of the app target."

if [[ "${SKIP_TESTS:-0}" != "1" ]]; then
  echo "==> Running unit tests"
  TEST_LOG="$BUILD_DIR/test-$BUILD_NUMBER.log"
  if ! xcodebuild test \
    -project AIFitnessCoach.xcodeproj \
    -scheme AIFitnessCoach \
    -destination "platform=iOS Simulator,name=${TEST_SIMULATOR:-iPhone 16}" \
    CODE_SIGNING_ALLOWED=NO > "$TEST_LOG" 2>&1; then
    grep -E "error:|failed" "$TEST_LOG" | tail -40 >&2 || true
    fail "Unit tests failed (see $TEST_LOG)."
  fi
  grep -E "Executed [0-9]+ tests" "$TEST_LOG" | tail -1 || true
fi

echo "==> Archiving Beta build $BUILD_NUMBER"
xcodebuild archive \
  -project AIFitnessCoach.xcodeproj \
  -scheme "AIFitnessCoach Beta" \
  -configuration Beta \
  -destination "generic/platform=iOS" \
  -archivePath "$ARCHIVE_PATH" \
  ${AUTH[@]+"${AUTH[@]}"} \
  "${SETTINGS[@]}"

DESTINATION="export"
[[ "$UPLOAD" == "1" ]] && DESTINATION="upload"
EXPORT_OPTIONS="$BUILD_DIR/ExportOptions-$BUILD_NUMBER.plist"
cat > "$EXPORT_OPTIONS" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>method</key>
    <string>app-store-connect</string>
    <key>destination</key>
    <string>$DESTINATION</string>
    <key>teamID</key>
    <string>$TEAM_ID</string>
    <key>signingStyle</key>
    <string>automatic</string>
    <key>uploadSymbols</key>
    <true/>
    <key>manageAppVersionAndBuildNumber</key>
    <false/>
    <key>testFlightInternalTestingOnly</key>
    <false/>
</dict>
</plist>
PLIST

echo "==> Exporting ($DESTINATION)"
xcodebuild -exportArchive \
  -archivePath "$ARCHIVE_PATH" \
  -exportPath "$EXPORT_PATH" \
  -exportOptionsPlist "$EXPORT_OPTIONS" \
  ${AUTH[@]+"${AUTH[@]}"}

if [[ "$UPLOAD" == "1" ]]; then
  echo "==> Build $BUILD_NUMBER uploaded. It appears in App Store Connect › TestFlight after processing (10-30 min)."
else
  echo "==> IPA exported to $EXPORT_PATH (not uploaded)."
fi
