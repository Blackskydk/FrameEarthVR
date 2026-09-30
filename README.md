# Frame Earth VR

Explore Earth on **Steam Frame**, from street level to giant scale. Fly over cities,
walk across terrain, resize the world around your feet, and choose destinations
with a globe carried by your left hand.

Steam Frame is the primary target. The standalone APK runs on the headset through
**Lepton**, using the Frame controllers. Terrain streams from Cesium ion, so an
internet connection and your own Cesium access token are required. This is a
preview project; headset comfort, performance, and the in-game update flow are
still being tested.

[Download the newest release](https://github.com/Blackskydk/FrameEarthVR/releases)
· [Controls](docs/CONTROLS.md)
· [Detailed installation guide](docs/STANDALONE_INSTALL.md)

## Install on Steam Frame

Initial installation uses Valve's Devkit Client and a token setup dialog on your
PC. You do not need Unity or a source checkout. Enabling updates on the headset
adds one terminal command, described below.

### 1. Get the files and tools

You need a **Windows PC with Steam**, a **Steam Frame on the same local network**,
and a **Cesium ion account**.

- From [Releases](https://github.com/Blackskydk/FrameEarthVR/releases), open the
  newest preview and download **`FrameEarthVR.apk`** and **`setup-token.ps1`** to
  your PC. Use the files under **Assets**, rather than the source-code ZIP.
- Create a folder called `FrameEarthVR` and put **only the APK** inside it. Keep
  the token script outside that folder; Devkit Client uploads everything in the
  folder you select.
- Install **SteamOS Devkit Client** through Steam on your PC, then open it from
  your Library's **Software** category. [Valve's tool installation guide](https://partner.steamgames.com/doc/steamhardware/loadgames#2).
- At [Cesium ion](https://ion.cesium.com/), add **Google Photorealistic 3D Tiles**
  to your assets and create a token with **`assets:read`** access to asset
  **`2275207`**. [Cesium's token guide](https://cesium.com/learn/ion/cesium-ion-access-tokens/).
  No separate Google API key is needed. Your Cesium account's usage limits apply.

### 2. Pair your PC with Frame

1. On Frame, enable **Settings → System → Developer Mode**.
2. Open **Settings → Developer → Pair new host**.
3. On the PC, open the Devkit Client's **Devkits** tab and click **Register**
   beside your Frame.
4. Accept the pairing request on the headset.

Pairing is needed once. If Frame is not listed, use the client's connection-by-IP
field with the headset's IP address. [Valve's Frame pairing and upload guide](https://partner.steamgames.com/doc/steamhardware/steamframe/loadgames).

### 3. Upload the APK

In Devkit Client, select your Frame, open **Title Upload**, and enter:

| Field | Value |
|---|---|
| Name | `FrameEarthVR` |
| Local Folder | The `FrameEarthVR` folder containing only the APK |
| Start Command | `FrameEarthVR.apk` |
| Runtime | **Lepton**; some client versions label the APK runtime **Android** |

Click **Upload** and wait for it to finish. The game will appear on Frame under
**Library → Non-Steam → Devkit Game: FrameEarthVR**. You can use the client's
**Start** button for the first launch.

### 4. Save your token and play

1. **Launch the game once on Frame, then close it.** This creates its data folder.
   Seeing the account setup panel is enough; you do not need to enter a token yet.
2. On the PC, right-click **`setup-token.ps1`** and select **Run with PowerShell**.
3. Choose **APK on Steam Frame**, leave the hostname as `frame` (or enter its IP),
   paste **your own token** into the masked field, and click **Save token**.
4. After the success message, reopen the game from the Frame's Steam Library.

The script checks access with Cesium and saves the token on your headset. It is
remembered across launches and updates. Do not put a token in Steam launch
options or the APK. Clearing app data or replacing the game's Steam compatdata
can remove the saved token.

If Windows blocks the downloaded script, or you need scripted token setup, see
the [token setup instructions](docs/STANDALONE_INSTALL.md#supply-your-own-token-on-first-launch).
The in-game controller keyboard is also available. The APK's **PC SETUP HELP**
button provides instructions instead of using Lepton's clipboard bridge.

## Enable updates on Frame — one-time setup

This is separate from installing the APK and saving your token. It lets the game
download and apply later releases without repeating the Devkit upload.

1. Open the **Frame's desktop**, directly or through
   [Windows Remote Desktop](https://partner.steamgames.com/doc/steamhardware/steamframe/debugging).
2. Download **`frame-updater.py`** from the same GitHub release into the **Frame's
   Downloads folder**.
3. Open a terminal **on Frame** and run this single command:

   ```sh
   python3 ~/Downloads/frame-updater.py --setup
   ```

4. Choose **`apk`**. If multiple installations are found, select the folder for
   the game you just installed. The game must have launched once and be closed.

After setup, open the game's hand menu and select **Check for Updates → Download
& Apply Update**. Let the game close, allow installation to finish, then reopen
the **same Steam shortcut**. The helper verifies the download and retains the
previous APK for recovery; your token, bookmarks, and settings stay in app data.
Startup checks notify you about updates; they do not install them automatically.

Without the helper, you can update manually: replace the APK in your PC's upload
folder, then upload it again using the **same Devkit title**. Keep the existing
Steam shortcut and data. See [update details and test status](docs/ON_DEVICE_UPDATES.md).

## Basic controls

| Frame control | Action |
|---|---|
| Left View/menu button | Open or close the destination globe and menu |
| Right stick | Fly in Flight mode; walk in Grounded mode |
| Aim right controller nearly straight up/down + right stick forward/back | Become bigger or smaller in Grounded mode |
| Right trigger | Grab terrain, select menu items, or drag the miniature globe |
| Either grip | Rotate the world |
| Right shoulder | Hold for faster travel |
| Left D-pad Right | Switch between Flight and Grounded |
| Left D-pad Up | Enter or leave planetary overview |

The globe/menu starts hidden and stays open until dismissed or travel begins;
looking at it does not toggle it. Grounded resizing anchors one geographic support
point and pauses walking and ground correction during the gesture. See [all controls](docs/CONTROLS.md).

## Windows build

The Windows build is an additional option for **Frame through Proton**, or for
PC VR testing. It is distributed as **`FrameEarthVR-Windows.zip`**.

- **On Frame:** extract the complete ZIP, upload the folder through Devkit Client
  with `FrameEarthVR.exe` as the start command and **Steam Play (Proton)** as the
  runtime. Launch once and close it, then choose **Windows/Proton on Steam Frame**
  in token setup. Choose `windows` or `both` in the Frame updater setup.
- **On a Windows PC:** extract the complete ZIP into a writable folder, choose
  **Windows on this PC** in token setup, and launch `FrameEarthVR.exe` with your
  PC VR headset's OpenXR runtime configured. Its updater is included.

APK and Windows installations each store their own token and settings. A live
Windows/Proton update still needs headset verification.

## Troubleshooting

- **Frame is not found:** keep PC and Frame on the same local network, check
  Developer Mode, and try the headset's IP address in Devkit Client/token setup.
- **Token setup cannot find game data:** launch the uploaded game once, close it,
  and select the correct APK/Windows destination in the setup dialog. Download
  the current script from Releases if using an older copy.
- **No terrain or token rejected:** confirm internet access, token permission
  `assets:read`, and access to asset `2275207`. Open **Your Cesium Account** in the
  hand menu to replace a saved token.
- **The game asks for updater setup:** run the helper setup on Frame, not on the
  Windows PC. Token setup and updater setup perform different jobs.
- **Terrain is still loading or frame rate is low:** this is streamed
  photogrammetry. Check the menu's status/performance information and see the
  [known limitations](docs/KNOWN_LIMITATIONS.md).

## Building from source

The project uses **Unity `6000.3.14f1`**, OpenXR, URP, and **Cesium for Unity
`1.25.1`**. Package versions are pinned in `Packages/manifest.json`.

1. Install that Unity version with **Android Build Support**, **Android SDK & NDK
   Tools**, and **OpenJDK**. Add Windows build support if building the Windows ZIP.
2. Open the project and let Package Manager restore dependencies, including
   Cesium and Valve's OpenXR utilities.
3. Run **EarthVR → Setup Project and Main Scene**, then
   **EarthVR → Steam Frame → Configure Android**. Accept an editor restart if
   requested.
4. Build with **EarthVR → Steam Frame → Build Release APK**. The APK is written to
   `Builds/SteamFrame/FrameEarthVR.apk`. Use **EarthVR → Windows → Build Release**
   for the Windows build.

Public build commands temporarily exclude developer credentials and require
players to provide their own tokens. Developer local credentials are ignored by
Git; never commit them or publish a development APK containing them.

Use Unity's **Window → General → Test Runner → EditMode** to run `EarthVR.Tests`.
The project also includes release, credential, and updater checks under `scripts/`.

Further reading: [Frame development and profiling](docs/STEAM_FRAME_MIGRATION.md),
[architecture](docs/ARCHITECTURE.md), [credential setup](docs/CESIUM_ION_TOKEN.md),
and [release packaging](docs/STANDALONE_INSTALL.md).

The miniature globe uses NASA/Goddard Scientific Visualization Studio's Blue
Marble imagery. Terrain is supplied through Cesium ion and Google Photorealistic
3D Tiles.
