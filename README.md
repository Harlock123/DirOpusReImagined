# DirOpusReImagined

This is **DirOpusReImagined** - a modern cross-platform file manager inspired by the classic Amiga Directory Opus 4.12.

## What It Does

It's a **dual-panel file manager** built with .NET 8 and Avalonia that runs on Windows, macOS, and Linux. Think of it as a professional-grade file browser with two independent panels side-by-side for efficient file management.

## Key Features

### Dual-Panel File Browsing
- Two independent file panels displayed side-by-side for efficient file management
- Each panel has its own path bar, back button, and drive selector
- Double-click a folder to navigate into it; use the back button to go up a level
- Swap panel contents or clone one panel's path to the other with dedicated buttons

### File Operations
- **Copy** files and folders between panels
- **Move** files and folders between panels
- **Delete** files and folders with confirmation dialog
- **Rename** files and folders with a pattern-based rename interface for batch operations
- **Create folders** in either panel
- **Create ZIP archives** from selected files

### Real-Time Filter
- Each panel has a filter text box that narrows the displayed files and folders as you type
- Filters by name using case-insensitive matching
- An X button next to each filter clears it instantly
- Filters reset automatically when navigating to a new directory

### Status Bar
- A status bar at the bottom of the window shows live information for each panel:
  - Total folder and file counts
  - Number of selected files and their combined size
  - Free disk space on the current drive

### Selection Management
- Click to select a single item; Ctrl+Click for multi-select; Shift+Click for range select
- "Select All" buttons to select all files (not folders) in a panel
- "Clear" buttons to deselect everything in a panel

### 36 Customizable Action Buttons
- A grid of 36 user-configurable command buttons below the panels
- Each button can launch any external program with flexible argument substitution
- Supports parameter placeholders like `%FD%`, `%LPATH%`, `%AF%`, and more
- Button appearance (color, text, alignment, tooltip) is fully configurable via XML

### Drive Presets
- Up to 5 configurable quick-navigation presets between the panels
- Each preset has a left-panel and right-panel button
- Supports environment variables: `$HOME`, `$ROOT`, `$DESKTOP`, `$DOCUMENTS`, `$PICTURES`

### Integrated Image Viewer
- Built-in image viewer for common formats (BMP, JPG, PNG, TIFF, GIF, ICO, etc.)
- Configurable via the `<UseIntegratedImageViewer>` setting

### Navigation
- Path text box with Enter key navigation
- Back button to go up one directory level
- Drive button for drive/volume selection
- Panel swap and clone buttons for quick path synchronization

### Display Options
- Toggle hidden file visibility with the "Show Hidden" checkbox
- Sort files by name or by size
- Configurable font sizes for grid content and headers
- Right-click context menu on panels for font size adjustment
- Tooltips on hover showing file/folder details

### Cross-Platform Support
- Runs on Windows, macOS, and Linux via Avalonia
- Platform-specific configuration files (Configuration.xml, MACConfiguration.xml, LINUXConfiguration.xml)
- Handles platform differences in path separators, special folders, and executable detection

### Configuration
- XML-based configuration file (`Configuration.xml`) for all customization
- Configurable executable file extensions for double-click launching
- Configurable image file extensions for the integrated viewer
- Per-panel start paths, font sizes, and titles

## Tech Stack

- **Framework**: Avalonia 11.0.0 (cross-platform UI)
- **Runtime**: .NET 8.0 / C#
- **XML-based configuration** for buttons and settings

The project is currently at version 0.0.4.6 and under active development. It's designed for power users, developers, and system administrators who need efficient file management with extensive customization options.

## Detailed Overview

The interface presents itself as two panels of files in a specified path.
The standard sorts of navigation allow one to move around the file system 
in each panel. There are also buttons to allow one to move up a directory 
to the left of each path text area above each panel.
Between the left and right panels is a button to allow one to copy and move
files and folders from one panel to the other. 

Doubkle clicking on a folder in a panel will open that folder in the same panel
adjusting the panels path text area accordingly.

To select a file simply click on it. To select multiple files hold down the
control key and click on the files you want to select. 

There are a series of buttons below the panels that allow one to user defined
functions. I first execution the application will read the definitions of these 
user defined functions from a file called `Configuration.xml` in the same folder.

A Sample of this file is shown below

```
<Settings>
	<Buttons>
		<Button>
			<Name>LPButton1</Name>
			<Content>VSCODE here</Content>
			<Background>Red</Background>
			<Foreground>Black</Foreground>
			<HorizontalAlignment>Center</HorizontalAlignment>
			<VeriticalAlignment>Center</VeriticalAlignment>
			<Margin>2,2,2,2</Margin>
			<Action>code</Action>
			<Args>%FD%</Args>
			<Shell>True</Shell>
			<Window>True</Window>
		</Button>
		<Button>
			<Name>LPButton2</Name>
			<Content>VSCODE All Files</Content>
			<Action>code</Action>
			<Args>%AF%</Args>
			<Shell>True</Shell>
			<Window>True</Window>
		</Button>
		<Button>
			<Name>LPButton3</Name>
			<Content>NotePad all seq</Content>
			<Action>notepad.exe</Action>
			<Args>%LAF%</Args>
			<Shell>False</Shell>
			<Window>False</Window>
		</Button>
		<Button>
			<Name>LPButton4</Name>
			<Content>Code Diff</Content>
			<Action>code</Action>
			<Args>--diff %LF1% %RF1%</Args>
			<Shell>True</Shell>
			<Window>False</Window>
			<ToolTip>Call VSCODE with the --diff parameter passing the first file selected in the Left and Right panel as arguments</ToolTip>
		</Button>
		<Button>
			<Name>LPButton33</Name>
			<Content>Windows Terminal on Left</Content>
			<Action>wt</Action>
			<Args>-d %LPATH%</Args>
			<Shell>False</Shell>
			<Window>False</Window>
			<ToolTip>Opens a windows terminal in the filder shown on the left panel</ToolTip>
		</Button>
		<Button>
			<Name>LPButton34</Name>
			<Content>Windows Terminal on Right</Content>
			<Action>wt</Action>
			<Args>-d %RPATH%</Args>
			<Shell>False</Shell>
			<Window>False</Window>
			<ToolTip>Opens windows terminal in the filder shown on the right panel</ToolTip>
		</Button>
		<Button>
			<Name>LPButton35</Name>
			<Content>Terminal on Left</Content>
			<Action>alacritty</Action>
			<Args>--working-directory %LPATH%</Args>
			<Shell>False</Shell>
			<Window>False</Window>
			<ToolTip>Opens an alacritty terminal in the filder shown on the left panel</ToolTip>
		</Button>
		<Button>
			<Name>LPButton36</Name>
			<Content>Terminal on right</Content>
			<Action>alacritty</Action>
			<Args>--working-directory %RPATH%</Args>
			<Shell>False</Shell>
			<Window>False</Window>
			<ToolTip>Opens an alacritty terminal in the filder shown on the right panel</ToolTip>
		</Button>
	</Buttons>

	<DrivePresets>
		<DrivePreset>
			<Order>1</Order>
			<Name>Home</Name>
			<Path>$HOME</Path>
		</DrivePreset>
		<DrivePreset>
			<Order>2</Order>
			<Name>Root</Name>
			<Path>$ROOT</Path>
		</DrivePreset>
		<DrivePreset>
			<Order>3</Order>
			<Name>DT</Name>
			<Path>$DESKTOP</Path>
		</DrivePreset>
		<DrivePreset>
			<Order>4</Order>
			<Name>Docs</Name>
			<Path>$DOCUMENTS</Path>
		</DrivePreset>
		<DrivePreset>
			<Order>5</Order>
			<Name>Pics</Name>
			<Path>$PICTURES</Path>
		</DrivePreset>
	</DrivePresets>

	<Executable>
		<Extensions>
			EXE,BAT,PS1,BMP,JPG,JPEG,TXT,PNG,TIFF,GIF,ICO,
			PNG,DOC,DOCX,XLS,XLSX,PPT,PPTX,PDF,ZIP,RAR,7Z,WAV,AAC,MP3,MP4,
			AVI,FLV,WMV,MOV,MPG,MPEG,FLAC,OGG,OGV,WEBM,HTML,HTM,XML,JSON,
			CSS,JS,TS,CS,CSHARP,CSHTML,ASPX,ASP,PHP,SQL,INI,CFG,LOG,MD,MARKDOWN
		</Extensions>
	</Executable>

	<Images>
		<ImageExtensions>BMP,JPG,JPEG,PNG,TIFF,TIF,GIF,ICO,PCX</ImageExtensions>
		<UseIntegratedImageViewer>False</UseIntegratedImageViewer>
	</Images>

	<LeftGrid>
		<FontSize>14</FontSize>
		<HeaderFontSize>16</HeaderFontSize>
		<Title>LEFT Grid</Title>
		<TitleFontSize>20</TitleFontSize>
		<StartPath>/</StartPath>
	</LeftGrid>

	<RightGrid>
		<FontSize>14</FontSize>
		<HeaderFontSize>16</HeaderFontSize>
		<Title>RIGHT Grid</Title>
		<TitleFontSize>20</TitleFontSize>
		<StartPath>/</StartPath>
	</RightGrid>

</Settings>

```

It is a work in progress.

Main Screen Interface

![Screenshot](https://github.com/Harlock123/DirOpusReImagined/blob/master/Images/MainScreenMac.png)

Arguments Parameters

The `<Args> </Args>` parameter can contain the following entries

* Any text that you want to pass to the command line

* `%FD%` - Full Path of the file or folder selected in the Left or Right Panel Left Panel is searched first

* `%AF%` - All Files in the Left or Right Panel Left Panel is searched first. Each argument is separated by a space

* `%LAF%` - All Files in the Left or Right Panel . Each argument is separated by a space Left panel is searched first

* `%RF1%` - Full Path of the file selected in the Right Panel

* `%LF1%` - Full Path of the file selected in the Left Panel

    Note That the above two parameters are usable together in a buttons definition
    To allow handing a file from each panel to a command. For example a diff command
    as shown in the above example configuration file

* `%RPAF%` - Full Path All Files in the Right Panel . Each argument is separated by a space

* `%LPAF%` - Full Path All Files in the Left Panel . Each argument is separated by a space

* `%LPATH%` - Full Path of what is currently being shown in the LEFT Panel

* `%RPATH%` - Full Path of what is currently being shown in the RIGHT Panel



The `<Action></Action>` Parameter needs to be the actual command that you want to execute on clicking the button. 
The Parsed ARGS from the above parameters will be appended to the command line.

The `<Name></Name>` Parameter is the name of the button. It is used to identify the button in the configuration file.
There are 36 buttons available in the interface numbered 1 to 36. The buttons are numbered from left to right
top to bottom. The first button is LPButton1 and the last button is LPButton36.

The `<Content></Content>` Parameter is the text that will appear on the button itself

The `<Background></Background>` Parameter is the background color of the button

The `<Foreground></Foreground>` Parameter is the foreground color of the button

The `<HorizontalAlignment></HorizontalAlignment>` Parameter is the horizontal alignment of the text on the button
Valid values are Left, Center, Right

The `<VerticalAlignment></VerticalAlignment>` Parameter is the vertical alignment of the text on the button
Valid values are Top, Center, Bottom

The `<Margin></Margin>` Parameter is the margin around the button. The values are in the order Left, Top, Right, Bottom

The `<Shell></Shell>` Parameter is a boolean value that indicates whether the command should be executed in a shell or not
valid values are True or False

The `<Window></Window>` Parameter is a boolean value that indicates whether the command should be executed in a new window or not
valid values are True or False

The `<ToolTip></ToolTip>` Parameter is the text that will appear when the mouse hovers over the button for a few seconds


The configuration file contains a section called `<DrivePresets></DrivePresets>` that allows one to define a set of hardcoded 
path specifications to be loaded into the LEFT and RIGHT panels. The buttons for this functionality are located in the 
interface between the left and right panels. Set as block of 10 buttons seperated between each panel. 5 dedicated to the
LEFT and 5 to the RIGHT panel. The panels button tuples are numbered from 1 to 5. The first button set at the top
is number 1 and the bottom set is number 5.

Each button is defined between a `<DrivePreset></DrivePreset>` tag.

The `<Order></Order>` tag is used to define the order of the button in the panel.

The `<Name></Name>` tag is used to define the text that will appear on the button.
care should be taken to ensure that the text is not too long as it will be truncated if it does not fit on the button.

The `<Path></Path>` Specifier is the path that will be loaded into the panel when the button is clicked.

There are a number of special variables that can be used in the path specification.
- $HOME - The users home directory
- $ROOT - The root directory of the system 
  - usually C:\ on Windows, / on Linux and MacOS
- $DESKTOP - The users desktop directory
- $DOCUMENTS - The users documents directory 
  - Note:
       On Mac and Linux it will compare the folder returned by .Nets Environment.SpecialFolder
       enumeration with the users HOME folder. If they are the same it will then search the
       Home folder for a Documents, documents, DOCUMENTS folder and if found will return that.
- $PICTURES - The users pictures directory

## Building

### Prerequisites

