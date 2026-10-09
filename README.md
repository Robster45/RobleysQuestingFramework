# JötunnModStub

A Valheim mod stub project using [Jötunn](https://github.com/Valheim-Modding/Jotunn) including build tools and a basic Unity project stub.
This project now includes a server-owned quest framework. Start with [Writing quests](QUEST-INVENTORY.md) for the base class contract, example quests, inventory design, and current limitations.

#  Setup Guide

Please see [Jötunn Docs](https://valheim-modding.github.io/Jotunn/guides/overview.html) detailed documentation and setup.

### Post Build automations

Included in this repo is a PowerShell script `publish.ps1`.
The script is referenced in the project file as a post-build event.
Depending on the chosen configuration in Visual Studio the script executes the following actions.

### Building Debug

The compiled dll and a dll.mdb debug file are copied to `<ValheimDir>\BepInEx\plugins` (or the path set in MOD_DEPLOYPATH).

### Building Release

A compressed file with the binaries is created in `<JotunnModStub>\Packages`ready for upload to ThunderStore.
Dont forget to include your information in the manifest.json and to change the project's readme file.

## Developing Assets with Unity

New Assets can be created with Unity and imported into Valheim using the mod.
A Unity project is included in this repository under `<JotunnModStub>\JotunnModUnity`.

### Unity Editor Setup

1. [Download](https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup.exe) UnityHub directly from Unity or install it with the Visual Studio Installer via `Individual Components` -> `Visual Studio Tools for Unity`
2. You will need an Unity account to register your PC and get a free licence. Create the account, login with it in Unity Hub and get your licence via `Settings` -> `Licence Management`
3. Install Unity Editor version 2022.3.17f
4. Compile the project. This copies all assemblies into `<JotunnModStub>\JotunnModUnity\Assets\Assemblies`. Don't open Unity yet before this step, it will remove assembly references.
5. **Warning:** These assembly files are copyrighted material and you can theoretically get into trouble when you distribute them in your github repository. To avoid that there is a .gitignore file in the Unity project folder. Keep that when you clone or copy this repository
6. Open Unity Hub and add the JotunnModUnity project
7. Open the project in Unity
8. Install the `AssetBundle Browser` package in the Unity Editor via `Window`-> `Package Manager` for easy bundle creation

## Debugging

See the Wiki page [Debugging Plugins via IDE](https://github.com/Valheim-Modding/Wiki/wiki/Debugging-Plugins-via-IDE) for more information

## Actions after a game update

When Valheim updates it is likely that parts of the assembly files change.
If this is the case, the references to the assembly files must be renewed in Visual Studio and Unity.

### Prebuild actions

1. There is a file called DoPrebuild.props included in the solution. When you set its only value to true, Jötunn will automatically generate publicized assemblies for you. Otherwise you have to do this step manually.

### Unity actions

1. Copy all `assembly_*.dll` from `<ValheimDir>\valheim_Data\Managed` into `<JotunnModStub>\JotunnModUnity\Assets\Assemblies`. <br />
  **Do this directly in the filesystem - don't import the dlls in Unity**.
2. Go to Unity Editor and press `Ctrl+R`. This reloads all files from the filesystem and "re-imports" the copied dlls into the project.

## Quest journal

Press **J** in-game to toggle the read-only journal. It starts hidden and is positioned 12 UI units below the minimap, aligned to its right edge. It does not capture the mouse or block movement. The shortcut is ignored while chat, console, text entry, inventory, or the menu is open.

The `[Journal]` section in `BepInEx/config/com.jotunn.robleysquestingframework.cfg` is generated on the first launch:

| Setting | Default | Effect |
| --- | --- | --- |
| ToggleKey | J | BepInEx keyboard shortcut |
| Width | 280 | Panel width in scaled UI units |
| MinimapGap | 12 | Distance below the minimap |
| OffsetX | 0 | Positive moves right, negative left |
| OffsetY | 0 | Positive moves up, negative down |
| FontSize | 16 | Text size |
| BackgroundOpacity | 0.8 | Background alpha, from 0 to 1 |
| ShowCancelledQuests | true | Include cancelled entries; completed quests are always removed |

The height grows with the entries. For a compact active tracker, disable `ShowCancelledQuests`. Completed quests disappear automatically when the visible journal next refreshes, or when it is reopened. A very large quest list can extend off-screen; this initial HUD does not have scrolling. Config changes made through a config manager apply while running; restart after editing the config file manually.

Clients request a fresh server snapshot when opening the journal. While visible, quest tracking changes mark that player's snapshot dirty and send it at the end of the update; multiple changes in one frame are combined. Closing unsubscribes from updates, stops text/layout refreshes, and ignores any in-flight snapshots. Quest tracking and saving continue normally while hidden. Both clients and the server need the updated mod for multiplayer journals. Dedicated servers create no UI.

Quest authors can override `Quest.JournalProgress` and call the protected `NotifyTrackingChanged()` after changing custom progress. Stage changes notify automatically. Kill and resource quests provide progress text and notifications out of the box. Hidden quests and other players' quests are excluded by the server.

Manual verification: open with J, advance a resource/kill quest and check its count; close, advance again, and reopen to confirm the latest count. Repeat with a remote client and verify another player's progress is absent. Check positioning at your HUD scale, change the layout settings, and reconnect to verify that the journal starts hidden.
