#!/bin/sh
set -eu
cd "$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
mkdir -p build/tests
swiftc -O -module-cache-path build/tests/ModuleCache -module-name LifecycleRegression -o build/tests/lifecycle-regression \
    Shared/Settings.swift Shared/LifecycleScreenSaverView.swift \
    StoddartReef/StoddartReef.swift Tests/Lifecycle/main.swift \
    -framework ScreenSaver -framework AppKit
exec build/tests/lifecycle-regression
