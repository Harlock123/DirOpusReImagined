# DirOpusReImagined

This is a project to reimagine the Amiga file manager Directory Opus 4.12. 
It is using the Avalonia Framework so as to afford the ability to compile for Linux
and MacOS as well as Windows.

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
		</Button>
	</Buttons>
	
	<Executable>
		<Extensions>EXE,BAT,PS1,BMP,JPG,JPEG,TXT,PNG,TIFF,GIF,ICO,
		PNG,DOC,DOCX,XLS,XLSX,PPT,PPTX,PDF,ZIP,RAR,7Z,WAV,AAC,MP3,MP4,
		AVI,FLV,WMV,MOV,MPG,MPEG,FLAC,OGG,OGV,WEBM,HTML,HTM,XML,JSON,
		CSS,JS,TS,CS,CSHARP,CSHTML,ASPX,ASP,PHP,SQL,INI,CFG,LOG,MD,MARKDOWN</Extensions>
	</Executable>
	
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

![Screenshot](https://github.com/Harlock123/DirOpusReImagined/blob/master/Images/MainScreen1.jpg)

Arguments Parameters

The <Args> </Args> parameter can contain the following entries

%FD% - Full Path of the file or folder selected in the Left or Right Panel Left Panel is searched first

%AF% - All Files in the Left or Right Panel Left Panel is searched first. Each argument is separated by a space

%LAF% - All Files in the Left or Right Panel . Each argument is separated by a space Left panel is searched first

%RF1% - Full Path of the file or folder selected in the Right Panel

%LF1% - Full Path of the file or folder selected in the Left Panel

%RPAF% - Full Path All Files in the Right Panel . Each argument is separated by a space

%LPAF% - Full Path All Files in the Left Panel . Each argument is separated by a space

The Action Parameter needs to be the actual command that you want to execute on clicking the button. 
The Parsed ARGS from the above parameters will be appended to the command line.

The Name Parameter is the name of the button. It is used to identify the button in the configuration file.
There are 36 buttons available in the interface numbered 1 to 36. The buttons are numbered from left to right
top to bottom. The first button is LPButton1 and the last button is LPButton36.

The Content Parameter is the text that will appear on the button itself

The Background Parameter is the background color of the button

The Foreground Parameter is the foreground color of the button

The HorizontalAlignment Parameter is the horizontal alignment of the text on the button
Valid values are Left, Center, Right

The VerticalAlignment Parameter is the vertical alignment of the text on the button
Valid values are Top, Center, Bottom

The Margin Parameter is the margin around the button. The values are in the order Left, Top, Right, Bottom

The Shell Parameter is a boolean value that indicates whether the command should be executed in a shell or not
valid values are True or False

The Window Parameter is a boolean value that indicates whether the command should be executed in a new window or not
valid values are True or False




## Building

### Prerequisites


