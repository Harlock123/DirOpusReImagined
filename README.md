# DirOpusReImagined

This is a project to reimagine the Amiga file manager Directory Opus 4.12. 
It is using the Avalonia FRamework so as to afford the ability to compile for Linux
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

A Sample of this flike is shown below

```
<Buttons>
	<Button>
		<Name>LPButton1</Name>
		<Content>VSCODE here</Content>
		<Background>Red</Background>
		<Foreground>Black</Foreground>
		<HorizontalAlignment>Center</HorizontalAlignment>
		<VeriticalAlignment>Center</VeriticalAlignment>
		<Margin>2,2,2,2</Margin>
		<Action>C:\Program Files\Microsoft VS Code\bin\code.cmd</Action>
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
```

It is a work in progress.

Main Screen Interface

![Screenshot](https://github.com/Harlock123/DirOpusReImagined/blob/master/Images/MainScreen1.jpg)

## Building

### Prerequisites


