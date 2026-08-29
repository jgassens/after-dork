SAVERS = FlyingFlasks GlasswarePipes LatticeMaze MystifyPolymers
MIN = 11.0
BUILD = build
FRAMEWORKS = -framework ScreenSaver -framework AppKit

all: $(foreach s,$(SAVERS),$(BUILD)/$(s).saver)

define SAVER_template
$(BUILD)/$(1).saver: $(1)/$(1).swift $(1)/Info.plist
	mkdir -p $(BUILD)/$(1).saver/Contents/MacOS
	cp $(1)/Info.plist $(BUILD)/$(1).saver/Contents/Info.plist
	swiftc -O -target arm64-apple-macos$(MIN) -module-name $(1) -emit-library \
	    -o $(BUILD)/$(1)-arm64.dylib $(1)/$(1).swift $(FRAMEWORKS)
	swiftc -O -target x86_64-apple-macos$(MIN) -module-name $(1) -emit-library \
	    -o $(BUILD)/$(1)-x8664.dylib $(1)/$(1).swift $(FRAMEWORKS)
	lipo -create -output $(BUILD)/$(1).saver/Contents/MacOS/$(1) \
	    $(BUILD)/$(1)-arm64.dylib $(BUILD)/$(1)-x8664.dylib
	codesign --force --sign - $(BUILD)/$(1).saver
	touch $(BUILD)/$(1).saver

$(BUILD)/preview-$(1): $(1)/$(1).swift Harness/main.swift
	mkdir -p $(BUILD)
	swiftc -O -DHARNESS -module-name $(1)Preview -o $$@ \
	    $(1)/$(1).swift Harness/main.swift $(FRAMEWORKS)

preview-$(1): $(BUILD)/preview-$(1)
	mkdir -p $(BUILD)/shots
	$(BUILD)/preview-$(1) 1280 720 240 $(BUILD)/shots/$(1)
endef

$(foreach s,$(SAVERS),$(eval $(call SAVER_template,$(s))))

previews: $(foreach s,$(SAVERS),preview-$(s))

APP = $(BUILD)/AfterHoodPreview.app

app: $(APP)

$(APP): $(foreach s,$(SAVERS),$(s)/$(s).swift) AppPreview/main.swift AppPreview/Info.plist
	mkdir -p $(APP)/Contents/MacOS
	cp AppPreview/Info.plist $(APP)/Contents/Info.plist
	swiftc -O -target arm64-apple-macos$(MIN) -module-name AfterHoodPreview \
	    -o $(APP)/Contents/MacOS/AfterHoodPreview \
	    $(foreach s,$(SAVERS),$(s)/$(s).swift) AppPreview/main.swift $(FRAMEWORKS)
	codesign --force --sign - $(APP)
	touch $(APP)

install: all
	mkdir -p "$(HOME)/Library/Screen Savers"
	for s in $(SAVERS); do \
	    rm -rf "$(HOME)/Library/Screen Savers/$$s.saver"; \
	    cp -R $(BUILD)/$$s.saver "$(HOME)/Library/Screen Savers/"; \
	done

clean:
	rm -rf $(BUILD)

.PHONY: all previews install clean $(foreach s,$(SAVERS),preview-$(s))
