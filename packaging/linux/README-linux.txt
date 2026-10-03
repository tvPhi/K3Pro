K3Pro — Linux

1. HID access permissions (once):
     sudo cp 70-k3pro.rules /etc/udev/rules.d/
     sudo udevadm control --reload-rules && sudo udevadm trigger
   Unplug / replug the numpad or the 2.4G receiver.

2. Run:
     ./K3Pro.App

App data: ~/.config/K3Pro/ (app-settings.json, keymap-state.json, logs/).
layout.json must be in the same folder as K3Pro.App.

3. Optional — app menu entry and icon:
     cp k3pro.png ~/.local/share/icons/hicolor/512x512/apps/k3pro.png
     # edit Exec= in k3pro.desktop to the full path of K3Pro.App, then:
     cp k3pro.desktop ~/.local/share/applications/
