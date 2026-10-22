#Requires AutoHotkey v2.0
#SingleInstance Force
#NoTrayIcon

<#<+F23::
{
    KeyWait "F23"
    KeyWait "LShift"
    KeyWait "LWin"
    SendEvent "{Pause}"
}
