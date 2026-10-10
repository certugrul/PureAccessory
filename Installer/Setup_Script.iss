; =====================================================================
; Pure Accessory Otomasyon - Profesyonel Kurulum Scripti (Inno Setup)
; =====================================================================

#define MyAppName "Pure Accessory Otomasyon"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Pure Accessory"
#define MyAppExeName "ERP.Desktop.exe"
#define SourcePublishDir "..\Release_Publish"

[Setup]
AppId={{D9530042-4465-4732-87A5-CE29C1FC1AF4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName=C:\PureAccessoryERP
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\Releases
OutputBaseFilename=PureAccessory_Setup_v{#MyAppVersion}
SetupIconFile=..\ERP.Desktop\Assets\logo.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
AppMutex=PureAccessoryERP_Mutex

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "firewall"; Description: "Windows Güvenlik Duvarı izinlerini otomatik tanımla"; GroupDescription: "Sistem Ayarları:"; Flags: checkedonce

[Files]
; Ana yayın klasöründeki tüm dosyaları kopyala
Source: "{#SourcePublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Logo ikonu
Source: "..\ERP.Desktop\Assets\logo.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Dirs]
; Kullanıcı izinleri: Tüm kullanıcılara tam okuma/yazma/değiştirme yetkisi ver (UAC ve yetki engellerini sıfırlar)
Name: "{app}"; Permissions: everyone-full users-full
Name: "{app}\Data"; Permissions: everyone-full users-full
Name: "{app}\Tools"; Permissions: everyone-full users-full

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\logo.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\logo.ico"; Tasks: desktopicon

[Run]
; 1. Klasör izinlerini Windows seviyesinde kesin olarak ver
Filename: "icacls"; Parameters: """{app}"" /grant Everyone:(OI)(CI)F /t /q"; Flags: runhidden
Filename: "icacls"; Parameters: """{app}"" /grant Users:(OI)(CI)F /t /q"; Flags: runhidden

; 2. Güvenlik Duvarı İzinleri (Barkod tarayıcı & SQL bağlantısı için)
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""Pure Accessory ERP"" dir=in action=allow program=""{app}\{#MyAppExeName}"" enable=yes"; Flags: runhidden; Tasks: firewall
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""Pure Accessory Tunnel"" dir=in action=allow program=""{app}\Tools\cloudflared.exe"" enable=yes"; Flags: runhidden; Tasks: firewall

; 3. Kurulum bitince uygulamayı başlat
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""Pure Accessory ERP"""; Flags: runhidden
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""Pure Accessory Tunnel"""; Flags: runhidden
