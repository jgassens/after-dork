SAVERS = FlyingFlasks GlasswarePipes LatticeMaze MystifyPolymers StoddartReef OrbitalBox SmilesRain CastawayChemist
MIN = 11.0
BUILD = build
FRAMEWORKS = -framework ScreenSaver -framework AppKit
SHARED = Shared/Settings.swift

all: $(foreach s,$(SAVERS),$(BUILD)/$(s).saver)

define SAVER_template
$(BUILD)/$(1).saver: $(1)/$(1).swift $(1)/Info.plist $(SHARED) $(wildcard $(1)/Resources/*)
	mkdir -p $(BUILD)/$(1).saver/Contents/MacOS
	cp $(1)/Info.plist $(BUILD)/$(1).saver/Contents/Info.plist
	swiftc -O -target arm64-apple-macos$(MIN) -module-name $(1) -emit-library \
	    -o $(BUILD)/$(1)-arm64.dylib $(1)/$(1).swift $(SHARED) $(FRAMEWORKS)
	swiftc -O -target x86_64-apple-macos$(MIN) -module-name $(1) -emit-library \
	    -o $(BUILD)/$(1)-x8664.dylib $(1)/$(1).swift $(SHARED) $(FRAMEWORKS)
	lipo -create -output $(BUILD)/$(1).saver/Contents/MacOS/$(1) \
	    $(BUILD)/$(1)-arm64.dylib $(BUILD)/$(1)-x8664.dylib
	rm -rf $(BUILD)/$(1).saver/Contents/Resources
	if [ -d $(1)/Resources ]; then \
	    mkdir -p $(BUILD)/$(1).saver/Contents/Resources; \
	    cp -R $(1)/Resources/ $(BUILD)/$(1).saver/Contents/Resources/; \
	fi
	xattr -cr $(BUILD)/$(1).saver
	codesign --force --sign - $(BUILD)/$(1).saver
	touch $(BUILD)/$(1).saver

$(BUILD)/preview-$(1): $(1)/$(1).swift Harness/main.swift $(SHARED)
	mkdir -p $(BUILD)
	swiftc -O -DHARNESS -module-name $(1)Preview -o $$@ \
	    $(1)/$(1).swift $(SHARED) Harness/main.swift $(FRAMEWORKS)

preview-$(1): $(BUILD)/preview-$(1)
	mkdir -p $(BUILD)/shots
	$(BUILD)/preview-$(1) 1280 720 240 $(BUILD)/shots/$(1)
endef

$(foreach s,$(SAVERS),$(eval $(call SAVER_template,$(s))))

previews: $(foreach s,$(SAVERS),preview-$(s))

APP = $(BUILD)/AfterDork.app

app: all
	mkdir -p $(APP)/Contents/MacOS $(APP)/Contents/Resources/Savers
	cp ControlPanel/Info.plist $(APP)/Contents/Info.plist
	swiftc -O -target arm64-apple-macos$(MIN) -module-name AfterDork \
	    -o $(APP)/Contents/MacOS/AfterDork \
	    $(foreach s,$(SAVERS),$(s)/$(s).swift) $(SHARED) ControlPanel/main.swift \
	    $(FRAMEWORKS) -F Vendor -framework Sparkle \
	    -Xlinker -rpath -Xlinker @executable_path/../Frameworks
	mkdir -p $(APP)/Contents/Frameworks
	rm -rf $(APP)/Contents/Frameworks/Sparkle.framework
	cp -R Vendor/Sparkle.framework $(APP)/Contents/Frameworks/
	cp ControlPanel/AfterDork.icns $(APP)/Contents/Resources/
	for s in $(SAVERS); do \
	    rm -rf $(APP)/Contents/Resources/Savers/$$s.saver; \
	    cp -R $(BUILD)/$$s.saver $(APP)/Contents/Resources/Savers/; \
	    rm -rf $(APP)/Contents/Resources/$$s; \
	    if [ -d $$s/Resources ]; then \
	        mkdir -p $(APP)/Contents/Resources/$$s; \
	        cp -R $$s/Resources/ $(APP)/Contents/Resources/$$s/; \
	    fi; \
	done
	xattr -cr $(APP)
	codesign --force --sign - $(APP)/Contents/Frameworks/Sparkle.framework
	codesign --force --sign - $(APP)

release: app
	scripts/release.sh

install: all
	mkdir -p "$(HOME)/Library/Screen Savers"
	for s in $(SAVERS); do \
	    rm -rf "$(HOME)/Library/Screen Savers/$$s.saver"; \
	    cp -R $(BUILD)/$$s.saver "$(HOME)/Library/Screen Savers/"; \
	done

clean:
	rm -rf $(BUILD)

.PHONY: all previews install clean app $(foreach s,$(SAVERS),preview-$(s))
