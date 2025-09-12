# AutoExtract

An FFXIV Dalamud plugin that automatically extracts materia from equipment with 100% spiritbond.

## Features

- Automatically extracts materia from equipment when spiritbond reaches 100%
- Simple checkbox overlay that appears above the Materialize window
- Configurable through the plugin's settings window
- Lightweight implementation focused solely on materia extraction

## Usage

1. Open the Materialize window in-game
2. A checkbox labeled "Auto Extract Materia" will appear above the window
3. Check the box to enable automatic extraction
4. The plugin will automatically extract materia from any equipment at 100% spiritbond
5. Uncheck the box to disable automatic extraction

## Commands

- `/autoextract` - Opens the configuration window

## Requirements

- Dalamud API 13
- .NET 9
- FFXIV with the quest "Forging the Spirit" completed (required for materia extraction)

## Installation

1. Build the plugin using `dotnet build`
2. Copy the built plugin to your Dalamud plugins directory
3. Enable the plugin in the Dalamud plugin installer

## Configuration

Access the configuration window using `/autoextract` or through the Dalamud plugin installer. The main setting is:

- **Enable Auto Extract Materia**: Toggles the automatic extraction functionality

## Building

1. Open up `AutoExtract.sln` in your C# editor of choice (Visual Studio 2022 or JetBrains Rider)
2. Build the solution (Debug or Release)
3. The resulting plugin can be found at `AutoExtract/bin/x64/Debug/AutoExtract.dll`

## Activating in-game

1. Launch the game and use `/xlsettings` to open Dalamud settings
2. Go to `Experimental`, and add the full path to the `AutoExtract.dll` to Dev Plugin Locations
3. Use `/xlplugins` to open Plugin Installer
4. Go to `Dev Tools > Installed Dev Plugins`, enable AutoExtract
5. You can now use `/autoextract` to configure the plugin

## Credits

Extraction logic inspired by the AutoDuty plugin by ffxivcode.