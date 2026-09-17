#define AppName "MYKEY"
#define AppVersion "1.1.5"
#define AppPublisher "MYKEY"
#define AppExeName "MYKEY.exe"
#define ProjectRoot SourcePath + "\.."
#define PublishDir ProjectRoot + "\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
#ifdef TestInstall
AppId={{29E6474D-BB28-4F44-B831-D3F2574A04BD}
#else
AppId={{14AC4C45-1DE0-4BC5-A069-0877D025C380}
#endif
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
OutputDir={#ProjectRoot}\dist
#ifdef TestInstall
OutputBaseFilename=MYKEY-Setup-{#AppVersion}-x64-test
#else
OutputBaseFilename=MYKEY-Setup-{#AppVersion}-x64
#endif
SetupIconFile={#ProjectRoot}\Assets\Icons\mykey-key.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no
SetupLogging=yes
VersionInfoVersion=1.1.5.0
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#SourcePath}\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectRoot}\Assets\Fonts\MiSans\MiSans字体知识产权许可协议.pdf"; DestDir: "{app}\licenses"; DestName: "MiSans-License.pdf"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
