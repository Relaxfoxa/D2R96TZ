Option Explicit

Dim shell, fso, root, exePath, configPath, command, service, processes, d2rProcesses
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

root = fso.GetParentFolderName(WScript.ScriptFullName)
exePath = root & "\bin\Release\D2R96TZ.exe"
configPath = root & "\config\reference-offsets.ini"

If Not fso.FileExists(exePath) Then
    MsgBox "Listener executable not found:" & vbCrLf & exePath, vbExclamation, "D2R96TZ"
    WScript.Quit 1
End If

If Not fso.FileExists(configPath) Then
    MsgBox "Configuration file not found:" & vbCrLf & configPath, vbExclamation, "D2R96TZ"
    WScript.Quit 1
End If

Set service = GetObject("winmgmts:\\.\root\cimv2")
Set d2rProcesses = service.ExecQuery("SELECT Name FROM Win32_Process WHERE Name='D2R.exe'")
If d2rProcesses.Count = 0 Then
    MsgBox "Please start D2R before launching the listener.", vbInformation, "D2R96TZ"
    WScript.Quit 1
End If

Set processes = service.ExecQuery("SELECT Name FROM Win32_Process WHERE Name='D2R96TZ.exe'")
If processes.Count > 0 Then
    WScript.Quit 0
End If

command = Chr(34) & exePath & Chr(34) & " follow-next-manual " & Chr(34) & configPath & Chr(34)
shell.CurrentDirectory = root
shell.Run command, 1, False
